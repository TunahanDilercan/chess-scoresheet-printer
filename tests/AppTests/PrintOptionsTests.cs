using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Toplu baskıda yazıcı penceresi ve çoklu yazıcı hedefleri.</summary>
[Collection("PrintRouter")]   // PrintRouter.Config ortak durum: bu testler sırayla
public class PrintOptionsTests : IDisposable
{
    private const string Pdf = "Microsoft Print to PDF";
    private readonly PrintConfig _saved = PrintRouter.Config;
    public void Dispose() => PrintRouter.Config = _saved;

    private static bool HasPdf() => PrinterSettings.InstalledPrinters.Cast<string>().Contains(Pdf);

    [Fact]
    public void NotSilent_AlwaysShowsDialog()
    {
        PrintRouter.Config = new PrintConfig { Silent = false };
        Assert.True(PrintRouter.ShowsDialog(null));
        Assert.True(PrintRouter.ShowsDialog(new PrintOptions(Bulk: true)));
        Assert.False(PrintRouter.ShowsDialog(new PrintOptions(NoDialog: true)));   // seçimler önceden onaylandı
    }

    [Fact]
    public void Silent_ShowsDialogOnlyForBulk_WhenEnabled()
    {
        if (!HasPdf()) return;
        PrintRouter.Config = new PrintConfig { Silent = true, PrinterName = Pdf, ConfirmBulk = true };
        Assert.True(PrintRouter.IsSilent);
        Assert.False(PrintRouter.ShowsDialog(null));                              // tek kategori: sormadan
        Assert.True(PrintRouter.ShowsDialog(new PrintOptions(Bulk: true)));       // toplu: önce yazıcı penceresi
        Assert.True(PrintRouter.SkipsPreview(new PrintOptions(Bulk: true)));      // önizleme yok, yalnız pencere

        PrintRouter.Config.ConfirmBulk = false;                                   // ayardan kapatılınca sormaz
        Assert.False(PrintRouter.ShowsDialog(new PrintOptions(Bulk: true)));
    }

    [Fact]
    public void ConfirmBulk_IsOnByDefault() => Assert.True(new PrintConfig().ConfirmBulk);

    [Fact]
    public void MultiTargets_NeedTwoInstalledDistinctPrinters()
    {
        PrintRouter.Config = new PrintConfig { MultiPrinter = true, MultiPrinters = { "Yok Yazıcı A", "Yok Yazıcı B" } };
        Assert.Empty(PrintRouter.MultiTargets());                                 // kurulu değiller
        PrintRouter.Config = new PrintConfig { MultiPrinter = false, MultiPrinters = { Pdf, "Microsoft XPS Document Writer" } };
        Assert.Empty(PrintRouter.MultiTargets());                                 // mod kapalı
        if (!HasPdf()) return;
        PrintRouter.Config = new PrintConfig { MultiPrinter = true, MultiPrinters = { Pdf, Pdf.ToUpperInvariant() } };
        Assert.Empty(PrintRouter.MultiTargets());                                 // aynı yazıcı iki kez sayılmaz
    }

    [Fact]
    public void ApplyTarget_UsesGivenPrinter()
    {
        if (!HasPdf()) return;
        PrintRouter.Config = new PrintConfig();
        using var doc = new PrintDocument();
        PrintRouter.ApplyTarget(doc, Pdf);
        Assert.Equal(Pdf, doc.PrinterSettings.PrinterName);
    }
}
