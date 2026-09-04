using System.Drawing;
using System.Drawing.Printing;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Hazır (önceden basılı) notasyon kağıdına yalnızca değişken yazıları GDI ile doğrudan basar.
/// Koordinatlar fiziksel kağıdın sol-üst köşesine göredir (yazıcı sabit kenar boşluğu telafi edilir),
/// böylece baskı, kağıttaki boşluklarla milimetrik hizalanır.
/// </summary>
public sealed class SheetPrinter
{
    private readonly OverlayTemplate _tpl;
    private readonly Tournament _t;
    private readonly int _perPage;
    private readonly string _pageSize;
    private readonly List<List<Pairing>> _pages;
    private Image? _bg;
    private int _pageIndex;

    public SheetPrinter(OverlayTemplate tpl, Tournament t, string pageSize = "A4")
    {
        _tpl = tpl;
        _t = t;
        _pageSize = pageSize;
        _perPage = tpl.PerPage is 1 or 2 ? tpl.PerPage : 1;
        _pages = Chunk(t.Pairings, _perPage);
    }

    /// <summary>Yazıcı seçtirip yazdırır. true = yazdırma başladı, false = iptal.</summary>
    public bool PrintWithDialog(IWin32Window owner)
    {
        using var doc = BuildDocument();
        using var dlg = new PrintDialog { Document = doc, UseEXDialog = true, AllowSomePages = false };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        doc.Print();
        return true;
    }

    /// <summary>
    /// Uygulama içi ÖNİZLEME penceresi (Win11'in native penceresi Win32 uygulamalarda önizleme
    /// göstermediği için). Üstte "🖨 Yazdır" (yazıcı seçtirir) ve "Kapat" butonları vardır.
    /// </summary>
    public bool PrintWithPreview(IWin32Window owner)
    {
        var doc = BuildDocument();
        try
        {
            using var form = new Form
            {
                Text = $"Yazdırma Önizleme — {doc.DocumentName}",
                StartPosition = FormStartPosition.CenterParent,
                WindowState = FormWindowState.Maximized,
                MinimumSize = new Size(700, 500),
                Font = new Font("Segoe UI", 9.75f)
            };
            if (owner is Form pf) form.Icon = pf.Icon;

            var green = Color.FromArgb(118, 150, 86);
            var preview = new PrintPreviewControl { Dock = DockStyle.Fill, Document = doc, UseAntiAlias = true, AutoZoom = true };

            var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(245, 245, 240) };
            var btnPrint = new Button { Text = "🖨  Yazdır", Width = 150, Height = 34, Left = 10, Top = 6, BackColor = green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            var btnClose = new Button { Text = "Kapat", Width = 100, Height = 34, Left = 168, Top = 6 };
            var lblHint = new Label { Text = "Önizlemeyi inceleyin → Yazdır ile yazıcı seçip basın.", AutoSize = true, Left = 282, Top = 14, ForeColor = Color.Gray };
            bar.Controls.Add(btnPrint); bar.Controls.Add(btnClose); bar.Controls.Add(lblHint);

            btnPrint.Click += (_, _) =>
            {
                using var pd = new PrintDialog { Document = doc, UseEXDialog = true, AllowSomePages = false };
                if (pd.ShowDialog(form) != DialogResult.OK) return;
                doc.PrintController = new StandardPrintController(); // önizleme değil, gerçek baskı
                doc.Print();
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            btnClose.Click += (_, _) => form.Close();

            form.Controls.Add(preview); // Fill önce
            form.Controls.Add(bar);     // Top sonra
            form.ShowDialog(owner);
            return true;
        }
        finally { doc.Dispose(); }
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_t.Name} - {_t.RoundNo}. tur notasyon" };
        doc.DefaultPageSettings.PaperSize = FindPaper(doc) ?? doc.DefaultPageSettings.PaperSize;
        doc.DefaultPageSettings.Landscape = false;
        doc.OriginAtMargins = false;

        // Arka planı her geçişte (önizleme + baskı) yeniden yükle/temizle ki çok geçiş bozulmasın.
        doc.BeginPrint += (_, _) =>
        {
            _pageIndex = 0;
            if (_tpl.PrintBackground && TryLoadBg(out var img)) _bg = img;
        };
        doc.EndPrint += (_, _) => { _bg?.Dispose(); _bg = null; _pageIndex = 0; };
        doc.PrintPage += OnPrintPage;
        return doc;
    }

    private void OnPrintPage(object? sender, PrintPageEventArgs e)
    {
        var g = e.Graphics!;
        g.PageUnit = GraphicsUnit.Point;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // Yazıcı sabit kenar boşluğunu telafi et → (0,0) fiziksel kağıt sol-üst.
        float hx = e.PageSettings.HardMarginX / 100f * 72f;
        float hy = e.PageSettings.HardMarginY / 100f * 72f;
        g.TranslateTransform(-hx, -hy);

        var sheets = OverlayLayout.SheetRects(_perPage, _pageSize);
        var pairings = _pages[_pageIndex];

        for (int i = 0; i < pairings.Count && i < sheets.Count; i++)
        {
            var sheet = sheets[i];
            if (_bg is not null) g.DrawImage(_bg, sheet.X, sheet.Y, sheet.Width, sheet.Height);

            foreach (var placed in OverlayLayout.Place(_tpl, _t, pairings[i], sheet))
                DrawPlaced(g, placed);
        }

        _pageIndex++;
        e.HasMorePages = _pageIndex < _pages.Count;
    }

    internal static void DrawPlaced(Graphics g, PlacedText p)
    {
        if (string.IsNullOrEmpty(p.Text)) return;
        using var font = TextFitter.CreateFont(p.FontPt, p.Bold);
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit,
            Trimming = StringTrimming.None,
            LineAlignment = StringAlignment.Center,
            Alignment = p.Align switch
            {
                HAlign.Center => StringAlignment.Center,
                HAlign.Right => StringAlignment.Far,
                _ => StringAlignment.Near
            }
        };
        g.DrawString(p.Text, font, Brushes.Black, p.RectPt, fmt);
    }

    private bool TryLoadBg(out Image? img)
    {
        img = null;
        var path = _tpl.BackgroundImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try { img = Image.FromFile(path); return true; }
        catch { return false; }
    }

    private PaperSize? FindPaper(PrintDocument doc)
    {
        var want = NotasyonOtomasyonu.Core.PageGeometry.IsA5(_pageSize) ? PaperKind.A5 : PaperKind.A4;
        foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
            if (ps.Kind == want) return ps;
        return null;
    }

    private static List<List<Pairing>> Chunk(IReadOnlyList<Pairing> items, int size)
    {
        var result = new List<List<Pairing>>();
        for (int i = 0; i < items.Count; i += size)
            result.Add(items.Skip(i).Take(size).ToList());
        if (result.Count == 0) result.Add(new List<Pairing>());
        return result;
    }
}
