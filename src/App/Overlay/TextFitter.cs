using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Render;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>Metni belirli genişliğe sığdırma sonucu.</summary>
public readonly record struct FitResult(string Text, float FontPt);

/// <summary>
/// Gömülü Türkçe fontu (DejaVu Sans) GDI+ ile ölçerek metni kutuya sığdırır:
/// önce fontu küçültür; yine sığmazsa moda göre "…" ile keser ya da adı kısaltır
/// (ör. "Mehmet Ali Yılmaz" → "M. A. Yılmaz" → "M. Yılmaz" → "Yılmaz").
/// Ölçüm ve baskı aynı fontla yapıldığından çıktı bire bir tutar.
/// </summary>
public static class TextFitter
{
    // Kalın yüz AYRI koleksiyonda: ikisi aynı koleksiyondayken GDI+ kalın dosyayı kullanmayıp
    // normal yüzü yapay kalınlaştırıyordu (PDF'teki gerçek kalından dar ve ince çıkıyordu).
    private static readonly PrivateFontCollection Fonts = LoadFonts(EmbeddedFonts.Regular());
    private static readonly PrivateFontCollection BoldFonts = LoadFonts(EmbeddedFonts.Bold());
    private static readonly FontFamily Family = ResolveFamily(Fonts);
    private static readonly FontFamily BoldFamily = ResolveFamily(BoldFonts);
    private static readonly Bitmap MeasureBmp = new(2, 2);

    private static PrivateFontCollection LoadFonts(byte[] data)
    {
        var pfc = new PrivateFontCollection();
        AddFont(pfc, data);
        return pfc;
    }

    private static void AddFont(PrivateFontCollection pfc, byte[] data)
    {
        // GDI+ font verisini kopyalar; yine de işaretçiyi uygulama ömrü boyunca tutmak güvenlidir.
        var ptr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
        System.Runtime.InteropServices.Marshal.Copy(data, 0, ptr, data.Length);
        pfc.AddMemoryFont(ptr, data.Length);
    }

    private static FontFamily ResolveFamily(PrivateFontCollection pfc)
        => pfc.Families.FirstOrDefault(f => f.Name == EmbeddedFonts.FamilyName) ?? pfc.Families[0];

    /// <summary>İstenen punto + kalınlıkta Font üretir (ölçüm/çizim için, birim = punto).</summary>
    public static Font CreateFont(float pt, bool bold)
        => bold ? new(BoldFamily, pt, BoldFamily.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point)
                : new(Family, pt, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>Tek satır metin genişliğini PUNTO cinsinden ölçer.</summary>
    public static float MeasureWidth(string text, float pt, bool bold)
    {
        using var g = Graphics.FromImage(MeasureBmp);
        g.PageUnit = GraphicsUnit.Point;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var font = CreateFont(pt, bold);
        return g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
    }

    /// <summary>
    /// Metni genişliği <paramref name="boxWidthPt"/>, yüksekliği <paramref name="boxHeightPt"/> olan
    /// kutuya sığdırır. İstenen punto üst sınırdır; en küçük punto alt sınırdır.
    /// </summary>
    public static FitResult Fit(string text, float boxWidthPt, float boxHeightPt,
        float maxPt, float minPt, bool bold, OverflowMode mode)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return new FitResult("", maxPt);

        // Yüksekliğe göre üst sınırı da kıs (satır yüksekliği ~ punto * 1.25).
        float capByHeight = boxHeightPt > 0 ? boxHeightPt / 1.2f : maxPt;
        float top = Math.Max(minPt, Math.Min(maxPt, capByHeight));
        float pad = 1.5f; // küçük iç boşluk (punto)
        float usableW = Math.Max(1, boxWidthPt - pad);

        // 1) Olduğu gibi, en büyük sığan punto
        var size = LargestFitting(text, usableW, top, minPt, bold);
        if (size is float s) return new FitResult(text, s);

        // 2) Sığmadı → moda göre
        switch (mode)
        {
            case OverflowMode.ShrinkThenAbbreviate:
                foreach (var cand in AbbreviationLadder(text).Skip(1))
                {
                    var cs = LargestFitting(cand, usableW, top, minPt, bold);
                    if (cs is float v) return new FitResult(cand, v);
                }
                return new FitResult(Ellipsize(text, usableW, minPt, bold), minPt);

            case OverflowMode.ShrinkThenEllipsis:
                return new FitResult(Ellipsize(text, usableW, minPt, bold), minPt);

            default: // Shrink: en küçük puntoda bırak (taşabilir/kırpılır)
                return new FitResult(text, minPt);
        }
    }

    private static float? LargestFitting(string text, float usableW, float top, float minPt, bool bold)
    {
        for (float pt = top; pt >= minPt - 0.01f; pt -= 0.5f)
            if (MeasureWidth(text, pt, bold) <= usableW)
                return pt;
        return null;
    }

    /// <summary>
    /// Giderek kısalan adaylar üretir. Soyad her zaman tam kalır:
    /// "YILMAZ, MEHMET ALİ" (chess-results biçimi) → "YILMAZ, M. A." → "YILMAZ, M." → "YILMAZ";
    /// "Mehmet Ali Yılmaz" → "M. A. Yılmaz" → "M. Yılmaz" → "Yılmaz".
    /// </summary>
    internal static IEnumerable<string> AbbreviationLadder(string name)
    {
        yield return name;

        int comma = name.IndexOf(',');
        if (comma > 0)
        {
            var surname = name[..comma].Trim();
            var given = name[(comma + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (surname.Length == 0 || given.Length == 0) yield break;
            yield return surname + ", " + string.Join(" ", given.Select(Initial));
            if (given.Length > 1) yield return surname + ", " + Initial(given[0]);
            yield return surname;
            yield break;
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) yield break;

        string last = parts[^1];
        var firsts = parts[..^1];

        // L2: ön adlar baş harf, soyad tam — "M. A. Yılmaz"
        yield return string.Join(" ", firsts.Select(Initial)) + " " + last;
        // L3: tek baş harf + soyad — "M. Yılmaz"
        yield return Initial(firsts[0]) + " " + last;
        // L4: yalnız soyad
        yield return last;
    }

    private static string Initial(string s)
        => s.Length == 0 ? s : char.ToUpper(s[0]) + ".";

    private static string Ellipsize(string text, float usableW, float pt, bool bold)
    {
        if (MeasureWidth(text, pt, bold) <= usableW) return text;
        for (int len = text.Length - 1; len >= 1; len--)
        {
            var cand = text[..len].TrimEnd() + "…";
            if (MeasureWidth(cand, pt, bold) <= usableW) return cand;
        }
        return "…";
    }
}
