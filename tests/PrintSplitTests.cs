using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

public class PrintSplitTests
{
    private static int[][] Split(int[] w, int k) => PrintSplit.Contiguous(w, k).Select(g => g.ToArray()).ToArray();

    [Fact]
    public void FourEqualCategories_TwoPrinters_TwoEach()
        => Assert.Equal(new[] { new[] { 0, 1 }, new[] { 2, 3 } }, Split(new[] { 20, 20, 20, 20 }, 2));

    [Fact]
    public void KeepsOrder_AndBalancesLoad()
    {
        // 40 | 10 10 10 10 → büyük kategori tek başına, diğerleri birlikte
        Assert.Equal(new[] { new[] { 0 }, new[] { 1, 2, 3, 4 } }, Split(new[] { 40, 10, 10, 10, 10 }, 2));
    }

    [Fact]
    public void ThreePrinters()
    {
        var w = new[] { 12, 30, 8, 16, 14, 20 };
        var g = Split(w, 3);
        Assert.Equal(3, g.Length);
        Assert.Equal(Enumerable.Range(0, 6), g.SelectMany(x => x));          // her kategori bir kez, sırayla
        Assert.Equal(42, g.Max(x => x.Sum(i => w[i])));                     // en iyi bölünme: 12+30 | 8+16+14 | 20
    }

    [Fact]
    public void FewerCategoriesThanPrinters_NoEmptyGroups()
        => Assert.Equal(new[] { new[] { 0 }, new[] { 1 } }, Split(new[] { 5, 7 }, 3));

    [Fact]
    public void SingleCategory_And_Empty()
    {
        Assert.Equal(new[] { new[] { 0 } }, Split(new[] { 50 }, 2));
        Assert.Empty(PrintSplit.Contiguous(Array.Empty<int>(), 2));
    }
}
