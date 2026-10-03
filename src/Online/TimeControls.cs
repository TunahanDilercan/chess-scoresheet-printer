using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>Turnuva temposu (FIDE Satranç Kuralları, Ek A/B: hızlı ve yıldırım satranç tanımları).</summary>
public enum Tempo { Klasik, Hizli, Yildirim }

/// <summary>
/// Düşünme süresi önerileri ve sınıflandırması. FIDE tanımı: 60 hamle için oyuncu başına toplam süre
/// (temel süre + 60 × artış) 60 dakika ve üzeri klasik (standart), 10 dakikadan fazla ve 60 dakikadan
/// az hızlı (rapid), 10 dakika ve altı yıldırım (blitz).
/// </summary>
public static class TimeControls
{
    public static string DisplayName(this Tempo t) => t switch
    {
        Tempo.Klasik => "Klasik (Standart)",
        Tempo.Hizli => "Hızlı (Rapid)",
        _ => "Yıldırım (Blitz)"
    };

    /// <summary>Tempoya göre resmi/yaygın süreler; ilki önerilendir.</summary>
    public static IReadOnlyList<(int Minutes, int Increment)> Presets(Tempo t) => t switch
    {
        // 90'+30'' FIDE derecelendirmeli klasik; 60'+30'' ve 45'+30'' TSF yaş grupları/il birinciliklerinde yaygın
        Tempo.Klasik => new[] { (90, 30), (60, 30), (45, 30), (35, 30), (90, 0) },
        Tempo.Hizli => new[] { (15, 10), (10, 5), (25, 10), (15, 5), (10, 10) },
        _ => new[] { (3, 2), (5, 3), (3, 0), (5, 0) }
    };

    /// <summary>Oyuncu başına 60 hamlelik toplam süre (dakika).</summary>
    public static double Total(int minutes, int increment) => minutes + increment; // 60 × artış(sn) / 60

    public static Tempo Classify(int minutes, int increment)
    {
        double total = Total(minutes, increment);
        return total >= 60 ? Tempo.Klasik : total > 10 ? Tempo.Hizli : Tempo.Yildirim;
    }

    /// <summary>"90'+30''" kısa yazımı.</summary>
    public static string Short(int minutes, int increment) => increment > 0 ? $"{minutes}'+{increment}''" : $"{minutes}'";

    /// <summary>Yönergedeki yazım: "90 DAKİKA + HAMLE BAŞINA 30 SANİYE EKLEMELİ TEMPO".</summary>
    public static string Format(int minutes, int increment)
        => increment > 0 ? $"{minutes} DAKİKA + HAMLE BAŞINA {increment} SANİYE EKLEMELİ TEMPO" : $"{minutes} DAKİKA";

    /// <summary>"35'+30''", "90 DAKİKA + HAMLE BAŞINA 30 SANİYE", "15 dk + 10 sn" → (35, 30); bulunamazsa null.</summary>
    public static (int Minutes, int Increment)? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = EventGrouping.Fold(s); // "DAKİKA", "BAŞINA" → "dakika", "basina"
        var m = Regex.Match(s, @"(\d{1,3})\s*(?:'|dakika|dk\.?|min)\s*(?:\+|ve)?\s*(?:hamle\s+basina\s*)?(\d{1,2})\s*(?:''|""|saniye|sn\.?|sec)");
        if (m.Success) return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        var m2 = Regex.Match(s, @"(\d{1,3})\s*(?:'|dakika|dk\.?|min)");
        return m2.Success ? (int.Parse(m2.Groups[1].Value), 0) : null;
    }

    /// <summary>chess-results "Zaman kontrolü (Rapid)" etiketinden tempo; yoksa null.</summary>
    public static Tempo? FromLabel(string? label)
    {
        var f = EventGrouping.Fold(label ?? "");
        if (f.Contains("blitz") || f.Contains("yildirim")) return Tempo.Yildirim;
        if (f.Contains("rapid") || f.Contains("hizli")) return Tempo.Hizli;
        if (f.Contains("standard") || f.Contains("klasik") || f.Contains("standart")) return Tempo.Klasik;
        return null;
    }
}
