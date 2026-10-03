using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>
/// Kategori bazlı turnuva sistemleri: Berger (döner), İsviçre ve takım. TestData'daki sayfalar
/// canlı chess-results'tan alınmıştır (Isparta Ekim 2026: 8 Yaş döner, 7 Yaş İsviçre; Süper Lig takım).
/// </summary>
public class SystemsTests
{
    private static string Load(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name), System.Text.Encoding.UTF8);

    // ---------------- Berger tablosu ----------------

    [Fact]
    public void Berger_SixPlayers_MatchesFideTable()
    {
        var r = BergerTable.Generate(6);
        Assert.Equal(5, r.Count);
        Assert.Equal(new[] { (1, 6), (2, 5), (3, 4) }, r[0].Select(g => (g.White, g.Black)));
        Assert.Equal(new[] { (6, 4), (5, 3), (1, 2) }, r[1].Select(g => (g.White, g.Black)));
        Assert.Equal(new[] { (2, 6), (3, 1), (4, 5) }, r[2].Select(g => (g.White, g.Black)));
        Assert.Equal(new[] { (6, 5), (1, 4), (2, 3) }, r[3].Select(g => (g.White, g.Black)));
        Assert.Equal(new[] { (3, 6), (4, 2), (5, 1) }, r[4].Select(g => (g.White, g.Black)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(13)]
    public void Berger_EveryoneOncePerRound_EveryPairOnce_ColoursBalanced(int n)
    {
        var rounds = BergerTable.Generate(n);
        Assert.Equal(BergerTable.RoundCount(n), rounds.Count);

        var asNames = rounds.Select(r => (IReadOnlyList<(string, string)>)r.Select(g => (g.White.ToString(), g.Black.ToString())).ToList()).ToList();
        Assert.Empty(BergerTable.Validate(asNames));
        Assert.Equal(n * (n - 1) / 2, rounds.Sum(r => r.Count)); // her ikili tam bir kez

        // Renk dengesi: her oyuncunun beyaz sayısı ile siyah sayısı farkı en fazla 1 (FIDE Berger)
        for (int p = 1; p <= n; p++)
        {
            int w = rounds.Sum(r => r.Count(g => g.White == p));
            int b = rounds.Sum(r => r.Count(g => g.Black == p));
            Assert.InRange(Math.Abs(w - b), 0, 1);
        }
    }

    [Fact]
    public void Berger_CurrentRound_IsFirstUnfinished()
    {
        var res = new List<IReadOnlyList<string?>>
        {
            new[] { "1 - 0", "½ - ½" }, new[] { "0 - 1", null }, new string?[] { null, null }
        };
        Assert.Equal(2, BergerTable.CurrentRound(res));
        Assert.Equal(1, BergerTable.CurrentRound(new List<IReadOnlyList<string?>> { new string?[] { null } }));
        Assert.Equal(1, BergerTable.CurrentRound(new List<IReadOnlyList<string?>> { new[] { "1 - 0" } }));
    }

    // ---------------- Döner turnuva sayfası ----------------

    [Fact]
    public void RoundRobinEvent_TypeAndRoundCount()
    {
        var info = ChessResultsParser.ParseEventInfo(Load("roundrobin_event.html"), 1510313);
        Assert.Equal(TournamentSystem.RoundRobin, info.System);
        Assert.Equal(5, info.MaxRound); // "Tur Sayısı" (menüde tur linki yok)
    }

    /// <summary>
    /// Asıl hata: chess-results döner turnuvada TÜM turları tek sayfada verir; eskiden 15 masa
    /// (5 tur × 3) "2. tur" sanılıyor, masa 1-2-3 beşer kez tekrar ediyordu.
    /// </summary>
    [Fact]
    public void RoundRobinPairings_OnlyRequestedRound_UniqueBoards()
    {
        var html = Load("roundrobin_all_rounds.html");
        for (int round = 1; round <= 5; round++)
        {
            var t = ChessResultsParser.ParsePairings(html, 1510313, round);
            Assert.Equal(TournamentSystem.RoundRobin, t.System);
            Assert.Equal(3, t.Pairings.Count);
            Assert.Equal(new[] { 1, 2, 3 }, t.Pairings.Select(p => p.Board));
        }
    }

    [Fact]
    public void RoundRobinPairings_FollowFideBergerTable()
    {
        var html = Load("roundrobin_all_rounds.html");
        var berger = BergerTable.Generate(6);
        var rounds = new List<IReadOnlyList<(string, string)>>();
        for (int round = 1; round <= 5; round++)
        {
            var t = ChessResultsParser.ParsePairings(html, 1510313, round);
            Assert.Equal(berger[round - 1].Select(g => (g.White, g.Black)),
                         t.Pairings.Select(p => (p.White.StartNo!.Value, p.Black!.StartNo!.Value)));
            rounds.Add(t.Pairings.Select(p => (p.White.Name, p.Black!.Name)).ToList());
        }
        Assert.Empty(BergerTable.Validate(rounds));
    }

    [Fact]
    public void RoundRobinSections_CurrentRoundFromResults()
    {
        var sections = ChessResultsParser.ParseRoundSections(Load("roundrobin_all_rounds.html"))
            .OrderBy(s => s.Round).ToList();
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, sections.Select(s => s.Round));
        int current = BergerTable.CurrentRound(sections.Select(s => (IReadOnlyList<string?>)s.Pairings.Select(p => p.Result).ToList()).ToList());
        Assert.Equal(2, current); // 1. tur oynanmış, 2. tur sırada
    }

    // ---------------- İsviçre ----------------

    [Fact]
    public void SwissEvent_TypeCurrentAndMax()
    {
        var info = ChessResultsParser.ParseEventInfo(Load("swiss_event_round2.html"), 1510312);
        Assert.Equal(TournamentSystem.Swiss, info.System);
        Assert.Equal(2, info.CurrentRound);
        Assert.Equal(5, info.MaxRound);
    }

    // ---------------- Takım ----------------

    [Fact]
    public void TeamBoards_LabelsTeamsAndColours()
    {
        var t = ChessResultsParser.ParsePairings(Load("team_boards_rd1.html"), 1469049, 1);
        Assert.True(t.System.IsTeam());
        Assert.Equal(48, t.Pairings.Count);                          // 8 maç × 6 masa
        Assert.Equal(Enumerable.Range(1, 48), t.Pairings.Select(p => p.Board)); // tur içinde tekil
        Assert.Equal("1.1", t.Pairings[0].BoardLabel);
        Assert.Equal("1.1", t.Pairings[0].BoardText);

        var b11 = t.Pairings[0];
        Assert.Equal("ERDOĞMUŞ, YAĞIZ KAAN", b11.White.Name);
        Assert.Equal("TÜRK HAVA YOLLARI SPOR KULÜBÜ", b11.WhiteTeam);
        Assert.Equal("HUKUKÇU FENERBAHÇELİLER SPOR KULÜBÜ", b11.BlackTeam);

        // 1.2'de renk işaretine göre deplasman takımı beyaz
        var b12 = t.Pairings[1];
        Assert.Equal("ŞANAL, VAHAP", b12.White.Name);
        Assert.Equal("HUKUKÇU FENERBAHÇELİLER SPOR KULÜBÜ", b12.WhiteTeam);
        Assert.Equal("SAFARLI, ELTAJ", b12.Black!.Name);
        Assert.Equal("½ - ½", b12.Result);
    }

    [Theory]
    [InlineData("Isviçre sistemi", TournamentSystem.Swiss)]
    [InlineData("Doner turnuva", TournamentSystem.RoundRobin)]
    [InlineData("Takımlar için doner turnuva", TournamentSystem.TeamRoundRobin)]
    [InlineData("Takımlar için İsviçre Sistemi", TournamentSystem.TeamSwiss)]
    [InlineData("Swiss-System", TournamentSystem.Swiss)]
    [InlineData("Round robin", TournamentSystem.RoundRobin)]
    [InlineData("", TournamentSystem.Unknown)]
    public void ParseSystem_RecognisesTypes(string text, TournamentSystem expected)
        => Assert.Equal(expected, ChessResultsParser.ParseSystem(text));

    // Arama sayfası adları 50 karakterde keser: aynı etkinliğin kategorileri tek girişe insin.
    [Theory]
    [InlineData("KONYA 30 AĞUSTOS ZAFER BAYRAMI SATRANÇ TURNUVASIAÇ", "KONYA 30 AĞUSTOS ZAFER BAYRAMI SATRANÇ TURNUVASI")]
    [InlineData("KONYA 30 AĞUSTOS ZAFER BAYRAMI SATRANÇ TURNUVASI16", "KONYA 30 AĞUSTOS ZAFER BAYRAMI SATRANÇ TURNUVASI")]
    [InlineData("KONYA AĞUSTOS AYI HIZLI SATRANÇ TURNUVASIAÇIK", "KONYA AĞUSTOS AYI HIZLI SATRANÇ TURNUVASI")]
    [InlineData("TRABZON YILDIRIM SATRANÇ İL BİRİNCİLİĞİ", "TRABZON YILDIRIM SATRANÇ İL BİRİNCİLİĞİ")]
    [InlineData("Isparta Ekim Ayı UKD Satranç Turnuvası", "Isparta Ekim Ayı UKD Satranç Turnuvası")]
    public void SearchResultNames_TruncatedCategoryCut(string input, string expected)
        => Assert.Equal(expected, ChessResultsService.TrimTruncatedName(input));

    [Fact]
    public void TemplateFor_UsesSystemMapping()
    {
        var cfg = new AppConfig();
        var main = new OverlayTemplate { Name = "Ana", Fields = { new OverlayField() } };
        var rr = new OverlayTemplate { Name = "Berger kağıdı", Fields = { new OverlayField() } };
        cfg.Templates.AddRange(new[] { main, rr });
        cfg.Overlay = main;
        cfg.TemplateBySystem["RoundRobin"] = "Berger kağıdı";

        Assert.Same(rr, cfg.TemplateFor(TournamentSystem.RoundRobin));
        Assert.Same(main, cfg.TemplateFor(TournamentSystem.Swiss));
        Assert.Same(main, cfg.TemplateFor(TournamentSystem.TeamSwiss)); // eşleme yok → etkin
    }
}
