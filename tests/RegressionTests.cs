using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.Parsers;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>
/// Gerçek chess-results turnuvalarıyla yapılan testte bulunan hataların regresyon testleri.
/// TestData'daki sayfalar canlı siteden alınmış kopyalardır (ağ gerektirmez).
/// </summary>
public class RegressionTests
{
    private static string Load(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name), System.Text.Encoding.UTF8);

    // Türkçe sayfada BAY "Tur", eşlenmeyen oyuncu "eşlendirilmeyenler" yazılır.
    // Önceden ikisi de gerçek rakip sanılıp kağıt basılıyordu.
    [Fact]
    public void Pairings_TurIsBye_NotPairedIsSkipped()
    {
        var t = ChessResultsParser.ParsePairings(Load("pairings_bye_notpaired.html"), tnr: 1504029, round: 5);

        Assert.Equal(6, t.Pairings.Count);                  // 7 satır − 1 eşlenmeyen
        var bye = Assert.Single(t.Pairings, p => p.IsBye);
        Assert.Equal(6, bye.Board);
        Assert.DoesNotContain(t.Pairings, p => p.Black?.Name is "Tur" or "eşlendirilmeyenler");
        Assert.DoesNotContain(t.Pairings, p => p.White.Name.StartsWith("SARITAŞ"));

        // "BAY" ile başlayan gerçek ad BAY sayılmamalı.
        Assert.Equal("BAYTAR, ÇAĞAN ENES", t.Pairings.Single(p => p.Board == 1).White.Name);
    }

    [Fact]
    public void EventInfo_FinishedTournament_CurrentAndMaxRound()
    {
        var info = ChessResultsParser.ParseEventInfo(Load("pairings_bye_notpaired.html"), tnr: 1504029);
        Assert.Equal(5, info.MaxRound);
        Assert.Equal(5, info.CurrentRound);
    }

    // Programdaki "6. Tur 16.30" saatleri tur numarası sanılıp 17 tur görünüyordu.
    [Fact]
    public void EventInfo_NotStarted_ScheduleTimesAreNotRounds()
    {
        var info = ChessResultsParser.ParseEventInfo(Load("event_not_started.html"), tnr: 1509183);
        Assert.Equal(0, info.CurrentRound);
        Assert.Equal(ChessResultsParser.UnknownMaxRound, info.MaxRound);
        Assert.Equal(4, info.Categories.Count);
    }

    [Theory]
    [InlineData("MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI AÇIK YAŞ KADIN KATEGORİSİ", "MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI")]
    [InlineData("MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI AÇIK YAŞ KATEGORİSİ", "MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI")]
    [InlineData("MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI 12 YAŞ GENEL KATEGORİSİ", "MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI")]
    [InlineData("DÜNYA ÖĞRETMENLER GÜNÜ SATRANÇ TURNUVASI AÇIK KATEGORİ", "DÜNYA ÖĞRETMENLER GÜNÜ SATRANÇ TURNUVASI")]
    [InlineData("GELENEKSEL AHİLİK YILI SATRANÇ TURNUVASI 14 YAŞ VE ALTI KATEGORİSİ (2020-2013)", "GELENEKSEL AHİLİK YILI SATRANÇ TURNUVASI")]
    [InlineData("2026_dunya_hayvanlari_koruma_gunu_satranc_turnuvasi 10_yaş_ve_altı_ kategorisi", "2026_dunya_hayvanlari_koruma_gunu_satranc_turnuvasi")]
    [InlineData("Ankara Eylül Ayı UKD Satranç Turnuvası C Kategorisi", "Ankara Eylül Ayı UKD Satranç Turnuvası")]
    [InlineData("MARMARİS EN İYİ 32 SATRANÇ TURNUVASI", "MARMARİS EN İYİ 32 SATRANÇ TURNUVASI")]
    // Turnuva adının asıl kelimeleri kategori eki sanılıp silinmemeli.
    [InlineData("2024 Kış Okul Ligi A Kategorisi", "2024 Kış Okul Ligi")]
    public void BaseName_StripsOnlyCategorySuffix(string input, string expected)
        => Assert.Equal(expected, EventGrouping.BaseName(input));

    [Theory]
    [InlineData("MANAVGAT GAZİLER HAFTASI SATRANÇ TURNUVASI AÇIK YAŞ KATEGORİSİ", "AÇIK YAŞ")]
    [InlineData("2026 ELAZIĞ AMATÖR SPOR HAFTASI SATRANÇ TURNUVASI GENEL KATEGORİ", "GENEL")]
    [InlineData("ALANYA SAĞLIK ÇALIŞANLARI MOTİVASYON TURNUVASI", "")]
    public void CategoryFromEventName_SingleGroupTournament(string input, string expected)
        => Assert.Equal(expected, EventGrouping.CategoryFromEventName(input));

    [Theory]
    [InlineData("A Kategorisi", "A")]
    [InlineData("AÇIK KATEGORİ", "AÇIK")]
    [InlineData("10 Yaş ve Altı Kategorisi", "10 Yaş ve Altı")]
    [InlineData("8 YAŞ VE ALTI", "8 YAŞ VE ALTI")]
    public void ShortCategory_IsCultureIndependent(string input, string expected)
        => Assert.Equal(expected, EventGrouping.ShortCategory(input));

    [Fact]
    public void Json_NameStartingWithBay_IsNotBye()
    {
        const string json = """
        { "name": "T", "roundNo": 1, "pairings": [
          { "board": 1, "white": "Kaya, Ali", "black": "BAYRAM, MEHMET" },
          { "board": 2, "white": "Öz, Can",  "black": "BAY" } ] }
        """;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        var t = ParserFactory.Parse(path, new AppConfig());

        Assert.False(t.Pairings.Single(p => p.Board == 1).IsBye);
        Assert.True(t.Pairings.Single(p => p.Board == 2).IsBye);
    }

    [Fact]
    public void BinaryTunx_GivesClearMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tunx");
        File.WriteAllBytes(path, new byte[] { 0x93, 0xFF, 0x89, 0x44, 0, 0, 0, 0, 0x4C, 0x54 });
        var ex = Assert.Throws<ParseException>(() => ParserFactory.Parse(path, new AppConfig()));
        Assert.Contains("Swiss-Manager", ex.Message);
    }
}
