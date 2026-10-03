using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>TSF-TK Yarışma Yönergeleri Hazırlama ve Uygulama Prosedürü kuralları.</summary>
public class TsfRulesTests
{
    [Theory]
    [InlineData(3, 2, 30, true)]
    [InlineData(10, 5, 60, true)]
    [InlineData(35, 30, 150, true)]
    [InlineData(45, 30, 180, true)]
    [InlineData(90, 30, 300, true)]
    [InlineData(30, 0, 90, false)]   // 30 dk: tabloda yok → toplam süresi ≥ olan ilk tempo (20+10 → 90)
    [InlineData(15, 5, 75, false)]   // 20 dk → 15+10 (25 dk) → 75
    public void MinRoundGap_FromEkB(int min, int inc, int gap, bool exact)
    {
        var r = TsfRules.MinRoundGap(min, inc);
        Assert.Equal(gap, r.Gap);
        Assert.Equal(exact, r.Exact);
    }

    private static List<ScheduleItem> Isparta() => new()
    {
        new("3 Ekim 2026 Cumartesi", "09.00-09.30", "Kayıt Kontrol Başlangıç-Bitiş"),
        new("3 Ekim 2026 Cumartesi", "09.30", "Teknik Toplantı"),
        new("3 Ekim 2026 Cumartesi", "10.00", "1. Tur"),
        new("3 Ekim 2026 Cumartesi", "12.30", "2. Tur"),
        new("3 Ekim 2026 Cumartesi", "15.00", "3. Tur"),
        new("4 Ekim 2026 Pazar", "10.00", "4. Tur"),
        new("4 Ekim 2026 Pazar", "12.30", "5. Tur"),
    };

    [Fact]
    public void RealIspartaProgram_Passes()
    {
        var checks = TsfRules.CheckSchedule(Isparta(), (35, 30), elo: false, checkIn: "09.00-09.30");
        Assert.DoesNotContain(checks, c => c.Level == RuleLevel.Error);
        Assert.Contains(checks, c => c.Title == "Tur aralıkları uygun");
        Assert.Contains(checks, c => c.Title.StartsWith("Kayıt kontrol süresi 30 dk") && c.Level == RuleLevel.Ok);
    }

    [Fact]
    public void TooShortGap_TooManyRounds_LongDay_BadCheckIn_AreErrors()
    {
        var items = new List<ScheduleItem>
        {
            new("Gün 1", "08.00-08.15", "Kayıt Kontrol"),
            new("Gün 1", "09.00", "1. Tur"),
            new("Gün 1", "11.00", "2. Tur"),   // 120 dk < 150
            new("Gün 1", "13.30", "3. Tur"),
            new("Gün 1", "16.00", "4. Tur"),
            new("Gün 1", "18.30", "5. Tur"),   // 5. tur: UKD'de günde en fazla 4; gün ~12,7 saat
        };
        var checks = TsfRules.CheckSchedule(items, (35, 30), elo: false, checkIn: "08.00-08.15");
        var gap = Assert.Single(checks, c => c.Title.StartsWith("2. Tur 11.00"));
        Assert.Equal(RuleLevel.Error, gap.Level);
        Assert.Contains("en erken 11.30", gap.Detail);
        Assert.Equal(2, gap.ProgramRow);
        Assert.Contains(checks, c => c.Level == RuleLevel.Error && c.Title == "Gün 1: 5 tur");
        Assert.Contains(checks, c => c.Level == RuleLevel.Error && c.Title.Contains("saat sürüyor"));
        Assert.Contains(checks, c => c.Level == RuleLevel.Error && c.Title == "Kayıt kontrol süresi 15 dk");
        // ELO'da 4 tur da fazla
        var elo = TsfRules.CheckSchedule(items.Take(5).ToList(), (35, 30), elo: true, checkIn: null);
        Assert.Contains(elo, c => c.Level == RuleLevel.Error && c.Title == "Gün 1: 4 tur");
    }

    [Fact]
    public void SuggestSchedule_UsesMinimumGap_AndDailyLimits()
    {
        var plan = TsfRules.SuggestSchedule(5, 2, 10 * 60, (35, 30), elo: false);
        Assert.Equal(new[] { "10.00", "12.30", "15.00" }, plan.Where(p => p.Day == 0).Select(p => TsfRules.FormatTime(p.Start)));
        Assert.Equal(new[] { 4, 5 }, plan.Where(p => p.Day == 1).Select(p => p.Round));
        // Hızlı: 15+10 → 75 dk; 7 tur tek günde UKD sınırı 4 → kalanlar son güne yığılır (kontrol hata gösterir)
        var rapid = TsfRules.SuggestSchedule(4, 1, 10 * 60, (15, 10), elo: false);
        Assert.Equal(new[] { "10.00", "11.15", "12.30", "13.45" }, rapid.Select(p => TsfRules.FormatTime(p.Start)));
        // Önerilen program kontrolden geçer
        var items = plan.Select(p => new ScheduleItem($"Gün {p.Day + 1}", TsfRules.FormatTime(p.Start), $"{p.Round}. Tur")).ToList();
        Assert.DoesNotContain(TsfRules.CheckSchedule(items, (35, 30), false, null), c => c.Level == RuleLevel.Error);
    }

    [Theory]
    [InlineData(4, false, true, 6)]     // çift tur döner
    [InlineData(7, false, true, 7)]     // tek tur döner (7 → 7 tur, BAY'lı)
    [InlineData(8, false, true, 7)]
    [InlineData(20, false, false, 5)]
    [InlineData(50, false, false, 6)]
    [InlineData(100, false, false, 7)]
    [InlineData(300, false, false, 11)]
    [InlineData(12, true, false, 5)]    // takım 9-16 → 5 tur
    public void ExpectedSystem_FromEkC(int players, bool team, bool roundRobin, int rounds)
    {
        var e = TsfRules.ExpectedSystem(players, team);
        Assert.Equal(roundRobin, e.System.IsRoundRobin());
        Assert.Equal(rounds, e.Rounds);
    }

    [Fact]
    public void CheckCategory_FlagsSmallAndMismatched()
    {
        Assert.Equal(RuleLevel.Error, TsfRules.CheckCategory("7 Yaş", 4, TournamentSystem.RoundRobin, 6).Level);   // 4 ve altı iptal
        Assert.Equal(RuleLevel.Ok, TsfRules.CheckCategory("8 Yaş", 7, TournamentSystem.RoundRobin, 7).Level);
        Assert.Equal(RuleLevel.Ok, TsfRules.CheckCategory("Açık", 20, TournamentSystem.Swiss, 5).Level);
        Assert.Equal(RuleLevel.Warning, TsfRules.CheckCategory("Açık", 20, TournamentSystem.Swiss, 6).Level);
        Assert.Equal(RuleLevel.Warning, TsfRules.CheckCategory("10 Yaş", 6, TournamentSystem.Swiss, 5).Level); // 6 kişi → döner
        Assert.Equal(RuleLevel.Info, TsfRules.CheckCategory("12 Yaş", 0, TournamentSystem.Swiss, 5).Level);
    }

    [Theory]
    [InlineData("10 Yaş ve Altı", RuleLevel.Ok)]
    [InlineData("8 YAŞ VE ALTI KATEGORİSİ", RuleLevel.Ok)]
    [InlineData("9 Yaş ve Altı", RuleLevel.Warning)]
    [InlineData("7 Yaş Altı", RuleLevel.Warning)]
    public void AgeCategory_MustEndOnEvenAge(string name, RuleLevel level)
        => Assert.Equal(level, TsfRules.CheckAgeCategory(name)!.Level);

    [Fact]
    public void AgeCategory_SingleAgeOrOpen_NotChecked()
    {
        Assert.Null(TsfRules.CheckAgeCategory("9 Yaş Kategorisi"));
        Assert.Null(TsfRules.CheckAgeCategory("Açık Kategori"));
    }

    [Fact]
    public void TempoUnity_WarnsOnDifferentTempos()
    {
        Assert.Equal(RuleLevel.Ok, TsfRules.CheckTempoUnity(new List<(string, string?)>
            { ("A", "35'+30'' Eklemeli Tempo"), ("B", "35 dakika + hamle başına 30 saniye") })!.Level);
        var w = TsfRules.CheckTempoUnity(new List<(string, string?)> { ("A", "35'+30''"), ("B", "45'+30''"), ("C", null) })!;
        Assert.Equal(RuleLevel.Warning, w.Level);
        Assert.Contains("45'+30'': B", w.Detail);
        Assert.Null(TsfRules.CheckTempoUnity(new List<(string, string?)> { ("A", "35'+30''") }));
    }

    [Fact]
    public void EloTempo_FollowsEkA()
    {
        Assert.Null(TsfRules.CheckEloTempo(false, 2500, (35, 30)));                       // UKD etkinliği
        var strong = TsfRules.CheckEloTempo(true, 2450, (60, 30))!.Value;
        Assert.Equal(RuleLevel.Warning, strong.Check.Level);                               // 2400+ → 90+30
        Assert.Equal(2, strong.MaxPerDay);
        var normal = TsfRules.CheckEloTempo(true, 1900, (60, 30))!.Value;
        Assert.Equal(RuleLevel.Ok, normal.Check.Level);
        Assert.Equal(3, normal.MaxPerDay);
        // 2400+ etkinlikte 3 tur/gün program hatası
        var items = new List<ScheduleItem> { new("G1", "09.00", "1. Tur"), new("G1", "14.00", "2. Tur"), new("G1", "19.00", "3. Tur") };
        var checks = TsfRules.CheckSchedule(items, (90, 30), elo: true, checkIn: null, maxPerDayOverride: strong.MaxPerDay, maxPerDaySource: "EK-A");
        Assert.Contains(checks, c => c.Level == RuleLevel.Error && c.Title == "G1: 3 tur" && c.Source == "EK-A");
    }

    [Theory]
    [InlineData("Ulusal rating", false)]
    [InlineData("Uluslararası ELO ve Ulusal rating", true)]
    [InlineData("FIDE rating", true)]
    [InlineData(null, false)]
    public void IsElo_FromRatingCalculation(string? text, bool elo) => Assert.Equal(elo, TsfRules.IsElo(text));
}
