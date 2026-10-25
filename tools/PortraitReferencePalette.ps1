Add-Type -AssemblyName System.Drawing -ErrorAction Stop

if (-not ('PortraitReferencePalette.Quantizer' -as [type])) {
    $source = @'
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PortraitReferencePalette
{
    public sealed class Quantizer
    {
        private readonly Color[] _colors;
        private readonly Dictionary<int, Color> _cache = new Dictionary<int, Color>();
        private readonly object _cacheLock = new object();

        private Quantizer(Color[] colors)
        {
            _colors = colors;
        }

        public Color[] Colors
        {
            get { return (Color[])_colors.Clone(); }
        }

        public int Count
        {
            get { return _colors.Length; }
        }

        public static Quantizer Create(Bitmap original, Rectangle[] regions, bool includeOralDetails)
        {
            if (original == null) throw new ArgumentNullException("original");
            if (regions == null || regions.Length == 0) throw new ArgumentException("At least one feature region is required.", "regions");

            var unique = new HashSet<int>();
            Color shadowMaster = Color.White;
            for (int i = 0; i < regions.Length; i++)
            {
                Rectangle region = regions[i];
                if (region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0 ||
                    region.Right > original.Width || region.Bottom > original.Height)
                    throw new ArgumentOutOfRangeException("regions", "Every feature rectangle must be positive and inside the source bitmap.");

                for (int y = region.Top; y < region.Bottom; y++)
                {
                    for (int x = region.Left; x < region.Right; x++)
                    {
                        Color c = original.GetPixel(x, y);
                        if (c.A == 0) continue;
                        unique.Add(Key(c.R, c.G, c.B));
                        if (c.GetBrightness() < shadowMaster.GetBrightness()) shadowMaster = c;
                    }
                }
            }

            if (includeOralDetails)
            {
                Color highlightMaster = Color.Black;
                int highestMinimum = -1;
                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        Color c = original.GetPixel(x, y);
                        if (c.A == 0) continue;
                        int minimum = Math.Min(c.R, Math.Min(c.G, c.B));
                        int maximum = Math.Max(c.R, Math.Max(c.G, c.B));
                        int chroma = maximum - minimum;
                        if (minimum > highestMinimum) { highestMinimum = minimum; highlightMaster = c; }
                        bool restrainedDark = maximum <= 90 && chroma <= 48;
                        // Daggerfall eye highlights can be warm ivory rather than gray.
                        bool restrainedHighlight = maximum >= 200 && minimum >= 125 && chroma <= 96;
                        if (restrainedDark || restrainedHighlight)
                            unique.Add(Key(c.R, c.G, c.B));
                    }
                }
                // Closed-mouth originals have no cavity or teeth. Add only three
                // restrained detail shades derived from the original's own colors.
                foreach (double scale in new double[] {0.35, 0.60})
                    unique.Add(Key((int)Math.Round(shadowMaster.R*scale), (int)Math.Round(shadowMaster.G*scale), (int)Math.Round(shadowMaster.B*scale)));
                int peak = Math.Max(highlightMaster.R, Math.Max(highlightMaster.G, highlightMaster.B));
                unique.Add(Key(peak, (int)Math.Round(peak*0.75+highlightMaster.G*0.25), (int)Math.Round(peak*0.65+highlightMaster.B*0.35)));
            }

            var keys = new List<int>(unique);
            if (keys.Count == 0) throw new ArgumentException("Feature palette contains no visible source colors.");
            keys.Sort(); // packed RGB key gives deterministic R, then G, then B order
            var colors = new Color[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                int key = keys[i];
                colors[i] = Color.FromArgb(255, (key >> 16) & 255, (key >> 8) & 255, key & 255);
            }
            return new Quantizer(colors);
        }

        public Color Quantize(Color input)
        {
            int key = Key(input.R, input.G, input.B);
            lock (_cacheLock)
            {
                Color cached;
                if (_cache.TryGetValue(key, out cached)) return cached;
            }

            long bestDistance = Int64.MaxValue;
            Color best = _colors[0];
            for (int i = 0; i < _colors.Length; i++)
            {
                Color candidate = _colors[i];
                int dr = input.R - candidate.R;
                int dg = input.G - candidate.G;
                int db = input.B - candidate.B;
                long distance = (long)dr * dr + (long)dg * dg + (long)db * db;
                // Strict comparison preserves the first (sorted) color on exact ties.
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            Color output = Color.FromArgb(255, best.R, best.G, best.B);
            lock (_cacheLock) _cache[key] = output;
            return output;
        }

        private static int Key(int r, int g, int b)
        {
            return (r << 16) | (g << 8) | b;
        }
    }
}
'@
    Add-Type -TypeDefinition $source -Language CSharp -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.Drawing.dll') -ErrorAction Stop
}

function New-OriginalFeaturePalette {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap]$Original,

        [Parameter(Mandatory = $true)]
        [object[]]$Regions,

        [switch]$IncludeOralDetails
    )

    if ($null -eq $Original) { throw [System.ArgumentNullException]::new('Original') }
    if ($null -eq $Regions -or $Regions.Count -eq 0) { throw [System.ArgumentException]::new('At least one feature region is required.', 'Regions') }

    $rectangles = New-Object 'System.Collections.Generic.List[System.Drawing.Rectangle]'
    foreach ($item in $Regions) {
        if ($item -is [System.Drawing.Rectangle]) {
            $rectangles.Add($item)
            continue
        }

        $x = $null; $y = $null; $width = $null; $height = $null
        if ($item -is [System.Array] -and $item.Length -eq 4) {
            $x = $item[0]; $y = $item[1]; $width = $item[2]; $height = $item[3]
        }
        elseif ($item -is [System.Collections.IDictionary]) {
            foreach ($name in @('x', 'y', 'width', 'height')) {
                if (-not $item.Contains($name)) { throw "Region is missing '$name'." }
            }
            $x = $item['x']; $y = $item['y']; $width = $item['width']; $height = $item['height']
        }
        else {
            foreach ($name in @('x', 'y', 'width', 'height')) {
                if ($null -eq $item.PSObject.Properties[$name]) { throw "Region is missing '$name'." }
            }
            $x = $item.x; $y = $item.y; $width = $item.width; $height = $item.height
        }

        try {
            $rectangles.Add([System.Drawing.Rectangle]::new([int]$x, [int]$y, [int]$width, [int]$height))
        }
        catch {
            throw "Region coordinates must be valid integers: $($_.Exception.Message)"
        }
    }

    return [PortraitReferencePalette.Quantizer]::Create($Original, $rectangles.ToArray(), [bool]$IncludeOralDetails.IsPresent)
}
