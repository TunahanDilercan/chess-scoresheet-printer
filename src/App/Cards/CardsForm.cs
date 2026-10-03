using System.Globalization;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Kartlar penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record CardsContext(
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    Func<CategoryRef, Task<int>> BoardCount,
    AppConfig Config,
    Action SaveConfig,
    string BaseDir,
    string OutputDir);

/// <summary>
/// Kategori masa kartları: her kategori kendi (kalıcı) renginde, üstte turnuva afişi, altta büyük
/// kategori adı. Adet varsayılan olarak kategorideki masa sayısıdır; yeniden basım için değiştirilebilir.
/// </summary>
public sealed class CardsForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly CultureInfo Tr = new("tr-TR");

    private readonly CardsContext _ctx;
    private readonly HashSet<int> _countEdited = new();   // kullanıcının adedini elle değiştirdiği satırlar

    private readonly PictureBox _picLogo = new() { Width = 270, Height = 170, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly Button _btnLogo = new() { Text = "🖼 Afiş / logo seç…", AutoSize = true, Height = 32 };
    private readonly Button _btnLogoClear = new() { Text = "Kaldır", AutoSize = true, Height = 32 };
    private readonly PictureBox _picPreview = new() { Width = 210, Height = 297, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnPdf = new() { Text = "📄 PDF Kaydet", Width = 150, Height = 40 };
    private readonly Button _btnPrint = new() { Text = "🖨 Önizle ve Yazdır", Width = 200, Height = 40, BackColor = Green, ForeColor = Color.White };
    private readonly Button _btnAll = new() { Text = "Tümünü seç", AutoSize = true, Height = 40 };
    private readonly Button _btnNone = new() { Text = "Hiçbiri", AutoSize = true, Height = 40 };

    public CardsForm(CardsContext ctx)
    {
        _ctx = ctx;
        Text = "Kategori Masa Kartları — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 680);
        MinimumSize = new Size(820, 560);
        BackColor = Color.FromArgb(250, 250, 247);

        BuildLayout();
        LoadLogo();
        FillGrid();

        _btnLogo.Click += (_, _) => ChooseLogo();
        _btnLogoClear.Click += (_, _) => { _ctx.Config.Cards.LogoPath = null; _ctx.SaveConfig(); LoadLogo(); UpdatePreview(); };
        _btnAll.Click += (_, _) => SetAll(true);
        _btnNone.Click += (_, _) => SetAll(false);
        _btnPrint.Click += (_, _) => Print();
        _btnPdf.Click += (_, _) => SavePdf();
        Shown += async (_, _) => await LoadCountsAsync();
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        // Sol sütun: üstte logo ayarları (sabit), altta kalan yüksekliği dolduran kart önizlemesi.
        var leftHost = new Panel { Dock = DockStyle.Left, Width = 300 };
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(14, 12, 8, 0)
        };
        left.Controls.Add(new Label { Text = "Turnuva afişi / logosu", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark });
        left.Controls.Add(_picLogo);
        var logoButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        logoButtons.Controls.AddRange(new Control[] { _btnLogo, _btnLogoClear });
        left.Controls.Add(logoButtons);
        left.Controls.Add(new Label
        {
            AutoSize = false, Width = 270, Height = 36, ForeColor = Color.Gray,
            Text = "Kartın üstüne oranı korunarak basılır. Seçilmezse turnuva adı yazılır."
        });
        left.Controls.Add(new Label { Text = "Önizleme (seçili kategori)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, 8, 0, 4) });
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 8, 10) };
        _picPreview.Dock = DockStyle.Fill;
        _picPreview.BorderStyle = BorderStyle.None;
        _picPreview.BackColor = BackColor;
        previewHost.Controls.Add(_picPreview);
        leftHost.Controls.Add(previewHost); // Fill önce
        leftHost.Controls.Add(left);        // Top sonra

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowTemplate.Height = 34;
        _grid.GridColor = Color.FromArgb(230, 230, 222);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bas", HeaderText = "Bas", Width = 44 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kategori", HeaderText = "Kartta yazacak ad", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Renk", HeaderText = "Renk (değiştir)", Width = 130, FlatStyle = FlatStyle.Flat });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Adet", HeaderText = "Adet", Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Masa", HeaderText = "Masa sayısı", Width = 100, ReadOnly = true });
        _grid.Columns["Adet"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["Masa"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["Masa"]!.DefaultCellStyle.ForeColor = Color.Gray;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Renk") PickColor(e.RowIndex); };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name == "Adet") _countEdited.Add(e.RowIndex);
            UpdateSummary();
            UpdatePreview();
        };
        _grid.CellValidating += (_, e) =>
        {
            if (_grid.Columns[e.ColumnIndex].Name != "Adet") return;
            if (!int.TryParse(e.FormattedValue?.ToString(), out var n) || n < 0 || n > 500)
            {
                e.Cancel = true;
                _lblStatus.Text = "Adet 0–500 arası bir sayı olmalı.";
            }
        };
        _grid.SelectionChanged += (_, _) => UpdatePreview();

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 14, 8) };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray,
            Text = "Adet, kategorideki masa sayısı kadar önerilir (yeniden basım için değiştirebilirsiniz). " +
                   "Her kategoriye otomatik ayrı bir renk atanır ve hatırlanır; değiştirmek için renge tıklayın."
        };
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.Add(hint);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var bl = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        bl.Controls.AddRange(new Control[] { _btnAll, _btnNone, _lblStatus });
        _lblStatus.Margin = new Padding(10, 11, 0, 0);
        var br = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        br.Controls.AddRange(new Control[] { _btnPdf, _btnPrint });
        bottom.Controls.Add(bl);
        bottom.Controls.Add(br);

        foreach (var b in new[] { _btnLogo, _btnLogoClear, _btnPdf, _btnPrint, _btnAll, _btnNone }) Style(b);
        _btnPrint.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        Controls.Add(gridPanel);
        Controls.Add(leftHost);
        Controls.Add(bottom);
    }

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

    // ================= Veri =================
    private void FillGrid()
    {
        var tnrs = _ctx.Categories.Select(c => c.Tnr).ToList();
        bool assigned = false;
        foreach (var c in _ctx.Categories)
        {
            assigned |= !_ctx.Config.Cards.CategoryColors.ContainsKey(c.Tnr.ToString());
            var color = CardLayout.ParseColor(_ctx.Config.Cards.ColorFor(c.Tnr, tnrs), Color.Gray);
            var label = EventGrouping.ShortCategory(c.Name).ToUpper(Tr);
            int i = _grid.Rows.Add(true, label, "", "…", "…");
            var row = _grid.Rows[i];
            row.Tag = c;
            SetColorCell(row, color);
        }
        if (assigned) _ctx.SaveConfig(); // yeni atanan renkler kalıcı olsun
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
        UpdateSummary();
    }

    private static void SetColorCell(DataGridViewRow row, Color color)
    {
        var cell = row.Cells["Renk"];
        cell.Value = CardLayout.ToHex(color);
        cell.Style.BackColor = color;
        cell.Style.SelectionBackColor = color;
        cell.Style.ForeColor = CardLayout.TextColorFor(color);
        cell.Style.SelectionForeColor = CardLayout.TextColorFor(color);
    }

    private int _countsPending; // masa sayısı henüz gelmemiş kategori sayısı

    private async Task LoadCountsAsync()
    {
        _countsPending = _grid.Rows.Count;
        UpdateSummary();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (IsDisposed) return;
            if (row.Tag is not CategoryRef c) { _countsPending--; continue; }
            int n;
            try { n = await _ctx.BoardCount(c); }
            catch { n = 0; }
            if (IsDisposed) return;
            row.Cells["Masa"].Value = n > 0 ? n.ToString() : "?";
            if (!_countEdited.Contains(row.Index))
            {
                row.Cells["Adet"].Value = Math.Max(1, n).ToString();
                _countEdited.Remove(row.Index); // programın yazdığı değer "elle" sayılmasın
            }
            _countsPending--;
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var cards = Selected().ToList();
        _lblStatus.Text = _countsPending > 0
            ? $"Masa sayıları hesaplanıyor… ({_grid.Rows.Count - _countsPending}/{_grid.Rows.Count})"
            : cards.Count == 0 ? "Basılacak kart seçilmedi." : $"{cards.Count} kategori • toplam {cards.Sum(c => c.Copies)} kart (A4)";
        _btnPrint.Enabled = _btnPdf.Enabled = cards.Sum(c => c.Copies) > 0;
    }

    private IEnumerable<CardSpec> Selected()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells["Bas"].Value is not true) continue;
            var name = row.Cells["Kategori"].Value?.ToString()?.Trim() ?? "";
            int.TryParse(row.Cells["Adet"].Value?.ToString(), out var copies);
            var color = CardLayout.ParseColor(row.Cells["Renk"].Value?.ToString(), Color.Gray);
            if (name.Length > 0 && copies > 0) yield return new CardSpec(name, color, copies);
        }
    }

    private void SetAll(bool on)
    {
        foreach (DataGridViewRow r in _grid.Rows) r.Cells["Bas"].Value = on;
        UpdateSummary();
    }

    private void PickColor(int rowIndex)
    {
        var row = _grid.Rows[rowIndex];
        if (row.Tag is not CategoryRef c) return;
        using var dlg = new ColorDialog
        {
            FullOpen = true, AnyColor = true,
            Color = CardLayout.ParseColor(row.Cells["Renk"].Value?.ToString(), Color.Gray),
            CustomColors = CardConfig.Palette.Select(h => { var k = ColorTranslator.FromHtml(h); return k.R | (k.G << 8) | (k.B << 16); }).ToArray()
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SetColorCell(row, dlg.Color);
        _ctx.Config.Cards.CategoryColors[c.Tnr.ToString()] = CardLayout.ToHex(dlg.Color);
        _ctx.SaveConfig();
        UpdatePreview();
    }

    // ================= Logo =================
    private void ChooseLogo()
    {
        using var dlg = new OpenFileDialog { Title = "Turnuva afişi / logosu seç", Filter = "Resim (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using (var test = Image.FromFile(dlg.FileName)) { } // geçerli resim mi
            // Kopyası exe yanında tutulur: asıl dosya taşınsa/silinse de kart basılabilsin.
            var dir = Path.Combine(_ctx.BaseDir, "cards");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "logo_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(dlg.FileName).ToLowerInvariant());
            File.Copy(dlg.FileName, dest, overwrite: true);
            _ctx.Config.Cards.LogoPath = dest;
            _ctx.SaveConfig();
            LoadLogo();
            UpdatePreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Resim açılamadı: " + ex.Message, "Logo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadLogo()
    {
        _picLogo.Image?.Dispose();
        _picLogo.Image = null;
        var path = _ctx.Config.Cards.LogoPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            using var fs = File.OpenRead(path); // dosyayı kilitlemeden yükle
            using var img = Image.FromStream(fs);
            _picLogo.Image = new Bitmap(img);
        }
        catch { /* bozuk resim: logosuz */ }
    }

    private void UpdatePreview()
    {
        var row = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : (_grid.Rows.Count > 0 ? _grid.Rows[0] : null);
        if (row is null) return;
        var name = row.Cells["Kategori"].Value?.ToString() ?? "";
        var color = CardLayout.ParseColor(row.Cells["Renk"].Value?.ToString(), Color.Gray);
        const float scale = 0.6f; // 595×842 pt → ~357×505 px
        var bmp = new Bitmap((int)(CardLayout.PageW * scale), (int)(CardLayout.PageH * scale));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PageUnit = GraphicsUnit.Point;         // yazı puntoları baskıdakiyle aynı ölçülsün
            float k = scale * 72f / bmp.HorizontalResolution; // Point biriminde 1 pt = dpi/72 px → scale px olsun
            g.ScaleTransform(k, k);
            CardPrinter.Draw(g, new CardSpec(name, color, 1), _picLogo.Image, EventGrouping.BaseName(_ctx.EventName));
        }
        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();
    }

    // ================= Çıktı =================
    private CardPrinter? Printer()
    {
        _grid.EndEdit();
        var cards = Selected().ToList();
        if (cards.Count == 0) { _lblStatus.Text = "Basılacak kart seçilmedi."; return null; }
        return new CardPrinter(cards, _ctx.Config.Cards.LogoPath, EventGrouping.BaseName(_ctx.EventName));
    }

    private void Print()
    {
        var p = Printer();
        if (p is null) return;
        if (p.PrintWithPreview(this)) _lblStatus.Text = $"{p.PageCount} kart yazıcıya gönderildi.";
    }

    private void SavePdf()
    {
        var p = Printer();
        if (p is null) return;
        Directory.CreateDirectory(_ctx.OutputDir);
        using var dlg = new SaveFileDialog
        {
            Title = "Masa kartlarını PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf",
            InitialDirectory = _ctx.OutputDir, FileName = "masa_kartlari.pdf"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            p.SavePdf(dlg.FileName);
            _lblStatus.Text = "PDF kaydedildi: " + Path.GetFileName(dlg.FileName);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "PDF oluşturulamadı: " + ex.Message, "PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; }
    }
}
