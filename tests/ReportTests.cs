using System.IO.Compression;
using System.Text;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>Yönerge/rapor şablon motoru, taslak kurucu ve masa kartı renkleri.</summary>
public class ReportTests
{
    private static byte[] Template()
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "turnuva_yonergesi_sablon.docx"));

    private static string DocumentXml(byte[] docx, string part = "word/document.xml")
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var r = new StreamReader(zip.GetEntry(part)!.Open(), Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static string Text(string xml)
        => string.Concat(System.Text.RegularExpressions.Regex.Matches(xml, "<w:t(?: [^>]*)?>([^<]*)</w:t>").Select(m => m.Groups[1].Value));

    [Fact]
    public void BuiltInTemplate_IsAnalyzed()
    {
        var a = DocxTemplate.Analyze(Template());
        Assert.True(a.HasCategoryTable);
        Assert.True(a.HasProgramTable);
        foreach (var k in new[] { "TURNUVA_ADI", "IL", "TARIH_ARALIGI", "YER", "SISTEM", "DUSUNME_SURESI", "DIREKTOR", "BASHAKEM", "TELEFON" })
            Assert.Contains(k, a.Fields);
    }

    [Fact]
    public void Fill_ReplacesTokens_ExpandsRows_MergesDays()
    {
        var d = new ReportData();
        d.Values["TURNUVA_ADI"] = "ISPARTA EKİM UKD";
        d.Values["IL"] = "Isparta";
        d.Values["TELEFON"] = "+90 555 000 00 00";
        d.Categories.Add(new() { ["adi"] = "7 Yaş Kategorisi", ["kriteri"] = "2020 Doğumlu Sporcular" });
        d.Categories.Add(new() { ["adi"] = "8 Yaş Kategorisi", ["kriteri"] = "2019 Doğumlu Sporcular" });
        d.Program.Add(new() { ["tarih"] = "3 Ekim 2026 Cumartesi", ["saat"] = "10.00", ["etkinlik"] = "1. Tur" });
        d.Program.Add(new() { ["tarih"] = "3 Ekim 2026 Cumartesi", ["saat"] = "12.30", ["etkinlik"] = "2. Tur" });
        d.Program.Add(new() { ["tarih"] = "4 Ekim 2026 Pazar", ["saat"] = "10.00", ["etkinlik"] = "3. Tur" });

        var output = DocxTemplate.Fill(Template(), d);
        var doc = DocumentXml(output);
        var text = Text(doc);
        var header = Text(DocumentXml(output, "word/header1.xml"));

        Assert.DoesNotContain("{{", text);
        Assert.Contains("ISPARTA EKİM UKD YÖNERGESİ", header);
        Assert.Contains("+90 555 000 00 00", text);
        Assert.Contains("8 Yaş Kategorisi", text);
        Assert.Contains("2. Tur", text);
        // Aynı gün tek hücrede: "3 Ekim" bir kez yazılır, ikinci satır birleştirilir.
        Assert.Equal(1, CountOf(text, "3 Ekim 2026 Cumartesi"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(doc, @"<w:vMerge w:val=""restart""\s*/>").Count);
        Assert.Contains("Ödül Töreni", text); // şablonun sabit son satırı korunur
    }

    private static int CountOf(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }

    /// <summary>İşaretsiz (kullanıcının kendi) belge: "İLİ | …" etiketinin yanı doldurulur, mevcut değer okunur.</summary>
    [Fact]
    public void LabelCells_AreDetectedAndFilled()
    {
        var docx = MakeDocx(
            Row("İLİ", "") + Row("YERİ", "Eski Salon") + Row("TARİH", "SAAT") /* başlık satırı: değer değil */);
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("IL", a.Fields);
        Assert.Contains("YER", a.Fields);
        Assert.Equal("Eski Salon", a.Existing["YER"]);
        Assert.False(a.Existing.ContainsKey("IL"));

        var d = new ReportData();
        d.Values["IL"] = "Burdur";
        d.Values["YER"] = "Yeni Salon";
        d.Values["TARIH_ARALIGI"] = "SAAT'İN ÜZERİNE YAZILMAMALI";
        var text = Text(DocumentXml(DocxTemplate.Fill(docx, d)));
        Assert.Contains("Burdur", text);
        Assert.Contains("Yeni Salon", text);
        Assert.DoesNotContain("Eski Salon", text);
        Assert.DoesNotContain("ÜZERİNE", text);
    }

    private static string Row(string a, string b)
        => $"<w:tr><w:tc><w:p><w:r><w:t>{a}</w:t></w:r></w:p></w:tc><w:tc><w:p>{(b.Length > 0 ? $"<w:r><w:t>{b}</w:t></w:r>" : "")}</w:p></w:tc></w:tr>";

    private static byte[] MakeDocx(string rows)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:tbl>"
                    + rows + "</w:tbl></w:body></w:document>");
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData("2026-10-03", "2026-10-04", "03-04.10.2026")]
    [InlineData("2026-10-31", "2026-11-01", "31.10-01.11.2026")]
    [InlineData("2026-12-30", "2027-01-02", "30.12.2026-02.01.2027")]
    [InlineData("2026-10-03", "2026-10-03", "03.10.2026")]
    public void DateRange_IsCompact(string a, string b, string expected)
        => Assert.Equal(expected, ReportBuilder.DateRange(DateTime.Parse(a), DateTime.Parse(b)));

    [Theory]
    [InlineData("7 Yaş", 2027, "7 Yaş Kategorisi", "2020 Doğumlu Sporcular")]
    [InlineData("8 Yaş Kız Kategorisi", 2027, "8 Yaş Kız Kategorisi", "2019 Doğumlu Kız Sporcular")]
    [InlineData("12 Yaş ve Altı", 2027, "12 Yaş ve Altı Kategorisi", "2015 ve Sonrası Doğumlu Sporcular")]
    [InlineData("Açık", 2027, "Açık Kategorisi", "Tüm sporcular")]
    public void CategoryCriteria_FromName(string cat, int seasonEnd, string name, string criteria)
    {
        var r = ReportBuilder.CategoryCriteria(cat, seasonEnd);
        Assert.Equal(name, r.Name);
        Assert.Equal(criteria, r.Criteria);
    }

    [Theory]
    [InlineData("8 YAŞ VE ALTI", "8 YAŞ VE ALTI KATEGORİSİ", "2019 ve Sonrası Doğumlu Sporcular")]
    [InlineData("10 YAŞ VE ALTI KATEGORİSİ [2020-2017 DOĞUMLU KADIN SPORCULAR]", "10 YAŞ VE ALTI KATEGORİSİ", "2020-2017 DOĞUMLU KADIN SPORCULAR")]
    [InlineData("ACIK KATEGORİ (2020 VE ÖNCESİ DOĞAN SPORCULAR", "ACIK KATEGORİ", "2020 VE ÖNCESİ DOĞAN SPORCULAR")]
    [InlineData("GENEL", "GENEL KATEGORİSİ", "")]
    public void CategoryCriteria_UpperCaseAndGivenCriteria(string cat, string name, string criteria)
    {
        var r = ReportBuilder.CategoryCriteria(cat, 2027);
        Assert.Equal(name, r.Name);
        Assert.Equal(criteria, r.Criteria);
    }

    [Theory]
    [InlineData("2026 ELAZIĞ AMATÖR SPOR HAFTASI", null, "Elazığ")]
    [InlineData("Dila Yüksel Anısına", "Merkez/Elazığ", "Elazığ")]
    [InlineData("Merkez Ankara AVM | Gökyay Vakfı", null, "Ankara")]
    [InlineData("Vanilya Kupası", "Ordusu Salonu", null)]   // sözcük içinde geçen il sayılmaz
    [InlineData("Afyon Zafer Turnuvası", null, "Afyonkarahisar")]
    public void DetectProvince_UsesWordBoundaries(string name, string? venue, string? expected)
        => Assert.Equal(expected, ReportBuilder.DetectProvince(name, venue));

    [Fact]
    public void Build_UnderCategories_GiveBirthYearRange_AndDotTimes()
    {
        var info = new Dictionary<string, string> { ["Tarih"] = "2026/10/03", ["Turnuva direktörü"] = "Duygu Nur Sapaz; Rümeysa Kafalı" };
        var schedule = new List<(int, DateTime?, string)> { (1, new DateTime(2026, 10, 3), "11:00") };
        var cats = new List<(CategoryRef, int, TournamentSystem)> { (new CategoryRef(1, "10 Yaş ve Altı", true), 5, TournamentSystem.Swiss) };
        var draft = ReportBuilder.Build(info, schedule, cats, "Elazığ Kupası", null, new Dictionary<string, string>(), DocxTemplate.Analyze(Template()));
        string V(string k) => draft.Fields.Single(f => f.Key == k).Value;
        Assert.Equal("2017-2020", V("DOGUM_YILLARI"));
        Assert.Equal("Elazığ", V("IL"));
        Assert.Equal("Duygu Nur Sapaz, Rümeysa Kafalı", V("DIREKTOR"));
        Assert.Equal("11.00", draft.Program[0].Time);
    }

    [Fact]
    public void Formatting_Helpers()
    {
        Assert.Equal("35 DAKİKA + HAMLE BAŞINA 30 SANİYE EKLEMELİ TEMPO", ReportBuilder.FormatTimeControl("35'+30'' Eklemeli Tempo"));
        Assert.Equal("NA Mehmet KUTDEMİR", ReportBuilder.FormatPerson("NA Kutdemir, Mehmet 34570535"));
        Assert.Equal("2026-2027", ReportBuilder.Season(new DateTime(2026, 10, 3)));
        Assert.Equal("2025-2026", ReportBuilder.Season(new DateTime(2026, 3, 1)));
    }

    [Fact]
    public void Build_FillsKnown_MarksMissing_SuggestsRemembered()
    {
        var info = new Dictionary<string, string>
        {
            ["Tarih"] = "2026/10/03 ile 2026/10/04",
            ["Yer"] = "Gençlik Merkezi",
            ["Zaman kontrolü (Standard)"] = "35'+30''",
            ["Başhakem"] = "NA Kutdemir, Mehmet 34570535",
        };
        var schedule = new List<(int, DateTime?, string)> { (1, new DateTime(2026, 10, 3), "10.00"), (2, new DateTime(2026, 10, 3), "12.30") };
        var cats = new List<(CategoryRef, int, TournamentSystem)>
        {
            (new CategoryRef(1, "7 Yaş", true), 5, TournamentSystem.Swiss),
            (new CategoryRef(2, "8 Yaş", false), 5, TournamentSystem.RoundRobin),
        };
        var analysis = DocxTemplate.Analyze(Template());
        var remembered = new Dictionary<string, string> { ["TELEFON"] = "+90 555" };

        var draft = ReportBuilder.Build(info, schedule, cats, "Isparta Ekim UKD 7 Yaş Kategorisi", "Isparta", remembered, analysis);
        string V(string k) => draft.Fields.Single(f => f.Key == k).Value;
        ReportFieldSource S(string k) => draft.Fields.Single(f => f.Key == k).Source;

        Assert.Equal("03-04.10.2026", V("TARIH_ARALIGI"));
        Assert.Equal(ReportFieldSource.Automatic, S("YER"));
        Assert.Equal("NA Mehmet KUTDEMİR", V("BASHAKEM"));
        Assert.Equal("2019-2020", V("DOGUM_YILLARI"));
        Assert.Contains("BERGER", V("SISTEM"));
        Assert.Equal("+90 555", V("TELEFON"));
        Assert.Equal(ReportFieldSource.Suggested, S("TELEFON"));
        Assert.Equal(ReportFieldSource.Missing, S("SON_BASVURU"));
        Assert.Equal(2, draft.Categories.Count);
        Assert.Equal(2, draft.Program.Count);

        // İsteğe bağlı açılış kalemleri: yalnız saat girilirse programa eklenir.
        draft.Fields.Single(f => f.Key == "PROGRAM_TEKNIK").Value = "09.30";
        var data = draft.ToReportData();
        Assert.Equal(3, data.Program.Count);
        Assert.Equal("Teknik Toplantı", data.Program[0]["etkinlik"]);
    }

    [Fact]
    public void CardColors_AreDistinctAndPersistent()
    {
        var cfg = new CardConfig();
        var tnrs = new[] { 10, 11, 12 };
        var a = cfg.ColorFor(10, tnrs);
        var b = cfg.ColorFor(11, tnrs);
        var c = cfg.ColorFor(12, tnrs);
        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
        Assert.Equal(b, cfg.ColorFor(11, tnrs)); // ikinci kez aynı renk

        cfg.CategoryColors["10"] = "#123456"; // kullanıcı değiştirdi
        Assert.Equal("#123456", cfg.ColorFor(10, tnrs));
    }
}
