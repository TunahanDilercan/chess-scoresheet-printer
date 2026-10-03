namespace NotasyonOtomasyonu.Core;

/// <summary>
/// FIDE Berger tabloları (döner turnuva). Swiss-Manager/chess-results döner eşleştirmeyi bu
/// tablolarla yapar; burada eşleştirmeyi doğrulamak ve tur yapısını çözmek için kullanılır.
///
/// Kurallar (n çift; tekse n+1 hayalî oyuncu = BAY eklenir):
///  • Tur sayısı n−1; her oyuncu her turda tam bir kez oynar, her ikili turnuvada bir kez karşılaşır.
///  • n numaralı oyuncu sabittir, 1. masada oynar. r. turdaki rakibi
///    p = (r tek) ? (r+1)/2 : n/2 + r/2. Tek turlarda p beyaz, çift turlarda n beyazdır.
///  • Diğer masalar k = 1 … n/2−1 için (p+k) – (p−k) çiftleridir (1…n−1 aralığında döngüsel);
///    beyaz her zaman p+k'dir. Böylece numaralar her tur n/2 adım döner ve renkler dengelenir.
/// </summary>
public static class BergerTable
{
    /// <summary>Bir masadaki eşleşme: oyuncuların eşleştirme (kura) numaraları.</summary>
    public readonly record struct Game(int Board, int White, int Black);

    /// <summary>Oyuncu sayısı için tur sayısı (tek sayıda BAY'lı tur da sayılır).</summary>
    public static int RoundCount(int players) => players < 2 ? 0 : (players % 2 == 0 ? players - 1 : players);

    /// <summary>
    /// <paramref name="players"/> kişilik turnuvanın tüm turları. Tek sayıda oyuncuda hayalî
    /// oyuncuyla (n+1) eşleşen BAY alır; o masa listede yer almaz.
    /// </summary>
    public static List<List<Game>> Generate(int players)
    {
        var rounds = new List<List<Game>>();
        if (players < 2) return rounds;
        int n = players % 2 == 0 ? players : players + 1;
        int m = n - 1;
        int Wrap(int x) => ((x - 1) % m + m) % m + 1; // 1..m

        for (int r = 1; r <= n - 1; r++)
        {
            var games = new List<Game>();
            int p = r % 2 == 1 ? (r + 1) / 2 : n / 2 + r / 2;
            int board = 1;
            var first = r % 2 == 1 ? (p, n) : (n, p);
            if (first.Item1 <= players && first.Item2 <= players)
                games.Add(new Game(board++, first.Item1, first.Item2));
            for (int k = 1; k <= n / 2 - 1; k++)
            {
                int w = Wrap(p + k), b = Wrap(p - k);
                if (w <= players && b <= players) games.Add(new Game(board++, w, b));
            }
            rounds.Add(games);
        }
        return rounds;
    }

    /// <summary>
    /// Döner turnuvanın tur yapısını denetler: bir turda aynı oyuncu iki kez oynamıyor ve
    /// iki oyuncu birden fazla karşılaşmıyor mu? Hata yoksa boş liste.
    /// </summary>
    public static List<string> Validate(IReadOnlyList<IReadOnlyList<(string White, string Black)>> rounds)
    {
        var errors = new List<string>();
        var met = new HashSet<(string, string)>();
        for (int r = 0; r < rounds.Count; r++)
        {
            var seen = new HashSet<string>();
            foreach (var (w, b) in rounds[r])
            {
                if (!seen.Add(w)) errors.Add($"{r + 1}. tur: {w} iki kez eşlenmiş.");
                if (!seen.Add(b)) errors.Add($"{r + 1}. tur: {b} iki kez eşlenmiş.");
                var key = string.CompareOrdinal(w, b) < 0 ? (w, b) : (b, w);
                if (!met.Add(key)) errors.Add($"{w} – {b} birden fazla kez karşılaşıyor ({r + 1}. tur).");
            }
        }
        return errors;
    }

    /// <summary>
    /// Döner turnuvada basılacak tur: sonucu girilmemiş oyunu olan İLK tur (oynanacak tur).
    /// Hiç oynanmadıysa 1, hepsi bittiyse son tur.
    /// </summary>
    public static int CurrentRound(IReadOnlyList<IReadOnlyList<string?>> resultsByRound)
    {
        for (int r = 0; r < resultsByRound.Count; r++)
            if (resultsByRound[r].Any(string.IsNullOrWhiteSpace)) return r + 1;
        return Math.Max(1, resultsByRound.Count);
    }
}
