using System.Diagnostics;
using System.Drawing.Imaging;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.Core;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Color = System.Drawing.Color;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using Xunit;
using Xunit.Abstractions;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>PDF aşamalarının süresi (CSP_BENCH=1): sayfa resmi kodlama ve PDF'e yazma.</summary>
public class PdfBenchTests
{
    private readonly ITestOutputHelper _out;
    public PdfBenchTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Phases()
    {
        if (Environment.GetEnvironmentVariable("CSP_BENCH") != "1") return;
        NotasyonOtomasyonu.Render.QuestPdfRenderer.EnsureInitialized();
        var p = new AthleteSheetPrinter(AthleteBenchTests.Many(8), new AthleteCardConfig { FooterBand = true }, AthleteBenchTests.BigImages(), "x");
        using var bmp = new Bitmap(2480, 3508);
        bmp.SetResolution(300, 300);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.PageUnit = GraphicsUnit.Point; AthleteSheetPrinter.Prepare(g); p.DrawPage(g, 0); }

        var sw = Stopwatch.StartNew();
        byte[] png; using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
        _out.WriteLine($"PNG kodlama: {sw.ElapsedMilliseconds} ms, {png.Length / 1024} KB");

        sw.Restart();
        byte[] jpg;
        using (var ms = new MemoryStream())
        {
            var enc = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
            using var ep = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L) } };
            bmp.Save(ms, enc, ep); jpg = ms.ToArray();
        }
        _out.WriteLine($"JPEG kodlama: {sw.ElapsedMilliseconds} ms, {jpg.Length / 1024} KB");

        foreach (var (name, data, orig) in new[] { ("PNG", png, false), ("PNG orijinal", png, true), ("JPEG", jpg, false), ("JPEG orijinal", jpg, true) })
        {
            sw.Restart();
            var img = QuestPDF.Infrastructure.Image.FromBinaryData(data);
            var bytes = Document.Create(d => d.Page(pg =>
            {
                pg.Size(595.28f, 841.89f, Unit.Point); pg.Margin(0);
                var i = pg.Content().Image(img).FitArea();
                if (orig) i.WithCompressionQuality(ImageCompressionQuality.Best).UseOriginalImage();
            })).GeneratePdf();
            _out.WriteLine($"QuestPDF ({name}): {sw.ElapsedMilliseconds} ms, {bytes.Length / 1024} KB");
        }
    }
}
