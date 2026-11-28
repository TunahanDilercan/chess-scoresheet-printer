using System.Runtime.CompilerServices;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Grup kutusu başlığı tek satırda kalır: sığmazsa sonu "…" ile kısalır (tamamı ipucunda ve ekran okuyucuda).
/// WinForms uzun başlığı iki satıra kaydırıp kutunun içindeki tablonun üstüne bindiriyordu.
/// </summary>
internal static class GroupCaption
{
    private static readonly ConditionalWeakTable<GroupBox, StrongBox<string>> Full = new();
    private static readonly ToolTip Tip = new();

    public static void Set(GroupBox g, string text)
    {
        if (!Full.TryGetValue(g, out var box))
        {
            box = new StrongBox<string>(text);
            Full.Add(g, box);
            g.SizeChanged += (_, _) => Fit(g);
            g.FontChanged += (_, _) => Fit(g);
        }
        box.Value = text;
        Fit(g);
    }

    /// <summary>Başlığın tamamı (kısaltılmış olsa da).</summary>
    public static string FullText(GroupBox g) => Full.TryGetValue(g, out var b) && b.Value is { } t ? t : g.Text;

    internal static string Fitted(string text, Font font, int maxWidth)
    {
        static int W(string s, Font f) => TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        if (W(text, font) <= maxWidth) return text;
        var s = text;
        while (s.Length > 1 && W(s.TrimEnd() + "…", font) > maxWidth) s = s[..^1];
        return s.TrimEnd(' ', '—', '•', '·', '(', ',', '-') + "…";
    }

    private static void Fit(GroupBox g)
    {
        var text = FullText(g);
        int max = Math.Max(40, g.Width - g.LogicalToDeviceUnits(22));   // başlık kutunun sol ve sağ çizgisi arasında
        var fitted = Fitted(text, g.Font, max);
        if (g.Text != fitted) g.Text = fitted;
        g.AccessibleName = text;
        Tip.SetToolTip(g, fitted == text ? null : text);
    }
}
