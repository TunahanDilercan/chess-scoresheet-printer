using System.Diagnostics;
using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using Xunit;
using Xunit.Abstractions;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Yazıcıya gönderme süresi (CSP_BENCH=1): "Microsoft Print to PDF" yazıcısına dosyaya basar (kâğıt harcanmaz).
/// Eski yol (sayfalar yazıcı sürücüsüne vektör çizilir, arayüz iş parçacığında) ile yeni yol (PagedOutput:
/// 300 dpi sayfa resmi, arka planda) karşılaştırılır.
/// </summary>
public class PrintBenchTests
{
    private readonly ITestOutputHelper _out;
    public PrintBenchTests(ITestOutputHelper o) => _out = o;

    private static PrintDocument ToPdfPrinter(string file)
    {
        var doc = new PrintDocument { PrintController = new StandardPrintController() };
        doc.PrinterSettings.PrinterName = "Microsoft Print to PDF";
        doc.PrinterSettings.PrintToFile = true;
        doc.PrinterSettings.PrintFileName = file;
        return doc;
    }

    [Fact]
    public async Task OldVersusNew()
    {
        if (Environment.GetEnvironmentVariable("CSP_BENCH") != "1") return;
        using var p = new AthleteSheetPrinter(AthleteBenchTests.Many(120), new AthleteCardConfig { FooterBand = true }, AthleteBenchTests.BigImages(), "Deneme");
        var f1 = Path.Combine(Path.GetTempPath(), $"csp-print-{Guid.NewGuid():N}.pdf");
        var f2 = Path.Combine(Path.GetTempPath(), $"csp-print-{Guid.NewGuid():N}.pdf");

        // eski: vektör, arayüz iş parçacığında
        using (var doc = ToPdfPrinter(f1))
        {
            int page = 0;
            doc.PrintPage += (_, e) =>
            {
                var g = e.Graphics!; g.PageUnit = GraphicsUnit.Point; AthleteSheetPrinter.Prepare(g);
                p.DrawPage(g, page++); e.HasMorePages = page < p.PageCount;
            };
            var sw = Stopwatch.StartNew();
            doc.Print();
            _out.WriteLine($"eski yol: {p.PageCount} sayfa {sw.ElapsedMilliseconds} ms (bu sürece arayüz donuk), dosya {new FileInfo(f1).Length / 1048576.0:0.0} MB");
        }

        // yeni: PagedOutput (arka planda; çağıran iş parçacığı serbest)
        var doc2 = ToPdfPrinter(f2);
        int reports = 0;
        var sw2 = Stopwatch.StartNew();
        var task = PagedOutput.PrintAsync(p, doc2, new Progress<(int, int)>(_ => Interlocked.Increment(ref reports)), default);
        long returned = sw2.ElapsedMilliseconds;   // çağrı hemen döner: arayüz donmaz
        await task;
        _out.WriteLine($"yeni yol: {sw2.ElapsedMilliseconds} ms toplam, çağrı {returned} ms'de döndü, dosya {new FileInfo(f2).Length / 1048576.0:0.0} MB");
        try { File.Delete(f1); File.Delete(f2); } catch { }
    }
}
