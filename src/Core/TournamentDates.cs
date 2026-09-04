using System.Globalization;
using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Turnuva tarih ifadelerini (tek gün veya aralık) ayrı günlere çevirir ve tek tarih seçer.
/// chess-results çeşitli biçimler verebilir: "2026-06-14", "14.06.2026",
/// "14.06.2026 - 16.06.2026", "14.-16.06.2026" vb.
/// </summary>
public static class TournamentDates
{
    public static string Format(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Türkçe gün adıyla: "14.06.2026 Cumartesi".</summary>
    public static string FormatLong(DateTime d)
        => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " " +
           new CultureInfo("tr-TR").DateTimeFormat.GetDayName(d.DayOfWeek);

    /// <summary>İfadedeki tüm günleri (aralık ise gün gün açılmış) sıralı döndürür.</summary>
    public static List<DateTime> Parse(string? text)
    {
        var result = new List<DateTime>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var s = text.Trim();

        // 1) Tam tarihler: yyyy-MM-dd / dd.MM.yyyy (/ . - ayraçlı). Önce bunlar.
        var dates = new List<DateTime>();
        foreach (Match m in Regex.Matches(s, @"(?<!\d)\d{4}[-/.]\d{1,2}[-/.]\d{1,2}(?!\d)"))
            if (TryExact(m.Value, ymd: true, out var d)) dates.Add(d);
        foreach (Match m in Regex.Matches(s, @"(?<!\d)\d{1,2}[-/.]\d{1,2}[-/.]\d{4}(?!\d)"))
            if (TryExact(m.Value, ymd: false, out var d)) dates.Add(d);
        dates = dates.Distinct().OrderBy(x => x).ToList();
        if (dates.Count >= 2) { AddRange(result, dates[0], dates[^1]); return result; }

        // 2) Kompakt gün aralığı: "14.-16.06.2026" / "14-16.06.2026" (tek tam tarih varken)
        var c = Regex.Match(s, @"(?<!\d)(\d{1,2})\s*\.?\s*[-–]\s*(\d{1,2})\s*\.\s*(\d{1,2})\s*\.\s*(\d{4})");
        if (c.Success &&
            TryMake(int.Parse(c.Groups[4].Value), int.Parse(c.Groups[3].Value), int.Parse(c.Groups[1].Value), out var ca) &&
            TryMake(int.Parse(c.Groups[4].Value), int.Parse(c.Groups[3].Value), int.Parse(c.Groups[2].Value), out var cb))
        {
            AddRange(result, ca, cb);
            if (result.Count > 0) return result;
        }

        // 3) Tek tam tarih
        if (dates.Count == 1) result.Add(dates[0]);
        return result;
    }

    /// <summary>Varsayılan tek tarih: bugün turnuva günlerindense bugün; değilse ilk gün; gün yoksa bugün.</summary>
    public static DateTime DefaultPick(IReadOnlyList<DateTime> days)
    {
        var today = DateTime.Today;
        if (days.Count == 0) return today;
        if (days.Any(d => d.Date == today)) return today;
        return days[0];
    }

    private static void AddRange(List<DateTime> into, DateTime a, DateTime b)
    {
        if (b < a) (a, b) = (b, a);
        for (var d = a; d <= b && (d - a).TotalDays < 120; d = d.AddDays(1)) into.Add(d);
    }

    private static bool TryMake(int y, int m, int d, out DateTime dt)
    {
        try { dt = new DateTime(y, m, d); return true; } catch { dt = default; return false; }
    }

    private static bool TryExact(string token, bool ymd, out DateTime dt)
    {
        var t = token.Replace('/', '.').Replace('-', '.');
        var fmts = ymd
            ? new[] { "yyyy.M.d", "yyyy.MM.dd" }
            : new[] { "d.M.yyyy", "dd.MM.yyyy" };
        return DateTime.TryParseExact(t, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
    }
}
