namespace NotasyonOtomasyonu.Online;

/// <summary>Federasyon listesindeki bir turnuva girişi (tnr + ad).</summary>
public record TournamentRef(int Tnr, string Name);

/// <summary>Bir etkinliğin bir kategorisi/grubu (ör. "A Kategorisi"). Her kategori ayrı tnr'dir.</summary>
public record CategoryRef(int Tnr, string Name, bool IsCurrent);

/// <summary>
/// Bir chess-results etkinlik (tnr) sayfasından çıkarılan üst bilgi:
/// ad, yer, hakem, tarih, tur sayısı ve tüm kategoriler.
/// </summary>
public record EventInfo(
    int Tnr,
    string Name,
    string? Location,
    string? Arbiter,
    string? Dates,
    string? TimeControl,
    int MaxRound,
    IReadOnlyList<CategoryRef> Categories);
