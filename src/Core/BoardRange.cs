namespace NotasyonOtomasyonu.Core;

/// <summary>
/// "3,7,12-15" gibi masa numarası ifadelerini ayrıştırır. Boşluk/noktalı virgül de kabul edilir.
/// Geçersiz parçalar sessizce atlanır (kullanıcı serbest yazabilsin).
/// </summary>
public static class BoardRange
{
    public static HashSet<int> Parse(string? text)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var rawPart in text.Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            int dash = part.IndexOf('-');
            if (dash > 0)
            {
                var a = part[..dash].Trim();
                var b = part[(dash + 1)..].Trim();
                if (int.TryParse(a, out var lo) && int.TryParse(b, out var hi))
                {
                    if (lo > hi) (lo, hi) = (hi, lo);
                    for (int i = lo; i <= hi && i - lo < 1000; i++) result.Add(i);
                }
            }
            else if (int.TryParse(part, out var n))
            {
                result.Add(n);
            }
        }
        return result;
    }
}
