using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

public class BoardRangeTests
{
    [Fact]
    public void Empty_ReturnsEmptySet()
    {
        Assert.Empty(BoardRange.Parse(null));
        Assert.Empty(BoardRange.Parse(""));
        Assert.Empty(BoardRange.Parse("   "));
    }

    [Fact]
    public void SingleAndCommaSeparated()
    {
        var set = BoardRange.Parse("3,7");
        Assert.Equal(new[] { 3, 7 }, set.OrderBy(x => x));
    }

    [Fact]
    public void Range_IsExpandedInclusive()
    {
        var set = BoardRange.Parse("12-15");
        Assert.Equal(new[] { 12, 13, 14, 15 }, set.OrderBy(x => x));
    }

    [Fact]
    public void Mixed_WithSpacesAndSemicolons()
    {
        var set = BoardRange.Parse(" 3, 7 ; 12-14 ");
        Assert.Equal(new[] { 3, 7, 12, 13, 14 }, set.OrderBy(x => x));
    }

    [Fact]
    public void ReversedRange_StillWorks()
    {
        var set = BoardRange.Parse("15-12");
        Assert.Equal(new[] { 12, 13, 14, 15 }, set.OrderBy(x => x));
    }

    [Fact]
    public void GarbageParts_AreIgnored()
    {
        var set = BoardRange.Parse("3, abc, , 7-, 9");
        Assert.Equal(new[] { 3, 9 }, set.OrderBy(x => x));
    }
}
