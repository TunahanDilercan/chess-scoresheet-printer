using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Kategori masa kartlarını A4'e (varsayılan yatay) basar (önizlemeli) veya PDF olarak kaydeder.</summary>
public sealed class CardPrinter : IPagedOutput, IDisposable
{

    private readonly List<CardSpec> _pages;   // adet kadar açılmış
    private readonly List<CardSpec> _distinct; // önizlemede her karttan bir sayfa
    private readonly string? _logoPath;
    private readonly string _eventName;
    private readonly string _font;
    private readonly CardLayout _layout;
    private readonly bool _filled;
    private readonly bool _fullWidth;
    private readonly float _bandPt;            // tüm kartlarda ortak kategori yazısı puntosu
    private readonly CardBranding _branding;
    private Image? _logo;
    private Image? _gsb, _tsf;

    public CardPrinter(IEnumerable<CardSpec> cards, string? logoPath, string eventName, string? font = null, bool landscape = true,
                       bool filled = false, bool fullWidth = false, CardBranding? branding = null)
    {
        _filled = filled;
        _fullWidth = fullWidth;
        _branding = branding ?? CardBranding.None;
        _pages = cards.SelectMany(c => Enumerable.Repeat(c, Math.Max(0, c.Copies))).ToList();
        _distinct = _pages.Distinct().ToList();
        _logoPath = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath) ? logoPath : null;
        _eventName = eventName;
        _font = string.IsNullOrWhiteSpace(font) ? CardFonts.Default : font!;
        _layout = CardLayout.For(landscape, ImageSize(_logoPath), fullWidth, _branding, eventName, _font);
        _bandPt = CommonBandPt(_layout, _pages.Select(p => p.Category), _font);
    }

    /// <summary>Resmin piksel boyutu (yalnız başlığı okunur); okunamazsa null.</summary>
    public static SizeF? ImageSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { using var fs = File.OpenRead(path); using var img = Image.FromStream(fs, false, false); return new SizeF(img.Width, img.Height); }
        catch { return null; }
    }

    public int PageCount => _pages.Count;

    // ================= Önizleme, yazıcı, PDF (PagedOutput: önizleme tembel, yazdırma ve PDF arka planda) =================
    public string DocumentName => $"{_eventName} - masa kartları";
    public SizeF PageSizePt => new(_layout.PageW, _layout.PageH);
    public bool Landscape => _layout.Landscape;
    /// <summary>Aynı kategorinin adet kadar sayfası aynıdır: bir kez hazırlanır.</summary>
    public object PageKey(int page) => _pages[page];
    public void DrawPage(Graphics g, int page) => DrawCard(g, _pages[page]);

    public string Hint => $"{PageCount} kart basılacak (önizlemede her kategoriden bir sayfa) • A4 {(_layout.Landscape ? "yatay" : "dikey")}";

    /// <summary>Önizleme: her kategoriden bir sayfa (30 kartın 30 sayfasını göstermek gereksiz).</summary>
    public IPagedOutput PreviewPages => new Distinct(this);

    private sealed class Distinct : IPagedOutput
    {
        private readonly CardPrinter _p;
        public Distinct(CardPrinter p) => _p = p;
        public string DocumentName => _p.DocumentName;
        public SizeF PageSizePt => _p.PageSizePt;
        public bool Landscape => _p.Landscape;
        public int PageCount => _p._distinct.Count;
        public object PageKey(int page) => page;
        public void DrawPage(Graphics g, int page) => _p.DrawCard(g, _p._distinct[page]);
    }

    private bool _imagesLoaded;

    /// <summary>Afiş ve resmi logolar bir kez baskı boyutuna (300 dpi) küçültülür; her sayfada büyük görseli ölçeklemek yavaştı.</summary>
    private void DrawCard(Graphics g, CardSpec card)
    {
        if (!_imagesLoaded)
        {
            _logo = ScaledLogo(_logoPath, 300);
            (_gsb, _tsf) = _branding.Load(1200);
            _imagesLoaded = true;
        }
        Draw(g, _layout, card, _logo, _eventName, _font, _filled, _fullWidth, _bandPt, _branding, _gsb, _tsf);
    }

    public void Dispose()
    {
        lock (PagedOutput.Gate)
        {
            _logo?.Dispose(); _gsb?.Dispose(); _tsf?.Dispose();
            _logo = _gsb = _tsf = null;
            _imagesLoaded = false;
        }
    }

    private Image? ScaledLogo(string? path, int dpi)
    {
        var src = LoadLogo(path);
        if (src is null) return null;
        var slot = _fullWidth ? _layout.HeaderRect : _layout.LogoRect;
        var fit = _fullWidth ? new SizeF(slot.Width, src.Height * slot.Width / src.Width)
                             : CardLayout.FitImage(new SizeF(src.Width, src.Height), slot).Size;
        int w = (int)Math.Ceiling(fit.Width / 72f * dpi), h = (int)Math.Ceiling(fit.Height / 72f * dpi);
        if (w >= src.Width || h <= 0) return src; // zaten küçük
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
        }
        src.Dispose();
        return bmp;
    }

    private static Image? LoadLogo(string? path)
    {
        if (path is null) return null;
        try { using var fs = File.OpenRead(path); using var img = Image.FromStream(fs); return new Bitmap(img); }
        catch { return null; }
    }

    private static PaperSize? FindA4(PrintDocument doc)
    {
        try
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                if (ps.Kind == PaperKind.A4) return ps;
        }
        catch (InvalidPrinterException) { }
        return null;
    }

    /// <summary>
    /// Bir kartı (punto koordinatlarında) çizer; önizleme küçük resmi ve PDF de bunu kullanır.
    /// <paramref name="filled"/>: false = beyaz zeminde tema renginde yazı, true = tema renginde zeminde beyaz yazı.
    /// </summary>
    public static void Draw(Graphics g, CardLayout layout, CardSpec card, Image? logo, string eventName, string? font, bool filled,
                            bool fullWidth = false, float? bandPt = null, CardBranding? branding = null, Image? gsb = null, Image? tsf = null)
    {
        var prevInterp = g.InterpolationMode;
        var prevSmooth = g.SmoothingMode;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        if (logo is not null)
            layout.DrawLogo(g, logo, fullWidth); // oran her iki modda da korunur
        else
            layout.DrawEventName(g, eventName, card.Color, font);

        if (filled)
            using (var b = new SolidBrush(card.Color)) g.FillRectangle(b, layout.BandRect);
        else if (logo is null)
            using (var pen = new Pen(card.Color, 2.5f)) // başlık ile kategori adını ayıran ince çizgi
                g.DrawLine(pen, CardLayout.Margin, layout.BandRect.Top, layout.PageW - CardLayout.Margin, layout.BandRect.Top);
        branding?.Draw(g, layout.Logos, gsb, tsf); // afiş/bant üstünde; yazılar logolardan kaçar
        var box = layout.BandTextRect;
        var fit = bandPt is float pt ? CardLayout.FitAt(card.Category, box, pt, font, MaxBandLines)
                                     : CardLayout.Fit(card.Category, box, MaxBandPt, font, 14, MaxBandLines);
        CardLayout.DrawLines(g, fit, box, filled ? Color.White : card.Color, font);
        g.InterpolationMode = prevInterp;
        g.SmoothingMode = prevSmooth;
    }

    private const float MaxBandPt = 230;
    private const int MaxBandLines = 3;

    /// <summary>Kategori adları için ortak punto: hepsinin sığdığı en büyük.</summary>
    public static float CommonBandPt(CardLayout layout, IEnumerable<string> categories, string? font)
        => CardLayout.CommonPt(categories, layout.BandTextRect, MaxBandPt, font, 14, MaxBandLines);

    // ================= PDF =================
    public void SavePdf(string path)
    {
        try { PagedOutput.SavePdfAsync(this, path, null, default).GetAwaiter().GetResult(); }
        finally { Dispose(); }
    }
}
