using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Badges;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Büyük dikey yaka kartında vesikalık yerine ortak yatay görsel (afiş / turnuva bilgisi).</summary>
public class BadgeBannerTests
{
    private const float Mm = 72f / 25.4f;
    private static readonly BadgeSpec Spec = new("MEHMET ARİF AVCI", "Başhakem", "Ulusal Hakem", Color.FromArgb(21, 101, 192), null, 1);

    /// <summary>95×135 mm kartı 2 px/pt ile çizer.</summary>
    private static Bitmap Render(bool banner, Image? bannerImage, Image? photo = null)
    {
        const float s = 2f;
        var bmp = new Bitmap((int)(95 * Mm * s), (int)(135 * Mm * s));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        float k = s * 72f / bmp.HorizontalResolution;
        g.ScaleTransform(k, k);
        BadgePrinter.DrawCard(g, new RectangleF(0, 0, 95 * Mm, 135 * Mm), Spec, null, photo, "Isparta Yıldızlar İl Birinciliği", null, banner, bannerImage);
        return bmp;
    }

    private static Bitmap Solid(int w, int h, Color c)
    {
        var b = new Bitmap(w, h);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    /// <summary>Kartın belli bir mm noktasındaki renk (2 px/pt).</summary>
    private static Color At(Bitmap bmp, float xMm, float yMm) => bmp.GetPixel((int)(xMm * Mm * 2), (int)(yMm * Mm * 2));

    [Fact]
    public void Banner_FillsCardWidth_WithoutCropping()
    {
        using var poster = Solid(1600, 900, Color.FromArgb(200, 30, 30));   // 16:9
        using var card = Render(banner: true, poster);
        // Kenarlara yakın da kırmızı: kart genişliğinde (5 mm kenar payı içinde)
        foreach (var x in new[] { 7f, 47.5f, 88f })
            Assert.Equal(Color.FromArgb(200, 30, 30).ToArgb(), At(card, x, 50).ToArgb());
        // 16:9 → 85 mm genişlikte ~48 mm yükseklik; görselin altı beyaz kalır (kırpılmaz, sığdırılır)
        Assert.NotEqual(Color.FromArgb(200, 30, 30).ToArgb(), At(card, 47.5f, 90).ToArgb());
    }

    [Fact]
    public void TallImage_IsLimitedTo50mm_AndCentered()
    {
        using var tall = Solid(400, 900, Color.FromArgb(30, 160, 30));
        using var card = Render(banner: true, tall);
        Assert.Equal(Color.FromArgb(30, 160, 30).ToArgb(), At(card, 47.5f, 50).ToArgb());   // ortada
        Assert.Equal(Color.White.ToArgb(), At(card, 8f, 50).ToArgb());                        // yanlarda boşluk
    }

    [Fact]
    public void BannerModeWithoutImage_ShowsPlaceholder_PhotoModeIgnoresBanner()
    {
        using var empty = Render(banner: true, null);
        using var poster = Solid(1600, 900, Color.FromArgb(200, 30, 30));
        using var photoMode = Render(banner: false, poster);   // fotoğraf modunda afiş kullanılmaz
        Assert.NotEqual(Color.FromArgb(200, 30, 30).ToArgb(), At(photoMode, 47.5f, 50).ToArgb());
        Assert.Equal(Color.White.ToArgb(), At(empty, 47.5f, 30).ToArgb());
    }

    [Fact]
    public void NameAndStripStillDrawn_InBannerMode()
    {
        using var poster = Solid(1600, 900, Color.FromArgb(200, 30, 30));
        using var card = Render(banner: true, poster);
        Assert.Equal(Spec.GradeColor.ToArgb(), At(card, 47.5f, 133).ToArgb() | unchecked((int)0xFF000000));   // alt şerit
    }

    [Fact]
    public void Printer_UsesBannerOnlyForPortrait()
    {
        var p = new BadgePrinter(new[] { Spec }, null, "T", "95x135", banner: true, bannerPath: null);
        Assert.Equal(1, p.CardCount);
        Assert.Equal(4, p.PerPage);
    }
}

/// <summary>Afiş görseli de veri klasörüne taşınır.</summary>
[Collection("AppPaths")]
public class BadgeBannerMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"csp-banner-{Guid.NewGuid():N}");

    public BadgeBannerMigrationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "exe", "badges"));
        Environment.SetEnvironmentVariable("CSP_CONFIG_DIR", Path.Combine(_root, "data"));
        AppPaths.Init(Path.Combine(_root, "exe"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CSP_CONFIG_DIR", null);
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void BannerPath_MovesToDataFolder()
    {
        var src = Path.Combine(_root, "exe", "badges", "afis_1.png");
        File.WriteAllText(src, "x");
        var cfg = new AppConfig();
        cfg.Badges.BannerPath = src;
        AppPaths.MigrateAssets(cfg, Path.Combine(_root, "exe"));
        Assert.Equal(Path.Combine(_root, "data", "badges", "afis_1.png"), cfg.Badges.BannerPath);
        Assert.True(File.Exists(cfg.Badges.BannerPath));
    }
}
