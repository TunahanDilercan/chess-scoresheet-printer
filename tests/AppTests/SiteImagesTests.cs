using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Lists;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// İndirme sayfasının (docs/) örnek görselleri: programın kendi çizim koduyla, uydurma adlarla.
/// CSP_SITE_SHOTS klasörü verilince yazılır; aksi hâlde hiçbir şey yapmaz.
/// </summary>
public class SiteImagesTests
{
    private const string Event = "Örnek Satranç Turnuvası 2026";
    private const string Category = "12 Yaş Açık";

    private static Player P(int sno, string name, int? elo, string club) => new(sno, name, null, elo, null, club, club.ToUpper(new System.Globalization.CultureInfo("tr-TR")));

    /// <summary>3. tur: tur başı puanları ve sonuçlarıyla (samples/ornek_turnuva.json ile aynı eşleştirme).</summary>
    internal static Tournament Round3(bool results)
    {
        var rows = new (Player W, Player? B, string Wp, string Bp, string Res)[]
        {
            (P(1, "Elif Yılmaz", 1685, "Isparta"), P(5, "Kerem Aksoy", 1642, "Burdur"), "2", "2", "½-½"),
            (P(9, "Ahmet Tunahan Dilercan", null, "Isparta"), P(2, "Defne Arslan", 1590, "Antalya"), "2", "2", "1-0"),
            (P(3, "Mert Kılıç", 1560, "Afyonkarahisar"), P(11, "Ece Demir", 1544, "Isparta"), "1½", "1½", "1-0"),
            (P(12, "Yusuf Koç", null, "Denizli"), P(4, "Zeynep Aydın", 1498, "Burdur"), "1½", "1½", "0-1"),
            (P(6, "Selin Kara", 1476, "Isparta"), P(13, "Emir Polat", null, "Antalya"), "1", "1", "1-0"),
            (P(14, "Bora Şimşek", null, "Isparta"), P(7, "Duru Erdem", 1452, "Konya"), "1", "1", "½-½"),
            (P(8, "Nehir Çetin", null, "Burdur"), P(15, "Alp Kurt", null, "Isparta"), "½", "½", "0-1"),
            (P(10, "Ada Taş", null, "Antalya"), null, "0", "", "1"),
        };
        var pairings = rows.Select((r, i) => new Pairing(i + 1, r.W, r.B, results ? r.Res : null, Category, 3,
            System: TournamentSystem.Swiss, WhitePoints: r.Wp, BlackPoints: r.B is null ? null : r.Bp)).ToList();
        return new Tournament(Event, 3, pairings, "28.11.2026", TournamentSystem.Swiss);
    }

    [Fact]
    public void Save()
    {
        var dir = Environment.GetEnvironmentVariable("CSP_SITE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var work = Path.Combine(Path.GetTempPath(), "csp-site-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var poster = Path.Combine(work, "afis.png");
            using (var bmp = Poster(1800, 1000)) bmp.Save(poster, ImageFormat.Png);

            AthleteCards(poster, dir);
            TableCard(poster, Path.Combine(dir, "sample-table-card.jpg"));
            Signs(poster, dir);
            Lists(dir);
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    // ---------------- Sporcu yaka kartı ve isimlik (kategori rengiyle)
    private static void AthleteCards(string poster, string dir)
    {
        using var banner = Image.FromFile(poster);
        // isimlik şeridi: programdaki "Afişten kırp" gibi şerit oranında (≈ 5,3:1) ayrı görsel
        var plateCfg0 = new AthleteCardConfig { Kind = AthleteCardConfig.Plate };
        using var strip = Poster(2120, (int)(2120 / AthleteSheetPrinter.PlateStripAspect(plateCfg0)));
        var images = new AthleteImages(banner, strip, null, null);
        var a = new Athlete("Ahmet Tunahan Dilercan", Category, "ISPARTA", null, 1412);
        const string color = "#1565C0";

        var badgeCfg = new AthleteCardConfig { Kind = AthleteCardConfig.Badge };
        using (var p = new AthleteSheetPrinter(new[] { a }, badgeCfg, images, Event))
            Card(p, g => AthleteSheetPrinter.DrawBadge(g, new RectangleF(0, 0, p.CardW, p.CardH), a, badgeCfg, images, color),
                 Path.Combine(dir, "sample-athlete-badge.png"));

        var plateCfg = new AthleteCardConfig { Kind = AthleteCardConfig.Plate };
        using (var p = new AthleteSheetPrinter(new[] { a }, plateCfg, images, Event))
            Card(p, g => AthleteSheetPrinter.DrawPlate(g, new RectangleF(0, 0, p.CardW, p.CardH), a, plateCfg, images, color),
                 Path.Combine(dir, "sample-name-plate.png"));
    }

    private static void Card(AthleteSheetPrinter p, Action<Graphics> draw, string outPath)
    {
        const float dpi = 170;
        using var bmp = new Bitmap((int)(p.CardW / 72 * dpi), (int)(p.CardH / 72 * dpi));
        bmp.SetResolution(dpi, dpi);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Point;
            AthleteSheetPrinter.Prepare(g);
            lock (PagedOutput.Gate) draw(g);
        }
        bmp.Save(outPath, ImageFormat.Png);
    }

    // ---------------- Kategori masa kartı (A4 yatay, afişli)
    private static void TableCard(string poster, string outPath)
    {
        using var p = new CardPrinter(new[] { new CardSpec("12 YAŞ AÇIK", ColorTranslator.FromHtml("#1565C0"), 1) }, poster, Event,
                                      landscape: true, branding: CardBranding.None);
        SavePage(p, 0, 110, outPath, ImageFormat.Jpeg);
    }

    // ---------------- Salon levhası (oklu) ve yasak levhası
    private static void Signs(string poster, string dir)
    {
        using var p = new CardPrinter(new[]
        {
            new CardSpec("KAYIT KONTROL", CardSigns.Ink, 1, CardKind.Sign, CardArrow.Right),
            new CardSpec("YASAK LEVHASI", CardSigns.Ink, 1, CardKind.Ban),
        }, poster, Event, landscape: true, branding: CardBranding.None);
        SavePage(p, 0, 110, Path.Combine(dir, "sample-hall-sign.jpg"), ImageFormat.Jpeg);
        SavePage(p, 1, 110, Path.Combine(dir, "sample-ban-sign.jpg"), ImageFormat.Jpeg);
    }

    // ---------------- Eşleştirme, sonuç ve sıralama listesi; hakem kağıdı ve ceza bildirim formu
    private static void Lists(string dir)
    {
        var info = "28.11.2026 • 14:00";
        var pair = new ListBlock(Event, Category, new[] { ListPrinter.RoundSection(Round3(false), 3, false, info) });
        SavePage(new ListPage(new ListPrinter(new[] { pair }, "eşleştirme")), 0, 110, Path.Combine(dir, "sample-pairings.png"), ImageFormat.Png);

        var standings = new ListTable("3. Turdan Sonra Sıralama",
            new[] { "Sıra", "SNo", "İsim", "Rtg", "Kulüp/Şehir", "Puan", "BH" },
            new IReadOnlyList<string>[]
            {
                new[] { "1", "9", "Ahmet Tunahan Dilercan", "", "Isparta", "3", "5" },
                new[] { "2", "3", "Mert Kılıç", "1560", "Afyonkarahisar", "2½", "5½" },
                new[] { "3", "4", "Zeynep Aydın", "1498", "Burdur", "2½", "5" },
                new[] { "4", "1", "Elif Yılmaz", "1685", "Isparta", "2½", "4½" },
                new[] { "5", "5", "Kerem Aksoy", "1642", "Burdur", "2½", "4½" },
                new[] { "6", "2", "Defne Arslan", "1590", "Antalya", "2", "6" },
                new[] { "7", "6", "Selin Kara", "1476", "Isparta", "2", "4" },
                new[] { "8", "11", "Ece Demir", "1544", "Isparta", "1½", "5" },
                new[] { "9", "12", "Yusuf Koç", "", "Denizli", "1½", "4½" },
                new[] { "10", "7", "Duru Erdem", "1452", "Konya", "1½", "4" },
            });
        var res = new ListBlock(Event, Category, new[]
        {
            ListPrinter.RoundSection(Round3(true), 3, true, null),
            ListPrinter.StandingsSection(standings),
        });
        SavePage(new ListPage(new ListPrinter(new[] { res }, "sonuç")), 0, 110, Path.Combine(dir, "sample-results.png"), ImageFormat.Png);

        var arb = new ArbiterSheetPrinter(new[] { new ArbiterSheet(Category, 3, "28.11.2026 14:00", Round3(false)) }, Event,
                                          SheetLayout.Separate, penalty: PenaltyForm.Below);
        using (var bmp = arb.RenderPage(0, 1240)) bmp.Save(Path.Combine(dir, "sample-arbiter-sheet.png"), ImageFormat.Png);
    }

    private static void SavePage(IPagedOutput o, int page, float dpi, string outPath, ImageFormat fmt)
    {
        var s = o.PageSizePt;
        using var bmp = new Bitmap((int)(s.Width / 72 * dpi), (int)(s.Height / 72 * dpi), PixelFormat.Format24bppRgb);
        bmp.SetResolution(dpi, dpi);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Point;
            PagedOutput.Draw(o, g, page);
        }
        if (fmt.Equals(ImageFormat.Jpeg))
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
            bmp.Save(outPath, codec, ep);
        }
        else bmp.Save(outPath, fmt);
    }

    /// <summary>Liste yazıcısının (A4 dikey) sayfa çizimini görsel üretimi için sarar.</summary>
    private sealed class ListPage(ListPrinter lp) : IPagedOutput
    {
        private static readonly System.Reflection.MethodInfo Draw =
            typeof(ListPrinter).GetMethod("DrawPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        public string DocumentName => "liste";
        public SizeF PageSizePt => new(595.28f, 841.89f);
        public bool Landscape => false;
        public int PageCount => lp.PageCount;
        public object PageKey(int page) => page;
        public void DrawPage(Graphics g, int page) => Draw.Invoke(lp, new object[] { g, page });
    }

    /// <summary>Uydurma turnuva afişi: koyu yeşil zemin, satranç tahtası deseni, turnuva adı.</summary>
    internal static Bitmap Poster(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(28, 52, 30), Color.FromArgb(74, 104, 52), 20f))
            g.FillRectangle(bg, 0, 0, w, h);
        // sağda eğik satranç tahtası deseni
        int sq = h / 8;
        using (var light = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
        {
            var st = g.Save();
            g.TranslateTransform(w * 0.62f, -h * 0.12f);
            g.RotateTransform(12);
            for (int r = 0; r < 10; r++) for (int c = 0; c < 8; c++) if ((r + c) % 2 == 0) g.FillRectangle(light, c * sq, r * sq, sq, sq);
            g.Restore(st);
        }
        using (var knight = new Font("Segoe UI Symbol", h * 0.55f, GraphicsUnit.Pixel))
        using (var kb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            g.DrawString("♞", knight, kb, w * 0.66f, h * 0.12f);
        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(225, 236, 214));
        using var accent = new SolidBrush(Color.FromArgb(232, 190, 80));
        float x = w * 0.07f;
        using (var f = new Font("Segoe UI", h * 0.055f, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString("ISPARTA", f, accent, x, h * 0.2f);
        using (var f = new Font("Segoe UI", h * 0.13f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            g.DrawString("ÖRNEK SATRANÇ", f, white, x - h * 0.008f, h * 0.29f);
            g.DrawString("TURNUVASI 2026", f, white, x - h * 0.008f, h * 0.44f);
        }
        using (var pen = new Pen(Color.FromArgb(232, 190, 80), h * 0.008f)) g.DrawLine(pen, x, h * 0.64f, x + w * 0.16f, h * 0.64f);
        using (var f = new Font("Segoe UI", h * 0.05f, GraphicsUnit.Pixel)) g.DrawString("28 – 30 Kasım 2026", f, soft, x, h * 0.68f);
        return bmp;
    }
}
