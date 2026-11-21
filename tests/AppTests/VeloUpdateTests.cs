using System.Text.Json;
using NotasyonOtomasyonu.App.Update;
using Velopack;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Kurulu sürüm güncellemesi (Velopack): indirilecek boyut planı, güncelleme yolu seçimi, kuruluma geçiş için
/// Setup paketinin bulunması ve özetinin okunması, "internetten indirildi" işaretinin kaldırılması.
/// </summary>
[Collection("updater")]
public class VeloUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-velo-test-" + Guid.NewGuid().ToString("N"));

    public VeloUpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private const string ZipHash = "d3eb539a556352f3f47881d71fb0e5777b2f3e9a4251d283c18c67ce996774b7";
    private const string SetupHash = "29167a702515842177c4361526ad7c55678bbe56f6352509a4d2235a0b61ae1e";
    private static readonly string ShaFile =
        $"{ZipHash}  ChessScoresheetPrinter-v1.7.0-win-x64.zip\n{SetupHash}  ChessScoresheetPrinter-win-Setup.exe\n";

    [Fact]
    public void ExpectedSha_PicksLineByFileName()
    {
        Assert.Equal(ZipHash, Updater.ExpectedSha(ShaFile, "ChessScoresheetPrinter-v1.7.0-win-x64.zip"));
        Assert.Equal(SetupHash, Updater.ExpectedSha(ShaFile, "ChessScoresheetPrinter-win-Setup.exe"));
        Assert.Equal(SetupHash, Updater.ExpectedSha(ShaFile.Replace("\n", "\r\n").ToUpperInvariant().Replace("CHESSSCORESHEETPRINTER-WIN-SETUP.EXE", "ChessScoresheetPrinter-win-Setup.exe"), "ChessScoresheetPrinter-win-Setup.exe"));
    }

    [Fact]
    public void ExpectedSha_UnknownName_FallsBackToFirstLine_AndOldFormatWorks()
    {
        Assert.Equal(ZipHash, Updater.ExpectedSha(ShaFile, "başka.zip"));
        Assert.Equal(ZipHash, Updater.ExpectedSha(ZipHash + "  paket.zip", "paket.zip"));
        Assert.Equal(ZipHash, Updater.ExpectedSha(ZipHash, "paket.zip"));
        Assert.Equal("", Updater.ExpectedSha("", "paket.zip"));
    }

    [Fact]
    public void ShaFile_StaysReadableByOldVersions()
    {
        // v1.6.x yalnız dosyanın ilk sözcüğünü okur: iki satırlı dosyada da ZIP özetini bulmalı
        var oldRead = ShaFile.Trim().Split(' ', '\t')[0].ToLowerInvariant();
        Assert.Equal(ZipHash, oldRead);
    }

    private static JsonElement Release(bool setup)
    {
        var assets = new List<string>
        {
            """{"name":"ChessScoresheetPrinter-v1.7.0-win-x64.zip","browser_download_url":"https://x/zip","size":80000000}""",
            """{"name":"ChessScoresheetPrinter-v1.7.0-win-x64.sha256","browser_download_url":"https://x/sha","size":200}""",
            """{"name":"ChessScoresheetPrinter-1.7.0-full.nupkg","browser_download_url":"https://x/nupkg","size":96000000}""",
            """{"name":"releases.win.json","browser_download_url":"https://x/feed","size":900}""",
        };
        if (setup) assets.Add("""{"name":"ChessScoresheetPrinter-win-Setup.exe","browser_download_url":"https://x/setup","size":104000000}""");
        var json = $$"""{"tag_name":"v1.7.0","body":"not","html_url":"https://x","assets":[{{string.Join(",", assets)}}]}""";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void Parse_FindsSetup_AndStillFindsLegacyZip()
    {
        var r = Updater.Parse(Release(setup: true))!;
        Assert.True(r.HasSetup);
        Assert.Equal("ChessScoresheetPrinter-win-Setup.exe", r.SetupName);
        Assert.Equal("https://x/setup", r.SetupUrl);
        Assert.Equal(104000000, r.SetupSize);
        Assert.Equal("ChessScoresheetPrinter-v1.7.0-win-x64.zip", r.ZipName);   // eski sürümlerin aradığı paket
        Assert.Equal("https://x/sha", r.ShaUrl);
    }

    [Fact]
    public void Parse_WithoutSetup_HasSetupFalse()
        => Assert.False(Updater.Parse(Release(setup: false))!.HasSetup);

    [Theory]
    [InlineData(true, false, true, "Installed")]    // kurulu: yalnız değişenler
    [InlineData(true, true, true, "Installed")]
    [InlineData(false, false, true, "Migrate")]     // taşınabilir exe + Setup var: kuruluma geç
    [InlineData(false, true, true, "Zip")]          // "portable" dosyası: ZIP ile yerinde güncelle
    [InlineData(false, false, false, "Zip")]        // Setup yoksa: eski usul
    public void Route_IsChosenCorrectly(bool installed, bool portable, bool setup, string expected)
        => Assert.Equal(expected, UpdateForm.RouteFor(installed, portable, Updater.Parse(Release(setup))!).ToString());

    [Theory]
    [InlineData("1.7.0", "1.6.12", false, false, true, true)]    // daha yeni sürüm
    [InlineData("1.7.0", "1.7.0", false, false, true, true)]     // aynı sürüm, taşınabilir exe: kuruluma geç
    [InlineData("1.7.0", "1.7.0", true, false, true, false)]     // zaten kurulu: yapılacak bir şey yok
    [InlineData("1.7.0", "1.7.0", false, true, true, false)]     // "portable" kullanım: kurulum önerilmez
    [InlineData("1.7.0", "1.7.0", false, false, false, false)]   // Setup yoksa geçiş yok
    [InlineData("1.6.12", "1.7.0", false, false, true, false)]   // daha eski sürüme geçilmez
    public void Actionable_NewerOrSameVersionMigration(string latest, string current, bool installed, bool portable, bool setup, bool expected)
    {
        var r = Updater.Parse(Release(setup))! with { Version = Version.Parse(latest) };
        Assert.Equal(expected, UpdateForm.Actionable(r, Version.Parse(current), installed, portable));
    }

    private static VelopackAsset Asset(string version, VelopackAssetType type, long size)
        => new() { PackageId = "ChessScoresheetPrinter", Version = SemanticVersion.Parse(version), Type = type, Size = size, FileName = $"x-{version}-{type}.nupkg" };

    [Fact]
    public void Plan_UsesDeltas_WhenSmaller()
    {
        var full = Asset("1.7.2", VelopackAssetType.Full, 96_000_000);
        var baseRel = Asset("1.7.0", VelopackAssetType.Full, 95_000_000);
        var deltas = new[] { Asset("1.7.1", VelopackAssetType.Delta, 2_000_000), Asset("1.7.2", VelopackAssetType.Delta, 3_000_000) };
        var plan = Velo.Plan(new UpdateInfo(full, false, baseRel, deltas));
        Assert.True(plan.UsesDelta);
        Assert.Equal(2, plan.DeltaCount);
        Assert.Equal(5_000_000, plan.DownloadBytes);
        Assert.Equal(96_000_000, plan.FullBytes);
        Assert.Equal(new Version(1, 7, 2), plan.Version);
    }

    [Fact]
    public void Plan_FullPackage_WhenNoDeltaOrDeltasLarger()
    {
        var full = Asset("1.7.2", VelopackAssetType.Full, 10_000_000);
        var noDelta = Velo.Plan(new UpdateInfo(full, false, null!, Array.Empty<VelopackAsset>()));
        Assert.False(noDelta.UsesDelta);
        Assert.Equal(10_000_000, noDelta.DownloadBytes);
        var big = Velo.Plan(new UpdateInfo(full, false, Asset("1.7.0", VelopackAssetType.Full, 9_000_000),
                                           new[] { Asset("1.7.1", VelopackAssetType.Delta, 6_000_000), Asset("1.7.2", VelopackAssetType.Delta, 6_000_000) }));
        Assert.False(big.UsesDelta);
        Assert.Equal(10_000_000, big.DownloadBytes);
    }

    [Fact]
    public void ToRelease_CarriesVersionAndNotes()
    {
        var full = Asset("1.7.2", VelopackAssetType.Full, 1000);
        full.NotesMarkdown = "### Değişenler\n- sporcu kartı";
        var r = Velo.ToRelease(Velo.Plan(new UpdateInfo(full, false, null!, Array.Empty<VelopackAsset>())));
        Assert.Equal(new Version(1, 7, 2), r.Version);
        Assert.Contains("sporcu kartı", r.Notes);
    }

    [Fact]
    public void Unblock_RemovesMarkOfTheWeb_FromExeAndFonts()
    {
        var exe = Path.Combine(_dir, "ChessScoresheetPrinter.exe");
        File.WriteAllText(exe, "x");
        Directory.CreateDirectory(Path.Combine(_dir, "LatoFont"));
        var font = Path.Combine(_dir, "LatoFont", "Lato-Regular.ttf");
        File.WriteAllText(font, "f");
        foreach (var f in new[] { exe, font })
            File.WriteAllText(f + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        Assert.True(File.Exists(exe + ":Zone.Identifier"));
        Assert.Equal(2, Updater.Unblock(_dir, exe));
        Assert.False(File.Exists(exe + ":Zone.Identifier"));
        Assert.False(File.Exists(font + ":Zone.Identifier"));
        Assert.Equal("x", File.ReadAllText(exe));   // dosyanın kendisi yerinde
        Assert.Equal(0, Updater.Unblock(_dir, exe));  // ikinci kez: yapılacak bir şey yok
    }

    [Fact]
    public void NotInstalled_WhenRunFromTests()
        => Assert.False(Velo.IsInstalled);
}
