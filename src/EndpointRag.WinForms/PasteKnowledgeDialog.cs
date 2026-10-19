using System.Text;

namespace EndpointRag.WinForms;

internal sealed class PasteKnowledgeDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, MaxLength = 100, PlaceholderText = "Contoh: Aturan cuti perusahaan" };
    private readonly TextBox _raw = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
        AcceptsReturn = true, AcceptsTab = true, MaxLength = 1_000_000, Font = new("Segoe UI", 10) };
    public string SourceName => _name.Text.Trim();
    public string RawText => _raw.Text;

    public PasteKnowledgeDialog(string name = "", string text = "", bool editing = false)
    {
        Text = editing ? "Edit dokumen RAW text" : "Paste dokumen / deskripsi bisnis";
        _name.Text = name; _name.ReadOnly = editing; _raw.Text = text;
        Size = new(850, 680); MinimumSize = new(600, 450); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Nama sumber / dokumen", AutoSize = true, Margin = new(0, 0, 0, 6) }, 0, 0);
        layout.Controls.Add(_name, 0, 1); layout.Controls.Add(_raw, 0, 2);
        var info = new Label { AutoSize = true, MaximumSize = new(780, 0) };
        layout.Controls.Add(info, 0, 3);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var add = new Button { Text = editing ? "Simpan perubahan" : "Tambahkan ke knowledge", AutoSize = true };
        var paste = new Button { Text = "Paste clipboard", AutoSize = true };
        paste.Click += (_, _) =>
        {
            try { if (Clipboard.ContainsText()) _raw.SelectedText = Clipboard.GetText(); else info.Text = "Clipboard belum berisi teks."; }
            catch (System.Runtime.InteropServices.ExternalException) { info.Text = "Clipboard sedang dipakai aplikasi lain. Coba Ctrl+V."; }
        };
        void UpdateState()
        {
            var bytes = Encoding.UTF8.GetByteCount(RawText);
            add.Enabled = SourceName.Length > 0 && !string.IsNullOrWhiteSpace(RawText) && bytes <= 1_000_000;
            info.Text = $"{RawText.Length:N0} karakter • {bytes / 1024.0:N1} KB / maks. 1 MB\n" +
                (bytes > 1_000_000 ? "Teks terlalu besar; pecah menjadi beberapa dokumen." : "Isi nama dan teks, lalu tambahkan. Tersimpan lokal; AI dipanggil terpisah.");
            info.ForeColor = bytes > 1_000_000 ? Color.Firebrick : Color.DimGray;
        }
        _raw.TextChanged += (_, _) => UpdateState(); _name.TextChanged += (_, _) => UpdateState(); UpdateState();
        var cancel = new Button { Text = "Batal", AutoSize = true, DialogResult = DialogResult.Cancel };
        add.Click += (_, _) =>
        {
            if (SourceName.Length == 0 || string.IsNullOrWhiteSpace(RawText))
            { MessageBox.Show(this, "Isi nama sumber dan teks dokumen terlebih dahulu."); return; }
            if (Encoding.UTF8.GetByteCount(RawText) > 1_000_000)
            { MessageBox.Show(this, "Teks maksimal 1 MB. Pecah dokumen menjadi beberapa input."); return; }
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(add); buttons.Controls.Add(cancel); buttons.Controls.Add(paste); layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout); CancelButton = cancel;
        // Enter in the text editor adds a line; adding knowledge requires the explicit button.
    }
}
