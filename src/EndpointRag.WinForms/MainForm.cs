using EndpointRag.Core;
using System.ComponentModel;
using System.Text.Json;

namespace EndpointRag.WinForms;

public sealed class MainForm : Form
{
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly TextBox _api = new() { Text = "my-api", Width = 180 };
    private readonly TextBox _domain = new() { Text = "general", Width = 130, MaxLength = 100 };
    private readonly TextBox _assistant = new() { Text = "API Assistant", Width = 220, MaxLength = 120 };
    private readonly TextBox _key = new() { UseSystemPasswordChar = true, Width = 250, MaxLength = 512, PlaceholderText = "Masukkan API key Sumopod di sini" };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 270 };
    private readonly TextBox _filter = new() { Width = 230, PlaceholderText = "Filter route, action, atau label" };
    private readonly ComboBox _operationFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox _skipMapped = new() { Text = "Lewati yang sudah dipetakan AI", Checked = true, AutoSize = true };
    private readonly Label _knowledgeLabel = new() { Text = "Belum ada dokumen knowledge", AutoSize = true, Padding = new(0, 8, 0, 0) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, RowTemplate = { Height = 30 } };
    private readonly TextBox _json = Preview();
    private readonly TextBox _prompt = Preview();
    private readonly TextBox _knowledgePreview = Preview();
    private readonly TextBox _evidence = Preview();
    private readonly TextBox _controllerSummary = Preview();
    private readonly TextBox _log = Preview();
    private readonly ToolStripStatusLabel _status = new("Siap — pilih folder API untuk mulai.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Width = 180 };
    private readonly TableLayoutPanel _config = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new(16, 8, 16, 8), ColumnCount = 1 };
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new(16, 4, 16, 8), WrapContents = true };
    private KnowledgeCatalog _catalog = new();
    private IReadOnlyList<KnowledgeChunk> _knowledge = [];
    private IReadOnlyList<KnowledgeChunk> _embeddedKnowledge = [];
    private string? _projectPath;
    private readonly List<string> _knowledgePaths = [];
    private CancellationTokenSource? _cts;
    private bool _closing;
    private bool _refreshing;
    private bool _readOnlySelection;
    private string? _mappingModel;
    private readonly SettingsStore _settings;
    private readonly System.Windows.Forms.Timer _settingsTimer = new() { Interval = 750 };
    private bool _loadingSettings;
    private string _exportFolder = "";
    private readonly Button _cancel = new() { Text = "Batalkan", AutoSize = true, Enabled = false };
    private readonly Label _workflow = new() { Dock = DockStyle.Top, Height = 62, Padding = new(18, 8, 12, 4), BackColor = Color.FromArgb(230, 240, 255), ForeColor = Color.FromArgb(28, 65, 115) };
    private readonly ToolTip _tips = new() { AutoPopDelay = 10000 };
    private Button _scanButton = null!;
    private Button _aiButton = null!;
    private Button _exportButton = null!;
    private Button _modelsButton = null!;

    public MainForm() : this(new SettingsStore()) { }

    internal MainForm(SettingsStore settings)
    {
        _settings = settings;
        Text = $"C# Endpoint RAG Studio v{Application.ProductVersion.Split('+')[0]} · .NET 10";
        Size = new(1420, 920); MinimumSize = new(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 9); BackColor = Color.FromArgb(245, 247, 250);
        var title = new Label { Text = "Endpoint → Knowledge → Prompt RAG", Font = new("Segoe UI Semibold", 17), Dock = DockStyle.Top, Height = 60, Padding = new(16, 12, 0, 0) };
        var subtitle = new Label { Text = "Summary controller dahulu → action → payload AppResult.Data / DTO → knowledge dan prompt RAG dengan Sumopod AI.",
            Dock = DockStyle.Top, Height = 36, Padding = new(18, 0, 0, 0), ForeColor = Color.FromArgb(75, 85, 100) };

        var folderRow = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Top, AutoSize = true };
        folderRow.ColumnStyles.Add(new(SizeType.Absolute, 100)); folderRow.ColumnStyles.Add(new(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new(SizeType.Absolute, 105));
        folderRow.Controls.Add(Label("Folder API"), 0, 0); folderRow.Controls.Add(_folder, 1, 0);
        folderRow.Controls.Add(Button("Browse…", (_, _) => BrowseSource()), 2, 0); _config.Controls.Add(folderRow);
        _config.Controls.Add(Row(Label("Nama API"), _api, Label("Domain"), _domain, Label("Asisten"), _assistant));
        _modelsButton = Button("Muat model live", async (_, _) => await LoadModels());
        var showKey = new CheckBox { Text = "Lihat key", AutoSize = true };
        showKey.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !showKey.Checked;
        _config.Controls.Add(Row(Label("API key"), _key, showKey, Label("Model"), _model,
            _modelsButton,
            new Label { Text = "Key disimpan terenkripsi Windows • https://ai.sumopod.com", AutoSize = true, Padding = new(6, 8, 0, 0), ForeColor = Color.DimGray }));
        _config.Controls.Add(Row(Button("Tambah knowledge…", async (_, _) => await AddKnowledge()),
            Button("Paste RAW text…", async (_, _) => await PasteKnowledge()),
            Button("Kelola knowledge…", async (_, _) => await ManageKnowledge()),
            Button("Hapus knowledge", (_, _) => { _knowledgePaths.Clear(); _embeddedKnowledge = []; _knowledge = []; foreach (var d in _catalog.Documents) d.RelatedKnowledgeIds = []; UpdateKnowledge(); RefreshPreview(); QueueSettingsSave(); }), _knowledgeLabel));
        _config.Controls.Add(Row(Button("Buka proyek… (Ctrl+O)", async (_, _) => await OpenProject()),
            Button("Simpan proyek… (Ctrl+S)", async (_, _) => await SaveProject()),
            new Label { Text = "Snapshot hasil + knowledge • API key terpisah", AutoSize = true, Padding = new(6, 8, 0, 0) }));
        _scanButton = Button("1. Scan controller", async (_, _) => await Scan());
        _aiButton = Button("2. Petakan AI", async (_, _) => await Enrich());
        _exportButton = Button("3. Ekspor RAG…", async (_, _) => await Export());
        _toolbar.Controls.AddRange([_scanButton, _aiButton, _exportButton]);
        var selectAll = Button("Select All", (_, _) => SelectAll(true));
        var unselectAll = Button("Unselect All", (_, _) => SelectAll(false));
        _tips.SetToolTip(selectAll, "Centang seluruh endpoint, termasuk yang tersembunyi oleh filter.");
        _tips.SetToolTip(unselectAll, "Hapus centang seluruh endpoint, termasuk yang tersembunyi oleh filter.");
        _toolbar.Controls.AddRange([selectAll, unselectAll]);
        _toolbar.Controls.Add(Button("Pilih hanya Read", (_, _) => SelectOnlyRead()));
        _operationFilter.Items.AddRange(["Semua operasi", "Read saja", "Write saja", "Unknown"]);
        _operationFilter.SelectedIndex = 0;
        _toolbar.Controls.Add(_operationFilter);
        _toolbar.Controls.Add(_skipMapped); _toolbar.Controls.Add(_filter);
        _toolbar.Controls.Add(Button("Panduan (F1)", (_, _) => ShowGuide()));
        _tips.SetToolTip(_scanButton, "Pilih folder API terlebih dahulu. Scan lokal tidak memanggil AI atau database.");
        _tips.SetToolTip(_aiButton, "Pilih endpoint dan masukkan key/model. Klik ini untuk mengirim bukti ke Sumopod.");
        _tips.SetToolTip(_exportButton, "Ekspor endpoint terpilih dan seluruh knowledge. Bisa tanpa AI/key.");
        _tips.SetToolTip(_filter, "Cari controller, action, route, atau label. Filter hanya mengubah tampilan.");
        _tips.SetToolTip(_skipMapped, "Matikan untuk menganalisis ulang endpoint yang sudah dipetakan AI.");
        KeyPreview = true;
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F1) { e.Handled = true; ShowGuide(); }
            else if (_cts is null && e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; await SaveProject(); }
            else if (_cts is null && e.Control && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; await OpenProject(); }
            else if (_cts is null && e.Control && e.KeyCode == Keys.V && !(_folder.Focused || _api.Focused || _domain.Focused || _assistant.Focused || _key.Focused || _model.Focused || _filter.Focused))
            { e.Handled = true; await PasteKnowledge(true); }
        };
        _cancel.Click += (_, _) => _cts?.Cancel();

        AddColumn("Selected", "Pilih", 45, true); AddColumn("Method", "HTTP", 65);
        AddColumn("Path", "Route", 260);
        AddColumn("Description", "Deskripsi", 320);
        AddColumn("Parameters", "Parameter endpoint", 320);
        AddColumn("Resolution", "Resolusi tipe", 180);
        AddColumn("Action", "Action", 180);
        _grid.ShowCellToolTips = true;
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                e.ToolTipText = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
        };
        AddColumn("Operation", "Operasi", 80); AddColumn("Status", "Mapping", 100); AddColumn("Exposure", "Exposure", 85);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        AddTab(tabs, "Summary controller", _controllerSummary);
        AddTab(tabs, "Knowledge JSON", _json); AddTab(tabs, "System prompt RAG", _prompt);
        AddTab(tabs, "Knowledge RAG", _knowledgePreview); AddTab(tabs, "Bukti endpoint", _evidence); AddTab(tabs, "Proses", _log);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Padding = new(12, 0, 12, 10) };
        split.Panel1.Controls.Add(_grid); split.Panel2.Controls.Add(tabs);
        Shown += (_, _) => { split.SplitterDistance = Math.Max(500, split.Width * 55 / 100); };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new(16, 4, 0, 4) };
        footer.Controls.Add(_cancel);
        footer.Controls.Add(new Label { AutoSize = true, Padding = new(8, 8, 0, 0), Text = "Petakan AI mengirim bukti source tersanitasi dan kandidat knowledge terpilih ke Sumopod; dikenakan biaya provider." });
        var statusBar = new StatusStrip(); statusBar.Items.Add(_status); statusBar.Items.Add(_progress);
        Controls.Add(split); Controls.Add(footer); Controls.Add(statusBar); Controls.Add(_toolbar); Controls.Add(_config); Controls.Add(_workflow); Controls.Add(subtitle); Controls.Add(title);
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != "Operation") return;
            e.CellStyle!.BackColor = e.Value?.ToString() switch { "read" => Color.FromArgb(222, 245, 229), "write" => Color.FromArgb(255, 226, 224), _ => Color.FromArgb(255, 244, 206) };
            e.CellStyle.ForeColor = Color.Black;
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += (_, _) => { if (!_refreshing) RefreshPreview(); };
        _grid.SelectionChanged += (_, _) => ShowEvidence();
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || e.Modifiers != Keys.None) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (_cts is not null || _grid.ReadOnly || _grid.CurrentRow?.DataBoundItem is not EndpointRow row) return;
            _grid.EndEdit();
            row.Selected = !row.Selected;
            _grid.InvalidateRow(_grid.CurrentRow.Index);
            RefreshPreview();
        };
        _tips.SetToolTip(_grid, "Pilih baris lalu tekan Space untuk centang/lepas centang endpoint.");
        _filter.TextChanged += (_, _) => RefreshGrid();
        _operationFilter.SelectedIndexChanged += (_, _) => RefreshGrid();
        _domain.TextChanged += (_, _) => RefreshPreview(); _assistant.TextChanged += (_, _) => RefreshPreview();
        FormClosing += (_, e) =>
        {
            if (_cts is not null) { _closing = true; _cts.Cancel(); e.Cancel = true; }
            else { _settingsTimer.Stop(); SaveSettings(); _loadingSettings = true; _key.Clear(); }
        };
        FormClosed += (_, _) => { _settingsTimer.Dispose(); _tips.Dispose(); };
        LoadSeedModels(); RestoreSettings(); RefreshPreview();
        _settingsTimer.Tick += (_, _) => { _settingsTimer.Stop(); SaveSettings(); };
        foreach (var input in new Control[] { _folder, _api, _domain, _assistant, _key, _model }) input.TextChanged += (_, _) => { QueueSettingsSave(); UpdateWorkflow(); };
        _operationFilter.SelectedIndexChanged += (_, _) => QueueSettingsSave();
        _skipMapped.CheckedChanged += (_, _) => { QueueSettingsSave(); UpdateWorkflow(); };
    }

    private void LoadSeedModels()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("EndpointRag.WinForms.models.txt");
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            _model.Items.AddRange(reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(SumopodAi.IsChatModel).Cast<object>().ToArray());
        }
        _model.Text = "gpt-4.1-mini";
    }

    private void BrowseSource()
    {
        using var picker = new FolderBrowserDialog { Description = "Pilih folder source API (controller, service, dan DTO)", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _folder.Text = picker.SelectedPath; _api.Text = Path.GetFileName(picker.SelectedPath.TrimEnd(Path.DirectorySeparatorChar));
    }

    private void QueueSettingsSave()
    {
        if (_loadingSettings) return;
        _settingsTimer.Stop(); _settingsTimer.Start();
    }
    private void SaveSettings()
    {
        try
        {
            _settings.Save(new AppSettings { FolderApi = _folder.Text, ApiName = _api.Text, Domain = _domain.Text,
                AssistantName = _assistant.Text, ApiKey = _key.Text, Model = _model.Text,
                OperationFilter = _operationFilter.SelectedIndex, SkipMapped = _skipMapped.Checked,
                ReadOnlySelection = _readOnlySelection, ExportFolder = _exportFolder, KnowledgeFiles = _knowledgePaths.ToArray() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { Log("Setting belum tersimpan. Periksa izin folder setting Windows."); }
    }
    private void RestoreSettings()
    {
        _loadingSettings = true;
        try
        {
            var saved = _settings.Load(out var warning);
            _folder.Text = saved.FolderApi; _api.Text = saved.ApiName; _domain.Text = saved.Domain;
            _assistant.Text = saved.AssistantName; _key.Text = saved.ApiKey; _model.Text = saved.Model;
            _operationFilter.SelectedIndex = saved.OperationFilter; _skipMapped.Checked = saved.SkipMapped;
            _readOnlySelection = saved.ReadOnlySelection; _exportFolder = saved.ExportFolder;
            if (warning is not null) Log(warning);
            var files = saved.KnowledgeFiles.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToArray();
            if (files.Length > 0)
            {
                try { _knowledge = KnowledgeFiles.Read(files); _knowledgePaths.AddRange(files); UpdateKnowledge(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { Log("Dokumen knowledge terakhir tidak dapat dipulihkan; tambahkan kembali melalui tombol knowledge."); }
            }
            if (files.Length != saved.KnowledgeFiles.Length) Log("Sebagian file knowledge terakhir tidak ditemukan.");
        }
        finally { _loadingSettings = false; }
    }

    private async Task Scan()
    {
        var folder = _folder.Text.Trim(); var api = _api.Text.Trim();
        await Run(async token =>
        {
            var progress = new Progress<ScanProgress>(p => SetProgress(p.Completed, p.Total, p.Message));
            var result = await Task.Run(() => new ControllerScanner().Scan(folder, api, progress, token), token);
            _catalog = result.Catalog; _mappingModel = null;
            if (_readOnlySelection) foreach (var doc in _catalog.Documents) doc.Selected = doc.Operation == "read";
            _controllerSummary.Text = string.Join("\r\n\r\n", result.Controllers.Select(c =>
                $"{c.Controller}\r\nSummary: {(c.Summary.Length > 0 ? c.Summary : "[XML summary tidak tersedia]")}\r\nSource: {c.File}:{c.Line}\r\nEndpoints: {c.EndpointCount}" +
                $" • Read: {_catalog.Documents.Count(d => d.Endpoint.Controller == c.Controller && d.Operation == "read")} • Write: {_catalog.Documents.Count(d => d.Endpoint.Controller == c.Controller && d.Operation == "write")} • Unknown: {_catalog.Documents.Count(d => d.Endpoint.Controller == c.Controller && d.Operation == "unknown")}"));
            RefreshGrid(); RefreshPreview();
            Log($"Scan: {result.SourceFileCount} file, {_catalog.DocumentCount} endpoint.");
            Log($"Operasi saat scan: {_catalog.Documents.Count(d => d.Operation == "read")} Read, {_catalog.Documents.Count(d => d.Operation == "write")} Write, {_catalog.Documents.Count(d => d.Operation == "unknown")} Unknown.");
            foreach (var warning in result.Warnings) Log(warning);
            if (_catalog.DocumentCount == 0) Log("Tidak ada controller dengan atribut HTTP. Pilih folder yang mencakup source API; minimal API belum didukung.");
        });
    }

    private async Task LoadModels() => await Run(async token =>
    {
        using var ai = new SumopodAi(_key.Text);
        var models = await ai.GetModelsAsync(token);
        if (models.Length == 0) throw new InvalidDataException("Tidak ada model chat dalam respons provider.");
        var selected = _model.Text; _model.Items.Clear(); _model.Items.AddRange(models.Cast<object>().ToArray());
        _model.Text = models.Contains(selected) ? selected : models[0];
        Log($"Katalog live: {models.Length} model chat. Ketersediaan inference tetap bergantung provider/key.");
    });

    private async Task ManageKnowledge()
    {
        if (_knowledgePaths.Count == 0) { MessageBox.Show(this, _embeddedKnowledge.Count > 0 ? "Knowledge proyek tersimpan di dalam snapshot dan dapat dibaca pada tab Knowledge RAG. Tambahkan file/RAW baru untuk melengkapi, atau Hapus knowledge untuk mengosongkan semuanya." : "Tambahkan file knowledge atau paste RAW text terlebih dahulu."); return; }
        using var manager = new KnowledgeManagerDialog(_knowledgePaths, Path.Combine(Path.GetDirectoryName(_settings.FilePath)!, "knowledge"));
        manager.ShowDialog(this);
        var paths = manager.Paths.ToArray();
        await Run(async token =>
        {
            var chunks = await Task.Run(() => KnowledgeFiles.Read(paths), token);
            _knowledgePaths.Clear(); _knowledgePaths.AddRange(paths); _knowledge = MergeKnowledge(chunks);
            var ids = _knowledge.Select(k => k.Id).ToHashSet();
            foreach (var d in _catalog.Documents) d.RelatedKnowledgeIds = d.RelatedKnowledgeIds.Where(ids.Contains).ToArray();
            UpdateKnowledge(); RefreshPreview(); Log("Knowledge diperbarui; tautan chunk yang sudah tidak berlaku dilepas.");
        });
    }

    private async Task PasteKnowledge(bool fromClipboard = false)
    {
        var pasted = "";
        if (fromClipboard)
        {
            try { if (Clipboard.ContainsText()) pasted = Clipboard.GetText(); }
            catch (System.Runtime.InteropServices.ExternalException) { Log("Clipboard sedang dipakai; paste manual dengan Ctrl+V di editor."); }
        }
        using var dialog = new PasteKnowledgeDialog(text: pasted);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var directory = Path.Combine(Path.GetDirectoryName(_settings.FilePath)!, "knowledge");
        var slug = System.Text.RegularExpressions.Regex.Replace(dialog.SourceName, @"[^\p{L}\p{N}_-]+", "-").Trim('-');
        if (slug.Length == 0) slug = "dokumen";
        var path = Path.Combine(directory, "raw-" + slug + "-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        var text = dialog.RawText;
        await Run(async token =>
        {
            Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(false), token);
                var paths = _knowledgePaths.Append(path).ToArray();
                var result = await Task.Run(() => KnowledgeFiles.Read(paths), token);
                _knowledgePaths.Add(path); _knowledge = MergeKnowledge(result);
                UpdateKnowledge(); RefreshPreview();
                Log($"RAW text ditambahkan: {_knowledge.Count} total chunk. Belum dikirim ke AI.");
            }
            catch
            {
                if (!_knowledgePaths.Contains(path) && File.Exists(path)) File.Delete(path);
                throw;
            }
        });
    }

    private async Task AddKnowledge()
    {
        using var picker = new OpenFileDialog { Filter = "Knowledge teks (*.md;*.txt)|*.md;*.txt", Multiselect = true, Title = "Tambahkan aturan/domain knowledge" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var paths = _knowledgePaths.Concat(picker.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await Run(async token =>
        {
            var result = await Task.Run(() => KnowledgeFiles.Read(paths), token);
            _knowledgePaths.Clear(); _knowledgePaths.AddRange(paths); _knowledge = MergeKnowledge(result);
            // A changed knowledge set invalidates earlier inferred relationships, without inventing new ones.
            var ids = _knowledge.Select(k => k.Id).ToHashSet();
            foreach (var d in _catalog.Documents) d.RelatedKnowledgeIds = d.RelatedKnowledgeIds.Where(ids.Contains).ToArray();
            UpdateKnowledge(); RefreshPreview(); Log($"Knowledge: {paths.Length} file, {result.Count} chunk.");
        });
    }

    private IReadOnlyList<KnowledgeChunk> MergeKnowledge(IReadOnlyList<KnowledgeChunk> files) =>
        files.Concat(_embeddedKnowledge).DistinctBy(k => k.Id).ToArray();

    private async Task SaveProject()
    {
        _grid.EndEdit();
        using var picker = new SaveFileDialog { Filter = "Proyek RAG (*.ragproject.json)|*.ragproject.json", DefaultExt = "ragproject.json",
            FileName = _projectPath ?? "project.ragproject.json", Title = "Simpan snapshot proyek RAG" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var project = new RagProject { Catalog = _catalog, Knowledge = _knowledge.ToList(), ControllerOverview = _controllerSummary.Text,
            MappingModel = _mappingModel, Options = new ProjectOptions { FolderApi = _folder.Text, ApiName = _api.Text,
                Domain = _domain.Text, AssistantName = _assistant.Text, Model = _model.Text, OperationFilter = _operationFilter.SelectedIndex,
                SkipMapped = _skipMapped.Checked, ReadOnlySelection = _readOnlySelection } };
        await Run(async token =>
        {
            await RagProject.SaveAsync(picker.FileName, project, token);
            _projectPath = picker.FileName;
            Log("Proyek tersimpan: " + _projectPath);
            MessageBox.Show(this, "Proyek berhasil disimpan. Hasil scan, mapping, pilihan endpoint, dan knowledge dapat dibuka kembali. API key disimpan terpisah.", "Proyek tersimpan");
        });
    }

    private async Task OpenProject()
    {
        using var picker = new OpenFileDialog { Filter = "Proyek RAG (*.ragproject.json)|*.ragproject.json", Title = "Buka snapshot proyek RAG" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if ((_catalog.DocumentCount > 0 || _knowledge.Count > 0) && MessageBox.Show(this,
            "Buka proyek akan mengganti pekerjaan yang sedang tampil. Simpan proyek dahulu jika diperlukan. Lanjutkan?",
            "Buka proyek", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await Run(async token =>
        {
            var project = await RagProject.LoadAsync(picker.FileName, token);
            _refreshing = true;
            try
            {
                _catalog = project.Catalog; _embeddedKnowledge = project.Knowledge; _knowledge = _embeddedKnowledge;
                _knowledgePaths.Clear(); _mappingModel = project.MappingModel; _projectPath = picker.FileName;
                _controllerSummary.Text = project.ControllerOverview;
                var o = project.Options;
                _folder.Text = o.FolderApi; _api.Text = o.ApiName; _domain.Text = o.Domain;
                _assistant.Text = o.AssistantName; _model.Text = o.Model;
                _operationFilter.SelectedIndex = Math.Clamp(o.OperationFilter, 0, 3);
                _skipMapped.Checked = o.SkipMapped; _readOnlySelection = o.ReadOnlySelection;
            }
            finally { _refreshing = false; }
            UpdateKnowledge(); RefreshGrid(); RefreshPreview();
            Log("Proyek dibuka: " + _projectPath + ". Snapshot lokal; tidak melakukan scan atau memanggil AI otomatis.");
        });
    }

    private async Task Enrich()
    {
        _grid.EndEdit();
        var docs = _catalog.Documents.Where(d => d.Selected && (!_skipMapped.Checked || d.MappingStatus != "ai-inferred")).ToArray();
        if (docs.Length == 0) { MessageBox.Show(this, "Pilih endpoint yang belum dipetakan, atau matikan opsi lewati mapping AI."); return; }
        var model = _model.Text.Trim(); var domain = _domain.Text.Trim();
        await Run(async token =>
        {
            using var ai = new SumopodAi(_key.Text);
            _mappingModel = model;
            for (var i = 0; i < docs.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var doc = docs[i];
                SetProgress(i, docs.Length, $"AI {i + 1}/{docs.Length}: {doc.Endpoint.Action}");
                var mapping = await ai.MapAsync(doc, model, domain, KnowledgeFiles.Candidates(doc, _knowledge), token);
                SumopodAi.Apply(doc, mapping);
                if (_readOnlySelection && doc.Operation != "read") doc.Selected = false;
                doc.MappingModel = model;
                Log($"Mapped: {doc.Endpoint.Method} {doc.Endpoint.Path} → {doc.Operation}, {doc.Intents.Length} intent.");
                foreach (var warning in doc.MappingWarnings) Log($"Review {doc.Endpoint.Action}: {warning}");
                RefreshGrid(); RefreshPreview();
            }
            SetProgress(docs.Length, docs.Length, "Pemetaan AI selesai");
        });
    }

    private async Task Export()
    {
        _grid.EndEdit();
        if (!_catalog.Documents.Any(d => d.Selected) && _knowledge.Count == 0) { MessageBox.Show(this, "Scan dan pilih endpoint, atau tambahkan dokumen knowledge dahulu."); return; }
        using var picker = new FolderBrowserDialog { Description = "Pilih folder tujuan; aplikasi membuat subfolder ekspor baru", UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_exportFolder) ? _exportFolder : "" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _exportFolder = picker.SelectedPath; SaveSettings();
        var bundle = BuildBundle();
        await Run(async token =>
        {
            var path = await RagExporter.ExportAsync(picker.SelectedPath, bundle, token);
            Log("Ekspor selesai: " + path);
            var openFolder = new TaskDialogButton("Buka folder");
            var selectZip = new TaskDialogButton("Pilih file ZIP");
            var done = new TaskDialogButton("Selesai");
            var result = TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "Ekspor RAG selesai",
                Heading = "Hasil ekspor berhasil disimpan",
                Text = $"Folder hasil dan paket ZIP siap digunakan:\n\n{path}\n\nPaket: rag-package.zip\nPanduan: README-RAG.md",
                Icon = TaskDialogIcon.Information,
                Buttons = { openFolder, selectZip, done },
                AllowCancel = true
            });
            if (result == openFolder || result == selectZip)
            {
                try
                {
                    if (result == selectZip)
                    {
                        var explorer = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                        explorer.ArgumentList.Add("/select,"); explorer.ArgumentList.Add(Path.Combine(path, "rag-package.zip"));
                        System.Diagnostics.Process.Start(explorer);
                    }
                    else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
                {
                    Log("Ekspor berhasil, tetapi folder tidak dapat dibuka otomatis.");
                    MessageBox.Show(this, $"Ekspor tetap berhasil. Buka folder secara manual:\n{path}", "Folder ekspor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        });
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_cts is not null) return;
        _settingsTimer.Stop(); SaveSettings();
        using var cts = new CancellationTokenSource(); _cts = cts;
        _config.Enabled = false; _toolbar.Enabled = false; _grid.ReadOnly = true; _cancel.Enabled = true;
        _progress.Style = ProgressBarStyle.Marquee; _status.Text = "Memproses…"; UpdateWorkflow();
        try { await action(cts.Token); _status.Text = "Selesai — hasil dapat ditinjau pada tab preview."; }
        catch (OperationCanceledException) { Log("Dibatalkan. Mapping yang sudah selesai tetap tersedia."); _status.Text = "Dibatalkan"; }
        catch (Exception ex)
        {
            var message = ex is ArgumentException or DirectoryNotFoundException or InvalidDataException or InvalidOperationException ? ex.Message :
                ex is HttpRequestException ? "Koneksi Sumopod gagal. Periksa jaringan/TLS; tidak ada retry otomatis." :
                ex is IOException or UnauthorizedAccessException ? "Gagal membaca/menulis file. Periksa path, akses, dan ruang disk." : "Operasi gagal; periksa folder source dan input.";
            // Provider response/error bodies and API keys are never shown or logged.
            if (_key.Text.Length > 0) message = message.Replace(_key.Text, "[REDACTED]", StringComparison.Ordinal);
            Log(message); _status.Text = "Gagal — hasil yang sudah selesai tetap tersedia.";
            MessageBox.Show(this, message, "Operasi gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts = null; _config.Enabled = true; _toolbar.Enabled = true; _grid.ReadOnly = false; _cancel.Enabled = false;
            SaveSettings();
            foreach (DataGridViewColumn column in _grid.Columns) column.ReadOnly = column.DataPropertyName != "Selected";
            _progress.Style = ProgressBarStyle.Blocks; RefreshPreview();
            if (_closing) Close();
        }
    }

    private void SetProgress(int current, int total, string message)
    {
        _status.Text = message;
        if (total > 0) { _progress.Style = ProgressBarStyle.Blocks; _progress.Maximum = total; _progress.Value = Math.Clamp(current, 0, total); }
    }
    private void SelectAll(bool selected)
    {
        _readOnlySelection = false;
        QueueSettingsSave();
        _grid.EndEdit();
        foreach (var doc in _catalog.Documents) doc.Selected = selected;
        _grid.Refresh(); RefreshPreview();
    }
    private void SelectOnlyRead()
    {
        _readOnlySelection = true;
        QueueSettingsSave();
        foreach (var doc in _catalog.Documents) doc.Selected = doc.Operation == "read";
        _operationFilter.SelectedIndex = 1; RefreshGrid(); RefreshPreview();
    }
    private IEnumerable<EndpointRow> VisibleRows() => _grid.DataSource as BindingList<EndpointRow> ?? [];
    private void RefreshGrid()
    {
        var selectedId = (_grid.CurrentRow?.DataBoundItem as EndpointRow)?.Document.Id;
        _refreshing = true;
        try
        {
            var filter = _filter.Text.Trim();
            var operation = _operationFilter.SelectedIndex switch { 1 => "read", 2 => "write", 3 => "unknown", _ => null };
            var rows = _catalog.Documents.Where(d => (operation is null || d.Operation == operation)
                && (filter.Length == 0 || (d.Label + " " + d.Endpoint.Path + " " + d.Endpoint.Action).Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .Select(d => new EndpointRow(d)).ToList();
            _grid.DataSource = new BindingList<EndpointRow>(rows);
            var index = rows.FindIndex(r => r.Document.Id == selectedId);
            if (index >= 0) _grid.CurrentCell = _grid.Rows[index].Cells[1];
        }
        finally { _refreshing = false; }
        ShowEvidence();
    }
    private RagBundle BuildBundle() => RagExporter.Build(_catalog, _knowledge, new(_assistant.Text.Trim(), _domain.Text.Trim()), _mappingModel);
    private void RefreshPreview()
    {
        if (_refreshing) return;
        var bundle = BuildBundle(); _json.Text = bundle.KnowledgeJson; _prompt.Text = bundle.SystemPrompt; _knowledgePreview.Text = bundle.KnowledgeMarkdown;
        UpdateWorkflow();
    }
    private void UpdateWorkflow()
    {
        var busy = _cts is not null;
        var selected = _catalog.Documents.Count(d => d.Selected);
        var mapped = _catalog.Documents.Count(d => d.Selected && d.MappingStatus == "ai-inferred");
        var pending = _catalog.Documents.Count(d => d.Selected && (!_skipMapped.Checked || d.MappingStatus != "ai-inferred"));
        _scanButton.Enabled = !busy && Directory.Exists(_folder.Text.Trim()) && !string.IsNullOrWhiteSpace(_api.Text);
        _aiButton.Enabled = !busy && pending > 0 && !string.IsNullOrWhiteSpace(_key.Text) && SumopodAi.IsChatModel(_model.Text.Trim());
        _exportButton.Enabled = !busy && (selected > 0 || _knowledge.Count > 0);
        _modelsButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_key.Text);
        var next = busy ? "Sedang diproses — gunakan Batalkan bila diperlukan." :
            selected == 0 && _knowledge.Count == 0 ? "Mulai: pilih folder API lalu Scan, atau tambahkan dokumen/Paste RAW text." :
            pending > 0 && string.IsNullOrWhiteSpace(_key.Text) ? "Siap ditinjau/ekspor. Untuk analisis AI, isi API key dan pilih model." :
            pending > 0 ? "Berikutnya: Petakan AI, lalu tinjau hasil sebelum Ekspor RAG." : "Berikutnya: tinjau knowledge dan prompt, lalu Ekspor RAG.";
        _workflow.Text = $"{next}\n{_catalog.DocumentCount} endpoint • {selected} dipilih • {mapped} hasil AI • {_knowledgePaths.Count} dokumen / {_knowledge.Count} chunks";
    }
    private void ShowGuide() => MessageBox.Show(this,
        "API controller: Browse folder → Scan → Pilih hanya Read bila diperlukan → isi key/model → Petakan AI → tinjau preview → Ekspor.\n\n" +
        "Dokumen bisnis: Tambah knowledge atau Paste RAW text → Kelola knowledge untuk membaca/edit RAW → tinjau Knowledge RAG → Ekspor (tidak memerlukan key).\n\n" +
        "Filter hanya mengubah tampilan; checkbox menentukan endpoint yang diekspor. Semua dokumen knowledge ikut ekspor.\n\n" +
        "Simpan proyek (Ctrl+S) menyimpan snapshot hasil + knowledge. Buka proyek (Ctrl+O) melanjutkan snapshot tanpa scan/AI otomatis. API key terpisah dari file proyek.\n\n" +
        "Klik baris untuk melihat bukti. Key dan setting disimpan otomatis. Paste/file knowledge tidak otomatis dikirim ke provider.", "Panduan cepat");
    private void ShowEvidence()
    {
        if (_grid.CurrentRow?.DataBoundItem is not EndpointRow row) { _evidence.Clear(); return; }
        _evidence.Text = $"{row.Document.Label}\r\n{row.Document.Endpoint.Method} {row.Document.Endpoint.Path}\r\nOperasi: {row.Document.Operation} • Scope: {row.Document.Scope}\r\n\r\nFungsi: {row.Document.Job.Description}\r\n\r\n" + RagJson.Serialize(new { endpoint = row.Document.Endpoint, evidence = row.Document.Evidence,
            row.Document.MappingStatus, row.Document.StaticOperation, row.Document.OperationEvidence, row.Document.MappingWarnings, row.Document.ExampleQuestions, row.Document.RelatedKnowledgeIds })
            + "\r\n\r\nAI source context (literal values removed):\r\n" + row.Document.Evidence.SafeContext;
    }
    private void UpdateKnowledge() => _knowledgeLabel.Text = $"{_knowledgePaths.Count} file • {_embeddedKnowledge.Count} chunk proyek • {_knowledge.Count} total chunk";
    private void Log(string message)
    {
        if (_log.TextLength > 100_000) _log.Clear();
        _log.AppendText($"[{DateTimeOffset.Now:HH:mm:ss}] {message}\r\n");
    }
    private void AddColumn(string property, string title, int width, bool check = false)
    {
        DataGridViewColumn column = check ? new DataGridViewCheckBoxColumn() : new DataGridViewTextBoxColumn();
        column.DataPropertyName = property; column.HeaderText = title; column.Width = width; column.ReadOnly = !check;
        column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.Columns.Add(column);
    }
    private static TextBox Preview() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new("Consolas", 9), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Padding = new(4, 8, 4, 0) };
    private static Button Button(string text, EventHandler handler) { var button = new Button { Text = text, AutoSize = true, Padding = new(6, 3, 6, 3) }; button.Click += handler; return button; }
    private static FlowLayoutPanel Row(params Control[] controls) { var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true }; row.Controls.AddRange(controls); return row; }
    private static void AddTab(TabControl tabs, string title, Control content) { var tab = new TabPage(title) { Padding = new(4) }; tab.Controls.Add(content); tabs.TabPages.Add(tab); }

    private sealed class EndpointRow(EndpointDocument doc)
    {
        [Browsable(false)] public EndpointDocument Document => doc;
        public bool Selected { get => doc.Selected; set => doc.Selected = value; }
        public string Method => doc.Endpoint.Method;
        public string Path => doc.Endpoint.Path;
        public string Action => doc.Endpoint.Action;
        public string Description => !string.IsNullOrWhiteSpace(doc.ActionSummary) ? doc.ActionSummary
            : !string.IsNullOrWhiteSpace(doc.Job.Description) ? doc.Job.Description
            : !string.IsNullOrWhiteSpace(doc.ControllerSummary) ? doc.ControllerSummary : "Belum tersedia";
        public string Parameters => doc.Request.Count == 0 ? "Tidak ada parameter pada signature"
            : string.Join("; ", doc.Request.Select(p => $"{p.Name}: {p.Type} [{p.Binding}]" + (p.Nullable ? " (nullable/default)" : "")));
        public string Operation => doc.Operation;
        public string Resolution
        {
            get
            {
                var reasons = new List<string>();
                if (doc.Quality.MissingSchemaIds.Length > 0) reasons.Add("Schema belum lengkap: " + string.Join(", ", doc.Quality.MissingSchemaIds));
                if (doc.Quality.UnresolvedSchemaIds.Length > 0) reasons.Add("Binding perlu review: " + string.Join(", ", doc.Quality.UnresolvedSchemaIds));
                if (doc.Quality.DynamicSchemaIds.Length > 0) reasons.Add("Payload Data belum teridentifikasi: " + string.Join(", ", doc.Quality.DynamicSchemaIds));
                return reasons.Count == 0 ? "Tipe teridentifikasi" : string.Join("; ", reasons);
            }
        }
        public string Status => doc.MappingStatus;
        public string Exposure => doc.AssistantExposure;
    }
}
