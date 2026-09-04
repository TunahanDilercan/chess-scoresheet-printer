using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

public class TournamentDatesTests
{
    [Fact]
    public void Single_Iso()
    {
        var d = TournamentDates.Parse("2026-06-14");
        Assert.Single(d);
        Assert.Equal(new DateTime(2026, 6, 14), d[0]);
    }

    [Fact]
    public void Single_Dotted()
    {
        var d = TournamentDates.Parse("14.06.2026");
        Assert.Single(d);
        Assert.Equal(new DateTime(2026, 6, 14), d[0]);
    }

    [Fact]
    public void Range_TwoFullDates()
    {
        var d = TournamentDates.Parse("14.06.2026 - 16.06.2026");
        Assert.Equal(3, d.Count);
        Assert.Equal(new DateTime(2026, 6, 14), d[0]);
        Assert.Equal(new DateTime(2026, 6, 16), d[^1]);
    }

    [Fact]
    public void Range_IsoTwoDates()
    {
        var d = TournamentDates.Parse("2026-06-14 ... 2026-06-15");
        Assert.Equal(2, d.Count);
    }

    [Fact]
    public void Range_Compact_DayDash()
    {
        var d = TournamentDates.Parse("14.-16.06.2026");
        Assert.Equal(3, d.Count);
        Assert.Equal(new DateTime(2026, 6, 14), d[0]);
        Assert.Equal(new DateTime(2026, 6, 16), d[^1]);
    }

    [Fact]
    public void Empty_ReturnsNone()
    {
        Assert.Empty(TournamentDates.Parse(null));
        Assert.Empty(TournamentDates.Parse("   "));
        Assert.Empty(TournamentDates.Parse("bilinmiyor"));
    }

    [Fact]
    public void DefaultPick_TodayInRange_PicksToday()
    {
        var today = DateTime.Today;
        var days = new List<DateTime> { today.AddDays(-1), today, today.AddDays(1) };
        Assert.Equal(today, TournamentDates.DefaultPick(days));
    }

    [Fact]
    public void DefaultPick_TodayNotInRange_PicksFirst()
    {
        var days = new List<DateTime> { new(2030, 1, 1), new(2030, 1, 2) };
        Assert.Equal(new DateTime(2030, 1, 1), TournamentDates.DefaultPick(days));
    }

    [Fact]
    public void DefaultPick_Empty_PicksToday()
        => Assert.Equal(DateTime.Today, TournamentDates.DefaultPick(new List<DateTime>()));
}
