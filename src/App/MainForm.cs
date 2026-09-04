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

    private Tournament? _syncedTournament;
    private int? _eventTnr;
    private bool _loading;

    /// <summary>Kategori (Tnr) → o kategorinin tur sayısı. Her kategori farklı tura sahip olabilir.</summary>
    private readonly Dictionary<int, int> _roundCache = new();

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

        var tip = new ToolTip();
        tip.SetToolTip(btnSync, "Eşleştirmeleri yeniden çek (otomatik çekilir; manuel yenileme)");

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
        var n = string.IsNullOrWhiteSpace(_config.Tournament.Name) ? "—" : EventGrouping.BaseName(_config.Tournament.Name);
        var d = string.IsNullOrWhiteSpace(_config.Tournament.Date) ? "" : "\nTarih: " + _config.Tournament.Date;
        lblInfo.Text = $"Turnuva: {n}{d}";
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
        SetBusy(true);
        SetStatus("Turnuva bilgisi çekiliyor…", ok: true);
        try
        {
            var info = await _online.GetEventAsync(tnr);
            bool isNewEvent = _config.Online.SelectedTnr != tnr;
            _eventTnr = tnr;
            _roundCache[tnr] = info.MaxRound;                 // bu etkinliğin kendi tur sayısı

            int? preferCat = preferRememberedCategory ? _config.Online.SelectedCategoryTnr : null;
            BuildCategoryButtons(info.Categories, preferCat);
            // Seçili kategorinin GERÇEK tur sayısına göre tur butonları (kategoriye göre değişebilir).
            await UpdateRoundsForCategoryAsync(SelectedCategory()?.Tnr ?? tnr);

            // Farklı turnuva seçildiyse elle override sıfırlanır → çekilen veri kullanılır.
            if (isNewEvent) _config.Tournament.ManualOverride = false;
            var baseName = EventGrouping.BaseName(info.Name); // "… A Kategorisi" ekini at
            ApplyAutoDate(info.Dates);                        // tek tarih (bugün/turnuva günü)
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = baseName;
                if (!string.IsNullOrWhiteSpace(info.TimeControl)) _config.Tournament.TimeControl = info.TimeControl!;
                UpdateInfoLabel();
            }

            _config.Online.SelectedTnr = tnr;
            _config.Online.SelectedTournamentName = baseName;

            Log($"Etkinlik yüklendi: {info.Name} (#{tnr})");
        }
        catch (Exception ex)
        {
            if (silent) { SetStatus("Turnuva otomatik yüklenemedi (çevrimdışı?). Ara/Getir ile deneyin.", ok: false); Log("Uyarı: " + FriendlyNet(ex)); }
            else Fail("Turnuva bilgisi alınamadı", FriendlyNet(ex));
            return;
        }
        finally { SetBusy(false); }

        // Turnuva seçilir seçilmez varsayılan kategori+turu OTOMATİK çek (kullanıcı butona basmasın).
        // Sonradan kategori/tur değiştirilirse "3) Eşleştirmeleri Çek" ile yeniden çekilir.
        await SyncPairingsAsync(showWarnings: false);
    }

    // ---- kategori / tur buton grupları ----
    private void BuildCategoryButtons(IReadOnlyList<CategoryRef> cats, int? preferTnr)
    {
        _loading = true;
        flowCategories.Controls.Clear();
        int idx = 0, sel = 0;
        foreach (var c in cats)
        {
            var rb = MakeToggle(ShortCategory(c.Name), c);
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
                Text = ShortCategory(c.Name),
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
            tip.SetToolTip(b, $"{c.Name} — son turu yazdır");
            flowQuick.Controls.Add(b);
        }
        btnPrintAll.Enabled = cats.Count > 0;
    }

    // Kategori değişince: o kategorinin gerçek tur sayısına göre tur butonlarını yenile.
    private async void category_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (sender is not RadioButton { Checked: true, Tag: CategoryRef c }) return;
        _syncedTournament = null; // kategori değişti → yeniden çekilmeli
        await UpdateRoundsForCategoryAsync(c.Tnr);
        SetStatus($"“{c.Name}” seçildi. Tur seçip 3) Eşleştirmeleri Çek'e basın.", ok: true);
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

    /// <summary>Seçili kategorinin tur sayısını (gerekirse online) bulup tur butonlarını kurar.</summary>
    private async Task UpdateRoundsForCategoryAsync(int catTnr)
    {
        bool busied = false;
        try
        {
            if (!_roundCache.TryGetValue(catTnr, out int maxRound))
            {
                SetBusy(true); busied = true;
                var info = await _online.GetEventAsync(catTnr);
                maxRound = info.MaxRound;
                _roundCache[catTnr] = maxRound;
            }
            BuildRoundButtons(maxRound);
        }
        catch { /* tur sayısı alınamadıysa mevcut butonlar kalsın */ }
        finally { if (busied) SetBusy(false); }
    }

    private void BuildRoundButtons(int maxRound)
    {
        _loading = true;
        flowRounds.Controls.Clear();
        for (int r = 1; r <= Math.Max(1, maxRound); r++)
            flowRounds.Controls.Add(MakeToggle(r.ToString(), r));
        // Varsayılan: her zaman SON tur
        var buttons = flowRounds.Controls.OfType<RadioButton>().ToList();
        if (buttons.Count > 0) buttons[^1].Checked = true;
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

    /// <summary>"A Kategorisi" → "A", "8 Yaş Kategorisi" → "8 Yaş" (butona sığsın).</summary>
    private static string ShortCategory(string name)
    {
        var s = name.Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*Kategorisi\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*Kategori\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s.Length == 0 ? name : s;
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
        _loading = true;
        buttons[next].Checked = true;
        _loading = false;
        _syncedTournament = null; // kategori değişti → yeniden çekilmeli
        var cat = SelectedCategory();
        if (cat is not null) _ = UpdateRoundsForCategoryAsync(cat.Tnr); // yeni kategorinin tur sayısı
        SetStatus($"Sıradaki kategori seçildi: {cat?.Name}. 3) Eşleştirmeleri Çek'e basın.", ok: true);
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
        SetBusy(true);
        SetStatus($"{round}. tur eşleştirmeleri çekiliyor…", ok: true);
        try
        {
            var t = await _online.GetPairingsAsync(catTnr.Value, round);
            t = StampCategory(t, ShortCategory(SelectedCategory()?.Name ?? ""));
            if (t.Pairings.Count == 0)
            {
                SetStatus($"{round}. tur için eşleştirme yok (henüz oluşturulmamış olabilir).", ok: false);
                _syncedTournament = null; return;
            }
            _syncedTournament = t;
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = EventGrouping.BaseName(t.Name); // kategori ekini at
                if (!string.IsNullOrWhiteSpace(t.TimeControl)) _config.Tournament.TimeControl = t.TimeControl!;
                UpdateInfoLabel();
            }

            var cat = SelectedCategory();
            _config.Online.SelectedCategoryTnr = catTnr;
            _config.Online.SelectedCategoryName = cat?.Name;
            _config.Online.SelectedTnr ??= _eventTnr;
            _config.Tournament.LastRound = round;
            _config.Save(_configPath);

            int bye = t.Pairings.Count(p => p.IsBye);
            SetStatus($"{t.Pairings.Count} masa çekildi" + (bye > 0 ? $" ({bye} BAY)" : "") + ". Yazdır hazır.", ok: true);
            Log($"✓ Çekildi: {t.Name} — {round}. tur, {t.Pairings.Count} masa.");
        }
        catch (Exception ex)
        {
            if (showWarnings) Fail("Eşleştirme çekilemedi", FriendlyNet(ex));
            else { SetStatus("Eşleştirmeler otomatik çekilemedi — 3) Eşleştirmeleri Çek'e basın.", ok: false); Log("Uyarı: " + FriendlyNet(ex)); }
            _syncedTournament = null;
        }
        finally { SetBusy(false); }
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
            await PrintTournament(t, _config.Layout.CopiesPerBoard);
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
                var t = await FetchCategoryAsync(cat);
                if (t.Pairings.Count == 0) { Log($"• {cat.Name}: masa yok, atlandı."); continue; }
                first ??= t;
                all.AddRange(t.Pairings);
                okCats++;
            }
            if (all.Count == 0 || first is null) { SetStatus("Hiçbir kategoride yazdırılacak masa bulunamadı.", ok: false); return; }
            var combined = first with { Pairings = all };
            await PrintTournament(combined, _config.Layout.CopiesPerBoard);
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
        t = StampCategory(t, ShortCategory(cat.Name)); // her kağıt kendi kategorisini taşısın

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

    /// <summary>Kategorinin yazdırılacak turu: kullanıcının seçtiği tur, o kategorinin son turunu aşmayacak şekilde.</summary>
    private async Task<int> ResolveRoundForCategoryAsync(int catTnr)
    {
        if (!_roundCache.TryGetValue(catTnr, out int max))
        {
            try { max = (await _online.GetEventAsync(catTnr)).MaxRound; _roundCache[catTnr] = max; }
            catch { max = SelectedRound(); }
        }
        int sel = SelectedRound();
        return Math.Max(1, Math.Min(sel, Math.Max(1, max)));
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
            if (!_config.Tournament.ManualOverride && !string.IsNullOrWhiteSpace(t.Name))
            {
                _config.Tournament.Name = EventGrouping.BaseName(t.Name);
                if (!string.IsNullOrWhiteSpace(t.TimeControl)) _config.Tournament.TimeControl = t.TimeControl!;
                UpdateInfoLabel();
            }
            int bye = t.Pairings.Count(p => p.IsBye);
            SetStatus($"{t.Pairings.Count} masa bulundu" + (bye > 0 ? $" ({bye} BAY)" : "") + ".", ok: true);
        }
        catch (Exception ex) { SetStatus("Dosya önizlenemedi.", ok: false); Log("Uyarı: " + ex.Message); }
    }

    // ================= Yazdır =================
    private async void btnPrint_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var baseT = BuildTournament();
            await PrintTournament(baseT, _config.Layout.CopiesPerBoard);
            if (rbOnline.Checked) AdvanceCategory(); // sıradaki kategori default
            _config.Save(_configPath);
        }
        catch (UserMessageException ex) { Fail("Yazdırılamadı", ex.Message); }
        catch (ParseException ex) { Fail("Veri okunamadı", ex.Message); }
        catch (Exception ex) { Fail("Yazdırma hatası", ex.Message); }
        finally { SetBusy(false); }
    }

    // ================= Özel / detaylı baskı =================
    private async void btnSpecial_Click(object? sender, EventArgs e)
    {
        try
        {
            UpdateConfigFromUi();
            var baseT = BuildTournament(); // mevcut kategori/turun verisi
            var allBoards = baseT.Pairings.Select(p => p.Board).Distinct().OrderBy(x => x).ToList();

            using var dlg = new SpecialPrintForm(
                title: $"{baseT.Name} — {baseT.RoundNo}. tur",
                availableBoards: allBoards,
                defaultCopies: _config.Layout.CopiesPerBoard);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var subset = dlg.Boards.Count == 0
                ? baseT
                : baseT with { Pairings = baseT.Pairings.Where(p => dlg.Boards.Contains(p.Board)).ToList() };

            if (subset.Pairings.Count == 0) { Fail("Özel baskı", "Seçilen masalar bulunamadı."); return; }

            SetBusy(true);
            if (dlg.DoPrint)
            {
                await PrintTournament(subset, dlg.Copies);
                SetStatus($"Özel baskı: {subset.Pairings.Count} masa × {dlg.Copies} nüsha gönderildi.", ok: true);
            }
            else
            {
                var t = ExpandCopies(subset, dlg.Copies);
                var path = await Task.Run(() => RenderToPdf(t, suffix: "_ozel"));
                OpenWithShell(path);
                SetStatus($"Özel baskı PDF açıldı: {Path.GetFileName(path)}", ok: true);
                Log($"✓ Özel baskı PDF: {path}");
            }
        }
        catch (UserMessageException ex) { Fail("Özel baskı yapılamadı", ex.Message); }
        catch (Exception ex) { Fail("Özel baskı hatası", ex.Message); }
        finally { SetBusy(false); }
    }

    private Task PrintTournament(Tournament baseT, int copies)
    {
        var t = ExpandCopies(baseT, copies);
        if (!_config.Overlay.IsConfigured)
            throw new UserMessageException("Önce ⚙ Ayarlar'dan hazır kağıt şablonu tasarlayın.");
        SetStatus("Önizleme açılıyor…", ok: true);
        var printer = new Overlay.SheetPrinter(_config.Overlay, t, _config.Layout.PageSize);
        printer.PrintWithPreview(this);
        SetStatus($"{t.Pairings.Count} kağıt hazır (önizleme).", ok: true);
        Log($"🖨 Önizleme/baskı: {baseT.Name} — {baseT.RoundNo}. tur.");
        return Task.CompletedTask;
    }

    // ================= Ortak üretim =================
    private Tournament BuildTournament()
    {
        Tournament src;
        if (rbOnline.Checked)
        {
            if (_syncedTournament is null)
                throw new UserMessageException("Önce '3) Eşleştirmeleri Çek' ile eşleştirmeleri çekin.");
            src = _syncedTournament;
        }
        else
        {
            var file = txtFile.Text.Trim();
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                throw new UserMessageException("Lütfen önce geçerli bir eşleştirme dosyası seçin.");
            src = ParserFactory.Parse(file, _config) with { RoundNo = (int)numRound.Value };
        }

        var t = ApplyConfigMeta(src);

        var validation = Validation.Validate(t);
        if (!validation.IsValid) throw new UserMessageException(string.Join("\n", validation.Errors));

        t = Validation.Normalize(t, _config.Layout.PrintByeSheets);

        // "Hariç Tut" ekranından seçilen masaları çıkar.
        var excluded = BoardRange.Parse(_config.Layout.ExcludedBoards);
        if (excluded.Count > 0)
            t = t with { Pairings = t.Pairings.Where(p => !excluded.Contains(p.Board)).ToList() };

        if (t.Pairings.Count == 0)
            throw new UserMessageException("İşlenecek masa kalmadı (BAY veya hariç tutulan masalar olabilir).");
        return t;
    }

    /// <summary>Config'teki turnuva adı/zaman/hakem bilgisini (varsa) kaynağa uygular.
    /// Turnuva adından kategori eki ("… A Kategorisi") HER ZAMAN atılır.</summary>
    private Tournament ApplyConfigMeta(Tournament src) => src with
    {
        Name = EventGrouping.BaseName(string.IsNullOrWhiteSpace(_config.Tournament.Name) ? src.Name : _config.Tournament.Name),
        TimeControl = string.IsNullOrWhiteSpace(_config.Tournament.TimeControl) ? src.TimeControl : _config.Tournament.TimeControl,
        Arbiter = string.IsNullOrWhiteSpace(_config.Tournament.Arbiter) ? src.Arbiter : _config.Tournament.Arbiter,
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
        foreach (Control c in new Control[] { btnPrint, btnSpecial, btnBrowse, btnSearch, btnSync, cboTournament, cboProvince, flowQuick })
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
