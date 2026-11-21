using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Badges;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Büyük dikey yaka kartında vesikalık yerine ortak yatay görsel (afiş / turnuva bilgisi).</summary>
public class BadgeBannerTests
{
    private const float Mm = 72f / 25.4f;
    private static readonly BadgeSpec Spec = new("DENEME HAKEM ÖRNEK", "Başhakem", "Ulusal Hakem", Color.FromArgb(21, 101, 192), null, 1);

    /// <summary>95×135 mm kartı 2 px/pt ile çizer.</summary>
    private static Bitmap Render(bool banner, Image? bannerImage, Image? photo = null, Image? logo = null)
    {
        const float s = 2f;
        var bmp = new Bitmap((int)(95 * Mm * s), (int)(135 * Mm * s));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        float k = s * 72f / bmp.HorizontalResolution;
        g.ScaleTransform(k, k);
        BadgePrinter.DrawCard(g, new RectangleF(0, 0, 95 * Mm, 135 * Mm), Spec, logo, photo, "Isparta Yıldızlar İl Birinciliği", null, banner, bannerImage);
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
    public void Banner_CoversTopEdgeToEdge_WithoutCropping()
    {
        var red = Color.FromArgb(200, 30, 30).ToArgb();
        using var poster = Solid(1600, 900, Color.FromArgb(200, 30, 30));   // 16:9
        using var card = Render(banner: true, poster);
        // En üst kenar ve iki yan kenar dahil kırmızı: kartın üstünü kenardan kenara kaplar
        foreach (var x in new[] { 0.5f, 47.5f, 94.5f })
        {
            Assert.Equal(red, At(card, x, 0.5f).ToArgb());
            Assert.Equal(red, At(card, x, 50f).ToArgb());
        }
        // 16:9 → 95 mm genişlikte ~53 mm yükseklik; altı kırpılmadan biter, sonrası ad için boş
        Assert.NotEqual(red, At(card, 47.5f, 58f).ToArgb());
    }

    [Fact]
    public void BannerMode_HidesLogoAndTitle_BannerIsOnTop()
    {
        using var poster = Solid(1600, 900, Color.FromArgb(200, 30, 30));
        using var logo = Solid(400, 400, Color.FromArgb(20, 40, 220));
        using var card = Render(banner: true, poster, logo: logo);
        Assert.Equal(Color.FromArgb(200, 30, 30).ToArgb(), At(card, 47.5f, 8f).ToArgb());   // logo yerinde afiş
        for (float y = 0.5f; y < 130; y += 1.5f)
            Assert.NotEqual(Color.FromArgb(20, 40, 220).ToArgb(), At(card, 47.5f, y).ToArgb());
    }

    [Fact]
    public void PhotoMode_KeepsLogoOnTop()
    {
        using var logo = Solid(400, 400, Color.FromArgb(20, 40, 220));
        using var card = Render(banner: false, null, logo: logo);
        Assert.Equal(Color.FromArgb(20, 40, 220).ToArgb(), At(card, 47.5f, 15f).ToArgb());
    }

    [Fact]
    public void TallImage_IsLimitedToMaxHeight_AndCentered()
    {
        var green = Color.FromArgb(30, 160, 30).ToArgb();
        using var tall = Solid(400, 900, Color.FromArgb(30, 160, 30));
        using var card = Render(banner: true, tall);
        Assert.Equal(green, At(card, 47.5f, 40).ToArgb());                    // ortada
        Assert.Equal(Color.White.ToArgb(), At(card, 8f, 40).ToArgb());        // yanlarda boşluk (kırpılmaz)
        Assert.Equal(green, At(card, 47.5f, BadgePrinter.BannerMaxMm - 1).ToArgb());
        Assert.NotEqual(green, At(card, 47.5f, BadgePrinter.BannerMaxMm + 2).ToArgb());   // en çok 75 mm
    }

    [Fact]
    public void NameFits_BelowTallestBanner()
    {
        // En yüksek afişten sonra da ad için yer kalır: ad alanı (afiş altı → görev satırı) en az 20 mm
        Assert.True(135 - 14 - 3 - 9 - 1 - (BadgePrinter.BannerMaxMm + 4) >= 20);
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
