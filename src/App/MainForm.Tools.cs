using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Reports;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>Başlıktaki ek araçlar: turnuva yönergesi/raporu ve kategori masa kartları.</summary>
public partial class MainForm
{
    private Button? _btnReport;
    private Button? _btnCards;

    private void InitToolButtons()
    {
        _btnReport = HeaderButton("📄 Yönerge / Rapor", "Turnuva yönergesi/raporunu chess-results verisiyle doldurup PDF olarak kaydet.");
        _btnCards = HeaderButton("🏷 Masa Kartları", "Kategori masa kartlarını (A4, kategori renginde) bas.");
        _btnReport.Click += (_, _) => OpenReport();
        _btnCards.Click += (_, _) => OpenCards();
    }

    private Button HeaderButton(string text, string tip)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = btnSettings.Height,
            MinimumSize = new Size(0, btnSettings.Height),
            Padding = new Padding(6, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ThGreenDark,
            Font = btnSettings.Font,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = ThGreenDark;
        new ToolTip().SetToolTip(b, tip);
        pnlHeader.Controls.Add(b);
        return b;
    }

    /// <summary>Ayarlar'ın soluna araç butonlarını dizer (PositionHeader'dan çağrılır).</summary>
    private int PositionToolButtons(int right)
    {
        foreach (var b in new[] { _btnCards, _btnReport })
        {
            if (b is null) continue;
            b.Top = (pnlHeader.Height - b.Height) / 2;
            b.Left = right - b.Width - 8;
            b.BringToFront();
            right = b.Left;
        }
        return right;
    }

    /// <summary>Etkinliğin kategorileri; kategori menüsü yoksa etkinliğin kendisi tek kategori sayılır.</summary>
    private List<CategoryRef>? ToolCategories()
    {
        if (!rbOnline.Checked || _eventTnr is not int tnr)
        {
            MessageBox.Show(this, "Önce chess-results'tan bir turnuva seçin.", "Turnuva seçilmedi",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        var cats = CurrentCategories();
        if (cats.Count == 0)
        {
            var name = _config.Online.SelectedTournamentName ?? $"Turnuva {tnr}";
            cats.Add(new CategoryRef(tnr, EventGrouping.CategoryFromEventName(name) is { Length: > 0 } c ? c : "Genel", true));
        }
        return cats;
    }

    private string CurrentEventName()
        => _config.Online.SelectedTournamentName ?? _config.Tournament.Name ?? "";

    private async Task<(int Max, int Current, TournamentSystem System)> RoundsOfAsync(int catTnr)
    {
        if (_roundCache.TryGetValue(catTnr, out var r)) return r;
        var info = await _online.GetEventAsync(catTnr);
        r = (info.MaxRound, info.CurrentRound, info.System);
        _roundCache[catTnr] = r;
        return r;
    }

    /// <summary>Turnuva ili: seçili il; yoksa turnuva adında geçen il.</summary>
    private string? CurrentProvince()
    {
        var sel = cboProvince.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(sel) && sel != Provinces.All) return sel;
        return ReportBuilder.DetectProvince(CurrentEventName()); // yoksa rapor turnuva yerinden de dener
    }

    private void OpenReport()
    {
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new ReportContext(
            _online, _eventTnr!.Value, CurrentEventName(), cats, CurrentProvince(),
            async t => { var r = await RoundsOfAsync(t); return (r.Max, r.System); },
            _config, () => _config.Save(_configPath), AppContext.BaseDirectory, _outputDir);
        using var f = new ReportForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    private void OpenCards()
    {
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new CardsContext(CurrentEventName(), cats, BoardCountAsync,
            _config, () => _config.Save(_configPath), AppContext.BaseDirectory, _outputDir);
        using var f = new CardsForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    /// <summary>Kategorinin masa sayısı: son eşlenen turdaki (başlamadıysa 1. tur) BAY dışı masalar.</summary>
    private async Task<int> BoardCountAsync(CategoryRef c)
    {
        var r = await RoundsOfAsync(c.Tnr);
        int round = r.Current > 0 ? r.Current : 1;
        var t = await _online.GetPairingsAsync(c.Tnr, round, r.System);
        return t.Pairings.Count(p => !p.IsBye);
    }
}
