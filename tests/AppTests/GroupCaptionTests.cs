using NotasyonOtomasyonu.App;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Grup başlığı tek satırda kalır; sığmazsa "…" ile kısalır, tamamı erişilebilir adda.</summary>
public class GroupCaptionTests
{
    private const string Long = "3) Kayıtlı sporcular (TSF, kayıtlar bitti) • eşleştirme henüz yok";

    [Fact]
    public void ShortText_IsUnchanged()
    {
        using var f = new Font("Segoe UI", 9f);
        Assert.Equal("3) Basılacak masalar", GroupCaption.Fitted("3) Basılacak masalar", f, 400));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(250)]
    [InlineData(300)]
    public void LongText_FitsOnOneLine(int width)
    {
        using var f = new Font("Segoe UI", 9f);
        var s = GroupCaption.Fitted(Long, f, width);
        Assert.EndsWith("…", s);
        Assert.True(TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width <= width);
        Assert.StartsWith(s.TrimEnd('…').TrimEnd(), Long);
    }

    [Fact]
    public void GroupBox_KeepsFullTextAccessible_AndRefitsOnResize()
    {
        using var g = new GroupBox { Width = 300, Font = new Font("Segoe UI", 9f) };
        GroupCaption.Set(g, Long);
        Assert.EndsWith("…", g.Text);
        Assert.Equal(Long, g.AccessibleName);
        g.Width = 900;
        Assert.Equal(Long, g.Text);
    }
}
