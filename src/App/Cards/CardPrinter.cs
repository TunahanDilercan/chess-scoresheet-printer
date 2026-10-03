using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Kategori masa kartlarını A4'e (varsayılan yatay) basar (önizlemeli) veya PDF olarak kaydeder.</summary>
public sealed class CardPrinter
{

    private readonly List<CardSpec> _pages;   // adet kadar açılmış
    private readonly string? _logoPath;
    private readonly string _eventName;
    private readonly string _font;
    private readonly CardLayout _layout;
    private readonly bool _filled;
    private readonly bool _fullWidth;
    private Image? _logo;
    private int _pageIndex;

    public CardPrinter(IEnumerable<CardSpec> cards, string? logoPath, string eventName, string? font = null, bool landscape = true,
                       bool filled = false, bool fullWidth = false)
    {
        _filled = filled;
        _fullWidth = fullWidth;
        _pages = cards.SelectMany(c => Enumerable.Repeat(c, Math.Max(0, c.Copies))).ToList();
        _logoPath = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath) ? logoPath : null;
        _eventName = eventName;
        _font = string.IsNullOrWhiteSpace(font) ? CardFonts.Default : font!;
        _layout = new CardLayout(landscape);
    }

    public int PageCount => _pages.Count;

    // ================= Önizleme + yazıcı =================
    /// <summary>Önizleme açar (sessiz modda doğrudan basar). true = yazıcıya gönderildi.</summary>
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        if (PrintRouter.IsSilent) return PrintRouter.Print(doc, owner, PreparePaper);
        return PreviewDialog.Show(owner, doc, PageCount, $"{PageCount} kart • A4 {(_layout.Landscape ? "yatay" : "dikey")}",
                                  f => PrintRouter.Print(doc, f, PreparePaper));
    }

    private bool PreparePaper(PrintDocument doc, IWin32Window owner)
    {
        if (FindA4(doc) is { } a4) doc.DefaultPageSettings.PaperSize = a4;
        doc.DefaultPageSettings.Landscape = _layout.Landscape;
        return true;
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_eventName} - masa kartları" };
        PrintRouter.ApplyTarget(doc);
        if (FindA4(doc) is { } a4) doc.DefaultPageSettings.PaperSize = a4;
        doc.DefaultPageSettings.Landscape = _layout.Landscape; // önizleme de doğrudan yatay ölçeklenir
        // Her sayfa yönü açıkça bildirir: önizleme ilk çizimde de yatay ölçülsün (yazıcı varsayılanı dikey olsa bile).
        doc.QueryPageSettings += (_, e) => e.PageSettings.Landscape = _layout.Landscape;
        doc.OriginAtMargins = false;
        doc.BeginPrint += (_, _) => { _pageIndex = 0; _logo = LoadLogo(_logoPath); };
        doc.EndPrint += (_, _) => { _logo?.Dispose(); _logo = null; _pageIndex = 0; };
        doc.PrintPage += OnPrintPage;
        return doc;
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

    private void OnPrintPage(object? sender, PrintPageEventArgs e)
    {
        var g = e.Graphics!;
        g.PageUnit = GraphicsUnit.Point;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        bool preview = (sender as PrintDocument)?.PrintController?.IsPreview == true;
        if (!preview) g.TranslateTransform(-e.PageSettings.HardMarginX / 100f * 72f, -e.PageSettings.HardMarginY / 100f * 72f);

        if (_pageIndex < _pages.Count) Draw(g, _layout, _pages[_pageIndex], _logo, _eventName, _font, _filled, _fullWidth);
        _pageIndex++;
        e.HasMorePages = _pageIndex < _pages.Count;
    }

    /// <summary>
    /// Bir kartı (punto koordinatlarında) çizer; önizleme küçük resmi ve PDF de bunu kullanır.
    /// <paramref name="filled"/>: false = beyaz zeminde tema renginde yazı, true = tema renginde zeminde beyaz yazı.
    /// </summary>
    public static void Draw(Graphics g, CardLayout layout, CardSpec card, Image? logo, string eventName, string? font, bool filled,
                            bool fullWidth = false)
    {
        var prevInterp = g.InterpolationMode;
        var prevSmooth = g.SmoothingMode;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        if (logo is not null)
            layout.DrawLogo(g, logo, fullWidth); // oran her iki modda da korunur
        else
            CardLayout.DrawLines(g, CardLayout.Fit(eventName.ToUpper(new System.Globalization.CultureInfo("tr-TR")), layout.LogoRect, 60, font, 12, 4),
                                 layout.LogoRect, card.Color, font);

        if (filled)
            using (var b = new SolidBrush(card.Color)) g.FillRectangle(b, layout.BandRect);
        var box = layout.BandTextRect;
        CardLayout.DrawLines(g, CardLayout.Fit(card.Category, box, 230, font, 14, layout.Landscape ? 2 : 3), box,
                             filled ? Color.White : card.Color, font);
        g.InterpolationMode = prevInterp;
        g.SmoothingMode = prevSmooth;
    }

    // ================= PDF =================
    public void SavePdf(string path)
    {
        using var logo = LoadLogo(_logoPath);
        GdiPdf.Save(path, _pages, _layout.PageW, _layout.PageH, (g, card) => Draw(g, _layout, card, logo, _eventName, _font, _filled, _fullWidth));
    }
}
