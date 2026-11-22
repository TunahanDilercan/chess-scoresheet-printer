using System.Drawing.Imaging;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Çok sayfalı çıktılar için hızlı önizleme: yalnız görünen sayfa (ve ardından bir sonraki) arka planda,
/// pencere boyutunda çizilir. WinForms'un önizlemesi bütün sayfaları açılışta tek seferde çizdiğinden
/// 120 sporculuk yaka kartında pencere saniyelerce donuyordu.
/// </summary>
internal sealed class PagePreviewDialog : AppForm
{
    private readonly int _pageCount;
    private readonly SizeF _pagePt;
    private readonly Action<Graphics, int> _draw;
    private readonly Func<IWin32Window, bool> _print;
    private readonly PagePanel _canvas = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(128, 128, 128) };
    private readonly Label _lblPage = new() { AutoSize = true };
    private readonly Dictionary<int, Bitmap> _pages = new();
    private int _page;
    private Size _renderedFor;
    private int _generation;
    private bool _closed;

    public bool Printed { get; private set; }

    private PagePreviewDialog(string title, int pageCount, SizeF pagePt, Action<Graphics, int> draw, string hint, Func<IWin32Window, bool> print)
    {
        _pageCount = Math.Max(1, pageCount);
        _pagePt = pagePt;
        _draw = draw;
        _print = print;
        Text = "Yazdırma Önizleme — " + title;
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(700, 500);
        Font = new Font("Segoe UI", 9.75f);
        KeyPreview = true;

        var green = Color.FromArgb(118, 150, 86);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), BackColor = Color.FromArgb(245, 245, 240), WrapContents = false };
        var btnPrint = Icons.Set(new Button { Text = "Yazdır", Width = 150, Height = 34, BackColor = green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) }, Ico.Print);
        var btnClose = new Button { Text = "Kapat", Width = 100, Height = 34 };
        var btnPrev = Icons.Set(new Button { Text = "", Width = 40, Height = 34, Margin = new Padding(18, 3, 3, 3) }, Ico.ChevronLeft);
        var btnNext = Icons.Set(new Button { Text = "", Width = 40, Height = 34 }, Ico.ChevronRight);
        _lblPage.Margin = new Padding(8, 10, 16, 0);
        var lblHint = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 10, 0, 0), Text = hint + " • Ctrl+P yazdır, Esc kapat, ← → sayfa" };
        bar.Controls.AddRange(new Control[] { btnPrint, btnClose, btnPrev, btnNext, _lblPage, lblHint });

        btnPrint.Click += (_, _) => DoPrint();
        btnClose.Click += (_, _) => Close();
        btnPrev.Click += (_, _) => ShowPage(_page - 1);
        btnNext.Click += (_, _) => ShowPage(_page + 1);
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.P) { e.Handled = true; DoPrint(); }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode is Keys.PageDown or Keys.Right or Keys.Down) { e.Handled = true; ShowPage(_page + 1); }
            else if (e.KeyCode is Keys.PageUp or Keys.Left or Keys.Up) { e.Handled = true; ShowPage(_page - 1); }
            else if (e.KeyCode == Keys.Home) ShowPage(0);
            else if (e.KeyCode == Keys.End) ShowPage(_pageCount - 1);
        };
        _canvas.MouseWheel += (_, e) => ShowPage(_page + (e.Delta < 0 ? 1 : -1));
        _canvas.Paint += PaintCanvas;
        _canvas.Resize += (_, _) => { if (Visible) RenderVisible(); };

        Controls.Add(_canvas);
        Controls.Add(bar);
        Shown += (_, _) => { btnPrint.Focus(); RenderVisible(); };
        FormClosed += (_, _) =>
        {
            _closed = true;
            foreach (var b in _pages.Values) b.Dispose();
            _pages.Clear();
        };
        ShowPage(0);
    }

    /// <summary>Önizlemeyi gösterir. <paramref name="draw"/> sayfayı punto koordinatlarında çizer (arka planda çağrılır).</summary>
    /// <returns>true = kullanıcı yazdırdı.</returns>
    public static bool Show(IWin32Window owner, string title, int pageCount, SizeF pagePt, Action<Graphics, int> draw,
                            string hint, Func<IWin32Window, bool> print)
    {
        using var f = new PagePreviewDialog(title, pageCount, pagePt, draw, hint, print);
        if (owner is Form pf) f.Icon = pf.Icon;
        f.ShowDialog(owner);
        return f.Printed;
    }

    private void DoPrint()
    {
        if (!_print(this)) return;
        Printed = true;
        Close();
    }

    private void ShowPage(int p)
    {
        _page = Math.Clamp(p, 0, _pageCount - 1);
        _lblPage.Text = $"Sayfa {_page + 1} / {_pageCount}";
        if (Visible) RenderVisible();
        _canvas.Invalidate();
    }

    /// <summary>Sayfanın pencereye sığan boyutu (piksel).</summary>
    private Size FitSize()
    {
        var avail = new Size(Math.Max(100, _canvas.ClientSize.Width - 24), Math.Max(100, _canvas.ClientSize.Height - 24));
        float s = Math.Min(avail.Width / _pagePt.Width, avail.Height / _pagePt.Height);
        return new Size(Math.Max(1, (int)(_pagePt.Width * s)), Math.Max(1, (int)(_pagePt.Height * s)));
    }

    private async void RenderVisible()
    {
        var size = FitSize();
        if (size != _renderedFor)
        {
            // pencere boyutu değişti: eski çizimler bulanık kalmasın
            foreach (var b in _pages.Values) b.Dispose();
            _pages.Clear();
            _renderedFor = size;
            _generation++;
        }
        int gen = _generation;
        foreach (var p in new[] { _page, _page + 1 })   // görünen sayfa, sonra bir sonraki (hızlı gezinme)
        {
            if (p >= _pageCount || _pages.ContainsKey(p)) continue;
            Bitmap bmp;
            try { bmp = await Task.Run(() => Render(p, size)); }
            catch (Exception ex) { _canvas.Error = "Sayfa çizilemedi: " + ex.Message; _canvas.Invalidate(); return; }
            if (_closed || gen != _generation || _pages.ContainsKey(p)) { bmp.Dispose(); return; }
            _pages[p] = bmp;
            if (p == _page) _canvas.Invalidate();
        }
    }

    private Bitmap Render(int page, Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        float dpi = size.Width / _pagePt.Width * 72f;   // 1 punto = dpi/72 piksel
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        _draw(g, page);
        return bmp;
    }

    private void PaintCanvas(object? sender, PaintEventArgs e)
    {
        var size = FitSize();
        var r = new Rectangle((_canvas.ClientSize.Width - size.Width) / 2, (_canvas.ClientSize.Height - size.Height) / 2, size.Width, size.Height);
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) e.Graphics.FillRectangle(shadow, r.X + 4, r.Y + 4, r.Width, r.Height);
        if (_pages.TryGetValue(_page, out var bmp)) e.Graphics.DrawImage(bmp, r.X, r.Y, bmp.Width, bmp.Height);
        else
        {
            e.Graphics.FillRectangle(Brushes.White, r);
            TextRenderer.DrawText(e.Graphics, _canvas.Error ?? "Sayfa hazırlanıyor…", Font, r, Color.Gray,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Titremesiz çizim yüzeyi.</summary>
    private sealed class PagePanel : Panel
    {
        public string? Error;
        public PagePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }
}
