using NotasyonOtomasyonu.App.Overlay;
using System.Text.Json;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.Parsers;

namespace NotasyonOtomasyonu.App.Athletes;

/// <summary>Sporcu kartları penceresinin ana pencereden aldığı bağlam (turnuva seçilmemişse liste Excel'den alınır).</summary>
public sealed record AthletesContext(
    string EventName,
    int? EventTnr,
    IReadOnlyList<CategoryRef> Categories,
    Func<int, CancellationToken, Task<IReadOnlyList<Player>>>? LoadStartingList,
    AppConfig Config,
    Action SaveConfig,
    string BaseDir,
    string OutputDir);

/// <summary>
/// Sporcu yaka kartı ve isimlik: liste chess-results'tan (ELO/UKD/il TSF kayıt listesiyle), TSF kayıt
/// listesinden ya da Excel'den (eski yazdırma dosyalarının "Veri_Listesi" sayfası) alınır; tabloda
/// düzeltilir, kategoriye göre süzülür, seçilenler tek önizlemeden basılır ya da PDF'e kaydedilir.
/// </summary>
public sealed class AthletesForm : AppForm
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private const string AllCats = "Tüm kategoriler";

    private readonly AthletesContext _ctx;
    private AthleteCardConfig Cfg => _ctx.Config.Athletes;

    private readonly RadioButton _rbBadge = new() { Text = "Yaka kartı (95×135 mm, A4'e 4)", AutoSize = true };
    private readonly RadioButton _rbPlate = new() { Text = "İsimlik (190 mm geniş, masaya)", AutoSize = true };
    private readonly ComboBox _cboPlates = new() { AccessibleName = "İsimlik dizilimi", DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly Button _btnBanner = Icons.Set(new Button { Text = "Afiş seç…", AutoSize = true, Height = 28 }, Ico.Image);
    private readonly Button _btnBannerCards = new() { Text = "Masa kartı afişini kullan", AutoSize = true, Height = 28 };
    private readonly Label _lblBanner = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(290, 0) };
    private readonly NumericUpDown _numCrop = new() { Minimum = 0, Maximum = 45, Width = 60 };
    private readonly CheckBox _chkFooter = new() { Text = "Afişin alt bandını kartın altına şerit yap", AutoSize = true };
    private readonly Button _btnPlateBanner = Icons.Set(new Button { Text = "İsimlik şeridi seç…", AutoSize = true, Height = 28 }, Ico.Image);
    private readonly Button _btnPlateBannerClear = new() { Text = "Kaldır", AutoSize = true, Height = 28 };
    private readonly Label _lblPlateBanner = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(290, 0) };
    private readonly Button _btnLogo1 = Icons.Set(new Button { Text = "Sol logo…", AutoSize = true, Height = 28 }, Ico.Image);
    private readonly Button _btnLogo1Clear = Icons.Set(new Button { Text = "", AccessibleName = "Sol logoyu kaldır", Width = 32, Height = 28 }, Ico.Close);
    private readonly Button _btnLogo2 = Icons.Set(new Button { Text = "Sağ logo…", AutoSize = true, Height = 28 }, Ico.Image);
    private readonly Button _btnLogo2Clear = Icons.Set(new Button { Text = "", AccessibleName = "Sağ logoyu kaldır", Width = 32, Height = 28 }, Ico.Close);
    private readonly Label _lblLogos = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(290, 0) };
    private readonly CheckBox _chkTsf = new() { Text = "Sağ logo seçilmezse TSF il logosu", AutoSize = true };
    private readonly CheckBox _chkTitle = new() { Text = "Unvanı ada ekle (IM, FM, WCM…)", AutoSize = true };
    private readonly ComboBox _cboLine3 = new() { AccessibleName = "Kartın üçüncü satırı", DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly CheckBox _chkElo = new() { Text = "ELO kutusu", AutoSize = true };
    private readonly CheckBox _chkUkd = new() { Text = "UKD kutusu", AutoSize = true };
    private readonly CheckBox _chkCrop = new() { Text = "Kesim (crop) işaretleri", AutoSize = true };
    private readonly Button _btnFrame = new() { Text = "Çerçeve rengi", AutoSize = true, Height = 28 };
    private readonly CheckBox _chkCatColors = new() { Text = "Kategori renkleri", AccessibleDescription = "Her kategori kendi renginde; renkler masa kartlarıyla aynı", AutoSize = true };
    /// <summary>Listedeki kategorilerin renk düğmeleri (tıklayınca renk değişir).</summary>
    private readonly FlowLayoutPanel _catColors = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(300, 0), Margin = new Padding(0, 2, 0, 4) };
    private readonly Button _btnText = new() { Text = "Yazı rengi", AutoSize = true, Height = 28 };

    private readonly Button _btnFromCr = Icons.Set(new Button { Text = "chess-results", AutoSize = true, Height = 32 }, Ico.Download);
    private readonly Button _btnFromTsf = Icons.Set(new Button { Text = "TSF kayıt listesi", AutoSize = true, Height = 32 }, Ico.Download);
    private readonly Button _btnFromXls = Icons.Set(new Button { Text = "Excel'den al…", AutoSize = true, Height = 32 }, Ico.Table);
    private readonly Button _btnClear = new() { Text = "Temizle", AutoSize = true, Height = 32 };
    private readonly ComboBox _cboCat = new() { AccessibleName = "Kategori süzgeci", DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AccessibleName = "Sporcu listesi" };
    private readonly PictureBox _picPreview = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 12, 0, 0) };
    private readonly Button _btnPdf = Icons.Set(new Button { Text = "PDF Kaydet", Width = 150, Height = 40 }, Ico.Pdf);
    private readonly Button _btnPrint = Icons.Set(new Button { Text = "Önizle ve Yazdır", Width = 200, Height = 40, BackColor = Green, ForeColor = Color.White }, Ico.Print);
    private readonly Button _btnAll = new() { Text = "Tümünü seç", AutoSize = true, Height = 40 };
    private readonly Button _btnNone = new() { Text = "Hiçbiri", AutoSize = true, Height = 40 };
    private readonly FlowLayoutPanel _badgeOnly = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private readonly FlowLayoutPanel _plateOnly = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };

    // Uzun işler (liste çekme, PDF) arka planda: ilerleme çubuğu + iptal; pencere ve ana program kullanılabilir.
    private readonly BusyStrip _busy = new();

    private AthleteImages _images = new(null, null, null, null);
    /// <summary>Pencere önizlemesi için küçültülmüş kopyalar (her satır seçiminde tam boy afiş çizilmesin).</summary>
    private AthleteImages _previewImages = new(null, null, null, null);
    private readonly List<Image> _previewOwned = new();
    private Image? _tsfLogo;
    private bool _loading;

    public AthletesForm(AthletesContext ctx)
    {
        _ctx = ctx;
        Text = "Sporcu Yaka Kartı / İsimlik" + (string.IsNullOrWhiteSpace(ctx.EventName) ? "" : " — " + EventGrouping.BaseName(ctx.EventName));
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1360, 780);
        MinimumSize = new Size(980, 600);
        BackColor = Color.FromArgb(250, 250, 247);
        this.FitToScreen();

        BuildLayout();
        _loading = true;
        LoadOptions();
        _loading = false;
        ReloadImages();
        WireEvents();
        RestoreList();
        UpdateOptionVisibility();
        UpdatePreview();
        UpdateSummary();
        FormClosing += (_, _) => _busy.Cancel();
        _busy.BusyChanged += SetBusy;
        FormClosed += (_, _) => { SaveList(); lock (AthleteSheetPrinter.RenderLock) { DisposeImages(); _tsfLogo?.Dispose(); } };
    }

    // ================= Yerleşim =================
    private static Label Head(string t) => new() { Text = t, AutoSize = true, Font = new Font("Segoe UI", 9.75f, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, 12, 0, 4) };

    private static FlowLayoutPanel Row(params Control[] cs)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        foreach (var c in cs) { c.Margin = new Padding(0, 0, 6, 0); p.Controls.Add(c); }
        return p;
    }

    private void BuildLayout()
    {
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 4, 8, 8) };
        var leftHost = new Panel { Dock = DockStyle.Left, Width = 335 };
        leftHost.Controls.Add(left);
        _cboPlates.Items.AddRange(new object[] { "A4'e 3 isimlik (190×64 mm)", "A4'e 4 isimlik (190×68 mm)" });
        _cboLine3.Items.AddRange(new object[] { "3. satır: kulüp / okul (yoksa il)", "3. satır: il" });

        _badgeOnly.Controls.Add(_chkFooter);
        _badgeOnly.Controls.Add(Head("Logolar (kartın altı)"));
        _badgeOnly.Controls.Add(Row(_btnLogo1, _btnLogo1Clear, _btnLogo2, _btnLogo2Clear));
        _badgeOnly.Controls.Add(_chkTsf);
        _badgeOnly.Controls.Add(_lblLogos);
        _plateOnly.Controls.Add(Head("İsimlik şeridi"));
        _plateOnly.Controls.Add(Row(_btnPlateBanner, _btnPlateBannerClear));
        _plateOnly.Controls.Add(_lblPlateBanner);
        _plateOnly.Controls.Add(Row(_chkElo, _chkUkd));

        left.Controls.AddRange(new Control[]
        {
            Head("Çıktı"), _rbBadge, _rbPlate, _cboPlates,
            Head("Turnuva afişi"), Row(_btnBanner, _btnBannerCards), _lblBanner,
            Row(new Label { Text = "Afişin altından kırp:", AutoSize = true, Margin = new Padding(0, 4, 4, 0) }, _numCrop, new Label { Text = "%", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }),
            _badgeOnly, _plateOnly,
            Head("Yazılar"), _chkTitle, _cboLine3,
            Head("Görünüm"), _chkCatColors, _catColors, Row(_btnFrame, _btnText), _chkCrop,
        });
        foreach (var b in left.Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<Button>())
                     .Concat(new[] { _btnFromCr, _btnFromTsf, _btnFromXls, _btnClear, _btnPdf, _btnAll, _btnNone }))
        {
            b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.White; b.Cursor = Cursors.Hand;
            b.FlatAppearance.BorderColor = Color.FromArgb(208, 208, 198);
        }
        _btnPrint.FlatStyle = FlatStyle.Flat; _btnPrint.FlatAppearance.BorderColor = GreenDark; _btnPrint.Font = new Font(Font, FontStyle.Bold);

        // Orta: kaynak düğmeleri + süzgeç + tablo
        var info = new Label
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray,
            Text = "Listeyi chess-results'tan, TSF kayıt listesinden ya da Excel'den (eski yazdırma dosyalarınızın \"Veri_Listesi\" sayfası) alın. " +
                   "Tabloda düzeltebilir, alttaki boş satıra yazarak ekleyebilir, Excel'den kopyalayıp Ctrl+V ile yapıştırabilirsiniz."
        };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 4) };   // dar pencerede alta iner
        // "Kategori:" yazısı ve kutusu tek parça: dar pencerede birlikte alt satıra iner
        var catBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(6, 0, 0, 0) };
        catBox.Controls.Add(new Label { Text = "Kategori:", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        catBox.Controls.Add(_cboCat);
        tools.Controls.AddRange(new Control[] { _btnFromCr, _btnFromTsf, _btnFromXls, _btnClear, catBox });
        foreach (Control c in tools.Controls) if (c is Button) c.Margin = new Padding(0, 4, 6, 0);
        _cboCat.Margin = new Padding(0, 6, 0, 0);
        BuildGrid();
        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 8) };
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.Add(tools);
        gridPanel.Controls.Add(info);

        var previewSide = new Panel { Dock = DockStyle.Right, Width = 310, Padding = new Padding(4, 12, 14, 8) };
        // dar pencerede (küçük ekran, yüksek ölçek) önizleme biraz daralır, tablo yatay kaymasın
        Resize += (_, _) => { float k = UiScale.FactorFor(this); previewSide.Width = Math.Clamp((int)(ClientSize.Width * 0.22f), (int)(230 * k), (int)(310 * k)); };
        previewSide.Controls.Add(_picPreview);
        previewSide.Controls.Add(new Label { Text = "Önizleme (seçili satır)", Dock = DockStyle.Top, Height = 24, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var bl = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        bl.Controls.AddRange(new Control[] { _btnAll, _btnNone, _lblStatus, _busy });
        var br = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        br.Controls.AddRange(new Control[] { _btnPdf, _btnPrint });
        // alt şeritteki düğmeler şeridin yüksekliği kadar: üst/alt kenar boşluğu taşırıp alt çizgiyi kesiyordu
        foreach (var b in new Control[] { _btnAll, _btnNone, _btnPdf, _btnPrint }) b.Margin = new Padding(0, 0, 6, 0);
        bottom.Controls.Add(bl);
        bottom.Controls.Add(br);

        Controls.Add(gridPanel);
        Controls.Add(previewSide);
        Controls.Add(leftHost);
        Controls.Add(bottom);
    }

    private void BuildGrid()
    {
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.RowTemplate.Height = 28;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bas", HeaderText = "Bas", Width = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ad", HeaderText = "Ad Soyad", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 100, MinimumWidth = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kat", HeaderText = "Kategori", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "L3", HeaderText = "Okul / Kulüp / İl", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 75, MinimumWidth = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Elo", HeaderText = "ELO", Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ukd", HeaderText = "UKD", Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Adet", HeaderText = "Adet", Width = 46 });
        foreach (var c in new[] { "Elo", "Ukd", "Adet" }) _grid.Columns[c]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultValuesNeeded += (_, e) => { e.Row.Cells["Bas"].Value = true; e.Row.Cells["Adet"].Value = "1"; };
    }

    // ================= Ayarlar =================
    private void LoadOptions()
    {
        _rbBadge.Checked = Cfg.Kind != AthleteCardConfig.Plate;
        _rbPlate.Checked = Cfg.Kind == AthleteCardConfig.Plate;
        _cboPlates.SelectedIndex = Cfg.PlatesPerPage >= 4 ? 1 : 0;
        _numCrop.Value = Math.Clamp(Cfg.CropBottomPercent, 0, 45);
        _chkFooter.Checked = Cfg.FooterBand;
        _chkTsf.Checked = Cfg.AutoTsfLogo;
        _chkTitle.Checked = Cfg.TitleInName;
        _cboLine3.SelectedIndex = Cfg.Line3Source == "Province" ? 1 : 0;
        _chkElo.Checked = Cfg.ShowElo;
        _chkUkd.Checked = Cfg.ShowUkd;
        _chkCrop.Checked = Cfg.CropMarks;
        _chkCatColors.Checked = Cfg.UseCategoryColors;
    }

    private void Changed(Action apply)
    {
        if (_loading) return;
        apply();
        _ctx.SaveConfig();
        UpdateOptionVisibility();
        UpdatePreview();
        UpdateSummary();
    }

    private void WireEvents()
    {
        _rbBadge.CheckedChanged += (_, _) => { if (_rbBadge.Checked) Changed(() => Cfg.Kind = AthleteCardConfig.Badge); };
        _rbPlate.CheckedChanged += (_, _) => { if (_rbPlate.Checked) Changed(() => Cfg.Kind = AthleteCardConfig.Plate); };
        _cboPlates.SelectedIndexChanged += (_, _) => Changed(() => Cfg.PlatesPerPage = _cboPlates.SelectedIndex == 1 ? 4 : 3);
        _numCrop.ValueChanged += (_, _) => Changed(() => Cfg.CropBottomPercent = (int)_numCrop.Value);
        _chkFooter.CheckedChanged += (_, _) => Changed(() => Cfg.FooterBand = _chkFooter.Checked);
        _chkTsf.CheckedChanged += (_, _) => Changed(() => { Cfg.AutoTsfLogo = _chkTsf.Checked; ReloadImages(); });
        _chkTitle.CheckedChanged += (_, _) => Changed(() => Cfg.TitleInName = _chkTitle.Checked);
        _cboLine3.SelectedIndexChanged += (_, _) => Changed(() => Cfg.Line3Source = _cboLine3.SelectedIndex == 1 ? "Province" : "Club");
        _chkElo.CheckedChanged += (_, _) => Changed(() => Cfg.ShowElo = _chkElo.Checked);
        _chkUkd.CheckedChanged += (_, _) => Changed(() => Cfg.ShowUkd = _chkUkd.Checked);
        _chkCrop.CheckedChanged += (_, _) => Changed(() => Cfg.CropMarks = _chkCrop.Checked);
        _chkCatColors.CheckedChanged += (_, _) => Changed(() => { Cfg.UseCategoryColors = _chkCatColors.Checked; RefreshCategoryColors(); });
        _btnBanner.Click += (_, _) => PickImage("Turnuva afişi", p => Cfg.BannerPath = p, "afis");
        _btnBannerCards.Click += (_, _) => { if (CardPoster() is { } p) SetImage(p, v => Cfg.BannerPath = v, "afis"); };
        _btnPlateBanner.Click += (_, _) => PickImage("İsimlik şeridi (geniş, yatay görsel)", p => Cfg.PlateBannerPath = p, "isimlik_serit");
        _btnPlateBannerClear.Click += (_, _) => Changed(() => { Cfg.PlateBannerPath = null; ReloadImages(); });
        _btnLogo1.Click += (_, _) => PickImage("Sol logo", p => Cfg.Logo1Path = p, "logo_sol");
        _btnLogo2.Click += (_, _) => PickImage("Sağ logo", p => Cfg.Logo2Path = p, "logo_sag");
        _btnLogo1Clear.Click += (_, _) => Changed(() => { Cfg.Logo1Path = null; ReloadImages(); });
        _btnLogo2Clear.Click += (_, _) => Changed(() => { Cfg.Logo2Path = null; ReloadImages(); });
        _btnFrame.Click += (_, _) => PickColor(Cfg.FrameColor, c => Cfg.FrameColor = c);
        _btnText.Click += (_, _) => PickColor(Cfg.TextColor, c => Cfg.TextColor = c);

        _btnFromCr.Click += async (_, _) => await FromChessResultsAsync();
        _btnFromTsf.Click += (_, _) => FromRegistrations();
        _btnFromXls.Click += (_, _) => FromExcel();
        _btnClear.Click += (_, _) =>
        {
            if (_grid.Rows.Count > 1 && MessageBox.Show(this, "Tablodaki bütün sporcular silinsin mi?", "Temizle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _grid.Rows.Clear(); RefreshCategories(); UpdateSummary(); UpdatePreview();
        };
        _cboCat.SelectedIndexChanged += (_, _) => { ApplyFilter(); UpdateSummary(); };
        // Tablo olayları birikir, tek seferde işlenir: "Tümünü seç" 120 satırda 120 kez önizleme çizmesin.
        _grid.SelectionChanged += (_, _) => Schedule(preview: true);
        _picPreview.SizeChanged += (_, _) => Schedule(preview: true);   // önizleme kutusu boyunda, keskin
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) Schedule(preview: true, categories: e.ColumnIndex == _grid.Columns["Kat"]!.Index); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.RowsRemoved += (_, _) => Schedule(preview: false);
        _grid.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.V && !_grid.IsCurrentCellInEditMode) { PasteRows(); e.Handled = true; } };
        _btnAll.Click += (_, _) => SetChecked(true);
        _btnNone.Click += (_, _) => SetChecked(false);
        _btnPrint.Click += (_, _) => Print();
        _btnPdf.Click += (_, _) => SavePdf();

        _btnFromCr.Enabled = _ctx.LoadStartingList is not null && _ctx.Categories.Count > 0;
        _btnFromTsf.Enabled = Registration.ProvinceBook.For(_ctx.Config, _ctx.EventTnr, _ctx.EventName) is { Entries.Count: > 0 };
        var tip = new ToolTip();
        tip.SetToolTip(_btnFromCr, "Turnuvanın her kategorisinin başlangıç listesi; ELO, UKD ve il TSF kayıt listesiyle eşleştirilir.");
        tip.SetToolTip(_btnFromTsf, "TSF il sitesindeki kayıt listesi (ad, kategori, il, ELO, UKD).");
        tip.SetToolTip(_btnFromXls, "Excel dosyası (.xlsx/.xlsm): ad soyad, kategori, okul/kulüp/il, ELO, UKD sütunları.");
        tip.SetToolTip(_chkCatColors, "Her kategorinin çerçevesi kendi renginde basılır (masa kartlarıyla aynı renkler). Rengi değiştirmek için kategorinin renk düğmesine tıklayın.");
        tip.SetToolTip(_btnLogo1Clear, "Sol logoyu kaldır");
        tip.SetToolTip(_btnLogo2Clear, "Sağ logoyu kaldır");
        tip.SetToolTip(_numCrop,"Afişin altındaki süs ya da logo bandı kartta yer kaplamasın diye kırpılır (Excel şablonunda %22).");
    }

    private bool _scheduled, _schedPreview, _schedCategories;

    /// <summary>Özet/önizleme yenilemesini bir sonraki boş ana kadar erteler; aradaki istekler birleşir.</summary>
    private void Schedule(bool preview, bool categories = false)
    {
        _schedPreview |= preview;
        _schedCategories |= categories;
        if (_scheduled) return;
        if (!IsHandleCreated) { RunScheduled(); return; }
        _scheduled = true;
        BeginInvoke(RunScheduled);
    }

    private void RunScheduled()
    {
        _scheduled = false;
        if (IsDisposed) return;
        if (_schedCategories) { _schedCategories = false; RefreshCategories(); }
        UpdateSummary();
        if (_schedPreview) { _schedPreview = false; UpdatePreview(); }
    }

    private void UpdateOptionVisibility()
    {
        bool plate = _rbPlate.Checked;
        _cboPlates.Visible = plate;
        _badgeOnly.Visible = !plate;
        _plateOnly.Visible = plate;
        _btnBannerCards.Visible = CardPoster() is not null;
        _lblBanner.Text = Cfg.BannerPath is { } b && File.Exists(b) ? "✓ " + Path.GetFileName(b) : "Afiş seçilmedi: yerinde boş çerçeve basılır.";
        _lblPlateBanner.Text = Cfg.PlateBannerPath is { } pb && File.Exists(pb)
            ? "✓ " + Path.GetFileName(pb)
            : "Seçilmezse afiş kullanılır. Geniş bir şerit görseli (ör. 1800×360) şeridi tam doldurur.";
        string L(string? p, string none) => p is not null && File.Exists(p) ? Path.GetFileName(p) : none;
        _lblLogos.Text = $"Sol: {L(Cfg.Logo1Path, "yok")} • Sağ: {L(Cfg.Logo2Path, Cfg.AutoTsfLogo ? "TSF il logosu" : "yok")}";
        _btnFrame.Visible = !Cfg.UseCategoryColors;
        _catColors.Visible = Cfg.UseCategoryColors;
        _btnFrame.BackColor = CardLayout.ParseColor(Cfg.FrameColor, Color.White);
        _btnFrame.ForeColor = CardLayout.TextColorFor(_btnFrame.BackColor);
        _btnText.BackColor = CardLayout.ParseColor(Cfg.TextColor, Color.White);
        _btnText.ForeColor = CardLayout.TextColorFor(_btnText.BackColor);
    }

    // ================= Görseller =================
    private string? CardPoster()
    {
        var c = _ctx.Config.Cards;
        return c.LogoSourcePath is { } s && File.Exists(s) ? s : c.LogoPath is { } l && File.Exists(l) ? l : null;
    }

    private void PickImage(string title, Action<string> set, string stem)
    {
        using var dlg = new OpenFileDialog { Title = title, Filter = "Resim (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SetImage(dlg.FileName, set, stem);
    }

    /// <summary>Görseli veri klasörüne kopyalar (asıl dosya silinse de kart basılabilsin) ve kaydeder.</summary>
    private void SetImage(string source, Action<string> set, string stem)
    {
        try
        {
            using (Image.FromFile(source)) { }
            var dir = Path.Combine(_ctx.BaseDir, "badges", "athletes");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, stem + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(source).ToLowerInvariant());
            File.Copy(source, dest, overwrite: true);
            Changed(() => { set(dest); ReloadImages(); });
        }
        catch (Exception ex) { MessageBox.Show(this, "Resim açılamadı: " + ex.Message, "Görsel", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void PickColor(string current, Action<string> set)
    {
        using var dlg = new ColorDialog { Color = CardLayout.ParseColor(current, Color.Navy), FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Changed(() => set($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}"));
    }

    private static Image? LoadImage(string? path)
        => ImageFiles.Load(path);

    private void DisposeImages()
    {
        foreach (var i in new[] { _images.Banner, _images.PlateBanner, _images.Logo1, _images.Logo2 })
            if (i is not null && i != _tsfLogo) i.Dispose();
        foreach (var i in _previewOwned) i.Dispose();
        _previewOwned.Clear();
    }

    private void ReloadImages()
    {
        lock (AthleteSheetPrinter.RenderLock)
        {
            DisposeImages();
            Image? logo2 = LoadImage(Cfg.Logo2Path);
            if (logo2 is null && Cfg.AutoTsfLogo)
                try { logo2 = _tsfLogo ??= Branding.OfficialLogos.Tsf(_ctx.Config.LogoProvince(), 600); } catch { }
            _images = new AthleteImages(LoadImage(Cfg.BannerPath), LoadImage(Cfg.PlateBannerPath), LoadImage(Cfg.Logo1Path), logo2);
            // önizleme kartı en çok ~750 piksel genişlikte çizilir
            _previewImages = AthleteSheetPrinter.ScaledCopy(_images, 1000, 1000, 160, _previewOwned);
        }
    }

    // ================= Liste kaynakları =================
    private async Task FromChessResultsAsync()
    {
        if (_ctx.LoadStartingList is not { } load) return;
        if (!ConfirmReplace()) return;
        var cats = _ctx.Categories.ToList();
        bool titleInName = Cfg.TitleInName, line3Province = Cfg.Line3Source == "Province";
        int failed = 0;
        var result = await RunBusy($"chess-results: {cats.Count} kategori", "kategori", async (progress, ct) =>
        {
            // Kategoriler aynı anda (en çok 4) çekilir: sırayla çekmek kategori başına 1-3 sn bekletiyordu.
            var lists = new (string, IReadOnlyList<Player>)[cats.Count];
            int done = 0;
            using var gate = new SemaphoreSlim(4);
            await Task.WhenAll(cats.Select(async (cat, i) =>
            {
                await gate.WaitAsync(ct);
                try { lists[i] = (cat.Name, await load(cat.Tnr, ct)); }
                catch (OperationCanceledException) { throw; }
                catch { lists[i] = (cat.Name, Array.Empty<Player>()); Interlocked.Increment(ref failed); }
                finally { gate.Release(); progress.Report((Interlocked.Increment(ref done), cats.Count)); }
            }));
            ct.ThrowIfCancellationRequested();
            // il eşleştirmesi büyük listelerde ağır: arayüzü bekletmesin
            return await Task.Run(() =>
            {
                var roster = lists.SelectMany(l => l.Item2.Select(p => new RosterPlayer(p.Name, p.Rating, l.Item1)));
                var province = Registration.ProvinceBook.Resolver(_ctx.Config, _ctx.EventTnr, roster, _ctx.EventName);
                var reg = Registration.ProvinceBook.For(_ctx.Config, _ctx.EventTnr, _ctx.EventName);
                return (Athletes: AthleteLists.FromStartingLists(lists, reg?.Entries, province, titleInName, line3Province), HasReg: reg is { Entries.Count: > 0 });
            }, ct);
        });
        // iptal ya da hata (durum satırında yazıyor) ya da pencere liste beklerken kapatıldı
        if (result.Athletes is null || IsDisposed) return;
        var r = result;
        Fill(r.Athletes);
        Note($"chess-results: {r.Athletes.Count} sporcu, {cats.Count} kategori" + (r.HasReg ? " (ELO/UKD/il kayıt listesinden)" : "") +
             (failed > 0 ? $" • {failed} kategori alınamadı" : ""));
    }

    /// <summary>
    /// Uzun işi arka planda yürütür: alttaki şeritte ilerleme çubuğu ve "İptal" görünür, liste/çıktı düğmeleri
    /// kapanır; pencere ve ana program kullanılmaya devam eder. İptal ya da hatada null döner.
    /// </summary>
    private Task<T?> RunBusy<T>(string title, string unit, Func<IProgress<(int Done, int Total)>, CancellationToken, Task<T>> work)
        => _busy.RunAsync(title, unit, work, ex => _lblStatus.Text = "İşlem tamamlanamadı: " + BusyStrip.Friendly(ex));

    private void FromRegistrations()
    {
        var reg = Registration.ProvinceBook.For(_ctx.Config, _ctx.EventTnr, _ctx.EventName);
        if (reg is not { Entries.Count: > 0 }) { _lblStatus.Text = "Bu turnuva için TSF kayıt listesi yok."; return; }
        if (!ConfirmReplace()) return;
        var athletes = AthleteLists.FromRegistrations(reg.Entries);
        Fill(athletes);
        Note($"TSF kayıt listesi: {athletes.Count} sporcu");
    }

    private async void FromExcel()
    {
        string path;
        using (var dlg = new OpenFileDialog { Title = "Sporcu listesi (Excel)", Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        if (!ConfirmReplace()) return;
        // büyük çalışma kitapları birkaç saniye sürebilir: arka planda okunur, pencere donmaz
        Exception? error = null;
        var r = await _busy.RunAsync("Excel okunuyor", "", (_, ct) => Task.Run(() => AthleteImport.Read(path), ct), ex => error = ex);
        if (IsDisposed) return;
        if (r is null)
        {
            if (error is not null)
                MessageBox.Show(this, "Excel okunamadı: " + (error is IOException ? "dosya açık olabilir; Excel'de kapatıp yeniden deneyin." : error.Message), "Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Fill(r.Athletes);
        Note($"Excel: {r.Athletes.Count} sporcu ({r.Sheet} sayfası)");
        new ToolTip().SetToolTip(_lblStatus, "Okunan sütunlar: " + r.Columns);
    }

    private bool ConfirmReplace()
        => Athletes(all: true).Count == 0 ||
           MessageBox.Show(this, "Tablodaki liste yenisiyle değiştirilsin mi?", "Sporcu listesi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void SetBusy(bool busy)
    {
        // bekleme imleci yok: pencere kullanılabilir kalır, ilerleme alttaki şeritte (BusyStrip) görünür
        _lblStatus.Visible = !busy;
        foreach (var b in new[] { _btnFromCr, _btnFromTsf, _btnFromXls, _btnClear, _btnPrint, _btnPdf }) b.Enabled = !busy;
        if (!busy)
        {
            _btnFromCr.Enabled = _ctx.LoadStartingList is not null && _ctx.Categories.Count > 0;
            _btnFromTsf.Enabled = Registration.ProvinceBook.For(_ctx.Config, _ctx.EventTnr, _ctx.EventName) is { Entries.Count: > 0 };
        }
    }

    // ================= Tablo =================
    private void Fill(IEnumerable<Athlete> athletes, IReadOnlyList<(bool Print, int Copies)>? state = null)
    {
        _grid.Rows.Clear();
        int i = 0;
        foreach (var a in athletes)
        {
            var (print, copies) = state is not null && i < state.Count ? state[i] : (true, 1);
            _grid.Rows.Add(print, a.Name, a.Category, a.Line3, a.Elo?.ToString() ?? "", a.Ukd?.ToString() ?? "", copies.ToString());
            i++;
        }
        RefreshCategories();
        UpdateSummary();
        if (_grid.Rows.Count > 0) { _grid.ClearSelection(); _grid.Rows[0].Selected = true; }
        UpdatePreview();
    }

    private void RefreshCategories()
    {
        var keep = _cboCat.SelectedItem?.ToString();
        var cats = _grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                        .Select(r => r.Cells["Kat"].Value?.ToString()?.Trim() ?? "").Where(c => c.Length > 0).Distinct().ToList();
        _cboCat.BeginUpdate();
        _cboCat.Items.Clear();
        _cboCat.Items.Add(AllCats);
        foreach (var c in cats) _cboCat.Items.Add(c);
        _cboCat.SelectedItem = keep is not null && _cboCat.Items.Contains(keep) ? keep : AllCats;
        _cboCat.EndUpdate();
        ApplyFilter();
        RefreshCategoryColors();
    }

    // ================= Kategori renkleri =================
    /// <summary>
    /// Kategorinin rengi: chess-results kategorisiyse masa kartlarındaki rengi (aynı palet, aynı kayıt; burada
    /// değiştirilirse orada da değişir); değilse (Excel'den gelen ad) bu listede kullanılmamış ilk palet rengi.
    /// </summary>
    private const string SampleCategory = "KATEGORİ";

    private string? CategoryColor(string category)
    {
        if (!Cfg.UseCategoryColors || string.IsNullOrWhiteSpace(category)) return null;
        var tnrs = _ctx.Categories.Select(c => c.Tnr).ToList();
        if (CategoryRefOf(category) is { } cat) return _ctx.Config.Cards.ColorFor(cat.Tnr, tnrs);
        if (category == SampleCategory) return null;   // boş satırın örnek önizlemesi: renk atanmaz, kaydedilmez
        var key = CategoryKey(category);
        if (Cfg.CategoryColors.TryGetValue(key, out var c)) return c;
        var used = tnrs.Select(t => _ctx.Config.Cards.CategoryColors.GetValueOrDefault(t.ToString()))
                       .Concat(Cfg.CategoryColors.Values).Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pick = CardConfig.Palette.FirstOrDefault(x => !used.Contains(x)) ?? CardConfig.Palette[Cfg.CategoryColors.Count % CardConfig.Palette.Length];
        Cfg.CategoryColors[key] = pick;
        return pick;
    }

    private CategoryRef? CategoryRefOf(string category)
        => _ctx.Categories.FirstOrDefault(c => CategoryKey(c.Name) == CategoryKey(category)
                                             || CategoryKey(EventGrouping.ShortCategory(c.Name)) == CategoryKey(category));

    private static string CategoryKey(string s) => s.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR"));

    /// <summary>Listedeki her kategori için renkli düğme; tıklayınca renk seçilir (kalıcı).</summary>
    private void RefreshCategoryColors()
    {
        _catColors.SuspendLayout();
        foreach (Control c in _catColors.Controls) c.Dispose();
        _catColors.Controls.Clear();
        if (Cfg.UseCategoryColors)
        {
            bool assigned = false;
            foreach (var cat in _cboCat.Items.Cast<object>().Select(o => o.ToString()!).Where(c => c != AllCats))
            {
                int before = Cfg.CategoryColors.Count + _ctx.Config.Cards.CategoryColors.Count;
                var color = CardLayout.ParseColor(CategoryColor(cat), Color.Gray);
                assigned |= Cfg.CategoryColors.Count + _ctx.Config.Cards.CategoryColors.Count != before;
                var b = new Button
                {
                    Text = cat, AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = color,
                    ForeColor = CardLayout.TextColorFor(color), Margin = new Padding(0, 0, 4, 4), Cursor = Cursors.Hand,
                    AccessibleName = $"{cat} kategorisinin rengi"
                };
                b.FlatAppearance.BorderColor = Color.FromArgb(120, 120, 120);
                b.Click += (_, _) => PickCategoryColor(cat);
                new ToolTip().SetToolTip(b, "Rengi değiştirmek için tıklayın (masa kartlarında da aynı renk kullanılır).");
                _catColors.Controls.Add(b);
            }
            if (assigned) _ctx.SaveConfig();
        }
        _catColors.ResumeLayout();
    }

    private void PickCategoryColor(string category)
    {
        using var dlg = new ColorDialog
        {
            Color = CardLayout.ParseColor(CategoryColor(category), Color.Gray), FullOpen = true,
            CustomColors = CardConfig.Palette.Select(h => { var k = ColorTranslator.FromHtml(h); return k.R | (k.G << 8) | (k.B << 16); }).ToArray()
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var hex = CardLayout.ToHex(dlg.Color);
        if (CategoryRefOf(category) is { } cat) _ctx.Config.Cards.CategoryColors[cat.Tnr.ToString()] = hex;
        else Cfg.CategoryColors[CategoryKey(category)] = hex;
        _ctx.SaveConfig();
        RefreshCategoryColors();
        UpdatePreview();
    }

    private void ApplyFilter()
    {
        var cat = _cboCat.SelectedItem?.ToString();
        _grid.CurrentCell = null;
        foreach (DataGridViewRow r in _grid.Rows)
            if (!r.IsNewRow) r.Visible = cat is null || cat == AllCats || string.Equals(r.Cells["Kat"].Value?.ToString()?.Trim(), cat, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int? Num(object? v) => int.TryParse(v?.ToString()?.Trim(), out var n) && n > 0 ? n : null;

    private static Athlete? AthleteOf(DataGridViewRow r)
    {
        var name = r.Cells["Ad"].Value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return new Athlete(name, r.Cells["Kat"].Value?.ToString()?.Trim() ?? "", r.Cells["L3"].Value?.ToString()?.Trim() ?? "", Num(r.Cells["Elo"].Value), Num(r.Cells["Ukd"].Value));
    }

    /// <summary>Tablodaki sporcular (all: tümü; değilse yalnız görünen ve işaretli olanlar, adet kadar).</summary>
    private List<Athlete> Athletes(bool all)
    {
        var list = new List<Athlete>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow || AthleteOf(r) is not { } a) continue;
            if (all) { list.Add(a); continue; }
            if (!r.Visible || r.Cells["Bas"].Value is not true) continue;
            int copies = Math.Clamp(Num(r.Cells["Adet"].Value) ?? 1, 1, 20);
            for (int i = 0; i < copies; i++) list.Add(a);
        }
        return list;
    }

    private void SetChecked(bool on)
    {
        foreach (DataGridViewRow r in _grid.Rows) if (!r.IsNewRow && r.Visible) r.Cells["Bas"].Value = on;
        Schedule(preview: false);
    }

    /// <summary>Excel'den kopyalanan satırları yapıştırır (sütun sırası: ad, kategori, okul/il, ELO, UKD).</summary>
    private void PasteRows()
    {
        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) return;
        int n = 0;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var c = line.Split('\t');
            if (c.Length == 0 || string.IsNullOrWhiteSpace(c[0])) continue;
            string At(int i) => i < c.Length ? c[i].Trim() : "";
            _grid.Rows.Add(true, At(0), At(1), At(2), Num(At(3))?.ToString() ?? "", Num(At(4))?.ToString() ?? "", "1");
            n++;
        }
        RefreshCategories();
        UpdateSummary();
        Note($"{n} satır yapıştırıldı");
    }

    // ================= Liste kaydı (pencere kapanınca; turnuvaya göre) =================
    private string ListFile => Path.Combine(_ctx.BaseDir, "athletes",
        (_ctx.EventTnr is int t ? "t" + t : "liste") + ".json");

    private sealed record SavedRow(string Name, string Category, string Line3, int? Elo, int? Ukd, bool Print, int Copies);

    private void SaveList()
    {
        try
        {
            _grid.EndEdit();
            var rows = _grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => (Row: r, A: AthleteOf(r))).Where(x => x.A is not null)
                .Select(x => new SavedRow(x.A!.Name, x.A.Category, x.A.Line3, x.A.Elo, x.A.Ukd, x.Row.Cells["Bas"].Value is true, Num(x.Row.Cells["Adet"].Value) ?? 1))
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(ListFile)!);
            File.WriteAllText(ListFile, JsonSerializer.Serialize(rows));
        }
        catch { /* liste kaydı isteğe bağlı */ }
    }

    private void RestoreList()
    {
        try
        {
            if (!File.Exists(ListFile)) return;
            var rows = JsonSerializer.Deserialize<List<SavedRow>>(File.ReadAllText(ListFile));
            if (rows is not { Count: > 0 }) return;
            Fill(rows.Select(r => new Athlete(r.Name, r.Category, r.Line3, r.Elo, r.Ukd)), rows.Select(r => (r.Print, r.Copies)).ToList());
            Note($"Son liste: {rows.Count} sporcu");
        }
        catch { }
    }

    // ================= Önizleme ve çıktı =================
    /// <summary>Son yükleme mesajı (özetin önünde gösterilir; ayar değişince özet güncellenir, mesaj kalır).</summary>
    private string _note = "";

    private string SummaryText()
    {
        var p = NewPrinter(Athletes(all: false));
        var what = _rbPlate.Checked ? "isimlik" : "yaka kartı";
        return p.CardCount == 0 ? "Basılacak sporcu seçilmedi." : $"{p.CardCount} {what} • {p.PageCount} sayfa A4 ({p.Cols}×{p.Rows})";
    }

    private void UpdateSummary() => _lblStatus.Text = (_note.Length > 0 ? _note + "  —  " : "") + SummaryText();

    private void Note(string text) { _note = text; UpdateSummary(); }

    private AthleteSheetPrinter NewPrinter(IEnumerable<Athlete> items)
    {
        var list = items.ToList();
        if (!Cfg.UseCategoryColors) return new(list, Cfg, _images, EventGrouping.BaseName(_ctx.EventName));
        // renkler burada (arayüzde) çözülür: yazdırma/PDF arka planda ayara dokunmadan sabit tablodan okur
        var colors = list.Select(a => a.Category).Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToDictionary(c => c, c => CategoryColor(c), StringComparer.OrdinalIgnoreCase);
        return new(list, Cfg, _images, EventGrouping.BaseName(_ctx.EventName), c => colors.GetValueOrDefault(c));
    }

    private void UpdatePreview()
    {
        var row = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : _grid.CurrentRow;
        var a = row is null || row.IsNewRow ? null : AthleteOf(row);
        a ??= new Athlete("Ad SOYAD", SampleCategory, "OKUL / KULÜP / İL", 1850, 1720);
        var p = NewPrinter(new[] { a });
        // önizleme kutusunun boyutunda (ekran ölçeği dahil) çizilir; küçültülmüş görsellerle
        float boxW = Math.Max(120, _picPreview.ClientSize.Width), boxH = Math.Max(120, _picPreview.ClientSize.Height);
        float scale = Math.Clamp(Math.Min(boxW / p.CardW, boxH / p.CardH), 0.8f, 4f);
        var bmp = new Bitmap((int)(p.CardW * scale) + 2, (int)(p.CardH * scale) + 2);
        using (var g = Graphics.FromImage(bmp))
        lock (AthleteSheetPrinter.RenderLock)
        {
            g.Clear(BackColor);
            g.PageUnit = GraphicsUnit.Point;
            float k = scale * 72f / bmp.HorizontalResolution;
            g.ScaleTransform(k, k);
            AthleteSheetPrinter.Prepare(g);
            var rect = new RectangleF(0, 0, p.CardW, p.CardH);
            var color = CategoryColor(a.Category);
            if (p.IsPlate) AthleteSheetPrinter.DrawPlate(g, rect, a, Cfg, _previewImages, color);
            else AthleteSheetPrinter.DrawBadge(g, rect, a, Cfg, _previewImages, color);
        }
        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();
    }

    private AthleteSheetPrinter? Printer()
    {
        _grid.EndEdit();
        var p = NewPrinter(Athletes(all: false));
        if (p.CardCount == 0) { _lblStatus.Text = "Basılacak sporcu seçilmedi."; return null; }
        return p;
    }

    /// <summary>Önizleme (yalnız görünen sayfa çizilir), yazıcı seçimi; sayfalar arka planda gönderilir, pencere donmaz.</summary>
    private async void Print()
    {
        var p = Printer();
        if (p is null) return;
        try
        {
            if (!PrintRouter.IsSilent && !PagedOutput.Preview(p, this, p.Hint)) return;
            if (IsDisposed || PagedOutput.PreparePrint(p, this) is not { } doc) return;
            _ = p.Images;   // görseller arayüzde küçültülür; arka plan yalnız kopyaları kullanır
            var ok = await RunBusy($"Yazıcıya gönderiliyor ({p.PageCount} sayfa)", "sayfa", (progress, ct) => PagedOutput.PrintAsync(p, doc, progress, ct));
            if (ok && !IsDisposed) Note($"{p.CardCount} {(p.IsPlate ? "isimlik" : "yaka kartı")} ({p.PageCount} sayfa) yazıcıya gönderildi.");
        }
        finally { p.Dispose(); }
    }

    /// <summary>PDF arka planda hazırlanır (ilerleme alttaki şeritte); bu sırada pencere ve ana program kullanılabilir.</summary>
    private async void SavePdf()
    {
        var p = Printer();
        if (p is null) return;
        try
        {
            Directory.CreateDirectory(_ctx.OutputDir);
            var name = p.IsPlate ? "sporcu_isimlikleri.pdf" : "sporcu_yaka_kartlari.pdf";
            string path;
            using (var dlg = new SaveFileDialog { Title = "PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf", InitialDirectory = _ctx.OutputDir, FileName = name })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            _ = p.Images;   // görseller burada (arayüzde) küçültülür; arka plan yalnız kopyaları kullanır
            var ok = await RunBusy($"PDF ({p.PageCount} sayfa)", "sayfa",
                                   (progress, ct) => Task.Run(() => { p.SavePdf(path, progress, ct); return true; }, ct));
            if (!ok || IsDisposed) return;
            Note("PDF kaydedildi: " + Path.GetFileName(path));
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
        finally { p.Dispose(); }
    }
}
