using System.Drawing.Imaging;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Overlay;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Salon levhaları (yönlendirme + ok, yasak simgeleri) ve boş kart.</summary>
public class CardSignTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-signs-" + Guid.NewGuid().ToString("N"));
    private readonly string _poster;

    public CardSignTests()
    {
        Directory.CreateDirectory(_dir);
        _poster = Path.Combine(_dir, "afis.png");
        using var bmp = SiteImagesTests.Poster(1800, 1000);
        bmp.Save(_poster, ImageFormat.Png);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Bitmap Render(CardPrinter p, int page, float dpi = 60)
    {
        var s = p.PageSizePt;
        var bmp = new Bitmap((int)(s.Width / 72 * dpi), (int)(s.Height / 72 * dpi));
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        PagedOutput.Draw(p, g, page);
        return bmp;
    }

    private static int Count(Bitmap b, Rectangle r, Func<Color, bool> f)
    {
        int n = 0;
        for (int y = r.Top; y < r.Bottom; y += 2) for (int x = r.Left; x < r.Right; x += 2) if (f(b.GetPixel(x, y))) n++;
        return n;
    }

    private static bool Dark(Color c) => c.R < 80 && c.G < 80 && c.B < 80;
    private static bool Colored(Color c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) > 40;
    private static bool Red(Color c) => c.R > 160 && c.G < 90 && c.B < 90;

    private CardPrinter Printer(params CardSpec[] cards) =>
        new(cards, _poster, "Örnek Turnuva", landscape: true, filled: true, branding: CardBranding.None);

    /// <summary>Kartın alt bandı (afişin altı) piksel olarak.</summary>
    private static Rectangle Band(Bitmap b) => new(0, b.Height * 58 / 100, b.Width, b.Height * 42 / 100 - 2);

    [Fact]
    public void Sign_IsBlackOnWhite_EvenInFilledStyle()
    {
        using var p = Printer(new CardSpec("WC", Color.Red, 1, CardKind.Sign, CardArrow.Right));
        using var bmp = Render(p, 0);
        var band = Band(bmp);
        Assert.True(Count(bmp, band, Dark) > 100, "levha yazısı siyah olmalı");
        Assert.Equal(0, Count(bmp, band, Colored));                       // renkli zemin ya da yazı yok
    }

    [Theory]
    [InlineData(CardArrow.Left)]
    [InlineData(CardArrow.Right)]
    [InlineData(CardArrow.Up)]
    [InlineData(CardArrow.Down)]
    public void Arrow_IsDrawnBelowText(CardArrow dir)
    {
        using var with = Printer(new CardSpec("KAYIT KONTROL", Color.Black, 1, CardKind.Sign, dir));
        using var without = Printer(new CardSpec("KAYIT KONTROL", Color.Black, 1, CardKind.Sign, CardArrow.None));
        using var a = Render(with, 0);
        using var b = Render(without, 0);
        // bandın alt kısmı (ok alanı): okta dolu, oksuz levhada (yazı ortada) boş
        var low = new Rectangle(0, a.Height * 87 / 100, a.Width, a.Height * 9 / 100);
        Assert.True(Count(a, low, Dark) > 40, $"{dir} oku çizilmedi");
        Assert.True(Count(a, low, Dark) > 4 * Count(b, low, Dark));
    }

    [Fact]
    public void Arrow_PointsTheRightWay()
    {
        using var right = Printer(new CardSpec("WC", Color.Black, 1, CardKind.Sign, CardArrow.Right));
        using var left = Printer(new CardSpec("WC", Color.Black, 1, CardKind.Sign, CardArrow.Left));
        using var r = Render(right, 0);
        using var l = Render(left, 0);
        var low = new Rectangle(0, r.Height * 87 / 100, r.Width, r.Height * 9 / 100);
        // ok başı (geniş kısım) sağdaysa sağ yarıda daha çok mürekkep olur
        int rr = Count(r, low with { X = r.Width / 2, Width = r.Width / 2 }, Dark), rl = Count(r, low with { Width = r.Width / 2 }, Dark);
        int lr = Count(l, low with { X = l.Width / 2, Width = l.Width / 2 }, Dark), ll = Count(l, low with { Width = l.Width / 2 }, Dark);
        Assert.True(rr > rl, "sağ ok sağa bakmalı");
        Assert.True(ll > lr, "sol ok sola bakmalı");
    }

    [Fact]
    public void Ban_DrawsFourRedSigns()
    {
        using var p = Printer(new CardSpec("YASAK", Color.Black, 1, CardKind.Ban));
        using var bmp = Render(p, 0);
        var band = Band(bmp);
        Assert.True(Count(bmp, band, Red) > 200, "kırmızı yasak işaretleri çizilmedi");
        // dört simge: bandı dörde bölünce her parçada kırmızı var
        for (int i = 0; i < 4; i++)
        {
            var part = new Rectangle(band.X + i * band.Width / 4, band.Y, band.Width / 4, band.Height);
            Assert.True(Count(bmp, part, Red) > 30, $"{i + 1}. simge eksik");
        }
    }

    [Fact]
    public void CategoryCards_KeepTheirSize_WhenSignsArePrintedTogether()
    {
        // uzun levha yazısı kategori adının puntosunu küçültmemeli
        using var alone = Printer(new CardSpec("12 YAŞ", Color.Blue, 1));
        using var mixed = Printer(new CardSpec("12 YAŞ", Color.Blue, 1), new CardSpec("İNCELEME MASASI", Color.Black, 1, CardKind.Sign, CardArrow.Right));
        using var a = Render(alone, 0);
        using var b = Render(mixed, 0);
        var band = Band(a);
        Assert.Equal(Count(a, band, Colored), Count(b, band, Colored));
    }

    [Fact]
    public void SaveSamples()
    {
        var dir = Environment.GetEnvironmentVariable("CSP_FORM_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        using var p = Printer(
            new CardSpec("12 YAŞ AÇIK", Color.FromArgb(21, 101, 192), 1),
            new CardSpec("İNCELEME MASASI", Color.Black, 1, CardKind.Sign, CardArrow.Right),
            new CardSpec("WC", Color.Black, 1, CardKind.Sign, CardArrow.Left),
            new CardSpec("SPORCU GİRİŞİ", Color.Black, 1, CardKind.Sign, CardArrow.Down),
            new CardSpec("YASAK", Color.Black, 1, CardKind.Ban),
            new CardSpec("HAKEM ODASI", Color.Black, 1, CardKind.Sign, CardArrow.None));
        for (int i = 0; i < p.PageCount; i++)
            using (var bmp = Render(p, i, 100)) bmp.Save(Path.Combine(dir, $"levha_{i + 1}.png"), ImageFormat.Png);
    }
}
