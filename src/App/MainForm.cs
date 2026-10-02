using System.Diagnostics;
using System.Text;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.Parsers;
using NotasyonOtomasyonu.Render;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Tek pencereli arayüz. Kaynak: chess-results (online) veya dosya.
/// Online akış: ara/seç (il+ad) → kategori+tur (yan yana butonlar) → Senkron Et → Oluştur/Yazdır.
/// </summary>
public partial class MainForm : Form
{
    private readonly string _configPath;
    private readonly string _outputDir;
    private AppConfig _config;
    private readonly ChessResultsService _online = new();

    private Tournament? _syncedTournament;   // online: son çekilen kategori+tur
    private Tournament? _fileTournament;     // dosya: son okunan dosya
    private Tournament? _gridTournament;     // sağdaki listede gösterilen (basılacak olan)
    private int? _eventTnr;
    private bool _loading;
    private int _syncSeq;                    // üst üste tıklamada eski çekim sonucunu atmak için

    /// <summary>
    /// Kategori (Tnr) → (toplam tur, eşlenmiş son tur). Her kategori farklı turda olabilir.
    /// </summary>
    private readonly Dictionary<int, (int Max, int Current)> _roundCache = new();

    public MainForm()
    {
        InitializeComponent();
        LoadBranding();
        var baseDir = AppContext.BaseDirectory;
        _configPath = Path.Combine(baseDir, "config.json");
        _outputDir = Path.Combine(baseDir, "outputs");
        Directory.CreateDirectory(_outputDir);

        _config = AppConfig.Load(_configPath);
        OverlayDefaults.EnsureDefaults(_config, baseDir); // Ana Örnek varsayılan şablonu hazırla
        ApplyConfigToUi();

        // Başlık çubuğundaki "⚙ Ayarlar" butonunu her boyutta sağ üste sabitle
        // (docked panel + anchor, DPI ölçeklemede butonu ekran dışına itebiliyordu).
        pnlHeader.Resize += (_, _) => PositionHeader();
        PositionHeader();
        // Yazı tipi ölçeklemesi (DPI) dikeyde büyütünce alttan çapalı günlük kutusu pencereden
        // taşıyordu; yüksekliği her boyut değişiminde pencereye göre hesapla.
        Load += (_, _) => FitLog();
        Resize += (_, _) => FitLog();

        var tip = new ToolTip();
        tip.SetToolTip(btnSync, "Eşleştirmeleri yeniden çek (F5). Kategori/tur seçince zaten otomatik çekilir.");
        tip.SetToolTip(btnPrint, "Önizleme açılır; oradan yazıcı seçip basılır (Ctrl+P).");
        tip.SetToolTip(btnPdf, "İşaretli masaları PDF olarak kaydedip açar.");
        tip.SetToolTip(numCopies, "Her masa için kaç kağıt (2 = iki oyuncuya birer notasyon).");

        dgvPairings.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (dgvPairings.IsCurrentCellDirty) dgvPairings.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        dgvPairings.CellValueChanged += (_, e) => { if (e.ColumnIndex == 0) UpdateSelectionSummary(); };
        dgvPairings.KeyDown += dgvPairings_KeyDown;
        ShowPairings(null);

        ApplyTheme(); // genel görsel cila (hover/flat, başlıklar, palet)

        // İlk açılışta: hatırlanan turnuva varsa otomatik yükle + eşleştirmeleri çek.
        Shown += MainForm_Shown;
    }

    // ================= Görsel tema =================
    private static readonly Color ThGreen     = Color.FromArgb(118, 150, 86);
    private static readonly Color ThGreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color ThGreenHover= Color.FromArgb(134, 168, 100);
    private static readonly Color ThBorder    = Color.FromArgb(208, 208, 198);
    private static readonly Color ThHoverLite = Color.FromArgb(236, 241, 231);
    private static readonly Color ThDownLite  = Color.FromArgb(224, 231, 216);

    /// <summary>Tüm butonlara tutarlı flat + hover/pressed stilini ve grup başlık rengini uygular.</summary>
    private void ApplyTheme()
    {
        ThemeRecursive(this);
    }

    private void ThemeRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button b: StyleButton(b); break;
                case GroupBox g: g.ForeColor = ThGreenDark; break;
            }
            if (c.HasChildren) ThemeRecursive(c);
        }
    }

    /// <summary>Yeşil zeminli butonlar = birincil; diğerleri = beyaz/nötr. İkisine de hover/pressed.</summary>
    private void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.Cursor = Cursors.Hand;

        bool primary = b.BackColor == ThGreen;
        if (primary)
        {
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = ThGreenDark;
            b.FlatAppearance.MouseOverBackColor = ThGreenHover;
            b.FlatAppearance.MouseDownBackColor = ThGreenDark;
        }
        else
        {
            if (b.BackColor == SystemColors.Control) b.BackColor = Color.White;
            b.FlatAppearance.BorderColor = ThBorder;
            b.FlatAppearance.MouseOverBackColor = ThHoverLite;
            b.FlatAppearance.MouseDownBackColor = ThDownLite;
        }
    }

    private void FitLog()
        => txtLog.Height = Math.Max(40, ClientSize.Height - txtLog.Top - 12);

    /// <summary>Ayarlar butonunu sağ üste, başlığı da ona değmeyecek genişliğe ayarlar.</summary>
    private void PositionHeader()
    {
        int margin = 12;
        btnSettings.Top = (pnlHeader.Height - btnSettings.Height) / 2;
        btnSettings.Left = pnlHeader.ClientSize.Width - btnSettings.Width - margin;
        btnSettings.BringToFront();
        lblTitle.Width = Math.Max(80, btnSettings.Left - lblTitle.Left - 8);
    }

    /// <summary>Gömülü ikon ve başlık logosunu yükler (tek dosya exe'de de çalışır).</summary>
    private void LoadBranding()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        try
        {
            using var ico = asm.GetManifestResourceStream("NotasyonOtomasyonu.App.app.ico");
            if (ico is not null) Icon = new System.Drawing.Icon(ico);
        }
        catch { /* ikon yoksa varsayılan */ }
        try
        {
            using var png = asm.GetManifestResourceStream("NotasyonOtomasyonu.App.logo.png");
            if (png is not null) pbLogo.Image = System.Drawing.Image.FromStream(png);
        }
        catch { /* logo yoksa boş */ }
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        Shown -= MainForm_Shown; // yalnızca ilk açılışta
        if (rbOnline.Checked) await AutoSelectLatestAsync();
    }

    /// <summary>
    /// İlk açılışta: seçili ildeki EN GÜNCEL turnuvayı (en yüksek Tnr ≈ en yeni) bulup
    /// listeyi doldurur, onu seçer ve otomatik çeker. İl seçili değilse/bulunamazsa
    /// hatırlanan turnuvaya düşer. Çevrimdışıysa sessizce geçer.
    /// </summary>
    private async Task AutoSelectLatestAsync()
    {
        string? prov = cboProvince.SelectedItem?.ToString();
        bool hasProvince = !string.IsNullOrWhiteSpace(prov) && prov != Provinces.All;
        try
        {
            if (hasProvince)
            {
                SetBusy(true);
                SetStatus($"{prov} ilindeki güncel turnuvalar yükleniyor…", ok: true);
                var list = await _online.SearchTurkeyAsync("", prov);
                if (list.Count > 0)
                {
                    var ordered = list.OrderByDescending(t => t.Tnr).ToList();
                    _loading = true;
                    cboTournament.Items.Clear();
                    foreach (var tr in ordered)
                        cboTournament.Items.Add(new Item<TournamentRef>($"{tr.Name}  (#{tr.Tnr})", tr));
                    cboTournament.SelectedIndex = 0; // en güncel başta
                    _loading = false;

                    var latest = ordered[0];
                    SetStatus($"{prov} — en güncel: {latest.Name}", ok: true);
                    await LoadEventAsync(latest.Tnr, silent: true, preferRememberedCategory: false);
                    return;
                }
                SetStatus($"{prov} için turnuva bulunamadı. Ara/Getir ile deneyin.", ok: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Otomatik yükleme yapılamadı (çevrimdışı?).", ok: false);
            Log("Uyarı: " + FriendlyNet(ex));
        }
        finally { SetBusy(false); }

        // Düşüş: il yoksa veya bulunamadıysa, en son kullanılan turnuvayı dene.
        if (_config.Online.SelectedTnr is int tnr) await LoadEventAsync(tnr, silent: true);
    }

    // ================= Config <-> UI =================
    private void ApplyConfigToUi()
    {
        _loading = true;

        cboProvince.Items.Clear();
        cboProvince.Items.AddRange(Provinces.List);
        cboProvince.SelectedItem = cboProvince.Items.Contains(_config.Online.Province)
            ? _config.Online.Province : Provinces.All;

        // Eski config'te ada kategori eki sızmış olabilir → temizle (kalıcı düzelir).
        _config.Tournament.Name = EventGrouping.BaseName(_config.Tournament.Name);

        numRound.Value = Clamp(_config.Tournament.LastRound, (int)numRound.Minimum, (int)numRound.Maximum);
        numCopies.Value = Clamp(_config.Layout.CopiesPerBoard, (int)numCopies.Minimum, (int)numCopies.Maximum);
        UpdateInfoLabel();

        txtSearch.Text = _config.Online.CityFilter;

        var o = _config.Online;
        if (o.SelectedTnr is int tnr)
        {
            _eventTnr = tnr;
            cboTournament.Items.Clear();
            var label = o.SelectedTournamentName ?? $"Turnuva {tnr}";
            cboTournament.Items.Add(new Item<TournamentRef>(label, new TournamentRef(tnr, label)));
            cboTournament.SelectedIndex = 0;
        }
        if (o.SelectedCategoryTnr is int ctnr)
        {
            // Tek kategori etiketini geri yükle (Senkron Et ile güncellenecek).
            BuildCategoryButtons(new[] { new CategoryRef(ctnr, o.SelectedCategoryName ?? $"Kategori {ctnr}", true) }, ctnr);
        }

        if (string.Equals(_config.Online.Source, "Dosya", StringComparison.OrdinalIgnoreCase)) rbFile.Checked = true;
        else rbOnline.Checked = true;

        _loading = false;
        UpdateSourceVisibility();
    }

    private void UpdateConfigFromUi()
    {
        // Ad/zaman config'te tutulur (oto-doldurulur ya da Düzenle ile); burada UI'dan okunmaz.
        _config.Online.CityFilter = txtSearch.Text.Trim();
        _config.Online.Province = cboProvince.SelectedItem?.ToString() ?? Provinces.All;
        _config.Online.Source = rbFile.Checked ? "Dosya" : "Online";
        if (rbFile.Checked) _config.Tournament.LastRound = (int)numRound.Value;
    }

    private void UpdateInfoLabel()
    {
        // Sadece notasyon kağıdına basılan ana bilgiyi göster: turnuva adı (kategori eki olmadan).
        // Tarih önce: uzun turnuva adı iki satıra taşınca tarih satırı kutudan taşıp görünmüyordu.
        var n = string.IsNullOrWhiteSpace(_config.Tournament.Name) ? "—" : EventGrouping.BaseName(_config.Tournament.Name);
        var d = string.IsNullOrWhiteSpace(_config.Tournament.Date) ? "" : "Tarih: " + _config.Tournament.Date + "\n";
        lblInfo.AutoEllipsis = true;
        lblInfo.Text = $"{d}Turnuva: {n}";
    }

    private void btnEditInfo_Click(object? sender, EventArgs e)
    {
        using var dlg = new MetaEditForm(_config);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _config.Tournament.ManualOverride = true; // artık senkronda ezilmesin
        _config.Save(_configPath);
        UpdateInfoLabel();
        SetStatus("Turnuva bilgisi güncellendi (kalıcı).", ok: true);
    }

    private void btnExclude_Click(object? sender, EventArgs e)
    {
        // Mevcut çekili masaları önizleme için topla (varsa).
        IReadOnlyList<int> boards = Array.Empty<int>();
        try
        {
            if (rbOnline.Checked && _syncedTournament is not null)
                boards = _syncedTournament.Pairings.Select(p => p.Board).Distinct().OrderBy(x => x).ToList();
        }
        catch { /* önizleme opsiyonel */ }

        using var dlg = new ExclusionsForm(_config, boards);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _config.Save(_configPath);
        ShowPairings(_gridTournament); // varsayılan işaretler yeni hariç tutmaya göre
        var ex = BoardRange.Parse(_config.Layout.ExcludedBoards);
        var parts = new List<string>();
        if (!_config.Layout.PrintByeSheets) parts.Add("BAY hariç");
        if (ex.Count > 0) parts.Add($"{ex.Count} masa hariç");
        SetStatus(parts.Count > 0 ? "Hariç tutma kaydedildi: " + string.Join(", ", parts) + "." : "Hariç tutma temizlendi.", ok: true);
    }

    private void source_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        UpdateSourceVisibility();
    }

    private void UpdateSourceVisibility()
    {
        grpOnline.Visible = rbOnline.Checked;
        grpFile.Visible = !rbOnline.Checked;
        grpQuick.Visible = rbOnline.Checked;
        ShowPairings(rbOnline.Checked ? _syncedTournament : _fileTournament);
    }

    private void cboProvince_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _config.Online.Province = cboProvince.SelectedItem?.ToString() ?? Provinces.All;
    }

    // ================= Online: arama =================
    private async void btnSearch_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        SetStatus("Türkiye turnuvaları aranıyor…", ok: true);
        try
        {
            string? prov = cboProvince.SelectedItem?.ToString();
            if (prov == Provinces.All) prov = null;
            var list = await _online.SearchTurkeyAsync(txtSearch.Text, prov);

            _loading = true;
            cboTournament.Items.Clear();
            foreach (var tr in list)
                cboTournament.Items.Add(new Item<TournamentRef>($"{tr.Name}  (#{tr.Tnr})", tr));
            _loading = false;

            if (cboTournament.Items.Count == 0)
                SetStatus("Eşleşen turnuva yok. Aramayı değiştirin ya da link/no kullanın.", ok: false);
            else { cboTournament.DroppedDown = true; SetStatus($"{cboTournament.Items.Count} turnuva listelendi.", ok: true); }
            UpdateConfigFromUi();
        }
        catch (Exception ex) { Fail("Arama başarısız", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    private async void cboTournament_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (cboTournament.SelectedItem is Item<TournamentRef> item) await LoadEventAsync(item.Value.Tnr);
    }

    // "Gelişmiş: link/no ile getir" — küçük bir giriş penceresi açar (akışı bölmesin diye gizli).
    private async void lnkAdvanced_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        var input = ShowLinkPrompt();
        if (string.IsNullOrWhiteSpace(input)) return;
        var tnr = ChessResultsClient.ParseTnr(input);
        if (tnr is null)
        {
            MessageBox.Show("Geçerli bir chess-results linki veya numarası girin.\nÖrn: 1437263",
                "Geçersiz giriş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await LoadEventAsync(tnr.Value);
    }

    private static string? ShowLinkPrompt()
    {
        using var f = new Form
        {
            Text = "Link / No ile Getir", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
            ClientSize = new System.Drawing.Size(420, 120), Font = new System.Drawing.Font("Segoe UI", 9.75F)
        };
        f.Controls.Add(new Label { Text = "chess-results linki veya turnuva no:", AutoSize = true, Location = new System.Drawing.Point(14, 14) });
        var tb = new TextBox();
        tb.SetBounds(14, 40, 392, 25);
        f.Controls.Add(new Label { Text = "Örn: 1437263  •  https://chess-results.com/Tnr1437263.aspx", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Location = new System.Drawing.Point(14, 68) });
        f.Controls.Add(tb);
        var ok = new Button { Text = "Getir", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(232, 84), Size = new System.Drawing.Size(84, 28) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(322, 84), Size = new System.Drawing.Size(84, 28) };
        f.Controls.Add(ok); f.Controls.Add(cancel);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
    }

    private async Task LoadEventAsync(int tnr, bool silent = false, bool preferRememberedCategory = true)
    {
        int currentRound;
        SetBusy(true);
        SetStatus("Turnuva bilgisi çekiliyor…", ok: true);
        try
        {
            var info = await _online.GetEventAsync(tnr);
            bool isNewEvent = _config.Online.SelectedTnr != tnr;
            _eventTnr = tnr;
            _roundCache[tnr] = (info.MaxRound, info.CurrentRound); // bu etkinliğin kendi turları

            int? preferCat = preferRememberedCategory ? _config.Online.SelectedCategoryTnr : null;
            BuildCategoryButtons(info.Categories, preferCat);
            // Seçili kategorinin GERÇEK turlarına göre tur butonları (kategoriye göre değişebilir).
            currentRound = await UpdateRoundsForCategoryAsync(SelectedCategory()?.Tnr ?? tnr);

            // Farklı turnuva seçildiyse elle override sıfırlanır → çekilen veri kullanılır.
            if (isNewEvent) _config.Tournament.ManualOverride = false;
            var baseName = EventGrouping.BaseName(info.Name); // "… A Kategorisi" ekini at
            ApplyAutoDate(info.Dates);                        // tek tarih (bugün/turnuva günü)
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = baseName;
                UpdateInfoLabel();
            }

            _config.Online.SelectedTnr = tnr;
            _config.Online.SelectedTournamentName = baseName;
            ShowTournamentInCombo(tnr, baseName); // link/no ile açılınca listede eski ad kalmasın

            Log($"Etkinlik yüklendi: {info.Name} (#{tnr})");
        }
        catch (Exception ex)
        {
            if (silent) { SetStatus("Turnuva otomatik yüklenemedi (çevrimdışı?). Ara/Getir ile deneyin.", ok: false); Log("Uyarı: " + FriendlyNet(ex)); }
            else Fail("Turnuva bilgisi alınamadı", FriendlyNet(ex));
            return;
        }
        finally { SetBusy(false); }

        if (currentRound == 0)
        {
            // Başlamamış turnuva: çekilecek eşleştirme yok, hata gibi göstermeyelim.
            _syncedTournament = null;
            ShowPairings(null);
            SetStatus("Bu turnuvada henüz eşleştirme yayımlanmamış (turnuva başlamamış olabilir).", ok: false);
            return;
        }

        // Turnuva seçilir seçilmez varsayılan kategori+turu OTOMATİK çek (kullanıcı butona basmasın).
        // Sonradan kategori/tur değiştirilirse "3) Eşleştirmeleri Çek" ile yeniden çekilir.
        await SyncPairingsAsync(showWarnings: false);
    }

    /// <summary>Turnuva kutusunda yüklü etkinliği gösterir (listede yoksa ekler); seçim olayı tetiklenmez.</summary>
    private void ShowTournamentInCombo(int tnr, string name)
    {
        // Aynı etkinliğin başka kategori tnr'siyle listelenmiş olabilir: ada göre de eşleştir.
        var existing = cboTournament.Items.OfType<Item<TournamentRef>>()
            .FirstOrDefault(i => i.Value.Tnr == tnr || i.Value.Name == name);
        bool prev = _loading;
        _loading = true;
        if (existing is null)
        {
            existing = new Item<TournamentRef>($"{name}  (#{tnr})", new TournamentRef(tnr, name));
            cboTournament.Items.Insert(0, existing);
        }
        cboTournament.SelectedItem = existing;
        _loading = prev;
    }

    // ---- kategori / tur buton grupları ----
    private void BuildCategoryButtons(IReadOnlyList<CategoryRef> cats, int? preferTnr)
    {
        _loading = true;
        flowCategories.Controls.Clear();
        int idx = 0, sel = 0;
        foreach (var c in cats)
        {
            var rb = MakeToggle(ButtonText(c), c);
            rb.Tag = c;
            rb.CheckedChanged += category_CheckedChanged;
            flowCategories.Controls.Add(rb);
            if (preferTnr is int p && c.Tnr == p) sel = idx;
            idx++;
        }
        // Varsayılan: tercih edilen ya da ilk kategori
        var buttons = flowCategories.Controls.OfType<RadioButton>().ToList();
        if (buttons.Count > 0) buttons[Math.Min(sel, buttons.Count - 1)].Checked = true;
        _loading = false;

        BuildQuickButtons(cats); // "A Yazdır / B Yazdır …" kısayolları
    }

    /// <summary>Her kategori için tek tık "yazdır" kısayolu üretir; "Tüm Kategoriler" butonunu açar.</summary>
    private void BuildQuickButtons(IReadOnlyList<CategoryRef> cats)
    {
        flowQuick.Controls.Clear();
        foreach (var c in cats)
        {
            var b = new Button
            {
                Text = ButtonText(c),
                Tag = c,
                AutoSize = true,
                MinimumSize = new System.Drawing.Size(40, 30),
                Margin = new Padding(2),
                Padding = new Padding(6, 2, 6, 2),
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("Segoe UI", 8.5F)
            };
            b.Click += quickCategory_Click;
            StyleButton(b); // hover/pressed cila
            var tip = new ToolTip();
            tip.SetToolTip(b, $"{c.Name} — seçili turu yazdır (kategori o tura gelmediyse son turu)");
            flowQuick.Controls.Add(b);
        }
        btnPrintAll.Enabled = cats.Count > 0;
    }

    // Kategori değişince: o kategorinin turlarını kur ve son eşlenmiş turu KENDİLİĞİNDEN çek.
    private async void category_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (sender is not RadioButton { Checked: true, Tag: CategoryRef c }) return;
        _syncedTournament = null; // kategori değişti → yeniden çekilmeli
        ShowPairings(null);
        int current = await UpdateRoundsForCategoryAsync(c.Tnr);
        if (current == 0)
        {
            SetStatus($"“{ButtonText(c)}”: henüz eşleştirme yayımlanmamış.", ok: false);
            return;
        }
        await SyncPairingsAsync(showWarnings: false);
    }

    // Tur değişince o turu kendiliğinden çek (ayrıca "çek" butonuna basmak gerekmesin).
    private async void round_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading || sender is not RadioButton { Checked: true }) return;
        await SyncPairingsAsync(showWarnings: false);
    }

    /// <summary>
    /// Turnuva tarih ifadesini saklar ve (elle değiştirilmediyse) notasyona basılacak TEK tarihi
    /// varsayılan seçer: bugün turnuva günlerindense bugün, değilse ilk gün.
    /// </summary>
    private void ApplyAutoDate(string? rangeStr)
    {
        _config.Tournament.DateRange = rangeStr;
        if (!_config.Tournament.ManualOverride)
            _config.Tournament.Date = TournamentDates.Format(
                TournamentDates.DefaultPick(TournamentDates.Parse(rangeStr)));
    }

    /// <summary>
    /// Seçili kategorinin turlarını (gerekirse online) bulup tur butonlarını kurar.
    /// Eşlenmiş son turu döndürür (henüz yoksa 0; bilgi alınamadıysa -1).
    /// </summary>
    private async Task<int> UpdateRoundsForCategoryAsync(int catTnr)
    {
        bool busied = false;
        try
        {
            if (!_roundCache.TryGetValue(catTnr, out var rounds))
            {
                SetBusy(true); busied = true;
                var info = await _online.GetEventAsync(catTnr);
                rounds = (info.MaxRound, info.CurrentRound);
                _roundCache[catTnr] = rounds;
            }
            BuildRoundButtons(rounds.Max, rounds.Current);
            return rounds.Current;
        }
        catch { return -1; /* tur bilgisi alınamadıysa mevcut butonlar kalsın */ }
        finally { if (busied) SetBusy(false); }
    }

    private void BuildRoundButtons(int maxRound, int currentRound)
    {
        _loading = true;
        flowRounds.Controls.Clear();
        int max = Math.Max(1, Math.Max(maxRound, currentRound));
        for (int r = 1; r <= max; r++)
        {
            var rb = MakeToggle(r.ToString(), r);
            rb.CheckedChanged += round_CheckedChanged;
            flowRounds.Controls.Add(rb);
        }
        // Varsayılan: eşleştirmesi yayımlanmış SON tur (henüz yoksa 1. tur).
        var buttons = flowRounds.Controls.OfType<RadioButton>().ToList();
        if (buttons.Count > 0) buttons[Math.Clamp(currentRound, 1, buttons.Count) - 1].Checked = true;
        _loading = false;
    }

    private static RadioButton MakeToggle(string text, object tag) => new()
    {
        Appearance = Appearance.Button,
        AutoSize = true,
        MinimumSize = new System.Drawing.Size(30, 28),
        Padding = new Padding(5, 2, 5, 2),
        Margin = new Padding(2),
        Font = new System.Drawing.Font("Segoe UI", 8.5F),
        Text = text,
        Tag = tag,
        TextAlign = System.Drawing.ContentAlignment.MiddleCenter
    };

    /// <summary>Kategori butonu yazısı; tek gruplu turnuvada kategori adı boşsa "Turnuva".</summary>
    private static string ButtonText(CategoryRef c)
    {
        var s = EventGrouping.ShortCategory(c.Name);
        return string.IsNullOrWhiteSpace(s) ? "Turnuva" : s;
    }

    private CategoryRef? SelectedCategory()
        => flowCategories.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked)?.Tag as CategoryRef;

    private int SelectedRound()
    {
        if (rbOnline.Checked)
        {
            var r = flowRounds.Controls.OfType<RadioButton>().FirstOrDefault(x => x.Checked);
            if (r?.Tag is int v) return v;
        }
        return (int)numRound.Value;
    }

    private void AdvanceCategory()
    {
        var buttons = flowCategories.Controls.OfType<RadioButton>().ToList();
        if (buttons.Count < 2) return;
        int cur = buttons.FindIndex(b => b.Checked);
        int next = (cur + 1) % buttons.Count;
        Log($"→ Sıradaki kategori: {ButtonText((CategoryRef)buttons[next].Tag!)}");
        buttons[next].Checked = true; // category_CheckedChanged turu kurar ve eşleştirmeleri çeker
    }

    // ================= Senkron =================
    private async void btnSync_Click(object? sender, EventArgs e) => await SyncPairingsAsync(showWarnings: true);

    /// <summary>
    /// Seçili kategori+turun eşleştirmelerini çeker. <paramref name="showWarnings"/> false ise
    /// (otomatik çekimde) seçim yoksa sessiz çıkar; true ise (butona basınca) uyarı gösterir.
    /// </summary>
    private async Task SyncPairingsAsync(bool showWarnings)
    {
        int? catTnr = SelectedCategory()?.Tnr ?? _eventTnr;
        if (catTnr is null)
        {
            if (showWarnings)
                MessageBox.Show("Önce turnuva (ve kategori) seçin ya da link/no ile getirin.",
                    "Seçim yok", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int round = SelectedRound();
        int seq = ++_syncSeq;
        SetBusy(true);
        SetStatus($"{round}. tur eşleştirmeleri çekiliyor…", ok: true);
        try
        {
            var t = await _online.GetPairingsAsync(catTnr.Value, round);
            if (seq != _syncSeq) return; // bu arada başka kategori/tur seçildi: eski sonucu at
            t = StampCategory(t, EventGrouping.ShortCategory(SelectedCategory()?.Name ?? ""));
            if (t.Pairings.Count == 0)
            {
                SetStatus($"{round}. tur için eşleştirme yok (henüz oluşturulmamış olabilir).", ok: false);
                _syncedTournament = null; ShowPairings(null); return;
            }
            _syncedTournament = t;
            ShowPairings(t);
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = EventGrouping.BaseName(t.Name); // kategori ekini at
                UpdateInfoLabel();
            }

            var cat = SelectedCategory();
            _config.Online.SelectedCategoryTnr = catTnr;
            _config.Online.SelectedCategoryName = cat?.Name;
            _config.Online.SelectedTnr ??= _eventTnr;
            _config.Tournament.LastRound = round;
            _config.Save(_configPath);

            int bye = t.Pairings.Count(p => p.IsBye);
            SetStatus($"{ButtonText(cat ?? new CategoryRef(0, "", true))} • {round}. tur: {t.Pairings.Count} masa" +
                      (bye > 0 ? $" ({bye} BAY)" : "") + ". Listeyi kontrol edip Yazdır'a basın.", ok: true);
            Log($"✓ Çekildi: {t.Name} — {round}. tur, {t.Pairings.Count} masa.");
        }
        catch (Exception ex)
        {
            if (seq != _syncSeq) return;
            if (showWarnings) Fail("Eşleştirme çekilemedi", FriendlyNet(ex));
            else { SetStatus($"{round}. tur eşleştirmeleri çekilemedi — 🔄 (F5) ile tekrar deneyin.", ok: false); Log("Uyarı: " + FriendlyNet(ex)); }
            _syncedTournament = null;
            ShowPairings(null);
        }
        finally { if (seq == _syncSeq) SetBusy(false); }
    }

    // ================= Hızlı yazdır (kısayollar) =================
    private async void quickCategory_Click(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: CategoryRef cat }) return;
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var t = await FetchCategoryAsync(cat);
            if (t.Pairings.Count == 0) { SetStatus($"{cat.Name}: yazdırılacak masa yok.", ok: false); return; }
            PrintTournament(t, _config.Layout.CopiesPerBoard);
        }
        catch (UserMessageException ex) { Fail("Kısayol", ex.Message); }
        catch (Exception ex) { Fail("Kısayol baskısı başarısız", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    private async void btnPrintAll_Click(object? sender, EventArgs e)
    {
        var cats = CurrentCategories();
        if (cats.Count == 0) { SetStatus("Önce bir turnuva yükleyin.", ok: false); return; }
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var all = new List<Pairing>();
            Tournament? first = null;
            int okCats = 0;
            foreach (var cat in cats)
            {
                SetStatus($"Çekiliyor: {cat.Name}…", ok: true);
                Tournament t;
                try { t = await FetchCategoryAsync(cat); }
                catch (InvalidOperationException) // bu kategoride o tur henüz eşlenmemiş
                {
                    Log($"• {cat.Name}: eşleştirme yok, atlandı.");
                    continue;
                }
                if (t.Pairings.Count == 0) { Log($"• {cat.Name}: masa yok, atlandı."); continue; }
                first ??= t;
                all.AddRange(t.Pairings);
                okCats++;
            }
            if (all.Count == 0 || first is null) { SetStatus("Hiçbir kategoride yazdırılacak masa bulunamadı.", ok: false); return; }
            var combined = first with { Pairings = all };
            PrintTournament(combined, _config.Layout.CopiesPerBoard);
            Log($"★ Tüm kategoriler: {okCats} kategori, {all.Count} masa.");
        }
        catch (UserMessageException ex) { Fail("Tüm kategoriler", ex.Message); }
        catch (Exception ex) { Fail("Tüm kategoriler baskısı başarısız", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    private List<CategoryRef> CurrentCategories() =>
        flowCategories.Controls.OfType<RadioButton>().Select(r => r.Tag).OfType<CategoryRef>().ToList();

    /// <summary>Bir kategorinin (son turunun) eşleştirmelerini çekip normalize+hariç tutma+meta uygular.</summary>
    private async Task<Tournament> FetchCategoryAsync(CategoryRef cat)
    {
        int round = await ResolveRoundForCategoryAsync(cat.Tnr);
        var raw = await _online.GetPairingsAsync(cat.Tnr, round);
        var t = Validation.Normalize(raw, _config.Layout.PrintByeSheets);
        t = StampCategory(t, EventGrouping.ShortCategory(cat.Name)); // her kağıt kendi kategorisini taşısın
        // Toplu baskıda kategoriler farklı turda olabilir → her kağıt kendi tur no'sunu taşısın.
        t = t with { Pairings = t.Pairings.Select(p => p with { Round = round }).ToList() };

        var excluded = BoardRange.Parse(_config.Layout.ExcludedBoards);
        if (excluded.Count > 0)
            t = t with { Pairings = t.Pairings.Where(p => !excluded.Contains(p.Board)).ToList() };

        return ApplyConfigMeta(t);
    }

    /// <summary>Tüm eşleştirmelere kategori kısa adını işler (notasyonda "Kategori" alanı için).</summary>
    private static Tournament StampCategory(Tournament t, string? catShort)
    {
        if (string.IsNullOrWhiteSpace(catShort)) return t;
        return t with { Pairings = t.Pairings.Select(p => p with { Category = catShort }).ToList() };
    }

    /// <summary>
    /// Kategorinin yazdırılacak turu: kullanıcının seçtiği tur; kategori henüz o tura
    /// gelmediyse (eşleştirmesi yoksa) o kategorinin eşlenmiş son turu.
    /// </summary>
    private async Task<int> ResolveRoundForCategoryAsync(int catTnr)
    {
        int sel = SelectedRound();
        if (!_roundCache.TryGetValue(catTnr, out var rounds))
        {
            try
            {
                var info = await _online.GetEventAsync(catTnr);
                rounds = (info.MaxRound, info.CurrentRound);
                _roundCache[catTnr] = rounds;
            }
            catch { return Math.Max(1, sel); }
        }
        int last = rounds.Current > 0 ? rounds.Current : rounds.Max;
        return Math.Max(1, Math.Min(sel, Math.Max(1, last)));
    }

    // ================= Dosya =================
    private void btnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Eşleştirme dosyası seç", Filter = ParserFactory.FileDialogFilter };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        txtFile.Text = dlg.FileName;
        TryPreviewFile(dlg.FileName);
    }

    private void TryPreviewFile(string file)
    {
        try
        {
            UpdateConfigFromUi();
            var t = ParserFactory.Parse(file, _config);
            if (t.RoundNo >= numRound.Minimum && t.RoundNo <= numRound.Maximum) numRound.Value = t.RoundNo;
            _fileTournament = t;
            ShowPairings(t);
            if (!_config.Tournament.ManualOverride && !string.IsNullOrWhiteSpace(t.Name))
            {
                _config.Tournament.Name = EventGrouping.BaseName(t.Name);
                if (!string.IsNullOrWhiteSpace(t.Date)) ApplyAutoDate(t.Date); // dosyadaki tarih
                UpdateInfoLabel();
            }
            int bye = t.Pairings.Count(p => p.IsBye);
            SetStatus($"{t.Pairings.Count} masa bulundu" + (bye > 0 ? $" ({bye} BAY)" : "") + ".", ok: true);
        }
        catch (Exception ex)
        {
            _fileTournament = null;
            ShowPairings(null);
            SetStatus("Dosya okunamadı.", ok: false);
            Log("✗ " + ex.Message);
            MessageBox.Show(this, ex.Message, "Dosya okunamadı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= Yazdır =================
    private void btnPrint_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var baseT = BuildTournament();
            bool printed = PrintTournament(baseT, _config.Layout.CopiesPerBoard);
            _config.Save(_configPath);
            // Yalnızca gerçekten basıldıysa sıradaki kategoriye geç (önizlemeyi kapatmak baskı değildir).
            if (printed && rbOnline.Checked) AdvanceCategory();
        }
        catch (UserMessageException ex) { Fail("Yazdırılamadı", ex.Message); }
        catch (ParseException ex) { Fail("Veri okunamadı", ex.Message); }
        catch (Exception ex) { Fail("Yazdırma hatası", ex.Message); }
        finally { SetBusy(false); }
    }

    // ================= PDF =================
    private async void btnPdf_Click(object? sender, EventArgs e)
    {
        try
        {
            UpdateConfigFromUi();
            var t = ExpandCopies(BuildTournament(), _config.Layout.CopiesPerBoard);
            SetBusy(true);
            SetStatus("PDF hazırlanıyor…", ok: true);
            var path = await Task.Run(() => RenderToPdf(t));
            OpenWithShell(path);
            SetStatus($"PDF açıldı: {Path.GetFileName(path)} ({t.Pairings.Count} kağıt)", ok: true);
            Log($"📄 PDF: {path}");
        }
        catch (UserMessageException ex) { Fail("PDF oluşturulamadı", ex.Message); }
        catch (Exception ex) { Fail("PDF hatası", ex.Message); }
        finally { SetBusy(false); }
    }

    // ================= Basılacak masalar listesi =================
    /// <summary>
    /// Sağdaki listeyi doldurur. Varsayılan işaret: "Hariç Tut"ta seçilmemiş ve (BAY ise) BAY kağıdı
    /// açıksa basılır. Kullanıcı işaretleri bu tur için değiştirebilir.
    /// </summary>
    private void ShowPairings(Tournament? t)
    {
        _gridTournament = t is null ? null : Validation.Normalize(t, keepByeSheets: true);
        dgvPairings.SuspendLayout();
        dgvPairings.Rows.Clear();
        if (_gridTournament is not null)
        {
            var excluded = BoardRange.Parse(_config.Layout.ExcludedBoards);
            foreach (var p in _gridTournament.Pairings)
            {
                bool print = !excluded.Contains(p.Board) && (!p.IsBye || _config.Layout.PrintByeSheets);
                int i = dgvPairings.Rows.Add(print, p.Board, p.White.Name, p.White.Rating?.ToString() ?? "",
                    p.IsBye ? "BAY" : p.Black!.Name, p.Black?.Rating?.ToString() ?? "");
                var row = dgvPairings.Rows[i];
                row.Tag = p;
                if (p.IsBye)
                {
                    row.DefaultCellStyle.ForeColor = System.Drawing.Color.Gray;
                    row.DefaultCellStyle.Font = new System.Drawing.Font(dgvPairings.Font, System.Drawing.FontStyle.Italic);
                }
            }
        }
        dgvPairings.ResumeLayout();
        dgvPairings.ClearSelection();

        var cat = rbOnline.Checked ? SelectedCategory() : null;
        grpPairings.Text = _gridTournament is null
            ? "3) Basılacak masalar"
            : $"3) Basılacak masalar — " +
              (cat is null ? "" : ButtonText(cat) + " • ") +
              (rbOnline.Checked ? $"{_gridTournament.RoundNo}. tur" : $"{numRound.Value}. tur (dosya)");
        UpdateSelectionSummary();
    }

    private HashSet<int> SelectedBoards()
    {
        var set = new HashSet<int>();
        foreach (DataGridViewRow r in dgvPairings.Rows)
            if (r.Cells[0].Value is true && r.Tag is Pairing p) set.Add(p.Board);
        return set;
    }

    private void UpdateSelectionSummary()
    {
        int total = dgvPairings.Rows.Count;
        int n = SelectedBoards().Count;
        int copies = (int)numCopies.Value;
        int sheets = n * copies;
        if (total == 0)
        {
            lblSelection.Text = rbOnline.Checked ? "Eşleştirme yok — turnuva/kategori/tur seçin." : "Dosya seçin.";
            btnPrint.Text = "🖨  Yazdır";
        }
        else
        {
            lblSelection.Text = $"{n} / {total} masa işaretli • {sheets} kağıt";
            btnPrint.Text = n > 0 ? $"🖨  Yazdır — {sheets} kağıt" : "🖨  Yazdır (masa seçilmedi)";
        }
    }

    private void SetAllChecks(bool value)
    {
        foreach (DataGridViewRow r in dgvPairings.Rows) r.Cells[0].Value = value;
        UpdateSelectionSummary();
    }

    private void btnSelectAll_Click(object? sender, EventArgs e) => SetAllChecks(true);
    private void btnSelectNone_Click(object? sender, EventArgs e) => SetAllChecks(false);

    /// <summary>Boşluk: seçili satırların işaretini tersine çevirir (birden çok satır seçilebilir).</summary>
    private void dgvPairings_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || dgvPairings.SelectedRows.Count <= 1) return;
        bool target = dgvPairings.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Cells[0].Value is not true);
        foreach (DataGridViewRow r in dgvPairings.SelectedRows) r.Cells[0].Value = target;
        e.Handled = true;
        UpdateSelectionSummary();
    }

    private void numCopies_ValueChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _config.Layout.CopiesPerBoard = (int)numCopies.Value;
        UpdateSelectionSummary();
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.P && btnPrint.Enabled) { e.SuppressKeyPress = true; btnPrint.PerformClick(); }
        else if (e.KeyCode == Keys.F5 && rbOnline.Checked && btnSync.Enabled) { e.SuppressKeyPress = true; btnSync.PerformClick(); }
    }

    /// <summary>Önizleme açar; true = kullanıcı yazıcıya gönderdi.</summary>
    private bool PrintTournament(Tournament baseT, int copies)
    {
        var t = ExpandCopies(baseT, copies);
        if (!_config.Overlay.IsConfigured)
            throw new UserMessageException("Önce ⚙ Ayarlar'dan hazır kağıt şablonu tasarlayın.");
        SetStatus("Önizleme açılıyor…", ok: true);
        var printer = new Overlay.SheetPrinter(_config.Overlay, t, _config.Layout.PageSize,
            _config.Layout.PrintOffsetXmm, _config.Layout.PrintOffsetYmm);
        bool printed = printer.PrintWithPreview(this);
        if (printed)
        {
            SetStatus($"✓ {t.Pairings.Count} kağıt yazıcıya gönderildi.", ok: true);
            Log($"🖨 Basıldı: {baseT.Name} — {DescribeRounds(baseT)}, {baseT.Pairings.Count} masa × {copies} = {t.Pairings.Count} kağıt.");
        }
        else SetStatus("Önizleme kapatıldı; baskı yapılmadı.", ok: true);
        return printed;
    }

    private static string DescribeRounds(Tournament t)
    {
        var rounds = t.Pairings.Select(p => p.Round ?? t.RoundNo).Distinct().OrderBy(r => r).ToList();
        return rounds.Count == 1 ? $"{rounds[0]}. tur" : $"{string.Join("/", rounds)}. turlar";
    }

    // ================= Ortak üretim =================
    /// <summary>Sağdaki listede İŞARETLİ masalardan basılacak turnuvayı kurar.</summary>
    private Tournament BuildTournament()
    {
        var src = _gridTournament ?? throw new UserMessageException(rbOnline.Checked
            ? "Önce turnuva, kategori ve tur seçin; eşleştirmeler sağdaki listede görünmeli."
            : "Lütfen önce geçerli bir eşleştirme dosyası seçin.");
        if (!rbOnline.Checked) src = src with { RoundNo = (int)numRound.Value };

        var t = ApplyConfigMeta(src);

        var validation = Validation.Validate(t);
        if (!validation.IsValid) throw new UserMessageException(string.Join("\n", validation.Errors));

        var boards = SelectedBoards();
        t = t with { Pairings = t.Pairings.Where(p => boards.Contains(p.Board)).ToList() };

        if (t.Pairings.Count == 0)
            throw new UserMessageException("Basılacak masa işaretlenmedi. Sağdaki listeden masaları işaretleyin.");
        return t;
    }

    /// <summary>Config'teki turnuva adı/tarih bilgisini (varsa) kaynağa uygular.
    /// Turnuva adından kategori eki ("… A Kategorisi") HER ZAMAN atılır.</summary>
    private Tournament ApplyConfigMeta(Tournament src) => src with
    {
        Name = EventGrouping.BaseName(string.IsNullOrWhiteSpace(_config.Tournament.Name) ? src.Name : _config.Tournament.Name),
        // Tek tarih (config'te seçilen/varsayılan); yoksa kaynaktan tek güne indir.
        Date = !string.IsNullOrWhiteSpace(_config.Tournament.Date)
            ? _config.Tournament.Date
            : TournamentDates.Format(TournamentDates.DefaultPick(TournamentDates.Parse(src.Date))),
    };

    /// <summary>Her masayı <paramref name="copies"/> kez tekrarlar (iki oyuncuya da aynı notasyon).</summary>
    private static Tournament ExpandCopies(Tournament t, int copies)
    {
        copies = Math.Max(1, copies);
        if (copies == 1) return t;
        var expanded = new List<Pairing>(t.Pairings.Count * copies);
        foreach (var p in t.Pairings)
            for (int i = 0; i < copies; i++) expanded.Add(p);
        return t with { Pairings = expanded };
    }

    private string RenderToPdf(Tournament t, string suffix = "")
    {
        if (!_config.Overlay.IsConfigured)
            throw new UserMessageException("Önce ⚙ Ayarlar'dan hazır kağıt şablonu tasarlayın.");
        var path = Path.Combine(_outputDir, BuildFileName(t, suffix + "_overlay"));
        Overlay.OverlayPdfRenderer.Render(_config.Overlay, t, path, _config.Layout.PageSize);
        return path;
    }

    // ================= Ayarlar =================
    private void btnSettings_Click(object? sender, EventArgs e)
    {
        UpdateConfigFromUi();
        using var dlg = new SettingsForm(_config);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _config.Save(_configPath);
            UpdateInfoLabel();
            if (!_config.Overlay.IsConfigured)
                SetStatus("Hazır kağıt şablonu henüz tanımlı değil.", ok: false);
            else SetStatus("Ayarlar kaydedildi.", ok: true);
        }
    }

    // ================= Kapat =================
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        UpdateConfigFromUi();
        _config.Save(_configPath);
        _online.Dispose();
    }

    // ================= Yardımcılar =================
    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Açılamadı: " + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string BuildFileName(Tournament t, string suffix = "")
    {
        var slug = Slugify(t.Name);
        if (slug.Length == 0) slug = "turnuva";
        return $"{slug}_tur{t.RoundNo}_notasyon{suffix}.pdf";
    }

    private static string Slugify(string s)
    {
        s = s.Trim().ToLowerInvariant();
        var map = new Dictionary<char, char>
        { ['ç'] = 'c', ['ğ'] = 'g', ['ı'] = 'i', ['ö'] = 'o', ['ş'] = 's', ['ü'] = 'u', ['â'] = 'a', ['î'] = 'i' };
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            var c = map.TryGetValue(ch, out var r) ? r : ch;
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    private static string FriendlyNet(Exception ex) => ex switch
    {
        HttpRequestException => "İnternet bağlantısı kurulamadı. Bağlantınızı kontrol edin.",
        TaskCanceledException => "İstek zaman aşımına uğradı. Tekrar deneyin.",
        _ => ex.Message
    };

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

    private void SetBusy(bool busy)
    {
        foreach (Control c in new Control[] { btnPrint, btnPdf, btnBrowse, btnSearch, btnSync, cboTournament, cboProvince, flowQuick })
            c.Enabled = !busy;
        btnPrintAll.Enabled = !busy && flowQuick.Controls.Count > 0;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void SetStatus(string text, bool ok)
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = ok ? System.Drawing.Color.FromArgb(95, 122, 70) : System.Drawing.Color.FromArgb(160, 60, 60);
    }

    private void Log(string line) => txtLog.AppendText(line + Environment.NewLine);

    private void Fail(string title, string message)
    {
        SetStatus(title, ok: false);
        Log("✗ " + message);
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed class Item<T>
    {
        public string Text { get; }
        public T Value { get; }
        public Item(string text, T value) { Text = text; Value = value; }
        public override string ToString() => Text;
    }

    private sealed class UserMessageException : Exception
    {
        public UserMessageException(string message) : base(message) { }
    }
}
