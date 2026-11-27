using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Yazıcıya gönderme: uzun çıktı küçük işler hâlinde sırayla gider, sayfalar eksiksiz ve sıralı.</summary>
public class PrintJobTests
{
    private const string PdfPrinter = "Microsoft Print to PDF";

    /// <summary>Her sayfaya kendi numarasını yazan basit çıktı.</summary>
    private sealed class Numbered(int pages) : IPagedOutput
    {
        public readonly List<int> Drawn = new();
        public string DocumentName => "Deneme kartları";
        public SizeF PageSizePt => new(200, 140);   // küçük sayfa: test hızlı
        public bool Landscape => true;
        public int PageCount => pages;
        public object PageKey(int page) => page;
        public void DrawPage(Graphics g, int page) { lock (Drawn) Drawn.Add(page); g.DrawString((page + 1).ToString(), SystemFonts.DefaultFont, Brushes.Black, 10, 10); }
    }

    private static bool HasPdfPrinter() => PrinterSettings.InstalledPrinters.Cast<string>().Contains(PdfPrinter);

    private static PrintDocument Doc(List<string> jobNames)
    {
        // önizleme denetleyicisi: sürücü gerçek sayfa düzenini verir ama kâğıda/dosyaya bir şey gitmez
        var doc = new PrintDocument { DocumentName = "Deneme kartları", PrintController = new PreviewPrintController() };
        doc.PrinterSettings.PrinterName = PdfPrinter;
        doc.BeginPrint += (s, _) => jobNames.Add(((PrintDocument)s!).DocumentName);
        return doc;
    }

    [Fact]
    public async Task LongOutput_IsSentAsSequentialJobs()
    {
        if (!HasPdfPrinter()) return;
        var o = new Numbered(23);
        var jobs = new List<string>();
        var reports = new List<(int, int)>();
        var ok = await PagedOutput.PrintAsync(o, Doc(jobs), new SyncProgress(reports), default);
        Assert.True(ok);
        Assert.Equal(new[] { "Deneme kartları (1/3)", "Deneme kartları (2/3)", "Deneme kartları (3/3)" }, jobs);
        Assert.Equal(Enumerable.Range(0, 23), o.Drawn);                 // her sayfa bir kez, sırayla
        Assert.Equal((23, 23), reports[^1]);
    }

    [Fact]
    public async Task ShortOutput_IsOneJob_WithPlainName()
    {
        if (!HasPdfPrinter()) return;
        var o = new Numbered(4);
        var jobs = new List<string>();
        await PagedOutput.PrintAsync(o, Doc(jobs), null, default);
        Assert.Equal(new[] { "Deneme kartları" }, jobs);
        Assert.Equal(4, o.Drawn.Count);
    }

    [Fact]
    public async Task FailingJob_ReportsHowManyPagesWereSent()
    {
        if (!HasPdfPrinter()) return;
        var o = new Numbered(25);
        var jobs = new List<string>();
        var doc = Doc(jobs);
        // ikinci iş başlarken yazıcı "kaybolsun"
        doc.BeginPrint += (_, e) => { if (jobs.Count == 2) throw new InvalidPrinterException(new PrinterSettings { PrinterName = "Yok" }); };
        var ex = await Assert.ThrowsAsync<PrintIncompleteException>(() => PagedOutput.PrintAsync(o, doc, null, default));
        Assert.Equal(10, ex.Sent);
        Assert.Equal(25, ex.Total);
        Assert.Contains("25 sayfanın 10 tanesi", ex.Message);
        Assert.Contains("yazıcı bulunamadı", ex.Message);
    }

    [Fact]
    public async Task Cancel_StopsBeforeNextJob()
    {
        if (!HasPdfPrinter()) return;
        var o = new Numbered(30);
        var jobs = new List<string>();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(new List<(int, int)>(), p => { if (p.Item1 == 10) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PagedOutput.PrintAsync(o, Doc(jobs), progress, cts.Token));
        Assert.Single(jobs);                       // ikinci iş hiç başlamadı
        Assert.True(o.Drawn.Count <= 10);
    }

    private sealed class SyncProgress(List<(int, int)> list, Action<(int, int)>? on = null) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) v) { lock (list) list.Add(v); on?.Invoke(v); }
    }
}
