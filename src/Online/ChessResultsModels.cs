namespace NotasyonOtomasyonu.Online;

/// <summary>Federasyon listesindeki bir turnuva girişi (tnr + ad).</summary>
public record TournamentRef(int Tnr, string Name);

/// <summary>Bir etkinliğin bir kategorisi/grubu (ör. "A Kategorisi"). Her kategori ayrı tnr'dir.</summary>
public record CategoryRef(int Tnr, string Name, bool IsCurrent);

/// <summary>
/// Bir chess-results etkinlik (tnr) sayfasından çıkarılan üst bilgi:
/// ad, tarih, tur sayısı, eşlenmiş son tur ve tüm kategoriler.
/// </summary>
/// <param name="MaxRound">Toplam tur sayısı (bilinmiyorsa makul bir üst sınır).</param>
/// <param name="CurrentRound">Eşleştirmesi yayımlanmış son tur; henüz hiç tur yoksa 0.</param>
public record EventInfo(
    int Tnr,
    string Name,
    string? Dates,
    int MaxRound,
    int CurrentRound,
    IReadOnlyList<CategoryRef> Categories);
