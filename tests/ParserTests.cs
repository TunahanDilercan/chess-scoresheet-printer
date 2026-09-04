using ClosedXML.Excel;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Parsers;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

public class ParserTests
{
    private static string WriteTemp(string content, string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        return path;
    }

    [Fact]
    public void Json_Structured_ParsesBoardsAndTurkishChars()
    {
        const string json = """
        {
          "name": "Şişli Açık", "roundNo": 4,
          "pairings": [
            { "board": 2, "white": {"startNo":1,"name":"Çağla Öz","rating":2100}, "black": {"name":"İbrahim Ş.","rating":0} },
            { "board": 1, "white": {"name":"Ümit Çelik","title":"FM","rating":2240}, "black": null }
          ]
        }
        """;
        var file = WriteTemp(json, ".json");
        var t = ParserFactory.Parse(file, new AppConfig());

        Assert.Equal(2, t.Pairings.Count);
        Assert.Equal(4, t.RoundNo);
        Assert.Equal("Şişli Açık", t.Name);

        var b2 = t.Pairings.Single(p => p.Board == 2);
        Assert.Equal("Çağla Öz", b2.White.Name);
        Assert.Equal(2100, b2.White.Rating);
        Assert.Null(b2.Black!.Rating); // 0 -> null

        var b1 = t.Pairings.Single(p => p.Board == 1);
        Assert.True(b1.IsBye); // black: null
        Assert.Equal("FM Ümit Çelik", b1.White.DisplayLine());

        File.Delete(file);
    }

    [Fact]
    public void Csv_SemicolonDelimited_MapsColumnsAndDetectsBye()
    {
        const string csv =
            "Masa;SNo;Beyaz;Unvan;Rtg;Siyah;Rtg2\n" +
            "1;1;Çağla Şahin;WFM;2105;Görkem Yıldız;1980\n" +
            "2;5;Oğuzhan Şen;;1930;BAY;\n";
        var file = WriteTemp(csv, ".csv");
        var t = ParserFactory.Parse(file, new AppConfig());

        Assert.Equal(2, t.Pairings.Count);

        var b1 = t.Pairings.Single(p => p.Board == 1);
        Assert.Equal("WFM Çağla Şahin", b1.White.DisplayLine());
        Assert.Equal(2105, b1.White.Rating);
        Assert.Equal(1980, b1.Black!.Rating);

        var b2 = t.Pairings.Single(p => p.Board == 2);
        Assert.True(b2.IsBye);

        File.Delete(file);
    }

    [Fact]
    public void Csv_MissingWhiteColumn_ThrowsHelpfulError()
    {
        const string csv = "FooColumn;Bar\nx;y\n";
        var file = WriteTemp(csv, ".csv");

        var ex = Assert.Throws<ParseException>(() => ParserFactory.Parse(file, new AppConfig()));
        Assert.Contains("Beyaz", ex.Message);

        File.Delete(file);
    }

    [Fact]
    public void Xlsx_RoundTrip_ParsesColumns()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Tur");
            string[] headers = { "Masa", "SNo", "Beyaz", "Unvan", "Rtg", "Siyah", "Rtg2" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            object[] r1 = { 1, 1, "Çağla Şahin", "WFM", 2105, "Görkem Yıldız", 1980 };
            object[] r2 = { 2, 5, "Oğuzhan Şen", "", 1930, "BAY", "" };
            for (int c = 0; c < r1.Length; c++) ws.Cell(2, c + 1).Value = XLCellValue.FromObject(r1[c]);
            for (int c = 0; c < r2.Length; c++) ws.Cell(3, c + 1).Value = XLCellValue.FromObject(r2[c]);
            wb.SaveAs(path);
        }

        var t = ParserFactory.Parse(path, new AppConfig());
        Assert.Equal(2, t.Pairings.Count);
        var b1 = t.Pairings.Single(p => p.Board == 1);
        Assert.Equal("WFM Çağla Şahin", b1.White.DisplayLine());
        Assert.Equal(1980, b1.Black!.Rating);
        Assert.True(t.Pairings.Single(p => p.Board == 2).IsBye);

        File.Delete(path);
    }

    [Fact]
    public void Tunx_JsonContent_IsSniffedAndParsed()
    {
        const string json = """{ "name":"Tunx Testi","roundNo":2,"pairings":[{"board":1,"white":{"name":"Ali Veli"},"black":{"name":"Veli Ali"}}] }""";
        var file = WriteTemp(json, ".tunx");
        var t = ParserFactory.Parse(file, new AppConfig());
        Assert.Single(t.Pairings);
        Assert.Equal("Ali Veli", t.Pairings[0].White.Name);
        File.Delete(file);
    }

    [Fact]
    public void Tunx_CsvContent_IsSniffedAndParsed()
    {
        const string csv = "Masa;Beyaz;Rtg;Siyah;Rtg2\n1;Çağla Şahin;2105;Görkem Yıldız;1980\n";
        var file = WriteTemp(csv, ".tunx");
        var t = ParserFactory.Parse(file, new AppConfig());
        Assert.Single(t.Pairings);
        Assert.Equal("Çağla Şahin", t.Pairings[0].White.Name);
        File.Delete(file);
    }

    [Fact]
    public void GarbageContent_Throws()
    {
        var file = WriteTemp("\x01\x02 binary junk", ".pdf");
        Assert.Throws<ParseException>(() => ParserFactory.Parse(file, new AppConfig()));
        File.Delete(file);
    }
}
