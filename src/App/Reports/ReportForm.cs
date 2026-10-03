using System.Diagnostics;
using System.Drawing.Printing;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>Rapor penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record ReportContext(
    ChessResultsService Service,
    int EventTnr,
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    string? Province,
    Func<int, Task<(int MaxRound, TournamentSystem System)>> CategoryInfo,
    AppConfig Config,
    Action SaveConfig,
    string BaseDir,
    string OutputDir);

/// <summary>
/// Turnuva yönergesi / raporu: şablon (gömülü veya kullanıcının .docx'i) analiz edilir, bilinen
/// her şey chess-results'tan doldurulur, eksikler adım adım sorulur; çıktı PDF (birincil),
/// yazıcı veya Word belgesi.
/// </summary>
public sealed class ReportForm : Form
{
    public const string BuiltInResource = "NotasyonOtomasyonu.App.turnuva_yonergesi_sablon.docx";
    private const string BuiltInLabel = "Turnuva Yönergesi (hazır şablon)";

    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly Color MissingBg = Color.FromArgb(253, 236, 234);
    private static readonly Color SuggestedBg = Color.FromArgb(255, 248, 225);

    private readonly ReportContext _ctx;
    private readonly string _reportsDir;

    // chess-results verisi bir kez çekilir; şablon değişince yalnız taslak yeniden kurulur.
    private IReadOnlyDictionary<string, string>? _info;
    private List<(int Round, DateTime? Date, string Time)> _schedule = new();
    private List<(CategoryRef Cat, int Max, TournamentSystem Sys)> _cats = new();

    private ReportDraft? _draft;
    private byte[]? _templateBytes;
    private bool _busy;

    private readonly ComboBox _cboTemplate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly Button _btnUpload = new() { Text = "📂 Kendi şablonumu yükle…", AutoSize = true, Height = 30 };
    private readonly Button _btnRemove = new() { Text = "Kaldır", AutoSize = true, Height = 30 };
    private readonly LinkLabel _lnkExport = new() { Text = "Hazır şablonu Word belgesi olarak al (düzenleyip kendi şablonunuz yapabilirsiniz)", AutoSize = true };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray };

    private readonly DataGridView _gridFields = MakeGrid();
    private readonly DataGridView _gridCats = MakeGrid();
    private readonly DataGridView _gridProgram = MakeGrid();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private readonly Button _btnAsk = new() { Text = "✎ Eksikleri doldur", AutoSize = true, Height = 40 };
    private readonly Button _btnDocx = new() { Text = "Word (.docx) kaydet", AutoSize = true, Height = 40 };
    private readonly Button _btnPrint = new() { Text = "🖨 Yazdır", Width = 120, Height = 40 };
    private readonly Button _btnPdf = new() { Text = "📄 PDF Olarak Kaydet", Width = 220, Height = 40, BackColor = Green, ForeColor = Color.White };

    public ReportForm(ReportContext ctx)
    {
        _ctx = ctx;
        _reportsDir = Path.Combine(ctx.BaseDir, "reports");

        Text = "Turnuva Yönergesi / Raporu — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 720);
        MinimumSize = new Size(820, 560);
        BackColor = Color.FromArgb(250, 250, 247);

        BuildLayout();
        LoadTemplateList();

        _cboTemplate.SelectedIndexChanged += async (_, _) => { if (!_busy) await RebuildAsync(); };
        _btnUpload.Click += async (_, _) => await UploadTemplateAsync();
        _btnRemove.Click += async (_, _) => await RemoveTemplateAsync();
        _lnkExport.LinkClicked += (_, _) => ExportBuiltIn();
        _btnAsk.Click += (_, _) => AskMissing();
        _btnPdf.Click += async (_, _) => await SavePdfAsync();
        _btnPrint.Click += async (_, _) => await PrintAsync();
        _btnDocx.Click += (_, _) => SaveDocx();
        Shown += async (_, _) => await RebuildAsync(askMissing: true);
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 10, 12, 4), WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        top.Controls.Add(new Label { Text = "Şablon:", AutoSize = true, Margin = new Padding(0, 7, 4, 0), Font = new Font(Font, FontStyle.Bold) });
        top.Controls.AddRange(new Control[] { _cboTemplate, _btnUpload, _btnRemove });
        _cboTemplate.Margin = new Padding(0, 3, 8, 0);
        _lnkExport.Margin = new Padding(0, 8, 0, 0);
        top.SetFlowBreak(_btnRemove, true);
        top.Controls.Add(_lnkExport);
        top.SetFlowBreak(_lnkExport, true);
        _lblStatus.Margin = new Padding(0, 6, 0, 0);
        top.Controls.Add(_lblStatus);

        var legend = new Label
        {
            Dock = DockStyle.Top, Height = 26, Padding = new Padding(14, 4, 0, 0), ForeColor = Color.DimGray,
            Text = "✓ chess-results'tan otomatik    • önerilen (kontrol edin)    ✎ eksik — hücreye tıklayıp düzenleyebilirsiniz"
        };

        _tabs.TabPages.Add(Page("Bilgiler", _gridFields));
        _tabs.TabPages.Add(Page("Kategoriler", _gridCats));
        _tabs.TabPages.Add(Page("Program", _gridProgram));
        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        center.Controls.Add(_tabs);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        left.Controls.AddRange(new Control[] { _btnAsk, _btnDocx });
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        right.Controls.AddRange(new Control[] { _btnPrint, _btnPdf });
        bottom.Controls.Add(left);
        bottom.Controls.Add(right);

        foreach (var b in new[] { _btnUpload, _btnRemove, _btnAsk, _btnDocx, _btnPrint, _btnPdf }) Style(b);
        _btnPdf.Font = new Font("Segoe UI", 11f, FontStyle.Bold);

        // Grid kolonları
        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Durum", HeaderText = "", Width = 34, ReadOnly = true });
        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Alan", HeaderText = "Alan", Width = 260, ReadOnly = true });
        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Deger", HeaderText = "Değer", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridFields.Columns["Deger"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _gridFields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _gridFields.CellEndEdit += (_, e) => FieldEdited(e.RowIndex);

        _gridCats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Adi", HeaderText = "Kategori adı", Width = 300 });
        _gridCats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kriter", HeaderText = "Kategori kriteri", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridCats.AllowUserToAddRows = _gridCats.AllowUserToDeleteRows = true;

        _gridProgram.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tarih", HeaderText = "Tarih", Width = 240 });
        _gridProgram.Columns.Add(new DataGridViewTextBoxColumn { Name = "Saat", HeaderText = "Saat", Width = 120 });
        _gridProgram.Columns.Add(new DataGridViewTextBoxColumn { Name = "Etkinlik", HeaderText = "Program", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridProgram.AllowUserToAddRows = _gridProgram.AllowUserToDeleteRows = true;

        Controls.Add(center);
        Controls.Add(legend);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private static TabPage Page(string title, Control content)
    {
        var p = new TabPage(title) { Padding = new Padding(6), BackColor = Color.White };
        content.Dock = DockStyle.Fill;
        p.Controls.Add(content);
        return p;
    }

    private static DataGridView MakeGrid() => new()
    {
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter,
        GridColor = Color.FromArgb(230, 230, 222), ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
    };

    private static void Style(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(4, 0, 4, 0);
        bool primary = b.BackColor == Green;
        if (!primary) b.BackColor = Color.White;
        b.FlatAppearance.BorderColor = primary ? GreenDark : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(134, 168, 100) : Color.FromArgb(236, 241, 231);
    }

    // ================= Şablonlar =================
    private sealed record TemplateItem(string Label, string? Path)
    {
        public override string ToString() => Label;
    }

    private void LoadTemplateList(string? select = null)
    {
        _busy = true;
        _cboTemplate.Items.Clear();
        _cboTemplate.Items.Add(new TemplateItem(BuiltInLabel, null));
        foreach (var name in _ctx.Config.ReportTemplates.ToList())
        {
            var path = Path.Combine(_reportsDir, name);
            if (File.Exists(path)) _cboTemplate.Items.Add(new TemplateItem(Path.GetFileNameWithoutExtension(name), path));
            else _ctx.Config.ReportTemplates.Remove(name);
        }
        int idx = 0;
        for (int i = 0; i < _cboTemplate.Items.Count; i++)
            if (select is not null && ((TemplateItem)_cboTemplate.Items[i]!).Path == select) idx = i;
        _cboTemplate.SelectedIndex = idx;
        _btnRemove.Enabled = idx > 0;
        _busy = false;
    }

    private TemplateItem SelectedTemplate => (TemplateItem)(_cboTemplate.SelectedItem ?? _cboTemplate.Items[0]!);

    public static byte[] BuiltInTemplate()
    {
        using var s = typeof(ReportForm).Assembly.GetManifestResourceStream(BuiltInResource)
                      ?? throw new InvalidOperationException("Gömülü yönerge şablonu bulunamadı.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private async Task UploadTemplateAsync()
    {
        using var dlg = new OpenFileDialog { Title = "Rapor/yönerge şablonu seç", Filter = "Word belgesi (*.docx)|*.docx" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(dlg.FileName);
            var a = DocxTemplate.Analyze(bytes); // bozuk/uyumsuz dosyayı baştan yakala
            Directory.CreateDirectory(_reportsDir);
            var name = Path.GetFileName(dlg.FileName);
            var dest = Path.Combine(_reportsDir, name);
            if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);
            if (!_ctx.Config.ReportTemplates.Contains(name, StringComparer.OrdinalIgnoreCase)) _ctx.Config.ReportTemplates.Add(name);
            _ctx.SaveConfig();
            LoadTemplateList(dest);

            if (a.Fields.Count == 0 && !a.HasCategoryTable && !a.HasProgramTable)
                MessageBox.Show(this,
                    "Bu belgede doldurulabilecek bir yer tanınamadı.\n\n" +
                    "İpucu: değerlerin yazılacağı yerlere {{IL}}, {{YER}}, {{TARIH_ARALIGI}} gibi işaretler koyun " +
                    "ya da \"İLİ | …\", \"YERİ | …\" gibi etiketli tablo hücreleri kullanın. Hazır şablonu " +
                    "örnek olarak alıp düzenleyebilirsiniz.", "Şablon", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RebuildAsync(askMissing: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Şablon okunamadı: " + ex.Message, "Şablon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RemoveTemplateAsync()
    {
        var t = SelectedTemplate;
        if (t.Path is null) return;
        if (MessageBox.Show(this, $"“{t.Label}” şablonu listeden kaldırılsın mı?", "Şablon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _ctx.Config.ReportTemplates.RemoveAll(n => string.Equals(Path.Combine(_reportsDir, n), t.Path, StringComparison.OrdinalIgnoreCase));
        try { File.Delete(t.Path); } catch { /* kullanımda olabilir; listeden çıkması yeter */ }
        _ctx.SaveConfig();
        LoadTemplateList();
        await RebuildAsync();
    }

    private void ExportBuiltIn()
    {
        using var dlg = new SaveFileDialog { Filter = "Word belgesi (*.docx)|*.docx", FileName = "turnuva_yonergesi_sablon.docx" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllBytes(dlg.FileName, BuiltInTemplate());
        OpenWithShell(dlg.FileName);
    }

    // ================= Taslak =================
    private async Task RebuildAsync(bool askMissing = false)
    {
        if (_busy) return;
        _busy = true;
        SetEnabled(false);
        try
        {
            var previous = CollectEdits();
            var tpl = SelectedTemplate;
            _btnRemove.Enabled = tpl.Path is not null;
            _templateBytes = tpl.Path is null ? BuiltInTemplate() : await File.ReadAllBytesAsync(tpl.Path);
            var analysis = DocxTemplate.Analyze(_templateBytes);

            if (_info is null)
            {
                _lblStatus.Text = "Turnuva bilgileri chess-results'tan çekiliyor (tarih, yer, hakemler, tur programı)…";
                _info = await _ctx.Service.GetInfoTableAsync(_ctx.EventTnr);
                try { _schedule = await _ctx.Service.GetScheduleAsync(_ctx.EventTnr); } catch { _schedule = new(); }
                _cats.Clear();
                foreach (var c in _ctx.Categories)
                {
                    _lblStatus.Text = $"Kategori bilgisi: {c.Name}…";
                    try { var (max, sys) = await _ctx.CategoryInfo(c.Tnr); _cats.Add((c, max, sys)); }
                    catch { _cats.Add((c, 0, TournamentSystem.Unknown)); }
                }
            }

            _draft = ReportBuilder.Build(_info, _schedule, _cats, _ctx.EventName, _ctx.Province,
                                         _ctx.Config.ReportAnswers, analysis);
            // Şablon değiştirildiyse kullanıcının girdikleri kaybolmasın.
            foreach (var f in _draft.Fields)
                if (previous.TryGetValue(f.Key, out var v) && v.Length > 0 && f.Source != ReportFieldSource.Automatic)
                { f.Value = v; f.Source = ReportFieldSource.Suggested; }

            FillGrids();
            int missing = _draft.Fields.Count(f => f.Source == ReportFieldSource.Missing && !f.Optional);
            _lblStatus.Text = $"{_draft.Fields.Count(f => f.Source == ReportFieldSource.Automatic)} alan otomatik dolduruldu" +
                              (missing > 0 ? $" • {missing} eksik bilgi var" : " • eksik yok, kaydetmeye hazır") +
                              (_draft.HasCategoryTable ? $" • {_draft.Categories.Count} kategori" : "") +
                              (_draft.HasProgramTable ? $" • {_draft.Program.Count} tur programı" : "");
            if (askMissing && _draft.Fields.Any(f => f.Source == ReportFieldSource.Missing))
            {
                _busy = false;
                BeginInvoke(AskMissing);
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Bilgiler alınamadı: " + (ex is HttpRequestException ? "internet bağlantısını kontrol edin." : ex.Message);
        }
        finally
        {
            _busy = false;
            SetEnabled(true);
        }
    }

    private Dictionary<string, string> CollectEdits()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_draft is null) return d;
        PullGrids();
        foreach (var f in _draft.Fields) d[f.Key] = f.Value;
        return d;
    }

    private void FillGrids()
    {
        if (_draft is null) return;
        _gridFields.Rows.Clear();
        foreach (var f in _draft.Fields)
        {
            int i = _gridFields.Rows.Add(Mark(f.Source), f.Label, f.Value);
            var row = _gridFields.Rows[i];
            row.Tag = f;
            PaintRow(row, f);
        }
        _gridCats.Rows.Clear();
        foreach (var c in _draft.Categories) _gridCats.Rows.Add(c.Name, c.Criteria);
        _gridProgram.Rows.Clear();
        foreach (var p in _draft.Program) _gridProgram.Rows.Add(p.Date, p.Time, p.Event);

        _tabs.TabPages[1].Text = _draft.HasCategoryTable ? $"Kategoriler ({_draft.Categories.Count})" : "Kategoriler (şablonda yok)";
        _tabs.TabPages[2].Text = _draft.HasProgramTable ? $"Program ({_draft.Program.Count})" : "Program (şablonda yok)";
        UpdateAskButton();
    }

    private static string Mark(ReportFieldSource s) => s switch
    {
        ReportFieldSource.Automatic => "✓",
        ReportFieldSource.Suggested => "•",
        _ => "✎"
    };

    private static void PaintRow(DataGridViewRow row, ReportField f)
    {
        var bg = f.Source switch
        {
            ReportFieldSource.Missing when !f.Optional => MissingBg,
            ReportFieldSource.Suggested => SuggestedBg,
            _ => System.Drawing.Color.White
        };
        row.DefaultCellStyle.BackColor = bg;
        row.Cells[0].Style.ForeColor = f.Source == ReportFieldSource.Automatic ? GreenDark : System.Drawing.Color.DarkGoldenrod;
    }

    private void FieldEdited(int rowIndex)
    {
        if (rowIndex < 0 || _gridFields.Rows[rowIndex].Tag is not ReportField f) return;
        var v = _gridFields.Rows[rowIndex].Cells["Deger"].Value?.ToString()?.Trim() ?? "";
        if (v == f.Value) return;
        f.Value = v;
        f.Source = v.Length == 0 ? ReportFieldSource.Missing : ReportFieldSource.Suggested;
        _gridFields.Rows[rowIndex].Cells[0].Value = Mark(f.Source);
        PaintRow(_gridFields.Rows[rowIndex], f);
        UpdateAskButton();
    }

    private void UpdateAskButton()
    {
        int n = _draft?.Fields.Count(f => f.Source == ReportFieldSource.Missing) ?? 0;
        _btnAsk.Text = n > 0 ? $"✎ Eksikleri doldur ({n})" : "✎ Bilgileri gözden geçir";
    }

    private void AskMissing()
    {
        if (_draft is null) return;
        _gridFields.EndEdit();
        PullGrids();
        var list = _draft.Fields.Where(f => f.Source == ReportFieldSource.Missing).ToList();
        if (list.Count == 0) list = _draft.Fields.Where(f => f.Source != ReportFieldSource.Automatic).ToList();
        if (list.Count == 0) return;
        using (var dlg = new MissingFieldsDialog(list)) dlg.ShowDialog(this);
        RememberAnswers();
        FillGrids();
    }

    /// <summary>Izgaralardaki düzenlemeleri taslağa geri yazar.</summary>
    private void PullGrids()
    {
        if (_draft is null) return;
        _gridFields.EndEdit(); _gridCats.EndEdit(); _gridProgram.EndEdit();
        foreach (DataGridViewRow r in _gridFields.Rows)
            if (r.Tag is ReportField f) f.Value = r.Cells["Deger"].Value?.ToString()?.Trim() ?? "";

        _draft.Categories.Clear();
        foreach (DataGridViewRow r in _gridCats.Rows)
        {
            if (r.IsNewRow) continue;
            var name = r.Cells[0].Value?.ToString()?.Trim() ?? "";
            var crit = r.Cells[1].Value?.ToString()?.Trim() ?? "";
            if (name.Length + crit.Length > 0) _draft.Categories.Add(new ReportCategoryRow { Name = name, Criteria = crit });
        }
        _draft.Program.Clear();
        foreach (DataGridViewRow r in _gridProgram.Rows)
        {
            if (r.IsNewRow) continue;
            var row = new ReportProgramRow
            {
                Date = r.Cells[0].Value?.ToString()?.Trim() ?? "",
                Time = r.Cells[1].Value?.ToString()?.Trim() ?? "",
                Event = r.Cells[2].Value?.ToString()?.Trim() ?? ""
            };
            if (row.Date.Length + row.Time.Length + row.Event.Length > 0) _draft.Program.Add(row);
        }
    }

    /// <summary>İletişim/organizasyon gibi turnuvadan turnuvaya aynı kalan cevapları saklar.</summary>
    private void RememberAnswers()
    {
        if (_draft is null) return;
        bool changed = false;
        foreach (var f in _draft.Fields)
        {
            if (!ReportBuilder.Remembered.Contains(f.Key) || f.Source == ReportFieldSource.Automatic) continue;
            var v = f.Value.Trim();
            if (_ctx.Config.ReportAnswers.TryGetValue(f.Key, out var old) && old == v) continue;
            if (v.Length == 0 && !_ctx.Config.ReportAnswers.ContainsKey(f.Key)) continue;
            _ctx.Config.ReportAnswers[f.Key] = v;
            changed = true;
        }
        if (changed) _ctx.SaveConfig();
    }

    // ================= Çıktı =================
    /// <summary>Doldurulmuş belgeyi (eksik varsa önce sorarak) üretir. null = kullanıcı vazgeçti.</summary>
    private byte[]? BuildDocument()
    {
        if (_draft is null || _templateBytes is null) return null;
        PullGrids();
        var missing = _draft.Fields.Where(f => !f.Optional && string.IsNullOrWhiteSpace(f.Value)).ToList();
        if (missing.Count > 0)
        {
            var ans = MessageBox.Show(this,
                $"{missing.Count} bilgi boş: {string.Join(", ", missing.Take(4).Select(m => m.Label))}{(missing.Count > 4 ? "…" : "")}\n\n" +
                "Şimdi doldurmak ister misiniz? (Hayır: boş bırakılarak devam edilir)",
                "Eksik bilgi", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ans == DialogResult.Cancel) return null;
            if (ans == DialogResult.Yes)
            {
                using (var dlg = new MissingFieldsDialog(missing))
                    if (dlg.ShowDialog(this) != DialogResult.OK) { RememberAnswers(); FillGrids(); return null; }
                FillGrids();
            }
        }
        RememberAnswers();
        return DocxTemplate.Fill(_templateBytes, _draft.ToReportData());
    }

    private string DefaultName(string ext)
    {
        var name = _draft?.Fields.FirstOrDefault(f => f.Key == "TURNUVA_ADI")?.Value;
        if (string.IsNullOrWhiteSpace(name)) name = EventGrouping.BaseName(_ctx.EventName);
        var slug = Slug(name);
        var kind = SelectedTemplate.Path is null ? "yonerge" : Slug(SelectedTemplate.Label);
        return $"{(slug.Length == 0 ? "turnuva" : slug)}_{kind}.{ext}";
    }

    private string WriteTempDocx(byte[] bytes)
    {
        Directory.CreateDirectory(_ctx.OutputDir);
        var path = Path.Combine(_ctx.OutputDir, DefaultName("docx"));
        try { File.WriteAllBytes(path, bytes); }
        catch (IOException) // Word'de açık olabilir
        {
            path = Path.Combine(_ctx.OutputDir, Path.GetFileNameWithoutExtension(path) + $"_{DateTime.Now:HHmmss}.docx");
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }

    private async Task SavePdfAsync()
    {
        var bytes = BuildDocument();
        if (bytes is null) return;
        if (!WordExport.IsAvailable)
        {
            var docx = WriteTempDocx(bytes);
            MessageBox.Show(this,
                "PDF'e dönüştürmek için Microsoft Word gerekiyor ve bu bilgisayarda bulunamadı.\n\n" +
                $"Belge Word dosyası olarak kaydedildi:\n{docx}\n\nAçılan programda \"Farklı Kaydet → PDF\" ile PDF alabilirsiniz.",
                "PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenWithShell(docx);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Title = "PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf",
            FileName = DefaultName("pdf"), InitialDirectory = _ctx.OutputDir
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await RunExport("PDF hazırlanıyor (Word arka planda çalışıyor)…", async () =>
        {
            var docx = WriteTempDocx(bytes);
            await WordExport.ToPdfAsync(docx, dlg.FileName);
            _lblStatus.Text = "PDF kaydedildi: " + dlg.FileName;
            OpenWithShell(dlg.FileName);
        });
    }

    private async Task PrintAsync()
    {
        var bytes = BuildDocument();
        if (bytes is null) return;
        if (!WordExport.IsAvailable)
        {
            var docx = WriteTempDocx(bytes);
            try { Process.Start(new ProcessStartInfo(docx) { UseShellExecute = true, Verb = "print" }); }
            catch { OpenWithShell(docx); }
            return;
        }
        using var pd = new PrintDialog { UseEXDialog = true, AllowSomePages = false, PrinterSettings = new PrinterSettings() };
        if (pd.ShowDialog(this) != DialogResult.OK) return;
        var printer = pd.PrinterSettings.PrinterName;
        int copies = pd.PrinterSettings.Copies;
        await RunExport($"“{printer}” yazıcısına gönderiliyor…", async () =>
        {
            var docx = WriteTempDocx(bytes);
            await WordExport.PrintAsync(docx, printer, copies);
            _lblStatus.Text = $"Yazıcıya gönderildi: {printer}";
        });
    }

    private void SaveDocx()
    {
        var bytes = BuildDocument();
        if (bytes is null) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Word belgesi olarak kaydet", Filter = "Word belgesi (*.docx)|*.docx",
            FileName = DefaultName("docx"), InitialDirectory = _ctx.OutputDir
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            _lblStatus.Text = "Kaydedildi: " + dlg.FileName;
            OpenWithShell(dlg.FileName);
        }
        catch (IOException ex) { MessageBox.Show(this, "Kaydedilemedi (dosya açık olabilir): " + ex.Message, "Kaydet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task RunExport(string status, Func<Task> work)
    {
        SetEnabled(false);
        UseWaitCursor = true;
        _lblStatus.Text = status;
        try { await work(); }
        catch (Exception ex)
        {
            _lblStatus.Text = "İşlem tamamlanamadı.";
            MessageBox.Show(this, "Word ile işlem yapılamadı: " + (ex.InnerException?.Message ?? ex.Message) +
                                  "\n\nBelgeyi \"Word (.docx) kaydet\" ile alıp elle dönüştürebilirsiniz.",
                            "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; SetEnabled(true); }
    }

    private void SetEnabled(bool on)
    {
        foreach (var c in new Control[] { _cboTemplate, _btnUpload, _btnAsk, _btnDocx, _btnPrint, _btnPdf })
            c.Enabled = on;
        _btnRemove.Enabled = on && SelectedTemplate.Path is not null;
    }

    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* açılamadıysa yine de kaydedildi */ }
    }

    private static string Slug(string s)
    {
        var f = EventGrouping.Fold(s.Trim());
        var sb = new System.Text.StringBuilder();
        foreach (var c in f)
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var r = sb.ToString().Trim('_');
        return r.Length > 60 ? r[..60].Trim('_') : r;
    }
}
