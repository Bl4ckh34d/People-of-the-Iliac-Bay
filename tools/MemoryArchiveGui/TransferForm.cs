namespace PeopleMemoryTransfer;

internal sealed class TransferForm : Form
{
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly Label status = new() { AutoSize = true, Text = "Choose Export or Import to begin.", Dock = DockStyle.Fill };
    private bool busy;

    public TransferForm()
    {
        Text = "People of the Iliac Bay — Portable saves";
        MinimumSize = new Size(760, 700);
        Size = new Size(900, 760);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 5, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "Close Daggerfall Unity before transferring. Export one SAVE folder with its exact memory history. Import only into an unused slot in your configured Saves folder.", Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(tabs);
        layout.Controls.Add(status);
        layout.Controls.Add(details);
        layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Text = TransferRunner.Recovery, Margin = new Padding(0, 10, 0, 0) });
        Controls.Add(layout);
        AddExport();
        AddImport();
        FormClosing += (_, e) =>
        {
            if (!busy) return;
            e.Cancel = true;
            status.Text = "Transfer is still running. Wait for completion before closing this window.";
        };
    }

    private void AddExport()
    {
        var page = NewPage("Export", out var grid);
        var source = AddPath(grid, "1. Existing SAVE folder", PickFolder);
        var output = AddPath(grid, "2. New package ZIP", PickNewPackage);
        AddTransferAction(grid, "Review and export", () => TransferRequest.Export(source.Text, output.Text), source, output);
        tabs.TabPages.Add(page);
    }

    private void AddImport()
    {
        var page = NewPage("Import", out var grid);
        var package = AddPath(grid, "1. Portable package ZIP", PickPackage);
        var saves = AddPath(grid, "2. Configured Saves folder", PickFolder);
        var slot = AddPath(grid, "3. Unused SAVE number", () => TransferRequest.FindUnusedSlot(saves.Text), "Find unused");
        saves.TextChanged += (_, _) =>
        {
            try { slot.Text = TransferRequest.FindUnusedSlot(saves.Text); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { slot.Clear(); }
        };
        AddTransferAction(grid, "Review and import", () => TransferRequest.Import(package.Text, saves.Text, slot.Text), package, saves, slot);
        tabs.TabPages.Add(page);
    }

    private static TabPage NewPage(string title, out TableLayoutPanel grid)
    {
        var page = new TabPage(title) { Padding = new Padding(12) };
        grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        page.Controls.Add(grid);
        return page;
    }

    private TextBox AddPath(TableLayoutPanel grid, string caption, Func<string?> pick, string buttonText = "Browse…")
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var field = new TextBox { Dock = DockStyle.Fill, AccessibleName = caption, Margin = new Padding(3, 6, 3, 6) };
        var button = new Button { Text = buttonText, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(field, 1, row);
        grid.Controls.Add(button, 2, row);
        button.Click += (_, _) =>
        {
            try { string? selected = pick(); if (selected != null) field.Text = selected; }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { status.Text = error.Message; }
        };
        return field;
    }

    private void AddTransferAction(TableLayoutPanel grid, string caption, Func<TransferRequest> request, params TextBox[] fields)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var preview = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, MinimumSize = new Size(0, 105), AccessibleName = "Resolved transfer paths" };
        grid.Controls.Add(preview, 0, row);
        grid.SetColumnSpan(preview, 3);
        var run = new Button { Text = caption, AutoSize = true, Anchor = AnchorStyles.Right, Enabled = false };
        row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(run, 0, row);
        grid.SetColumnSpan(run, 3);
        void RefreshReview()
        {
            try { preview.Text = request().Review; run.Enabled = true; }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { preview.Text = error.Message; run.Enabled = false; }
        }
        foreach (var field in fields) field.TextChanged += (_, _) => RefreshReview();
        RefreshReview();
        run.Click += async (_, _) =>
        {
            TransferRequest transfer;
            try { transfer = request(); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            { status.Text = error.Message; RefreshReview(); return; }
            try
            {
                var backend = new BackendCommand(Path.Combine(AppContext.BaseDirectory, "MemoryArchiveTool.exe"));
                var result = await TransferRunner.RunReviewedAsync(backend, transfer, () =>
                {
                    if (MessageBox.Show(this, transfer.Review + "\r\n\r\nDaggerfall Unity must be closed. Continue?", caption,
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return false;
                    busy = true;
                    tabs.Enabled = false;
                    status.Text = "Validating and transferring. Keep this window open until completion.";
                    details.Text = transfer.Review;
                    return true;
                });
                if (result is null) { status.Text = "Transfer cancelled. No transfer was started."; return; }
                status.Text = result.Success ? "Transfer completed." : "Transfer failed. See details and recovery guidance below.";
                details.Text = result.Message + "\r\n\r\n" + result.Output;
            }
            finally { busy = false; tabs.Enabled = true; RefreshReview(); }
        };
    }

    private string? PickFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the folder shown in the field label.", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }
    private string? PickNewPackage()
    {
        using var dialog = new SaveFileDialog { Title = "Choose a NEW portable save package", Filter = "Portable save ZIP (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }
    private string? PickPackage()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a portable save package", Filter = "Portable save ZIP (*.zip)|*.zip", CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }
}
