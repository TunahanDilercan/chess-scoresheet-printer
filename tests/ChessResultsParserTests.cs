using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>
/// chess-results parser testleri — kaydedilmiş gerçek HTML örnekleri üzerinde (ağ gerektirmez).
/// </summary>
public class ChessResultsParserTests
{
    private static string Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", name);
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    [Fact]
    public void Pairings_ParsesBoardsRatingsAndTurkishNames()
    {
        var html = Load("pairings_rd1.html");
        var t = ChessResultsParser.ParsePairings(html, tnr: 1437263, round: 1);

        Assert.Equal(14, t.Pairings.Count);                 // 28 oyuncu -> 14 masa
        Assert.Equal(1, t.RoundNo);
        Assert.Contains("Isparta", t.Name);

        var b1 = t.Pairings.Single(p => p.Board == 1);
        Assert.Equal("METEHAN PEHLİVAN", b1.White.Name);    // Türkçe İ
        Assert.Equal(1558, b1.White.Rating);
        Assert.Equal("AHMET DÖNMEZ", b1.Black!.Name);       // Türkçe Ö
        Assert.Equal(1940, b1.Black.Rating);

        var b3 = t.Pairings.Single(p => p.Board == 3);
        Assert.Equal("DAĞHAN ŞAHİN", b3.White.Name);        // Ğ ve Ş
    }

    [Fact]
    public void EventInfo_DetectsAllCategories()
    {
        var html = Load("pairings_rd1.html");
        var info = ChessResultsParser.ParseEventInfo(html, tnr: 1437263);

        Assert.Equal(5, info.MaxRound);
        Assert.Contains("Isparta", info.Name);

        var names = info.Categories.Select(c => c.Name).ToList();
        Assert.Contains(names, n => n.Contains("A Kategorisi"));
        Assert.Contains(names, n => n.Contains("B Kategorisi"));
        Assert.Contains(names, n => n.Contains("8 Yaş"));
        Assert.Contains(names, n => n.Contains("7 Yaş"));

        // Mevcut kategori A = current tnr
        var current = Assert.Single(info.Categories, c => c.IsCurrent);
        Assert.Equal(1437263, current.Tnr);
        // Diğer kategorilerin ayrı tnr'leri var
        Assert.Contains(info.Categories, c => c.Tnr == 1437268);
    }

    [Fact]
    public void FederationList_ParsesTurkishTournaments()
    {
        var html = Load("fed_tur.html");
        var list = ChessResultsParser.ParseFederationList(html);

        Assert.True(list.Count >= 10, $"beklenen >=10, gelen {list.Count}");
        Assert.Contains(list, t => t.Tnr == 1437263); // Isparta A
        Assert.All(list, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
    }

    [Fact]
    public void Dedupe_CollapsesCategoriesOfSameEvent()
    {
        var input = new List<TournamentRef>
        {
            new(1437269, "Isparta Haziran Ayı ELO Satranç Turnuvası 8 Yaş Kategorisi"),
            new(1437263, "Isparta Haziran Ayı ELO Satranç Turnuvası A Kategorisi"),
            new(1437268, "Isparta Haziran Ayı ELO Satranç Turnuvası B Kategorisi"),
            new(1384773, "12th Cesme International Open Rapid Chess Tournament"),
        };
        var grouped = EventGrouping.Dedupe(input);

        // Isparta'nın 3 kategorisi tek girişe indi; Çeşme ayrı kaldı.
        Assert.Equal(2, grouped.Count);
        Assert.Contains(grouped, g => g.Name.StartsWith("Isparta") && !g.Name.Contains("Kategori"));
        Assert.Contains(grouped, g => g.Name.Contains("Cesme"));
    }

    [Theory]
    [InlineData("1437263", 1437263)]
    [InlineData("tnr1437263", 1437263)]
    [InlineData("https://s3.chess-results.com/Tnr1437263.aspx?lan=8&art=2", 1437263)]
    [InlineData("https://chess-results.com/tnr1437268.aspx?lan=8", 1437268)]
    [InlineData("saçma metin", null)]
    public void ParseTnr_HandlesNumberUrlAndGarbage(string input, int? expected)
    {
        Assert.Equal(expected, ChessResultsClient.ParseTnr(input));
    }
}
