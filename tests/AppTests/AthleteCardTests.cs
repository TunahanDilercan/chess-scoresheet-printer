using System.Reflection;
using ClosedXML.Excel;
using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.Parsers;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Sporcu listesi Excel'den: eski yazdırma dosyalarının "Veri_Listesi" sayfası ve başka düzenler.</summary>
public class AthleteImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-ath-" + Guid.NewGuid().ToString("N"));
    public AthleteImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Book(string file, params (string Sheet, string[][] Rows)[] sheets)
    {
        var path = Path.Combine(_dir, file);
        using var wb = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var ws = wb.AddWorksheet(name);
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++) ws.Cell(r + 1, c + 1).Value = rows[r][c];
        }
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void BadgeWorkbookLayout_NameCategorySchool()
    {
        // Yaka kartı dosyasındaki düzen: Yazdirma_Paneli, Sablon, Veri_Listesi (UNVAN AD SOYAD | KATEGORİ | OKUL ADI)
        var path = Book("yaka.xlsx",
            ("Yazdirma_Paneli", new[] { new[] { "", "" }, new[] { "Baskı Başlangıç Sıra No", "1" } }),
            ("Sablon", new[] { new[] { "x" } }),
            ("Veri_Listesi", new[]
            {
                new[] { "UNVAN AD SOYAD", "KATEGORİ", "OKUL ADI" },
                new[] { "Deniz  Örnek ", "GENEL", "AFYON" },
                new[] { "Ali Sınama", "GENEL", "AĞRI" },
                new[] { "", "GENEL", "BOŞ SATIR ATLANIR" },
            }));
        var r = AthleteImport.Read(path);
        Assert.Equal("Veri_Listesi", r.Sheet);
        Assert.Equal(2, r.Athletes.Count);
        Assert.Equal(new Athlete("Deniz Örnek", "GENEL", "AFYON"), r.Athletes[0]);
        Assert.Contains("OKUL ADI", r.Columns);
    }

    [Fact]
    public void PlateWorkbookLayout_WithEloUkd_AndExtraColumn()
    {
        var path = Book("isimlik.xlsm.xlsx",
            ("Veri_Listesi", new[]
            {
                new[] { "Sporcu Adı Soyadı", "Kategori", "Yurdu veya İli", "ELO", "UKD", "" },
                new[] { "IM ÖRNEK KAMER", "KULÜPLER 2026", "ÖRNEK SATRANÇ SPOR KULÜBÜ", "2319", "2259", "2319" },
                new[] { "DENEME ALİ", "KULÜPLER 2026", "ISPARTA", "0", "", "0" },
            }),
            ("Sheet1", new[] { new[] { "Okul", "Kategori", "İl", "rtg" }, new[] { "BAŞKA", "X", "Y", "1" } }));
        var r = AthleteImport.Read(path);
        Assert.Equal(2, r.Athletes.Count);
        Assert.Equal(new Athlete("IM ÖRNEK KAMER", "KULÜPLER 2026", "ÖRNEK SATRANÇ SPOR KULÜBÜ", 2319, 2259), r.Athletes[0]);
        Assert.Null(r.Athletes[1].Elo);   // 0 → puan yok (kartta "0" basılmaz)
        Assert.Null(r.Athletes[1].Ukd);
    }

    [Fact]
    public void UnknownHeaders_InDataSheet_ReadByMacroOrder()
    {
        var path = Book("sira.xlsx",
            ("Panel", new[] { new[] { "a" } }),
            ("Veri_Listesi", new[] { new[] { "x1", "x2", "x3", "x4", "x5" }, new[] { "Elif Sınama", "12 YAŞ", "BURDUR", "1500", "1400" } }));
        var r = AthleteImport.Read(path);
        // başlık tanınmadı: ilk satır da veri sayılır (A ad, B kategori, C il, D ELO, E UKD)
        Assert.Equal(2, r.Athletes.Count);
        Assert.Equal(new Athlete("Elif Sınama", "12 YAŞ", "BURDUR", 1500, 1400), r.Athletes[1]);
        Assert.Contains("sıra ile", r.Columns);
    }

    [Fact]
    public void FirstSheetWithNameColumn_IsUsed_WhenNoDataSheet()
    {
        var path = Book("liste.xlsx",
            ("Kapak", new[] { new[] { "Turnuva" } }),
            ("Sporcular", new[] { new[] { "Ad Soyad", "Kulüp", "İl" }, new[] { "Can Deneme", "ÖRNEK SK", "ISPARTA" } }));
        var r = AthleteImport.Read(path);
        Assert.Equal("Sporcular", r.Sheet);
        Assert.Equal("ÖRNEK SK", r.Athletes[0].Line3);   // kulüp, ilden önce gelir
    }

    [Fact]
    public void NoAthletes_Throws()
    {
        var path = Book("bos.xlsx", ("A", new[] { new[] { "Başlık" } }), ("B", new[] { new[] { "x" } }));
        Assert.Throws<ParseException>(() => AthleteImport.Read(path));
    }

    [Theory]
    [InlineData("UNVAN AD SOYAD", "unvanadsoyad")]
    [InlineData("Yurdu veya İli", "yurduveyaili")]
    [InlineData("KATEGORİ", "kategori")]
    [InlineData("Sporcu Adı Soyadı", "sporcuadisoyadi")]
    public void HeaderNormalization(string header, string expected) => Assert.Equal(expected, AthleteImport.Norm(header));
}

/// <summary>chess-results başlangıç listesi + TSF kayıt listesi → kart listesi.</summary>
public class AthleteListsTests
{
    private static readonly List<PlayerRegistration> Regs = new()
    {
        new() { Given = "DENİZ", Surname = "ÖRNEK", Province = "ISPARTA", Elo = 1650, Ukd = 1580, Category = "12 Yaş" },
        new() { Given = "ALİ", Surname = "SINAMA", Province = "BURDUR", Elo = null, Ukd = 1200, Category = "12 Yaş" },
    };

    private static List<(string, IReadOnlyList<Player>)> Lists() => new()
    {
        ("12 Yaş", new List<Player>
        {
            new(1, "Örnek, Deniz", Title: "CM", Rating: 1650, Club: "Örnek Satranç SK"),
            new(2, "Sınama, Ali", Rating: 0),
            new(3, "Kayıtsız, Can", Rating: 1300, Province: "ANTALYA"),
        }),
    };

    [Fact]
    public void FromStartingLists_TakesEloUkdFromRegistration_ClubAsLine3()
    {
        var a = AthleteLists.FromStartingLists(Lists(), Regs, null, titleInName: true, line3Province: false);
        Assert.Equal(3, a.Count);
        Assert.Equal("CM Deniz Örnek", a[0].Name);   // "Soyad, Ad" → "Ad Soyad"
        Assert.Equal("12 Yaş", a[0].Category);
        Assert.Equal("Örnek Satranç SK", a[0].Line3);
        Assert.Equal((1650, 1580), (a[0].Elo, a[0].Ukd));
        Assert.Equal("BURDUR", a[1].Line3);               // kulüp yok: il
        Assert.Equal((null, 1200), (a[1].Elo, a[1].Ukd));   // 0 rating basılmaz
        Assert.Equal(("ANTALYA", 1300, (int?)null), (a[2].Line3, a[2].Elo, a[2].Ukd));
    }

    [Fact]
    public void FromStartingLists_ProvinceLine_NoTitle()
    {
        var a = AthleteLists.FromStartingLists(Lists(), Regs, n => n.StartsWith("Örnek") ? "ISPARTA (düzeltilmiş)" : null, titleInName: false, line3Province: true);
        Assert.Equal("Deniz Örnek", a[0].Name);
        Assert.Equal("ISPARTA (düzeltilmiş)", a[0].Line3);    // elle düzeltilen il önce gelir
        Assert.Equal("BURDUR", a[1].Line3);
    }

    [Theory]
    [InlineData("GÖKDEMİR, DEMİR", "DEMİR GÖKDEMİR")]
    [InlineData("Örnek, Deniz Can", "Deniz Can Örnek")]
    [InlineData("Deniz Örnek", "Deniz Örnek")]
    [InlineData("Örnek,", "Örnek,")]
    [InlineData("  Sınama ,  Ali ", "Ali Sınama")]
    public void DisplayName_SurnameCommaGiven(string raw, string expected) => Assert.Equal(expected, AthleteLists.DisplayName(raw));

    [Fact]
    public void FromRegistrations_UsesProvinceAndRatings()
    {
        var a = AthleteLists.FromRegistrations(Regs);
        Assert.Equal(new Athlete("DENİZ ÖRNEK", "12 Yaş", "ISPARTA", 1650, 1580), a[0]);
        Assert.Null(a[1].Elo);
    }
}

/// <summary>Sporcu yaka kartı ve isimlik çizimi: sayfa düzeni, afiş, şerit, puan kutuları.</summary>
public class AthleteSheetPrinterTests
{
    private const float Mm = 72f / 25.4f;

    private static Bitmap Solid(int w, int h, Color c) { var b = new Bitmap(w, h); using var g = Graphics.FromImage(b); g.Clear(c); return b; }

    /// <summary>Üstü kırmızı, alt %12'si yeşil afiş (alt bant).</summary>
    private static Bitmap Poster()
    {
        var b = new Bitmap(1600, 900);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.FromArgb(200, 30, 30));
        using var green = new SolidBrush(Color.FromArgb(30, 160, 30));
        g.FillRectangle(green, 0, 900 - 108, 1600, 108);
        return b;
    }

    private static Bitmap Card(bool plate, Athlete a, AthleteCardConfig cfg, AthleteImages img)
    {
        float w = plate ? 190 : 95, h = plate ? (cfg.PlatesPerPage >= 4 ? 68 : 64) : 135;
        var bmp = new Bitmap((int)(w * Mm * 2), (int)(h * Mm * 2));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        float k = 2f * 72f / bmp.HorizontalResolution;
        g.ScaleTransform(k, k);
        var r = new RectangleF(0, 0, w * Mm, h * Mm);
        if (plate) AthleteSheetPrinter.DrawPlate(g, r, a, cfg, img);
        else AthleteSheetPrinter.DrawBadge(g, r, a, cfg, img);
        return bmp;
    }

    private static Color At(Bitmap b, float xMm, float yMm) => b.GetPixel((int)(xMm * Mm * 2), (int)(yMm * Mm * 2));
    private static bool IsRed(Color c) => c.R > 150 && c.G < 90 && c.B < 90;
    private static bool IsGreen(Color c) => c.G > 120 && c.R < 90 && c.B < 90;

    private static readonly Athlete Sample = new("Deniz Örnek", "GENEL", "ISPARTA", 1850, 1720);

    [Theory]
    [InlineData("Badge", 3, 9, 4, 3)]
    [InlineData("Plate", 3, 7, 3, 3)]
    [InlineData("Plate", 4, 7, 4, 2)]
    public void PageLayout(string kind, int perPage, int count, int expectedPer, int expectedPages)
    {
        var cfg = new AthleteCardConfig { Kind = kind, PlatesPerPage = perPage };
        var p = new AthleteSheetPrinter(Enumerable.Repeat(Sample, count), cfg, new AthleteImages(null, null, null, null), "T");
        Assert.Equal(expectedPer, p.PerPage);
        Assert.Equal(expectedPages, p.PageCount);
        Assert.True(p.Cols * p.CardW <= 595.28f && p.Rows * p.CardH <= 841.89f - 20 * Mm);   // A4'e kesim payıyla sığar
    }

    [Fact]
    public void Badge_PosterAtTop_EdgeToEdge()
    {
        using var poster = Poster();
        using var card = Card(false, Sample, new AthleteCardConfig(), new AthleteImages(poster, null, null, null));
        Assert.True(IsRed(At(card, 2, 3)) && IsRed(At(card, 47, 3)) && IsRed(At(card, 93, 3)));   // üst kenar boyunca afiş
        Assert.True(IsGreen(At(card, 47, 48)));   // kırpılmadı: alt bant da görünür (95 mm → ~53 mm yükseklik)
        Assert.False(IsRed(At(card, 47, 80)));     // afiş yazı alanına taşmaz
    }

    [Fact]
    public void Badge_CropBottom_And_FooterBand()
    {
        using var poster = Poster();
        var cfg = new AthleteCardConfig { CropBottomPercent = 20, FooterBand = true };
        using var card = Card(false, Sample, cfg, new AthleteImages(poster, null, null, null));
        Assert.False(IsGreen(At(card, 47, 30)));     // alt bant kırpıldı: üstte yeşil yok
        Assert.True(IsGreen(At(card, 47, 131)));      // ...ve kartın altına şerit olarak geldi
    }

    [Fact]
    public void Badge_RoundLogos_AtBottom()
    {
        using var logo = Solid(300, 300, Color.FromArgb(20, 40, 220));
        using var card = Card(false, Sample, new AthleteCardConfig(), new AthleteImages(null, null, logo, logo));
        Assert.Equal(Color.FromArgb(20, 40, 220).ToArgb(), At(card, 95 * 0.244f, 135 - 12.5f).ToArgb());
        Assert.Equal(Color.FromArgb(20, 40, 220).ToArgb(), At(card, 95 * 0.765f, 135 - 12.5f).ToArgb());
        Assert.NotEqual(Color.FromArgb(20, 40, 220).ToArgb(), At(card, 47.5f, 135 - 12.5f).ToArgb());
        using var corner = Card(false, Sample, new AthleteCardConfig(), new AthleteImages(null, null, logo, null));
        Assert.Equal(Color.FromArgb(20, 40, 220).ToArgb(), At(corner, 47.5f, 135 - 12.5f).ToArgb());   // tek logo ortada
        Assert.Equal(Color.White.ToArgb(), At(corner, 47.5f - 9.6f, 135 - 12.5f - 9.6f).ToArgb());   // daire dışı kırpıldı
    }

    [Fact]
    public void Plate_StripOnTop_ScoreBoxesOnlyWithRatings()
    {
        using var strip = Solid(1800, 360, Color.FromArgb(200, 30, 30));
        var cfg = new AthleteCardConfig { Kind = AthleteCardConfig.Plate };
        using var full = Card(true, Sample, cfg, new AthleteImages(null, strip, null, null));
        Assert.True(IsRed(At(full, 3, 3)) && IsRed(At(full, 187, 20)));     // şerit kenardan kenara
        Assert.False(IsRed(At(full, 95, 40)));
        // ELO kutusunun kenarı (sağda): puan varken çizgi var, yokken bembeyaz
        int Dark(Bitmap b) { int n = 0; for (float x = 168; x < 188; x += 0.25f) for (float y = 54; y < 63; y += 0.5f) if (At(b, x, y).GetBrightness() < 0.6f) n++; return n; }
        using var none = Card(true, Sample with { Elo = null, Ukd = null }, cfg, new AthleteImages(null, strip, null, null));
        Assert.True(Dark(full) > 20);
        Assert.Equal(0, Dark(none));
        var noBoxes = new AthleteCardConfig { Kind = AthleteCardConfig.Plate, ShowElo = false, ShowUkd = false };
        using var hidden = Card(true, Sample, noBoxes, new AthleteImages(null, strip, null, null));
        Assert.Equal(0, Dark(hidden));
    }

    [Fact]
    public void Plate_WithoutPlateBanner_UsesPosterContained()
    {
        using var poster = Poster();
        var cfg = new AthleteCardConfig { Kind = AthleteCardConfig.Plate };
        using var card = Card(true, Sample, cfg, new AthleteImages(poster, null, null, null));
        Assert.True(IsRed(At(card, 95, 15)));            // ortada afiş
        Assert.Equal(Color.White.ToArgb(), At(card, 10, 15).ToArgb());   // 16:9 afiş şeride sığdırıldı (yanlar boş)
    }

    [Fact]
    public void LongTexts_FitInsideBoxes()
    {
        var box = new RectangleF(0, 0, 85 * Mm, 20 * Mm);
        var (lines, pt) = AthleteText.Fit("Muhammed Mustafa Abdurrahman Kemalettin Uzunsoyadlıoğulları", box, AthleteText.Black, 20, 9, 2);
        Assert.InRange(lines.Count, 1, 2);
        Assert.True(pt <= 20 && pt >= 4);
        using var card = Card(false, new Athlete("Muhammed Mustafa Abdurrahman Kemalettin Uzunsoyadlıoğulları", "ÇOK UZUN BİR KATEGORİ ADI GENÇLER", "ALMAN LİSESİ KÜLTÜR VE EĞİTİM VAKFI ÖZEL FEN LİSESİ"),
                              new AthleteCardConfig(), new AthleteImages(null, null, null, null));
        Assert.NotNull(card);
    }

    [Fact]
    public void Pdf_IsWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"csp-ath-{Guid.NewGuid():N}.pdf");
        try
        {
            new AthleteSheetPrinter(Enumerable.Repeat(Sample, 5), new AthleteCardConfig(), new AthleteImages(null, null, null, null), "T").SavePdf(path);
            var head = File.ReadAllBytes(path)[..5];
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
        }
        finally { File.Delete(path); }
    }
}

/// <summary>Sporcu kartları penceresi: açılış, liste doldurma, süzgeç, kayıt ve geri yükleme.</summary>
[Collection("AppPaths")]
public class AthletesFormTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-athform-" + Guid.NewGuid().ToString("N"));
    public AthletesFormTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private AthletesForm NewForm(AppConfig cfg)
        => new(new AthletesContext("Deneme Turnuvası", null, Array.Empty<CategoryRef>(), null, cfg, () => { }, _dir, _dir));

    private static T Call<T>(object o, string name, params object?[] args)
        => (T)o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args)!;
    private static void Call(object o, string name, params object?[] args)
        => o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);
    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    [Fact]
    public void FillFilterCountAndRestore()
    {
        var cfg = new AppConfig();
        var athletes = new[]
        {
            new Athlete("Deniz Örnek", "12 Yaş", "ISPARTA", 1600, null),
            new Athlete("Ali Sınama", "12 Yaş", "BURDUR"),
            new Athlete("Elif Deneme", "14 Yaş", "ANTALYA"),
        };
        using (var f = NewForm(cfg))
        {
            Call(f, "Fill", athletes, null);
            Assert.Equal(3, Call<List<Athlete>>(f, "Athletes", false).Count);
            var cat = Field<ComboBox>(f, "_cboCat");
            Assert.Equal(new[] { "Tüm kategoriler", "12 Yaş", "14 Yaş" }, cat.Items.Cast<object>().Select(o => o.ToString()));
            cat.SelectedItem = "14 Yaş";
            var only = Call<List<Athlete>>(f, "Athletes", false);
            Assert.Equal("Elif Deneme", Assert.Single(only).Name);   // süzgeçteki kategori basılır
            cat.SelectedItem = "Tüm kategoriler";
            var grid = Field<DataGridView>(f, "_grid");
            grid.Rows[1].Cells["Bas"].Value = false;
            grid.Rows[0].Cells["Adet"].Value = "2";
            Assert.Equal(new[] { "Deniz Örnek", "Deniz Örnek", "Elif Deneme" }, Call<List<Athlete>>(f, "Athletes", false).Select(a => a.Name));
            Call(f, "SaveList");
        }
        using (var f2 = NewForm(cfg))
        {
            // kaydedilen liste, işaret ve adetleriyle geri gelir
            Assert.Equal(new[] { "Deniz Örnek", "Deniz Örnek", "Elif Deneme" }, Call<List<Athlete>>(f2, "Athletes", false).Select(a => a.Name));
            Assert.Equal(3, Call<List<Athlete>>(f2, "Athletes", true).Count);
        }
    }

    [Fact]
    public void KindSwitch_SavesConfig_AndPreviewRenders()
    {
        var cfg = new AppConfig();
        using var f = NewForm(cfg);
        Field<RadioButton>(f, "_rbPlate").Checked = true;
        Assert.Equal(AthleteCardConfig.Plate, cfg.Athletes.Kind);
        var pic = Field<PictureBox>(f, "_picPreview");
        Assert.NotNull(pic.Image);
        Assert.True(pic.Image!.Width > pic.Image.Height * 2);   // isimlik önizlemesi yatay
        Field<RadioButton>(f, "_rbBadge").Checked = true;
        Assert.Equal(AthleteCardConfig.Badge, cfg.Athletes.Kind);
        Assert.True(pic.Image!.Height > pic.Image.Width);       // yaka kartı dikey
        Assert.False(Field<Button>(f, "_btnFromCr").Enabled);  // turnuva seçilmedi: chess-results düğmesi kapalı
    }
}

/// <summary>Büyük görseller yüklenirken baskıya yetecek boyuta küçültülür (önizleme ve baskı hızı).</summary>
public class ImageFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-img-" + Guid.NewGuid().ToString("N"));
    public ImageFilesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void BigImage_IsBounded_KeepingAspect_AndFileNotLocked()
    {
        var path = Path.Combine(_dir, "afis.png");
        using (var big = new Bitmap(6000, 3375)) { using (var g = Graphics.FromImage(big)) g.Clear(Color.SteelBlue); big.Save(path); }
        using (var img = ImageFiles.Load(path)!)
        {
            Assert.Equal(2400, img.Width);
            Assert.Equal(1350, img.Height);
            Assert.Equal(Color.SteelBlue.ToArgb(), img.GetPixel(1200, 675).ToArgb());
            Assert.Equal(Color.SteelBlue.ToArgb(), img.GetPixel(0, 0).ToArgb());   // kenarda koyu çizgi yok
        }
        File.Delete(path);   // dosya kilitli kalmadı
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SmallImage_Unchanged_MissingFile_Null()
    {
        var path = Path.Combine(_dir, "logo.png");
        using (var b = new Bitmap(300, 200)) b.Save(path);
        using (var img = ImageFiles.Load(path)!) Assert.Equal((300, 200), (img.Width, img.Height));
        Assert.Null(ImageFiles.Load(Path.Combine(_dir, "yok.png")));
        Assert.Null(ImageFiles.Load(null));
        File.WriteAllText(Path.Combine(_dir, "bozuk.png"), "resim değil");
        Assert.Null(ImageFiles.Load(Path.Combine(_dir, "bozuk.png")));
    }
}
