param([Parameter(Mandatory=$true)][ValidateSet(77,78)][int]$Record)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root 'art/portraits/mobile-mouths'
$id='{0:D3}' -f $Record
$source=[System.Drawing.Bitmap]::FromFile((Join-Path $folder "tfac-$id-original.png"))
$generated=[System.Drawing.Bitmap]::FromFile((Join-Path $folder "tfac-$id-mouth-sheet-v2.png"))
$atlas=New-Object System.Drawing.Bitmap(192,192)
$shapes='ABCDEFGHX'

function Is-RedLip([System.Drawing.Color]$c) {
    return $c.R -gt 40 -and $c.G -lt ($c.R*0.53) -and $c.B -lt ($c.R*0.70)
}

function Read-MouthContour([System.Drawing.Color[]]$pixels) {
    $candidates=New-Object bool[] 560
    for($y=4;$y -le 18;$y++){for($x=3;$x -le 25;$x++){
        $candidates[$y*28+$x]=Is-RedLip $pixels[$y*28+$x]
    }}
    # Only the connected red-lip component nearest the expected mouth center.
    $seed=-1; $distance=[double]::PositiveInfinity
    for($y=5;$y -le 14;$y++){for($x=5;$x -le 23;$x++){
        $index=$y*28+$x
        if($candidates[$index]){
            $d=($x-14)*($x-14)+($y-9)*($y-9)
            if($d -lt $distance){$distance=$d;$seed=$index}
        }
    }}
    if($seed -lt 0){throw 'No red-lip anchor found; this asset requires manual contour annotation.'}
    $seen=New-Object bool[] 560
    $queue=New-Object 'System.Collections.Generic.Queue[int]'
    $queue.Enqueue($seed);$seen[$seed]=$true
    while($queue.Count -gt 0){
        $index=$queue.Dequeue();$x=$index%28;$y=[int][Math]::Floor($index/28)
        for($dy=-1;$dy -le 1;$dy++){for($dx=-1;$dx -le 1;$dx++){
            $nx=$x+$dx;$ny=$y+$dy
            if($nx -lt 0 -or $nx -ge 28 -or $ny -lt 0 -or $ny -ge 20){continue}
            $next=$ny*28+$nx
            if($candidates[$next] -and -not $seen[$next]){$seen[$next]=$true;$queue.Enqueue($next)}
        }}
    }
    $minX=28;$minY=20;$maxX=-1;$maxY=-1
    $mask=New-Object bool[] 560
    for($y=0;$y -lt 20;$y++){
        $left=28;$right=-1
        for($x=0;$x -lt 28;$x++){if($seen[$y*28+$x]){$left=[Math]::Min($left,$x);$right=[Math]::Max($right,$x)}}
        if($right -ge $left){
            # Fill between lip boundaries so teeth, tongue and cavity remain included.
            for($x=$left;$x -le $right;$x++){$mask[$y*28+$x]=$true}
            $minX=[Math]::Min($minX,$left);$maxX=[Math]::Max($maxX,$right)
            $minY=[Math]::Min($minY,$y);$maxY=[Math]::Max($maxY,$y)
        }
    }
    if($maxX-$minX -lt 4 -or $maxY-$minY -lt 1){throw 'Lip contour is too small; refusing to compose.'}
    return @{mask=$mask;left=$minX;top=$minY;width=($maxX-$minX+1);height=($maxY-$minY+1)}
}

try {
    $nativeCrop=New-Object 'System.Drawing.Color[]' 560
    for($y=0;$y -lt 20;$y++){for($x=0;$x -lt 28;$x++){$nativeCrop[$y*28+$x]=$source.GetPixel(($x+18),($y+35))}}
    $native=Read-MouthContour $nativeCrop
    $nativeLeft=18+$native.left;$nativeTop=35+$native.top
    $frames=@();$skinCopies=0
    for($tile=0;$tile -lt 9;$tile++){
        $col=$tile%3;$row=[int][Math]::Floor($tile/3)
        $pixels=New-Object 'System.Drawing.Color[]' 4096
        for($y=0;$y -lt 64;$y++){for($x=0;$x -lt 64;$x++){$pixels[$y*64+$x]=$source.GetPixel($x,$y)}}
        if($tile -ne 0 -and $tile -ne 8){
            $crop=New-Object 'System.Drawing.Color[]' 560
            for($y=0;$y -lt 20;$y++){for($x=0;$x -lt 28;$x++){
                $sx=[int][Math]::Floor(($col+($x+0.5)/28.0)*$generated.Width/3.0)
                $sy=[int][Math]::Floor(($row+($y+0.5)/20.0)*$generated.Height/3.0)
                $crop[$y*28+$x]=$generated.GetPixel($sx,$sy)
            }}
            $contour=Read-MouthContour $crop
            $width=$native.width
            if($tile -eq 4){$width=[int][Math]::Round($native.width*0.85)}
            if($tile -eq 5){$width=[int][Math]::Round($native.width*0.65)}
            $height=switch($tile){1{$native.height}2{$native.height+1}3{$native.height+3}4{$native.height+2}5{$native.height}6{$native.height}7{$native.height+1}}
            $left=[int][Math]::Round($nativeLeft+($native.width-$width)/2.0)
            $top=$nativeTop
            # Remove only original lip pixels that the new contour does not cover.
            # Sample native skin at the same row, outside the original lip corners.
            for($y=0;$y -lt 20;$y++){for($x=0;$x -lt 28;$x++){
                if(-not $native.mask[$y*28+$x]){continue}
                $gx=$x+18;$gy=$y+35
                $a=$source.GetPixel(($nativeLeft-2),$gy)
                $b=$source.GetPixel(($nativeLeft+$native.width+1),$gy)
                $weight=($gx-$nativeLeft+0.5)/$native.width
                $r=[int][Math]::Round($a.R*(1-$weight)+$b.R*$weight)
                $g=[int][Math]::Round($a.G*(1-$weight)+$b.G*$weight)
                $blue=[int][Math]::Round($a.B*(1-$weight)+$b.B*$weight)
                $pixels[$gy*64+$gx]=[System.Drawing.Color]::FromArgb(255,$r,$g,$blue)
            }}
            $written=0
            for($y=0;$y -lt $height;$y++){for($x=0;$x -lt $width;$x++){
                $sx=$contour.left+[int][Math]::Floor(($x+0.5)*$contour.width/$width)
                $sy=$contour.top+[int][Math]::Floor(($y+0.5)*$contour.height/$height)
                if(-not $contour.mask[$sy*28+$sx]){continue}
                $pixels[($top+$y)*64+$left+$x]=$crop[$sy*28+$sx]
                $written++
            }}
            $frames+=@{shape=[string]$shapes[$tile];target=@($left,$top,$width,$height);generatedContour=@($contour.left,$contour.top,$contour.width,$contour.height);mouthPixels=$written}
        }
        for($y=0;$y -lt 64;$y++){for($x=0;$x -lt 64;$x++){
            $pixel=$pixels[$y*64+$x]
            if($pixel.ToArgb() -ne $source.GetPixel($x,$y).ToArgb() -and ($x -lt 24 -or $x -ge 40 -or $y -lt 42 -or $y -ge 50)){throw 'Contour write escaped native mouth envelope.'}
            $atlas.SetPixel(($col*64+$x),($row*64+$y),$pixel)
        }}
    }
    $path=Join-Path $folder "tfac-$id-visemes-v4.png"
    $atlas.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
    @{record=$Record;version=4;status='draft; visual-review-pending; not installed';nativeLipBounds=@($nativeLeft,$nativeTop,$native.width,$native.height);generatedSkinCopied=$skinCopies;frames=$frames;atlasSha256=(Get-FileHash $path).Hash} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $folder "tfac-$id-manifest-v4.json")
    Write-Output "MOUTH_CONTOUR_PASS: face=$Record; nativeLipBounds=$nativeLeft,$nativeTop,$($native.width),$($native.height); generatedSkinCopied=0; atlas=$path"
} finally {$source.Dispose();$generated.Dispose();$atlas.Dispose()}
