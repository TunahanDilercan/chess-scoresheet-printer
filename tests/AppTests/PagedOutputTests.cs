using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

public class PagedOutputTests
{
    private static AthleteSheetPrinter Printer(int n, Func<string, string?>? color = null) =>
        new(AthleteBenchTests.Many(n), new AthleteCardConfig(), new AthleteImages(null, null, null, null), "Deneme", color);

    private static Bitmap Render(IPagedOutput o, int page)
    {
        var bmp = new Bitmap(620, 877);
        bmp.SetResolution(75, 75);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        PagedOutput.Draw(o, g, page);
        return bmp;
    }

    private static int Count(Bitmap b, Func<Color, bool> match)
    {
        int n = 0;
        for (int y = 0; y < b.Height; y += 2) for (int x = 0; x < b.Width; x += 2) if (match(b.GetPixel(x, y))) n++;
        return n;
    }

    [Fact]
    public void CategoryColor_DrawsFrameInThatColor()
    {
        static bool Red(Color c) => c.R > 170 && c.G < 90 && c.B < 90;
        using var plain = Printer(4);
        using var red = Printer(4, _ => "#D32F2F");
        using (var a = Render(plain, 0)) Assert.Equal(0, Count(a, Red));
        using (var b = Render(red, 0)) Assert.True(Count(b, Red) > 50, "kategori rengi çerçevede görünmeli");
    }

    [Fact]
    public void Readable_DarkensLightColors()
    {
        var y = AthleteSheetPrinter.Readable(ColorTranslator.FromHtml("#F9A825"));   // sarı: beyaz kâğıtta okunmaz
        Assert.True(y.R * 0.299 + y.G * 0.587 + y.B * 0.114 < 0.56 * 255);
        Assert.Equal(Color.FromArgb(21, 101, 192).ToArgb(), AthleteSheetPrinter.Readable(Color.FromArgb(21, 101, 192)).ToArgb());
    }

    [Fact]
    public async Task SavePdf_RunsInBackground_ReportsProgress()
    {
        using var p = Printer(20);
        var path = Path.Combine(Path.GetTempPath(), $"csp-po-{Guid.NewGuid():N}.pdf");
        var seen = new List<(int, int)>();
        var progress = new SyncProgress(seen);
        try
        {
            await PagedOutput.SavePdfAsync(p, path, progress, default);
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.Equal((p.PageCount, p.PageCount), seen[^1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SavePdf_Cancelled_Throws()
    {
        using var p = Printer(40);
        var path = Path.Combine(Path.GetTempPath(), $"csp-po-{Guid.NewGuid():N}.pdf");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PagedOutput.SavePdfAsync(p, path, null, cts.Token)); }
        finally { File.Delete(path); }
    }

    private sealed class SyncProgress(List<(int, int)> list) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) v) { lock (list) list.Add(v); }
    }
}
