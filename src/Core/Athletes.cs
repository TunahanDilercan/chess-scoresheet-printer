namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Sporcu yaka kartı / isimlik için bir sporcu. <paramref name="Line3"/>: kulüp, okul ya da il (kartta
/// kategorinin altındaki satır). Puanı olmayan sporcuda ELO/UKD kutusu basılmaz ("0" yazılmaz).
/// </summary>
public sealed record Athlete(string Name, string Category = "", string Line3 = "", int? Elo = null, int? Ukd = null);

/// <summary>Sporcu yaka kartı ve isimlik ayarları (son seçimler hatırlanır).</summary>
public sealed class AthleteCardConfig
{
    public const string Badge = "Badge";   // yaka kartı: 95×135 mm, A4'e 2×2
    public const string Plate = "Plate";   // isimlik: 190 mm genişlik, A4'e 3 ya da 4

    /// <summary>Çıktı türü: <see cref="Badge"/> ya da <see cref="Plate"/>.</summary>
    public string Kind { get; set; } = Badge;

    /// <summary>Turnuva afişi (yaka kartının üstü, isimliğin şeridi).</summary>
    public string? BannerPath { get; set; }

    /// <summary>İsimlik için ayrı geniş şerit görseli (boşsa afiş kullanılır).</summary>
    public string? PlateBannerPath { get; set; }

    /// <summary>Afişin altından kırpılacak pay (yüzde, 0–45): alttaki süs/logo bandı kartta yer kaplamasın.</summary>
    public int CropBottomPercent { get; set; }

    /// <summary>Yaka kartında afişin en alt bandı kartın altına şerit olarak basılsın (Excel şablonundaki gibi).</summary>
    public bool FooterBand { get; set; }

    /// <summary>Yaka kartının altındaki iki yuvarlak logo (sol, sağ). Sağ boşsa (seçenek açıksa) TSF il logosu.</summary>
    public string? Logo1Path { get; set; }
    public string? Logo2Path { get; set; }
    public bool AutoTsfLogo { get; set; } = true;

    /// <summary>Çerçeve ve yazı renkleri (#RRGGBB).</summary>
    public string FrameColor { get; set; } = "#0070C0";
    public string TextColor { get; set; } = "#1F3864";

    /// <summary>İsimlik: sayfa başına 3 (190×64 mm) ya da 4 (190×68 mm).</summary>
    public int PlatesPerPage { get; set; } = 3;

    /// <summary>İsimlikte ELO / UKD kutuları.</summary>
    public bool ShowElo { get; set; } = true;
    public bool ShowUkd { get; set; } = true;

    /// <summary>chess-results'tan alırken unvan (IM, FM…) adın önüne yazılsın.</summary>
    public bool TitleInName { get; set; } = true;

    /// <summary>chess-results'tan alırken üçüncü satır: "Club" (kulüp/okul) ya da "Province" (il).</summary>
    public string Line3Source { get; set; } = "Club";

    /// <summary>Kesim köşe işaretleri.</summary>
    public bool CropMarks { get; set; } = true;
}
