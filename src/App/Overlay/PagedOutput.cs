using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Çok sayfalı görsel çıktı (sporcu kartı, hakem yaka kartı, masa kartı). Önizleme, yazdırma ve PDF için ortak:
/// <see cref="PagedOutput"/> bunları arka planda, ilerleme bildirerek yapar.
/// </summary>
public interface IPagedOutput
{
    string DocumentName { get; }
    /// <summary>Sayfa boyutu (punto); yatay sayfada genişlik büyüktür.</summary>
    SizeF PageSizePt { get; }
    bool Landscape { get; }
    int PageCount { get; }
    /// <summary>Aynı içerikli sayfalar aynı anahtarı verir (bir kez çizilir; ör. masa kartı adetleri).</summary>
    object PageKey(int page);
    /// <summary>Sayfayı punto koordinatlarında çizer. Arka planda çağrılabilir (<see cref="PagedOutput.Gate"/> altında).</summary>
    void DrawPage(Graphics g, int page);
}

public static class PagedOutput
{
    /// <summary>
    /// Kart çizimleri bu kilitle sırayla yapılır: arka planda yazdırma/PDF sürerken pencere de önizleme çizebilir;
    /// aynı görsel ve yazı tipi nesneleri iki iş parçacığında aynı anda kullanılamaz (GDI+).
    /// </summary>
    public static readonly object Gate = new();

    /// <summary>Baskı ve PDF çözünürlüğü.</summary>
    public const int Dpi = 300;

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    /// <summary>Kilit altında çizer.</summary>
    public static void Draw(IPagedOutput o, Graphics g, int page)
    {
        lock (Gate) { Prepare(g); o.DrawPage(g, page); }
    }

    /// <summary>Önizleme: yalnız görünen sayfa çizilir. true = kullanıcı "Yazdır"a bastı (baskı <see cref="PrintAsync"/> ile).</summary>
    public static bool Preview(IPagedOutput o, IWin32Window owner, string hint)
        => PagePreviewDialog.Show(owner, o.DocumentName, o.PageCount, o.PageSizePt, (g, p) => Draw(o, g, p), hint, _ => true);

    /// <summary>Yazıcıyı seçtirir (arayüzde; "doğrudan yazdır" açıksa sormaz). null = vazgeçildi.</summary>
    public static PrintDocument? PreparePrint(IPagedOutput o, IWin32Window owner)
    {
        var doc = new PrintDocument { DocumentName = o.DocumentName, OriginAtMargins = false };
        PrintRouter.ApplyTarget(doc);
        if (!PrintRouter.Prepare(doc, owner, (d, _) => { SetA4(d, o.Landscape); return true; })) { doc.Dispose(); return null; }
        return doc;
    }

    /// <summary>
    /// Hazırlanan belgeyi arka planda yazıcıya gönderir (program donmaz). Her sayfa önce 300 dpi resme çizilir
    /// (PDF ile birebir aynı) ve aynı sayfalar bir kez hazırlanır: yazıcı sürücüsüne vektör olarak çizmek 120 kartta
    /// 40 sn'yi aşıyordu. İptal edilirse kalan sayfalar gönderilmez.
    /// </summary>
    public static async Task<bool> PrintAsync(IPagedOutput o, PrintDocument doc, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        doc.QueryPageSettings += (_, e) => e.PageSettings.Landscape = o.Landscape;
        // Tekrar eden sayfalar (masa kartı adetleri) bir kez hazırlanıp sıkıştırılmış saklanır; diğerleri doğrudan çizilir.
        var repeats = Enumerable.Range(0, o.PageCount).GroupBy(o.PageKey).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var cache = new Dictionary<object, byte[]>();
        int w = (int)Math.Round(o.PageSizePt.Width / 72f * Dpi), h = (int)Math.Round(o.PageSizePt.Height / 72f * Dpi);
        using var page300 = new Bitmap(w, h, PixelFormat.Format24bppRgb);   // tek sayfa resmi, her sayfada yeniden kullanılır
        page300.SetResolution(Dpi, Dpi);
        int page = 0;
        doc.BeginPrint += (_, _) => page = 0;
        doc.PrintPage += (_, e) =>
        {
            if (ct.IsCancellationRequested) { e.Cancel = true; return; }
            var key = o.PageKey(page);
            if (repeats.Contains(key))
            {
                if (!cache.TryGetValue(key, out var jpeg)) cache[key] = jpeg = RenderJpeg(o, page);
                using var ms = new MemoryStream(jpeg);
                using var decoded = Image.FromStream(ms);
                // düz resim olarak verilir: JPEG'i olduğu gibi alan sürücüler onu yeniden işleyip baskıyı yavaşlatıyordu
                using var dg = Graphics.FromImage(page300);
                dg.DrawImageUnscaled(decoded, 0, 0);
            }
            else
            {
                using var bg = Graphics.FromImage(page300);
                bg.Clear(Color.White);
                bg.PageUnit = GraphicsUnit.Point;
                Draw(o, bg, page);
            }
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Point;
            // 300 dpi resim yazıcı çözünürlüğüne (600 dpi) tam katla büyür: basit ölçekleme yeterli ve çok hızlı
            g.InterpolationMode = InterpolationMode.Bilinear;
            // kâğıdın basılamayan kenarı: çizim kâğıdın köşesinden başlasın
            g.TranslateTransform(-e.PageSettings.HardMarginX / 100f * 72f, -e.PageSettings.HardMarginY / 100f * 72f);
            g.DrawImage(page300, 0, 0, o.PageSizePt.Width, o.PageSizePt.Height);
            page++;
            progress?.Report((page, o.PageCount));
            e.HasMorePages = page < o.PageCount && !ct.IsCancellationRequested;
        };
        progress?.Report((0, o.PageCount));
        try { await RunSta(doc.Print); }
        finally { doc.Dispose(); }
        ct.ThrowIfCancellationRequested();
        return true;
    }

    /// <summary>PDF (300 dpi, arka planda); aynı sayfalar bir kez gömülür.</summary>
    public static Task SavePdfAsync(IPagedOutput o, string path, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
        => Task.Run(() => GdiPdf.Save(path, Enumerable.Range(0, o.PageCount).Select(o.PageKey).ToList(), o.PageSizePt.Width, o.PageSizePt.Height,
                                      (g, key) => Draw(o, g, FirstPage(o, key)), dpi: Dpi, format: GdiPdf.PageFormat.Photo,
                                      progress: progress, ct: ct), ct);

    private static int FirstPage(IPagedOutput o, object key)
    {
        for (int i = 0; i < o.PageCount; i++) if (Equals(o.PageKey(i), key)) return i;
        return 0;
    }

    /// <summary>Sayfayı 300 dpi resme çizip JPEG (%95) olarak döndürür; yazıcıya küçük ve hızlı gider.</summary>
    private static byte[] RenderJpeg(IPagedOutput o, int page)
    {
        int w = (int)Math.Round(o.PageSizePt.Width / 72f * Dpi), h = (int)Math.Round(o.PageSizePt.Height / 72f * Dpi);
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        bmp.SetResolution(Dpi, Dpi);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Point;
            Draw(o, g, page);
        }
        using var ms = new MemoryStream();
        var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
        bmp.Save(ms, jpeg, ep);
        return ms.ToArray();
    }

    private static void SetA4(PrintDocument doc, bool landscape)
    {
        try
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                if (ps.Kind == PaperKind.A4) { doc.DefaultPageSettings.PaperSize = ps; break; }
        }
        catch (InvalidPrinterException) { }
        doc.DefaultPageSettings.Landscape = landscape;
    }

    /// <summary>İşi ayrı bir STA iş parçacığında çalıştırır (yazdırma; arayüz iş parçacığı serbest kalır).</summary>
    private static Task RunSta(Action work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { work(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "Yazdırma" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }
}
