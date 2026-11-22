using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Lists;

/// <summary>Ceza / itiraz penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record IncidentsContext(
    int EventTnr,
    string EventName,
    IReadOnlyList<string> Categories,
    string? DefaultCategory,
    int DefaultRound,
    string? DefaultArbiter,
    Func<string, int, string, Task<(string White, string Black)?>>? LookupBoard,   // kategori, tur, masa → oyuncular
    AppConfig Config,
    Action SaveConfig);

/// <summary>
/// Turnuvada verilen cezalar ve yapılan itirazlar. Kayıtlar turnuvaya göre saklanır; Ceza Tutanağı, İtiraz
/// Tutanağı ve Turnuva Sonu Raporu ({{ceza.…}}, {{itiraz.…}}, CEZA_SAYISI, ITIRAZ_SAYISI) bunlarla dolar.
/// Kategori, tur ve masa girilince sporcu eşleştirmeden önerilir ("⇄" ile beyaz / siyah değişir).
/// </summary>
public sealed class IncidentsForm : AppForm
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);

    /// <summary>Neden / konu sütununda önerilenler (serbest yazılabilir).</summary>
    public static readonly string[] Reasons =
    {
        "Kuraldışı hamle", "İkinci kuraldışı hamle", "Elektronik cihaz bulundurma", "Rakibi rahatsız etme",
        "Dokunulan taşı oynamama", "Notasyon tutmama", "Saate yanlış elle basma", "Hakem uyarısına uymama",
        "Oyun sırasında konuşma / yardım alma", "Oyun alanını izinsiz terk etme", "Eşleştirmeye itiraz", "Sonuca itiraz", "Hakem kararına itiraz"
    };

    /// <summary>Verilen ceza / karar sütununda önerilenler.</summary>
    public static readonly string[] Decisions =
    {
        "Uyarı", "Rakibe 2 dakika eklendi", "Süresinden 2 dakika düşüldü", "Oyunu kaybetti", "Turnuvadan çıkarıldı",
        "Kabul edildi", "Reddedildi", "Kısmen kabul edildi"
    };

    private readonly IncidentsContext _ctx;
    private readonly DataGridView _grid = new() { AccessibleName = "Ceza ve itiraz kayıtları" };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 11, 0, 0) };
    private readonly Label _hint = new() { AutoSize = true, ForeColor = Color.FromArgb(95, 122, 70), Margin = new Padding(14, 11, 0, 0) };
    private bool _loading;

    private string Key => _ctx.EventTnr.ToString();

    public IncidentsForm(IncidentsContext ctx)
    {
        _ctx = ctx;
        Text = "Ceza ve İtiraz Kayıtları — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1240, 600);
        MinimumSize = new Size(860, 420);
        this.FitToScreen();
        BackColor = Color.FromArgb(250, 250, 247);
        BuildLayout();
        LoadRows();
        FormClosing += (_, _) => { _grid.EndEdit(); Save(); };
    }

    private void BuildLayout()
    {
        var help = new Label
        {
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 10, 12, 0), ForeColor = Color.DimGray,
            Text = "Verilen cezaları ve yapılan itirazları yazın; Ceza Tutanağı, İtiraz Tutanağı ve Turnuva Sonu Raporu (Yönerge / Tutanak) " +
                   "bunlarla dolar. Kategori, tur ve masa girilince sporcu eşleştirmeden gelir; \"⇄\" ile rakibi seçilir. Kayıtlar kendiliğinden saklanır."
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = true;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.RowTemplate.Height = 30;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        var kind = new DataGridViewComboBoxColumn { Name = "Tur_", HeaderText = "Tür", Width = 80, FlatStyle = FlatStyle.Flat };
        kind.Items.AddRange(IncidentRecord.Penalty, IncidentRecord.Appeal);
        _grid.Columns.Add(kind);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tarih", HeaderText = "Tarih", Width = 92 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Saat", HeaderText = "Saat", Width = 58 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Round", HeaderText = "Tur", Width = 44 });
        var cat = new DataGridViewComboBoxColumn { Name = "Kategori", HeaderText = "Kategori", Width = 130, FlatStyle = FlatStyle.Flat };
        cat.Items.Add("");
        foreach (var c in _ctx.Categories) cat.Items.Add(c);
        _grid.Columns.Add(cat);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Masa", HeaderText = "Masa", Width = 50 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sporcu", HeaderText = "Sporcu / itiraz eden", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 120, MinimumWidth = 130 });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Swap", HeaderText = "", Text = "⇄", UseColumnTextForButtonValue = true, Width = 34, FlatStyle = FlatStyle.Flat });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hamle", HeaderText = "Hamle", Width = 56 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Neden", HeaderText = "Ceza nedeni / itirazın konusu", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 160, MinimumWidth = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Karar", HeaderText = "Verilen ceza / karar", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 110, MinimumWidth = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hakem", HeaderText = "Hakem", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 80, MinimumWidth = 90 });
        foreach (var c in new[] { "Neden", "Karar" }) _grid.Columns[c]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        foreach (var c in new[] { "Tarih", "Saat", "Round", "Masa", "Hamle" }) _grid.Columns[c]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        new ToolTip().SetToolTip(_grid, "Satırı silmek için seçip Delete tuşuna basın.");

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += async (_, e) =>
        {
            if (_loading || e.RowIndex < 0) return;
            var col = _grid.Columns[e.ColumnIndex].Name;
            if (col is "Kategori" or "Round" or "Masa") await LookupAsync(_grid.Rows[e.RowIndex]);
            Save();
        };
        _grid.UserDeletedRow += (_, _) => Save();
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Swap") SwapPlayer(_grid.Rows[e.RowIndex]); };
        _grid.EditingControlShowing += (_, e) =>
        {
            // Neden ve karar sütununda öneriler (yazdıkça tamamlanır; serbest metin de olur)
            if (e.Control is not TextBox tb) return;
            var col = _grid.CurrentCell?.OwningColumn.Name;
            tb.AutoCompleteMode = AutoCompleteMode.None;
            if (col is "Neden" or "Karar")
            {
                var src = new AutoCompleteStringCollection();
                src.AddRange(col == "Neden" ? Reasons : Decisions);
                tb.AutoCompleteCustomSource = src;
                tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
                tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            }
        };
        _grid.DataError += (_, e) => e.ThrowException = false;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(10), BackColor = Color.FromArgb(242, 242, 236) };
        var addPenalty = Btn("+ Ceza ekle", primary: true);
        var addAppeal = Btn("+ İtiraz ekle");
        var delete = Btn("Seçili satırı sil");
        var close = Btn("Kapat");
        close.DialogResult = DialogResult.OK;
        addPenalty.Click += (_, _) => AddRow(IncidentRecord.Penalty);
        addAppeal.Click += (_, _) => AddRow(IncidentRecord.Appeal);
        delete.Click += (_, _) =>
        {
            if (_grid.CurrentRow is { } r) { _grid.Rows.Remove(r); Save(); }
        };
        bottom.Controls.AddRange(new Control[] { addPenalty, addAppeal, delete, close, _status, _hint });
        CancelButton = close;

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 8) };
        host.Controls.Add(_grid);
        Controls.Add(host);
        Controls.Add(help);
        Controls.Add(bottom);
    }

    /// <summary>Açık hücre düzenlemesi kapatılmadan tablo yok edilirse WinForms hata veriyor: önce bitir.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            try { _grid.EndEdit(); _grid.CurrentCell = null; } catch (InvalidOperationException) { }
        base.Dispose(disposing);
    }

    private static Button Btn(string text, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 2, 4, 2), Cursor = Cursors.Hand };
        b.BackColor = primary ? Green : Color.White;
        b.ForeColor = primary ? Color.White : Color.Black;
        b.FlatAppearance.BorderColor = primary ? GreenDark : Color.FromArgb(208, 208, 198);
        return b;
    }

    // ================= Veri =================
    private List<IncidentRecord> Stored => _ctx.Config.Incidents.GetValueOrDefault(Key) ?? new List<IncidentRecord>();

    private void LoadRows()
    {
        _loading = true;
        foreach (var r in Stored) AddGridRow(r);
        _loading = false;
        UpdateStatus();
    }

    private void AddGridRow(IncidentRecord r)
    {
        int i = _grid.Rows.Add(r.Kind, r.Date, r.Time, r.Round, _ctx.Categories.Contains(r.Category) ? r.Category : "", r.Board, r.Player, null, r.Move, r.Reason, r.Decision, r.Arbiter);
        var catCell = (DataGridViewComboBoxCell)_grid.Rows[i].Cells["Kategori"];
        if (r.Category.Length > 0 && !catCell.Items.Contains(r.Category)) { catCell.Items.Add(r.Category); catCell.Value = r.Category; }
    }

    /// <summary>Yeni satır: bugünün tarihi, şimdiki saat, seçili kategori ve tur, son yazılan hakem.</summary>
    public void AddRow(string kind)
    {
        var last = Stored.LastOrDefault(r => r.Arbiter.Length > 0)?.Arbiter ?? _ctx.DefaultArbiter ?? "";
        _loading = true;
        AddGridRow(new IncidentRecord
        {
            Kind = kind, Date = DateTime.Today.ToString("dd.MM.yyyy"), Time = DateTime.Now.ToString("HH.mm"),
            Round = _ctx.DefaultRound > 0 ? _ctx.DefaultRound.ToString() : "", Category = _ctx.DefaultCategory ?? "", Arbiter = last
        });
        _loading = false;
        var row = _grid.Rows[^1];
        _grid.CurrentCell = row.Cells["Masa"];
        _grid.BeginEdit(true);
        Save();
    }

    /// <summary>Kategori + tur + masa girildiyse sporcuları eşleştirmeden bulur; sporcu boşsa beyazı yazar.</summary>
    private async Task LookupAsync(DataGridViewRow row)
    {
        if (_ctx.LookupBoard is null) return;
        var cat = row.Cells["Kategori"].Value?.ToString() ?? "";
        var board = row.Cells["Masa"].Value?.ToString()?.Trim() ?? "";
        if (cat.Length == 0 || board.Length == 0 || !int.TryParse(row.Cells["Round"].Value?.ToString(), out var round) || round <= 0) return;
        (string White, string Black)? pair;
        try { pair = await _ctx.LookupBoard(cat, round, board); }
        catch { pair = null; }
        if (IsDisposed || row.DataGridView is null) return;
        if (pair is not { } p) { _hint.Text = $"{cat} • {round}. tur • {board}. masa eşleştirmede bulunamadı."; return; }
        row.Tag = p;
        _hint.Text = $"{board}. masa: Beyaz {p.White} — Siyah {p.Black}  (⇄ ile değiştirin)";
        if (string.IsNullOrWhiteSpace(row.Cells["Sporcu"].Value?.ToString()))
        {
            _loading = true;
            row.Cells["Sporcu"].Value = p.White;
            _loading = false;
        }
    }

    private void SwapPlayer(DataGridViewRow row)
    {
        if (row.Tag is not ValueTuple<string, string> p) { _hint.Text = "Önce kategori, tur ve masayı girin."; return; }
        var cur = row.Cells["Sporcu"].Value?.ToString();
        row.Cells["Sporcu"].Value = cur == p.Item1 ? p.Item2 : p.Item1;
        Save();
    }

    /// <summary>Tablodaki kayıtlar (boş satırlar atlanır).</summary>
    public List<IncidentRecord> Records()
    {
        var list = new List<IncidentRecord>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string V(string c) => row.Cells[c].Value?.ToString()?.Trim() ?? "";
            var r = new IncidentRecord
            {
                Kind = V("Tur_") == IncidentRecord.Appeal ? IncidentRecord.Appeal : IncidentRecord.Penalty,
                Date = V("Tarih"), Time = V("Saat"), Round = V("Round"), Category = V("Kategori"), Board = V("Masa"),
                Player = V("Sporcu"), Move = V("Hamle"), Reason = V("Neden"), Decision = V("Karar"), Arbiter = V("Hakem")
            };
            if (!r.IsEmpty) list.Add(r);
        }
        return list;
    }

    private void Save()
    {
        if (_loading) return;
        var list = Records();
        if (list.Count == 0) _ctx.Config.Incidents.Remove(Key);
        else _ctx.Config.Incidents[Key] = list;
        _ctx.SaveConfig();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var list = _ctx.Config.Incidents.GetValueOrDefault(Key) ?? new();
        int pen = list.Count(r => r.Kind == IncidentRecord.Penalty), app = list.Count(r => r.Kind == IncidentRecord.Appeal);
        _status.Text = $"{pen} ceza • {app} itiraz kayıtlı";
    }
}
