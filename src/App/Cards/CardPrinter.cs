using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RenderInit = NotasyonOtomasyonu.Render.QuestPdfRenderer;
using Color = System.Drawing.Color;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Kategori masa kartlarını A4'e basar (önizlemeli) veya PDF olarak kaydeder.</summary>
public sealed class CardPrinter
{
    private const string FontName = "DejaVu Sans";
    private static readonly Color TitleColor = Color.FromArgb(40, 40, 40);

    private readonly List<CardSpec> _pages;   // adet kadar açılmış
    private readonly string? _logoPath;
    private readonly string _eventName;
    private System.Drawing.Image? _logo;
    private int _pageIndex;

    public CardPrinter(IEnumerable<CardSpec> cards, string? logoPath, string eventName)
    {
        _pages = cards.SelectMany(c => Enumerable.Repeat(c, Math.Max(0, c.Copies))).ToList();
        _logoPath = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath) ? logoPath : null;
        _eventName = eventName;
    }

    public int PageCount => _pages.Count;

    // ================= GDI (önizleme + yazıcı) =================
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        return PreviewDialog.Show(owner, doc, PageCount, $"{PageCount} kart • A4", f => ChoosePrinterAndPrint(doc, f));
    }

    private static bool ChoosePrinterAndPrint(PrintDocument doc, IWin32Window owner)
    {
        using var pd = new PrintDialog { Document = doc, UseEXDialog = true, AllowSomePages = false };
        if (pd.ShowDialog(owner) != DialogResult.OK) return false;
        if (FindA4(doc) is { } a4) doc.DefaultPageSettings.PaperSize = a4;
        doc.DefaultPageSettings.Landscape = false;
        doc.PrintController = new StandardPrintController();
        doc.Print();
        return true;
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_eventName} - masa kartları" };
        if (FindA4(doc) is { } a4) doc.DefaultPageSettings.PaperSize = a4;
        doc.DefaultPageSettings.Landscape = false;
        doc.OriginAtMargins = false;
        doc.BeginPrint += (_, _) =>
        {
            _pageIndex = 0;
            try { _logo = _logoPath is null ? null : System.Drawing.Image.FromFile(_logoPath); } catch { _logo = null; }
        };
        doc.EndPrint += (_, _) => { _logo?.Dispose(); _logo = null; _pageIndex = 0; };
        doc.PrintPage += OnPrintPage;
        return doc;
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

        if (_pageIndex < _pages.Count) Draw(g, _pages[_pageIndex], _logo, _eventName);
        _pageIndex++;
        e.HasMorePages = _pageIndex < _pages.Count;
    }

    /// <summary>Bir kartı (punto koordinatlarında) çizer; önizleme küçük resmi de bunu kullanır.</summary>
    public static void Draw(Graphics g, CardSpec card, System.Drawing.Image? logo, string eventName)
    {
        if (logo is not null)
            g.DrawImage(logo, CardLayout.FitImage(new SizeF(logo.Width, logo.Height), CardLayout.LogoRect));
        else
            DrawLines(g, TitleLines(eventName), CardLayout.LogoRect, TitleColor);

        var band = CardLayout.BandRect;
        using (var b = new SolidBrush(card.Color)) g.FillRectangle(b, band);
        DrawLines(g, CardLayout.Fit(card.Category, CardLayout.BandTextRect, 220), CardLayout.BandTextRect, CardLayout.TextColorFor(card.Color));
    }

    private static (List<string> Lines, float Pt) TitleLines(string eventName)
        => CardLayout.Fit(eventName.ToUpper(new System.Globalization.CultureInfo("tr-TR")), CardLayout.LogoRect, 60, 12, 5);

    private static void DrawLines(Graphics g, (List<string> Lines, float Pt) fit, RectangleF box, Color color)
    {
        using var font = TextFitter.CreateFont(fit.Pt, bold: true);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        float lh = fit.Pt * CardLayout.LineSpacing;
        float y = box.Y + (box.Height - lh * fit.Lines.Count) / 2;
        foreach (var line in fit.Lines)
        {
            g.DrawString(line, font, brush, new RectangleF(box.X - 50, y, box.Width + 100, lh), fmt);
            y += lh;
        }
    }

    // ================= PDF =================
    public void SavePdf(string path)
    {
        RenderInit.EnsureInitialized();
        QuestPDF.Infrastructure.Image? logo = _logoPath is null ? null : QuestPDF.Infrastructure.Image.FromFile(_logoPath);
        var logoBox = CardLayout.LogoRect;
        if (_logoPath is not null)
        {
            using var probe = System.Drawing.Image.FromFile(_logoPath);
            logoBox = CardLayout.FitImage(new SizeF(probe.Width, probe.Height), CardLayout.LogoRect);
        }
        var title = TitleLines(_eventName);
        var fits = _pages.Select(p => p.Category).Distinct()
                         .ToDictionary(c => c, c => CardLayout.Fit(c, CardLayout.BandTextRect, 220));

        Document.Create(doc =>
        {
            foreach (var card in _pages)
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(s => s.FontFamily(FontName).Bold());
                    page.Content().Column(col =>
                    {
                        col.Item().Height(CardLayout.PageH * (1 - CardLayout.BandRatio)).Padding(CardLayout.Margin)
                           .AlignCenter().AlignMiddle().Element(c =>
                           {
                               // Hizalanan kapta resim doğal boyutunda kalıyordu: oranı korunmuş
                               // kutu ölçüsünü açıkça ver ki alan dolsun.
                               if (logo is not null) c.Width(logoBox.Width - 1).Height(logoBox.Height - 1).Image(logo).FitArea();
                               else Lines(c, title, TitleColor);
                           });
                        var bandText = CardLayout.BandTextRect;
                        col.Item().Height(CardLayout.PageH * CardLayout.BandRatio).Background(CardLayout.ToHex(card.Color))
                           .PaddingHorizontal(bandText.X).AlignCenter().AlignMiddle()
                           .Element(c => Lines(c, fits[card.Category], CardLayout.TextColorFor(card.Color)));
                    });
                });
            }
        }).GeneratePdf(path);
    }

    private static void Lines(IContainer c, (List<string> Lines, float Pt) fit, Color color)
    {
        c.ScaleToFit().Column(col =>
        {
            foreach (var line in fit.Lines)
                col.Item().AlignCenter().Text(line).FontSize(fit.Pt)
                   .FontColor(CardLayout.ToHex(color));
        });
    }
}
