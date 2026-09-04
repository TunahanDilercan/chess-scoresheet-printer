namespace NotasyonOtomasyonu.Core;

/// <summary>Doğrulama sonucu: hatalar (üretimi engeller) ve uyarılar (bilgilendirme).</summary>
public sealed class ValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Parser çıktısını render öncesi denetler ve güvenli hâle getirir.
/// Amaç: bozuk/eksik veride çökmeden anlaşılır mesaj üretmek (bkz. edge case'ler).
/// </summary>
public static class Validation
{
    public static ValidationResult Validate(Tournament t)
    {
        var r = new ValidationResult();

        if (t.Pairings.Count == 0)
            r.Errors.Add("Dosyada hiç eşleştirme (masa) bulunamadı.");

        if (string.IsNullOrWhiteSpace(t.Name))
            r.Warnings.Add("Turnuva adı boş.");

        var seenBoards = new HashSet<int>();
        foreach (var p in t.Pairings)
        {
            if (p.Board <= 0)
                r.Warnings.Add($"Geçersiz masa numarası: {p.Board}.");
            else if (!seenBoards.Add(p.Board))
                r.Warnings.Add($"Tekrarlanan masa numarası: {p.Board}.");

            if (string.IsNullOrWhiteSpace(p.White.Name))
                r.Warnings.Add($"Masa {p.Board}: Beyaz oyuncu adı boş.");

            if (!p.IsBye && string.IsNullOrWhiteSpace(p.Black!.Name))
                r.Warnings.Add($"Masa {p.Board}: Siyah oyuncu adı boş.");
        }

        return r;
    }

    /// <summary>
    /// Render için güvenli kopya: masalar artan sıraya konur, BAY masaları config'e göre elenebilir.
    /// </summary>
    public static Tournament Normalize(Tournament t, bool keepByeSheets)
    {
        IEnumerable<Pairing> pairings = t.Pairings;
        if (!keepByeSheets)
            pairings = pairings.Where(p => !p.IsBye);

        var ordered = pairings
            .OrderBy(p => p.Board)
            .ToList();

        return t with { Pairings = ordered };
    }
}
