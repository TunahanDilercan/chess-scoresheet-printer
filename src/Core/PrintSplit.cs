namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Çoklu yazıcı: kategoriler bölünmeden ve sırası bozulmadan yazıcılara dağıtılır; en çok kâğıt basan yazıcının
/// yükü en aza indirilir (ör. 4 kategori 2 yazıcıya: ilk ikisi birine, son ikisi diğerine).
/// </summary>
public static class PrintSplit
{
    /// <summary>
    /// <paramref name="weights"/> (kategori başına kâğıt) sırayla en çok <paramref name="parts"/> ardışık gruba bölünür.
    /// Dönen her grup kategori sıra numaralarıdır; boş grup dönmez (kategori sayısı yazıcıdan azsa daha az grup).
    /// </summary>
    public static List<List<int>> Contiguous(IReadOnlyList<int> weights, int parts)
    {
        int n = weights.Count;
        var result = new List<List<int>>();
        if (n == 0) return result;
        int k = Math.Clamp(parts, 1, n);
        var prefix = new long[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + Math.Max(0, weights[i]);
        long Sum(int a, int b) => prefix[b] - prefix[a];   // [a, b)

        // best[j, i]: ilk i kategoriyi j yazıcıya bölünce en yüklü yazıcının kâğıdı; cut: son grubun başı
        var best = new long[k + 1, n + 1];
        var cut = new int[k + 1, n + 1];
        for (int i = 0; i <= n; i++) best[1, i] = Sum(0, i);
        for (int j = 2; j <= k; j++)
            for (int i = 0; i <= n; i++)
            {
                best[j, i] = long.MaxValue;
                for (int c = j - 1; c <= i; c++)
                {
                    long v = Math.Max(best[j - 1, c], Sum(c, i));
                    if (v < best[j, i]) { best[j, i] = v; cut[j, i] = c; }
                }
            }

        int end = n;
        for (int j = k; j >= 1; j--)
        {
            int start = j == 1 ? 0 : cut[j, end];
            result.Insert(0, Enumerable.Range(start, end - start).ToList());
            end = start;
        }
        return result.Where(g => g.Count > 0).ToList();
    }
}
