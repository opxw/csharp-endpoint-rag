namespace EndpointRag.WinForms;

internal sealed class KnowledgeManagerDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly TextBox _preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly string _managedDirectory;
    public IReadOnlyList<string> Paths => _list.Items.Cast<string>().ToArray();

    public KnowledgeManagerDialog(IEnumerable<string> paths, string managedDirectory)
    {
        _managedDirectory = Path.GetFullPath(managedDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Text = "Kelola knowledge"; Size = new(1000, 650); MinimumSize = new(700, 450); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 9);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 360 };
        split.Panel1.Controls.Add(_list); split.Panel2.Controls.Add(_preview);
        _list.Items.AddRange(paths.Cast<object>().ToArray());
        _list.HorizontalScrollbar = true;
        var edit = new Button { Text = "Edit RAW text…", AutoSize = true, Enabled = false };
        var remove = new Button { Text = "Lepas dokumen", AutoSize = true, Enabled = false };
        var close = new Button { Text = "Selesai", AutoSize = true, DialogResult = DialogResult.OK };
        var help = new Label { AutoSize = true, Text = "Pilih sumber untuk membaca. Edit tersedia untuk RAW text yang disimpan aplikasi. Lepas dokumen tidak menghapus file." };
        void SelectionChanged()
        {
            var path = _list.SelectedItem as string;
            remove.Enabled = path is not null; edit.Enabled = path is not null && IsManaged(path);
            try { _preview.Text = path is not null && File.Exists(path) && new FileInfo(path).Length <= 1_000_000 ? File.ReadAllText(path) : "Dokumen tidak tersedia."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _preview.Text = "Dokumen tidak dapat dibaca."; }
        }
        _list.SelectedIndexChanged += (_, _) => SelectionChanged();
        remove.Click += (_, _) => { if (_list.SelectedIndex >= 0) { _list.Items.RemoveAt(_list.SelectedIndex); SelectionChanged(); } };
        edit.Click += (_, _) =>
        {
            if (_list.SelectedItem is not string path || !IsManaged(path)) return;
            using var editor = new PasteKnowledgeDialog(Path.GetFileNameWithoutExtension(path), _preview.Text, true);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var original = File.ReadAllText(path);
                try { File.WriteAllText(path, editor.RawText, new System.Text.UTF8Encoding(false)); EndpointRag.Core.KnowledgeFiles.Read(Paths); }
                catch { File.WriteAllText(path, original, new System.Text.UTF8Encoding(false)); throw; }
                SelectionChanged();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            { MessageBox.Show(this, "Perubahan belum tersimpan. Periksa akses file atau batas total knowledge (2 juta karakter)."); }
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new(8) };
        footer.Controls.AddRange([edit, remove, close]);
        help.Dock = DockStyle.Top; help.Padding = new(8); help.Height = 50;
        Controls.Add(split); Controls.Add(footer); Controls.Add(help); AcceptButton = close;
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
    private bool IsManaged(string path)
    {
        var full = Path.GetFullPath(path);
        return (Path.GetDirectoryName(full)?.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).Equals(_managedDirectory, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("raw-", StringComparison.OrdinalIgnoreCase)
            && File.Exists(full) && !new FileInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
