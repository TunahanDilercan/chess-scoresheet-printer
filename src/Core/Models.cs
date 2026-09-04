namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Tek bir oyuncu. Eksik alanlar (rating, unvan, kulüp) null/0 olabilir; render bunları boş bırakır.
/// </summary>
public record Player(
    int? StartNo,        // Sıra no (SNo / StartNo)
    string Name,         // Ad Soyad (zorunlu)
    string? Title = null,// Unvan (GM/IM/FM…)
    int? Rating = null,  // ELO (0/boş olabilir)
    string? Federation = null,
    string? Club = null)
{
    /// <summary>"FM Ali Veli (12) 2300" gibi tek satır gösterim için yardımcı.</summary>
    public string DisplayLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title!.Trim());
        parts.Add(string.IsNullOrWhiteSpace(Name) ? "—" : Name.Trim());
        return string.Join(" ", parts);
    }

    /// <summary>BAY (bye) oyuncusunu temsil eden sahte oyuncu.</summary>
    public static Player Bye() => new(null, "BAY");
}

/// <summary>
/// Bir masadaki eşleştirme. <see cref="Black"/> null ise oyuncu BAY almıştır.
/// </summary>
public record Pairing(
    int Board,           // Masa no
    Player White,
    Player? Black,       // null = BAY (bye)
    string? Result = null, // genelde boş
    string? Category = null) // ait olduğu kategori (kısa: "A", "8 Yaş") — notasyona basmak için
{
    public bool IsBye => Black is null;
}

/// <summary>
/// Bir turun tüm bilgisi. Pairings her zaman masa no'ya göre artan sırada tutulur.
/// </summary>
public record Tournament(
    string Name,
    int RoundNo,
    IReadOnlyList<Pairing> Pairings,
    string? Location = null,
    string? Date = null,
    string? TimeControl = null,
    string? Arbiter = null);
