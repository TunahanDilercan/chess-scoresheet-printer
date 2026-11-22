using System.Diagnostics;
using System.Drawing.Printing;
using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.Core;
using Xunit;
using Xunit.Abstractions;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Süre ölçümü (CSP_BENCH=1 verilince çalışır): 120 sporculuk önizleme ve PDF.</summary>
public class AthleteBenchTests
{
    private readonly ITestOutputHelper _out;
    public AthleteBenchTests(ITestOutputHelper o) => _out = o;

    internal static AthleteImages BigImages()
    {
        var rnd = new Random(1);
        Bitmap Noise(int w, int h)
        {
            var b = new Bitmap(w, h);
            using var g = Graphics.FromImage(b);
            for (int i = 0; i < 400; i++)
                using (var br = new SolidBrush(Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256))))
                    g.FillEllipse(br, rnd.Next(w), rnd.Next(h), rnd.Next(40, 400), rnd.Next(40, 400));
            return b;
        }
        return new AthleteImages(Noise(2400, 1350), Noise(2400, 600), Noise(1500, 1500), Noise(600, 600));
    }

    internal static List<Athlete> Many(int n)
        => Enumerable.Range(1, n).Select(i => new Athlete($"Deneme Sporcu {i}", i % 3 == 0 ? "12 Yaş Kız" : "14 Yaş", "ISPARTA FEN LİSESİ", 1500 + i, 1400 + i)).ToList();

    [Theory]
    [InlineData(AthleteCardConfig.Badge)]
    [InlineData(AthleteCardConfig.Plate)]
    public void Measure(string kind)
    {
        if (Environment.GetEnvironmentVariable("CSP_BENCH") != "1") return;
        var cfg = new AthleteCardConfig { Kind = kind, FooterBand = true };
        var img = BigImages();
        var p = new AthleteSheetPrinter(Many(120), cfg, img, "Deneme Turnuvası");
        _out.WriteLine($"{kind}: {p.CardCount} kart, {p.PageCount} sayfa");

        // 1) PDF yolu: sayfa başına 300 dpi resme çizim
        var sw = Stopwatch.StartNew();
        using (var bmp = new Bitmap(2480, 3508))
        {
            bmp.SetResolution(300, 300);
            using var g = Graphics.FromImage(bmp);
            g.PageUnit = GraphicsUnit.Point;
            for (int i = 0; i < p.PageCount; i++) { AthleteSheetPrinter.Prepare(g); p.DrawPage(g, i); }
        }
        _out.WriteLine($"  çizim (300 dpi, tüm sayfalar): {sw.ElapsedMilliseconds} ms");

        // 2) Önizleme yolu: PrintPreviewControl'ün yaptığı gibi bütün sayfaları metafile'a kaydeder
        sw.Restart();
        var doc = new PrintDocument();
        int page = 0;
        doc.PrintPage += (_, e) => { var g = e.Graphics!; g.PageUnit = GraphicsUnit.Point; AthleteSheetPrinter.Prepare(g); p.DrawPage(g, page++); e.HasMorePages = page < p.PageCount; };
        var ctl = new PreviewPrintController();
        doc.PrintController = ctl;
        long before = GC.GetTotalMemory(true);
        try { doc.Print(); } catch (InvalidPrinterException) { _out.WriteLine("  yazıcı yok"); return; }
        var pages = ctl.GetPreviewPageInfo();
        _out.WriteLine($"  önizleme kaydı (tüm sayfalar): {sw.ElapsedMilliseconds} ms, {pages.Length} sayfa");
        // bir sayfayı ekrana çizmek
        sw.Restart();
        using (var screen = new Bitmap(800, 1130))
        using (var g = Graphics.FromImage(screen))
            for (int i = 0; i < Math.Min(3, pages.Length); i++) g.DrawImage(pages[i].Image, 0, 0, 800, 1130);
        _out.WriteLine($"  önizleme sayfa gösterimi (3 sayfa): {sw.ElapsedMilliseconds} ms");
        foreach (var pi in pages) pi.Image.Dispose();

        // 3) PDF'in tamamı
        var pdf = Path.Combine(Path.GetTempPath(), $"csp-bench-{Guid.NewGuid():N}.pdf");
        sw.Restart();
        p.SavePdf(pdf);
        _out.WriteLine($"  PDF (tamamı): {sw.ElapsedMilliseconds} ms, {new FileInfo(pdf).Length / 1048576.0:0.0} MB");
        File.Delete(pdf);
        p.Dispose();
    }
}
