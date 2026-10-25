using System.Reflection;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Supply an output folder for offscreen PNGs.");
        ApplicationConfiguration.Initialize();
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(Path.GetTempPath(), "memory-gui-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            string saves = Path.Combine(fixture, "Saves");
            string source = Path.Combine(saves, "SAVE4");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "mod_peopleoftheiliacbay.txt"), "{}");
            string package = Path.Combine(fixture, "portable-save.zip");
            var assembly = Assembly.Load("MemoryArchiveGui");
            using var form = (Form)Activator.CreateInstance(assembly.GetType("PeopleMemoryTransfer.TransferForm", true)!, nonPublic: true)!;
            // WinForms skips hidden child controls in DrawToBitmap. Give the test
            // form an offscreen handle without displaying a window on the desktop.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            var controls = Descendants(form).ToArray();
            var tabs = controls.OfType<TabControl>().Single();
            var exportButton = controls.OfType<Button>().Single(control => control.Text == "Review and export");
            var importButton = controls.OfType<Button>().Single(control => control.Text == "Review and import");
            Check(!exportButton.Enabled && !importButton.Enabled, "Empty forms do not enable transfers.");
            Field(controls, "1. Existing SAVE folder").Text = source;
            Field(controls, "2. New package ZIP").Text = package;
            Check(exportButton.Enabled, "Valid export selections enable review.");
            Capture(form, Path.Combine(output, "export.png"));
            File.WriteAllText(package, "fixture package; never imported");
            tabs.SelectedIndex = 1;
            Field(controls, "1. Portable package ZIP").Text = package;
            Field(controls, "2. Configured Saves folder").Text = saves;
            var slot = Field(controls, "3. Unused SAVE number");
            Check(slot.Text == "SAVE0" && importButton.Enabled, "Selecting Saves suggests an unused slot and enables review.");
            var preview = Descendants(tabs.SelectedTab!).OfType<TextBox>().Single(control => control.AccessibleName == "Resolved transfer paths");
            Check(preview.Text.Contains(Path.Combine(saves, "SAVE0")), "Import UI displays the exact destination.");
            Capture(form, Path.Combine(output, "import.png"));
            slot.Text = "SAVE4";
            Check(!importButton.Enabled && preview.Text.Contains("already exists"), "Occupied selection disables import and explains why.");
            Capture(form, Path.Combine(output, "occupied-slot.png"));
            slot.Text = "SAVE0";
            form.Size = form.MinimumSize;
            Capture(form, Path.Combine(output, "import-minimum-size.png"));
            Check(preview.Bottom <= importButton.Top, "Minimum-size preview does not overlap the import button.");
            foreach (var control in Descendants(form).Where(control => control.Visible && control is Button or Label))
            {
                Point corner = form.PointToClient(control.PointToScreen(new Point(control.Width, control.Height)));
                Check(corner.X <= form.ClientSize.Width && corner.Y <= form.ClientSize.Height,
                    "Minimum-size form keeps control visible: " + control.Text);
            }
            Console.WriteLine("PASS offscreen GUI checks; PNGs: " + output);
        }
        finally { Directory.Delete(fixture, true); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static TextBox Field(Control[] controls, string name) => controls.OfType<TextBox>().Single(control => control.AccessibleName == name);
    private static void Capture(Form form, string path)
    {
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
