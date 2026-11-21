using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using NotasyonOtomasyonu.App.Lists;
using NotasyonOtomasyonu.App.Reports;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Testlerde kullanılan küçük Word belgeleri.</summary>
internal static class TestDocx
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static byte[] Make(string body, string? header = null)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "</w:body></w:document>");
            if (header is not null)
                Write(zip, "word/header1.xml", "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" + header + "</w:hdr>");
        }
        return ms.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string xml)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + xml);
    }

    public static XDocument Xml(byte[] docx, string part = "word/document.xml")
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var s = zip.GetEntry(part)!.Open();
        return XDocument.Load(s);
    }

    /// <summary>Belgenin düz metni (paragraflar satır satır).</summary>
    public static string Text(byte[] docx, string part = "word/document.xml")
        => string.Join("\n", Xml(docx, part).Descendants(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))));

    public static string P(params string[] runs) => "<w:p>" + string.Concat(runs) + "</w:p>";
    public static string R(string text, bool bold = false) => $"<w:r>{(bold ? "<w:rPr><w:b/></w:rPr>" : "")}<w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(text)}</w:t></w:r>";
    public static string Cell(string text) => $"<w:tc><w:p>{(text.Length > 0 ? R(text) : "")}</w:p></w:tc>";
    public static string Row(params string[] cells) => "<w:tr>" + string.Concat(cells.Select(Cell)) + "</w:tr>";
    public static string Table(params string[] rows) => "<w:tbl>" + string.Concat(rows) + "</w:tbl>";

    public static ReportData Data(params (string Key, string Value)[] values)
    {
        var d = new ReportData();
        foreach (var (k, v) in values) d.Values[k] = v;
        return d;
    }
}

/// <summary>
/// Şablondaki işaretsiz yerler: "Etiket : ………" noktalı satırlar ve "Etiket | boş hücre" tablo satırları
/// alan olarak tanınır, sorulur ve doldurulur; imza yerleri ve tablo başlıkları dokunulmadan kalır.
/// </summary>
public class CustomSpotTests
{
    private static readonly string Dots = new('.', 30);

    [Fact]
    public void DottedLine_UnknownLabel_BecomesCustomField_AndIsFilled()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("İtiraz Eden : ", bold: true), TestDocx.R(Dots)));
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("ALAN_ITIRAZ_EDEN", a.Fields);
        Assert.Equal("İtiraz Eden", a.FieldLabels["ALAN_ITIRAZ_EDEN"]);

        var filled = DocxTemplate.Fill(docx, TestDocx.Data(("ALAN_ITIRAZ_EDEN", "Ali VELİ")));
        Assert.Equal("İtiraz Eden : Ali VELİ", TestDocx.Text(filled));
        // Etiketin kalın yazısı korunur; değer noktaların koşusuna yazılır
        var runs = TestDocx.Xml(filled).Descendants(TestDocx.W + "r").ToList();
        Assert.NotNull(runs[0].Element(TestDocx.W + "rPr")?.Element(TestDocx.W + "b"));
        Assert.Equal("Ali VELİ", runs[1].Value);
    }

    [Fact]
    public void DottedLine_KnownLabel_UsesExistingField()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Turnuva Adı : " + Dots)) + TestDocx.P(TestDocx.R("Başhakem: " + Dots)));
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("TURNUVA_ADI", a.Fields);
        Assert.Contains("BASHAKEM", a.Fields);
        Assert.DoesNotContain(a.Fields, f => f.StartsWith("ALAN_"));
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("TURNUVA_ADI", "ISPARTA İL BİRİNCİLİĞİ"), ("BASHAKEM", "Mehmet KUTDEMİR"))));
        Assert.Contains("Turnuva Adı : ISPARTA İL BİRİNCİLİĞİ", text);
        Assert.Contains("Başhakem: Mehmet KUTDEMİR", text);
    }

    [Fact]
    public void EmptyValue_KeepsDotsForHandwriting()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Karar : " + Dots)));
        Assert.Equal("Karar : " + Dots, TestDocx.Text(DocxTemplate.Fill(docx, new ReportData())));
    }

    [Fact]
    public void TwoSpotsInOneParagraph_BothFilled()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Sporcu : ……………   Masa No : ________")));
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("ALAN_SPORCU", a.Fields);
        Assert.Contains("ALAN_MASA_NO", a.Fields);
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("ALAN_SPORCU", "DENİZ ÖRNEK"), ("ALAN_MASA_NO", "3"))));
        Assert.Equal("Sporcu : DENİZ ÖRNEK   Masa No : 3", text);
    }

    [Fact]
    public void RepeatedLabel_GetsNumberedKeys()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Adı Soyadı : " + Dots)) + TestDocx.P(TestDocx.R("Adı Soyadı : " + Dots)));
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("ALAN_ADI_SOYADI", a.Fields);
        Assert.Contains("ALAN_ADI_SOYADI_2", a.Fields);
        Assert.Equal("Adı Soyadı (2)", a.FieldLabels["ALAN_ADI_SOYADI_2"]);
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("ALAN_ADI_SOYADI", "BİRİNCİ"), ("ALAN_ADI_SOYADI_2", "İKİNCİ"))));
        Assert.Equal("Adı Soyadı : BİRİNCİ\nAdı Soyadı : İKİNCİ", text);
    }

    [Theory]
    [InlineData("İmza : ")]
    [InlineData("Hakem İmzası : ")]
    [InlineData("Paraf : ")]
    [InlineData("Mühür / Kaşe : ")]
    public void SignaturePlaces_AreNotFields(string label)
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R(label + Dots)));
        Assert.Empty(DocxTemplate.Analyze(docx).Fields);
        Assert.Equal(label + Dots, TestDocx.Text(DocxTemplate.Fill(docx, new ReportData())));
    }

    [Fact]
    public void DotsSplitAcrossRuns_AreFilledOnce()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Hamle No", bold: true), TestDocx.R(" : ...."), TestDocx.R("........"), TestDocx.R(" (rakip)")));
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("ALAN_HAMLE_NO", a.Fields);
        Assert.Equal("Hamle No : 34 (rakip)", TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("ALAN_HAMLE_NO", "34")))));
    }

    [Fact]
    public void PlainSentenceWithColon_IsNotAField()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Not: Bu tutanak iki nüsha olarak düzenlenmiştir.")) + TestDocx.P(TestDocx.R(new string('.', 80))));
        Assert.Empty(DocxTemplate.Analyze(docx).Fields);
    }

    [Fact]
    public void LongSentenceBeforeDots_IsNotALabel()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Turnuvanın üçüncü turunda yaşanan olay ile ilgili olarak hakemlerin görüşü : " + Dots)));
        Assert.Empty(DocxTemplate.Analyze(docx).Fields);
    }

    [Fact]
    public void TableLabelWithEmptyCell_IsFilled()
    {
        var docx = TestDocx.Make(TestDocx.Table(
            TestDocx.Row("İtirazın Konusu", ""),
            TestDocx.Row("Karar", "........"),
            TestDocx.Row("Yeri", "")));                        // bilinen etiket: YER alanı
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("ALAN_ITIRAZIN_KONUSU", a.Fields);
        Assert.Contains("ALAN_KARAR", a.Fields);
        Assert.Contains("YER", a.Fields);
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("ALAN_ITIRAZIN_KONUSU", "Eşleştirme"), ("ALAN_KARAR", "Reddedildi"), ("YER", "Spor Salonu"))));
        Assert.Contains("Eşleştirme", text);
        Assert.Contains("Reddedildi", text);
        Assert.Contains("Spor Salonu", text);
        Assert.DoesNotContain("....", text);
    }

    [Fact]
    public void TableHeaders_AndListHeaders_AreNotFields()
    {
        var docx = TestDocx.Make(
            TestDocx.Table(TestDocx.Row("Ad Soyad", "Kulüp", "İmza"), TestDocx.Row("", "", ""), TestDocx.Row("1", "", "")) +
            TestDocx.Table(TestDocx.Row("Sporcu", "Kategori"), TestDocx.Row("{{sporcu.adi}}", "{{sporcu.kategori}}")));
        var a = DocxTemplate.Analyze(docx);
        Assert.DoesNotContain(a.Fields, f => f.StartsWith("ALAN_"));
        Assert.Contains("sporcu", a.Lists);
    }

    [Fact]
    public void HeaderPart_IsScannedToo()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Gövde")), header: TestDocx.P(TestDocx.R("Belge No : " + Dots)));
        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("ALAN_BELGE_NO", a.Fields);
        var filled = DocxTemplate.Fill(docx, TestDocx.Data(("ALAN_BELGE_NO", "2026/14")));
        Assert.Equal("Belge No : 2026/14", TestDocx.Text(filled, "word/header1.xml"));
    }

    [Fact]
    public void EditorMode_ShowsPlaceholderForEmptyCustomField()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("İtiraz Eden : " + Dots)));
        var marker = new FieldMarker(k => $"[{k}]");
        var text = TestDocx.Text(DocxTemplate.Fill(docx, new ReportData(), marker));
        Assert.Equal("İtiraz Eden : [ALAN_ITIRAZ_EDEN]", text);
    }

    [Fact]
    public void ExistingMarkersStillWork_NextToCustomSpots()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("{{TURNUVA_ADI}}")) + TestDocx.P(TestDocx.R("Sporcu : " + Dots)));
        var a = DocxTemplate.Analyze(docx);
        Assert.Equal(new[] { "TURNUVA_ADI", "ALAN_SPORCU" }, a.Fields);
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("TURNUVA_ADI", "X TURNUVASI"), ("ALAN_SPORCU", "Y"))));
        Assert.Equal("X TURNUVASI\nSporcu : Y", text);
    }
}

/// <summary>Tutanak tabloları: hakem, sporcu, eşleştirme, ceza, itiraz.</summary>
public class ReportListsTests
{
    private static readonly Dictionary<string, string> Info = new()
    {
        ["Başhakem"] = "NA Mehmet KUTDEMİR",
        ["Başhakem Yardımcısı"] = "Can DENEMECİ",
        ["Hakem"] = "FA Ahmet Burak GUNAYDIN; Suleyman UYMAZ",
        ["Turnuva direktörü"] = "Ramazan DURMUS"
    };

    [Fact]
    public void Arbiters_RolesNumbersAndGrades()
    {
        var grades = new Dictionary<string, string> { [EventGrouping.Fold("Suleyman UYMAZ").Replace(" ", "")] = Arbiters.Il };
        var rows = ReportLists.Arbiters(Info, grades);
        Assert.Equal(5, rows.Count);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, rows.Select(r => r["sira"]));
        Assert.Equal(("Mehmet KUTDEMİR", "Başhakem", Arbiters.Ulusal), (rows[0]["adi"], rows[0]["gorev"], rows[0]["unvan"]));
        Assert.Equal(Arbiters.Fide, rows.Single(r => r["adi"] == "Ahmet Burak GUNAYDIN")["unvan"]);
        Assert.Equal(Arbiters.Il, rows.Single(r => r["adi"] == "Suleyman UYMAZ")["unvan"]);      // elle seçilen derece
        Assert.Equal("", rows.Single(r => r["adi"] == "Can DENEMECİ")["unvan"]);
        Assert.Equal("Turnuva Direktörü", rows[^1]["gorev"]);
    }

    [Fact]
    public void Players_PerCategory_WithProvinceAndRating()
    {
        var lists = new List<(string, IReadOnlyList<Player>)>
        {
            ("17 Yaş", new[] { new Player(1, "DENİZ ÖRNEK", Rating: 1678, Club: "İzmir SK"), new Player(null, "ALİ SINAMA", Rating: 0) }),
            ("14 Yaş", new[] { new Player(3, "ARDA DENEME", Title: "CM", Rating: 1500, Province: "ISPARTA") })
        };
        var rows = ReportLists.Players(lists, name => name == "DENİZ ÖRNEK" ? "İZMİR" : null);
        Assert.Equal(3, rows.Count);
        Assert.Equal(("1", "DENİZ ÖRNEK", "1678", "İzmir SK", "İZMİR", "17 Yaş"), (rows[0]["sira"], rows[0]["adi"], rows[0]["elo"], rows[0]["kulup"], rows[0]["il"], rows[0]["kategori"]));
        Assert.Equal(("2", "", ""), (rows[1]["sira"], rows[1]["elo"], rows[1]["il"]));         // sıra no yoksa sırası; 0 rating boş
        Assert.Equal(("CM", "ISPARTA", "14 Yaş"), (rows[2]["unvan"], rows[2]["il"], rows[2]["kategori"]));   // kayıttaki il önce gelir
    }

    [Fact]
    public void Pairings_ByeLast_AndTeamBoards()
    {
        var t = new Tournament("T", 2, new List<Pairing>
        {
            new(3, new Player(5, "BAY ALAN"), null),
            new(2, new Player(3, "C"), new Player(4, "D"), Result: "½-½"),
            new(1, new Player(1, "A"), new Player(2, "B"), Result: "1-0"),
        });
        var team = new Tournament("Takım", 1, new List<Pairing> { new(1, new Player(1, "X"), new Player(2, "Y"), BoardLabel: "1.1") });
        var rows = ReportLists.Pairings(new[] { ("17 Yaş", t), ("Okul", team) });
        Assert.Equal(new[] { "1", "2", "" , "1.1" }, rows.Select(r => r["masa"]));
        Assert.Equal(("A", "B", "1-0", "2"), (rows[0]["beyaz"], rows[0]["siyah"], rows[0]["sonuc"], rows[0]["tur"]));
        Assert.Equal(("BAY ALAN", "BAY"), (rows[2]["beyaz"], rows[2]["siyah"]));
        Assert.Equal("Okul", rows[3]["kategori"]);
    }

    private static List<IncidentRecord> Sample() => new()
    {
        new() { Kind = IncidentRecord.Penalty, Date = "25.10.2026", Time = "10.40", Round = "3", Category = "17 Yaş", Board = "2", Player = "DENİZ ÖRNEK", Move = "34", Reason = "Kuraldışı hamle", Decision = "Rakibe 2 dakika eklendi", Arbiter = "Can DENEMECİ" },
        new() { Kind = IncidentRecord.Penalty, Date = "24.10.2026", Time = "16.05", Round = "2", Category = "14 Yaş", Board = "1", Player = "ARDA DENEME", Reason = "Elektronik cihaz", Decision = "Oyunu kaybetti" },
        new() { Kind = IncidentRecord.Appeal, Date = "25.10.2026", Time = "13.00", Round = "4", Category = "16 Yaş", Board = "1", Player = "Veli (antrenör)", Reason = "Eşleştirmeye itiraz", Decision = "Reddedildi" },
        new() { Kind = IncidentRecord.Penalty },   // boş satır: sayılmaz
    };

    [Fact]
    public void Incidents_FilteredSortedAndColumnsByKind()
    {
        var pen = ReportLists.Incidents(Sample(), IncidentRecord.Penalty);
        Assert.Equal(3, pen.Count);                                    // 2 kayıt + 1 boş satır (en az 3)
        Assert.Equal("24.10.2026", pen[0]["tarih"]);                   // tarihe göre
        Assert.Equal(("DENİZ ÖRNEK", "34", "Kuraldışı hamle", "Rakibe 2 dakika eklendi"), (pen[1]["sporcu"], pen[1]["hamle"], pen[1]["neden"], pen[1]["ceza"]));
        Assert.All(pen[2].Values, v => Assert.Equal("", v));
        Assert.Equal(ReportLists.Catalog["ceza"].Columns.Select(c => c.Column).OrderBy(c => c), pen[2].Keys.OrderBy(k => k));

        var app = ReportLists.Incidents(Sample(), IncidentRecord.Appeal, minRows: 0);
        Assert.Single(app);
        Assert.Equal(("Veli (antrenör)", "Eşleştirmeye itiraz", "Reddedildi"), (app[0]["eden"], app[0]["konu"], app[0]["karar"]));
        Assert.False(app[0].ContainsKey("sporcu"));
    }

    [Fact]
    public void Incidents_NoRecords_GiveBlankRowsForHandwriting()
    {
        Assert.Equal(ReportLists.MinIncidentRows, ReportLists.Incidents(null, IncidentRecord.Appeal).Count);
        Assert.Equal(new Dictionary<string, string> { ["CEZA_SAYISI"] = "0", ["ITIRAZ_SAYISI"] = "0" }, ReportLists.IncidentValues(null));
    }

    [Fact]
    public void IncidentValues_CountOnlyFilledRecords()
    {
        var v = ReportLists.IncidentValues(Sample());
        Assert.Equal(("2", "1"), (v["CEZA_SAYISI"], v["ITIRAZ_SAYISI"]));
    }

    [Fact]
    public void Catalog_CoversEveryListUsedByTemplates()
    {
        foreach (var prefix in new[] { "kategori", "program", "katilim", "il", "derece", "siralama", "hakem", "sporcu", "eslesme", "ceza", "itiraz" })
            Assert.True(ReportLists.Catalog.ContainsKey(prefix), prefix);
    }
}

/// <summary>Rapor taslağı: noktalı alanların soruları, ek tablolar ve değerlendirme önerisi.</summary>
public class ReportBuilderExtrasTests
{
    private static readonly IReadOnlyDictionary<string, string> Info = new Dictionary<string, string>
    {
        ["Tarih"] = "2026/10/24 bis 2026/10/26", ["Yer"] = "Isparta", ["Başhakem"] = "Mehmet KUTDEMİR"
    };

    private static ReportDraft Build(byte[] docx, ReportSources sources)
        => ReportBuilder.Build(Info, Array.Empty<(int, DateTime?, string)>(), Array.Empty<(CategoryRef, int, TournamentSystem)>(),
                               "Isparta Yıldızlar İl Birinciliği", "Isparta", DocxTemplate.Analyze(docx), sources);

    [Fact]
    public void CustomFields_AreOptional_WithDocumentLabel_AndRememberedPerEvent()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("İtiraz Eden : ..........")));
        var d = Build(docx, new ReportSources());
        var f = Assert.Single(d.Fields);
        Assert.Equal(("ALAN_ITIRAZ_EDEN", "İtiraz Eden", true, ReportFieldSource.Missing), (f.Key, f.Label, f.Optional, f.Source));

        var again = Build(docx, new ReportSources { EventMemory = new Dictionary<string, string> { ["ALAN_ITIRAZ_EDEN"] = "Ali VELİ" } });
        Assert.Equal(("Ali VELİ", ReportFieldSource.Suggested), (again.Fields[0].Value, again.Fields[0].Source));
    }

    [Fact]
    public void ExtraListsAndValues_AreUsed()
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("Ceza: {{CEZA_SAYISI}}")) + TestDocx.Table(TestDocx.Row("{{ceza.sporcu}}")));
        var lists = new Dictionary<string, List<Dictionary<string, string>>> { ["ceza"] = new() { new() { ["sporcu"] = "EGE" } } };
        var d = Build(docx, new ReportSources { ExtraLists = lists, ExtraValues = new Dictionary<string, string> { ["CEZA_SAYISI"] = "1" } });
        Assert.Equal(("1", ReportFieldSource.Automatic), (d.Fields.Single(f => f.Key == "CEZA_SAYISI").Value, d.Fields.Single(f => f.Key == "CEZA_SAYISI").Source));
        Assert.Equal("EGE", d.Lists["ceza"][0]["sporcu"]);
        var text = TestDocx.Text(DocxTemplate.Fill(docx, d.ToReportData()));
        Assert.Contains("Ceza: 1", text);
        Assert.Contains("EGE", text);
    }

    [Theory]
    [InlineData("0", "0", "sorunsuz", "itiraz olmamıştır")]
    [InlineData("2", "1", "2 itiraz yapılmış", "1 ceza verilmiştir")]
    public void Evaluation_FollowsIncidentCounts(string appeals, string penalties, string expect1, string expect2)
    {
        var docx = TestDocx.Make(TestDocx.P(TestDocx.R("{{DEGERLENDIRME}}")));
        var d = Build(docx, new ReportSources { ExtraValues = new Dictionary<string, string> { ["ITIRAZ_SAYISI"] = appeals, ["CEZA_SAYISI"] = penalties } });
        var v = d.Fields.Single(f => f.Key == "DEGERLENDIRME").Value;
        Assert.Contains(expect1, v);
        Assert.Contains(expect2, v);
    }
}

/// <summary>Hazır şablonlar: yükleniyor, doğru alan/tabloları istiyor, uçtan uca doluyor (hiç işaret kalmıyor).</summary>
public class BuiltInTemplateTests
{
    public static IEnumerable<object[]> All() => ReportForm.BuiltIns.Select(b => new object[] { b.Resource });

    [Theory]
    [MemberData(nameof(All))]
    public void EveryBuiltIn_LoadsAndFillsWithoutLeftoverMarkers(string resource)
    {
        var bytes = ReportForm.BuiltInTemplate(resource);
        var a = DocxTemplate.Analyze(bytes);
        Assert.NotEmpty(a.Fields);
        var data = new ReportData();
        foreach (var f in a.Fields) data.Values[f] = "DEĞER-" + f;
        foreach (var l in a.Lists.Concat(new[] { "kategori", "program" }))
        {
            var cols = ReportLists.Catalog.TryGetValue(l, out var c) ? c.Columns.Select(x => x.Column) : new[] { "adi" };
            var rows = Enumerable.Range(1, 2).Select(i => cols.ToDictionary(col => col, col => $"{l}{i}.{col}")).ToList();
            if (l == "kategori") data.Categories.AddRange(rows); else if (l == "program") data.Program.AddRange(rows); else data.Lists[l] = rows;
        }
        var filled = DocxTemplate.Fill(bytes, data);
        using var zip = new ZipArchive(new MemoryStream(filled));
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("word/") && e.FullName.EndsWith(".xml")))
        {
            using var s = new StreamReader(e.Open());
            Assert.DoesNotContain("{{", s.ReadToEnd());
        }
    }

    [Theory]
    [InlineData(ReportForm.CezaResource, "CEZA TUTANAĞI", "ceza", new[] { "IL", "TARIH_ARALIGI", "YER", "BASHAKEM", "CEZA_SAYISI", "HAKEMLER" })]
    [InlineData(ReportForm.ItirazResource, "İTİRAZ TUTANAĞI", "itiraz", new[] { "IL", "BASHAKEM", "ITIRAZ_SAYISI", "ITIRAZ_KURULU", "DIREKTOR" })]
    [InlineData(ReportForm.HakemResource, "HAKEM GÖREV ÇİZELGESİ", "hakem", new[] { "IL", "BASHAKEM", "DIREKTOR" })]
    public void NewTemplates_AskForTheirDataAndTitle(string resource, string title, string list, string[] fields)
    {
        var bytes = ReportForm.BuiltInTemplate(resource);
        var a = DocxTemplate.Analyze(bytes);
        Assert.Contains(list, a.Lists);
        foreach (var f in fields) Assert.Contains(f, a.Fields);
        Assert.DoesNotContain(a.Fields, f => f.StartsWith("ALAN_"));             // hepsi tanınan alanlar
        var header = TestDocx.Text(DocxTemplate.Fill(bytes, TestDocx.Data(("TURNUVA_ADI", "ISPARTA YILDIZLAR"))), "word/header1.xml");
        Assert.Contains("ISPARTA YILDIZLAR " + title, header);
    }

    [Fact]
    public void PenaltyReport_EndToEnd_WithRecords()
    {
        var bytes = ReportForm.BuiltInTemplate(ReportForm.CezaResource);
        var records = new List<IncidentRecord>
        {
            new() { Kind = IncidentRecord.Penalty, Date = "25.10.2026", Time = "10.40", Round = "3", Category = "17 Yaş", Board = "2", Player = "DENİZ ÖRNEK", Move = "34", Reason = "Kuraldışı hamle", Decision = "Rakibe 2 dakika eklendi", Arbiter = "Can DENEMECİ" },
            new() { Kind = IncidentRecord.Appeal, Player = "İtiraz eden", Reason = "Sonuç" }
        };
        var d = ReportBuilder.Build(new Dictionary<string, string> { ["Başhakem"] = "Mehmet KUTDEMİR" }, Array.Empty<(int, DateTime?, string)>(),
            Array.Empty<(CategoryRef, int, TournamentSystem)>(), "Isparta Yıldızlar", "Isparta", DocxTemplate.Analyze(bytes),
            new ReportSources
            {
                ExtraLists = new Dictionary<string, List<Dictionary<string, string>>> { ["ceza"] = ReportLists.Incidents(records, IncidentRecord.Penalty) },
                ExtraValues = ReportLists.IncidentValues(records)
            });
        var filled = DocxTemplate.Fill(bytes, d.ToReportData());
        var x = TestDocx.Xml(filled);
        var text = TestDocx.Text(filled);
        Assert.Contains("Toplam ceza sayısı: 1.", text);
        Assert.Contains("Mehmet KUTDEMİR", text);
        // Ceza tablosu: başlık + 1 kayıt + 2 boş satır (elle yazmak için)
        var table = x.Descendants(TestDocx.W + "tbl").Single(t => t.Descendants(TestDocx.W + "t").Any(tt => tt.Value == "Ceza Nedeni"));
        var rows = table.Elements(TestDocx.W + "tr").ToList();
        Assert.Equal(1 + ReportLists.MinIncidentRows, rows.Count);
        var cells = rows[1].Elements(TestDocx.W + "tc").Select(c => string.Concat(c.Descendants(TestDocx.W + "t").Select(t => t.Value))).ToList();
        Assert.Equal(new[] { "25.10.2026", "10.40", "3", "17 Yaş", "2", "DENİZ ÖRNEK", "34", "Kuraldışı hamle", "Rakibe 2 dakika eklendi", "Can DENEMECİ" }, cells);
        Assert.DoesNotContain("İtiraz eden", text);                                    // itiraz ceza tutanağına girmez
    }

    [Fact]
    public void ExistingTemplates_GainNoRequiredQuestions()
    {
        // Eski hazır şablonlarda noktalı satırlar etiketsiz: yeni soru eklenmemeli.
        foreach (var res in new[] { ReportForm.BuiltInResource, ReportForm.TutanakResource, ReportForm.SonRaporResource })
            Assert.DoesNotContain(DocxTemplate.Analyze(ReportForm.BuiltInTemplate(res)).Fields, f => f.StartsWith("ALAN_"));
    }
}

/// <summary>"Kullanılabilir alanlar" penceresinin içeriği.</summary>
public class FieldListTests
{
    [Fact]
    public void ListsEveryFieldAndTable_WithCurrentValues()
    {
        var draft = new ReportDraft();
        draft.Fields.Add(new ReportField { Key = "IL", Label = "İl", Value = "Isparta" });
        draft.Lists["hakem"] = new() { new() { ["adi"] = "Mehmet KUTDEMİR", ["sira"] = "1" } };
        var rows = FieldListForm.Build(draft);
        foreach (var key in ReportBuilder.Catalog.Keys) Assert.Contains(rows, r => r.Marker == "{{" + key + "}}");
        foreach (var (prefix, (_, cols)) in ReportLists.Catalog)
            foreach (var (col, _) in cols) Assert.Contains(rows, r => r.Marker == $"{{{{{prefix}.{col}}}}}");
        Assert.Equal("Isparta", rows.Single(r => r.Marker == "{{IL}}").Value);
        Assert.Equal("Mehmet KUTDEMİR", rows.Single(r => r.Marker == "{{hakem.adi}}").Value);
        Assert.Contains(rows, r => r.Marker == "" && r.Description.StartsWith("TABLO: HAKEMLER") && r.Value == "1 satır");
    }

    [Fact]
    public void WorksWithoutDraft()
        => Assert.True(FieldListForm.Build(null).Count > ReportBuilder.Catalog.Count);
}

/// <summary>Ceza / itiraz kayıt penceresi: satır ekleme, saklama, boş satırların atlanması.</summary>
public class IncidentsFormTests
{
    [Fact]
    public void AddRows_AreSavedPerEvent()
    {
        var cfg = new AppConfig();
        int saves = 0;
        var ctx = new IncidentsContext(1520070, "Isparta Kasım", new[] { "17 Yaş", "16 Yaş" }, "17 Yaş", 3, "Mehmet KUTDEMİR", null, cfg, () => saves++);
        using var f = new IncidentsForm(ctx);
        f.CreateControl();
        f.AddRow(IncidentRecord.Penalty);
        f.AddRow(IncidentRecord.Appeal);
        var stored = cfg.Incidents["1520070"];
        Assert.Equal(2, stored.Count);
        Assert.Equal((IncidentRecord.Penalty, "3", "17 Yaş", "Mehmet KUTDEMİR"), (stored[0].Kind, stored[0].Round, stored[0].Category, stored[0].Arbiter));
        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), stored[0].Date);
        Assert.Equal(IncidentRecord.Appeal, stored[1].Kind);
        Assert.True(saves >= 2);
    }

    [Fact]
    public void ExistingRecords_AreLoadedBack()
    {
        var cfg = new AppConfig();
        cfg.Incidents["7"] = new() { new IncidentRecord { Kind = IncidentRecord.Appeal, Player = "X", Reason = "Y", Category = "Eski kategori" } };
        using var f = new IncidentsForm(new IncidentsContext(7, "T", new[] { "A" }, null, 0, null, null, cfg, () => { }));
        f.CreateControl();
        var r = Assert.Single(f.Records());
        Assert.Equal((IncidentRecord.Appeal, "X", "Y", "Eski kategori"), (r.Kind, r.Player, r.Reason, r.Category));
    }
}


/// <summary>Gerçek çıktıda bulunan sorunların testleri (regresyon).</summary>
public class ReportRegressionTests
{
    [Fact]
    public void SignatureTableHeader_IsNotOverwrittenByNeighbourLabel()
    {
        // "Başhakem | Yarışma Direktörü | İtiraz Kurulu" başlığı; altında {{…}} değerler.
        // "İtiraz Kurulu" başlığına direktörün adı yazılıyordu.
        var bytes = ReportForm.BuiltInTemplate(ReportForm.ItirazResource);
        var text = TestDocx.Text(DocxTemplate.Fill(bytes, TestDocx.Data(("DIREKTOR", "Ramazan DURMUŞ"), ("BASHAKEM", "Mehmet KUTDEMİR"))));
        Assert.Contains("İtiraz Kurulu", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Ramazan DURMUŞ"));
    }

    [Fact]
    public void ColumnHeaderRow_InUserTemplate_KeepsHeaders()
    {
        var docx = TestDocx.Make(TestDocx.Table(TestDocx.Row("Yarışma Direktörü", "Gözlemci"), TestDocx.Row("{{DIREKTOR}}", "")));
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("DIREKTOR", "X"))));
        Assert.Equal("Yarışma Direktörü\nGözlemci\nX\n", text);
    }

    [Fact]
    public void LabelValueRows_StillFilled()
    {
        var docx = TestDocx.Make(TestDocx.Table(TestDocx.Row("Başhakem", ""), TestDocx.Row("Yeri", "")));
        var text = TestDocx.Text(DocxTemplate.Fill(docx, TestDocx.Data(("BASHAKEM", "A"), ("YER", "B"))));
        Assert.Equal("Başhakem\nA\nYeri\nB", text);
    }

    [Theory]
    [InlineData("SON BAŞVURU TARİHİ 18.09.2026 17.00 SİSTEM", "")]
    [InlineData("BAŞLAMA", "")]
    [InlineData("Isparta Spor Salonu SİSTEM İsviçre", "Isparta Spor Salonu")]
    [InlineData("Merkez", "Merkez")]
    [InlineData("Gençlik Merkezi, Konferans Salonu", "Gençlik Merkezi, Konferans Salonu")]
    public void Yonerge_EmptyTableCell_DoesNotTakeNextLabel(string raw, string expected)
        => Assert.Equal(expected, YonergeExtractor.CutAtLabel(raw));

    [Fact]
    public void Yonerge_OldTable_WithEmptyCells()
    {
        var d = YonergeExtractor.Extract("İLİ ISPARTA İLÇESİ BAŞLAMA-BİTİŞ TARİHİ 24.10.2026\nYERİ SON BAŞVURU TARİHİ 18.09.2026 17.00 SİSTEM\n");
        Assert.Equal("ISPARTA", d["IL"]);
        Assert.False(d.ContainsKey("ILCE"));
        Assert.False(d.ContainsKey("YER"));
        var ok = YonergeExtractor.Extract("İLİ Isparta İLÇESİ Merkez\nYERİ Isparta Spor Salonu\n");
        Assert.Equal(("Merkez", "Isparta Spor Salonu"), (ok["ILCE"], ok["YER"]));
    }

    [Fact]
    public void EmptyArbiterList_LeavesRowsForHandwriting()
    {
        var rows = ReportLists.Padded(new(), "hakem", 4);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(new[] { "adi", "gorev", "sira", "unvan" }, r.Keys.OrderBy(k => k)));
        var two = ReportLists.Padded(new() { new() { ["adi"] = "A" }, new() { ["adi"] = "B" } }, "hakem", 4);
        Assert.Equal(new[] { "A", "B", "", "" }, two.Select(r => r["adi"]));
        Assert.Equal(6, ReportLists.Padded(Enumerable.Range(0, 6).Select(_ => new Dictionary<string, string>()).ToList(), "hakem", 4).Count);
    }
}
