using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// Sporcu yaka kartı / isimlik listesi: chess-results başlangıç listelerinden (ELO, UKD ve il TSF kayıt
/// listesiyle eşleştirilerek) ya da doğrudan TSF kayıt listesinden.
/// </summary>
public static class AthleteLists
{
    /// <param name="lists">Kategori adı → başlangıç listesi.</param>
    /// <param name="registrations">TSF kayıt listesi (ELO/UKD/il için; yoksa boş).</param>
    /// <param name="province">Ad → il (kayıt listesi + elle düzeltmeler); yoksa null.</param>
    /// <param name="titleInName">Unvan (IM, FM…) adın önüne yazılsın.</param>
    /// <param name="line3Province">Üçüncü satır il olsun (kapalıysa kulüp/okul; kulüp yoksa il).</param>
    public static List<Athlete> FromStartingLists(IEnumerable<(string Category, IReadOnlyList<Player> Players)> lists,
        IReadOnlyList<PlayerRegistration>? registrations, Func<string, string?>? province, bool titleInName, bool line3Province)
    {
        var all = lists.ToList();
        var roster = all.SelectMany(l => l.Players.Select(p => new RosterPlayer(p.Name, p.Rating, l.Category))).ToList();
        var byPlayer = new Dictionary<RosterPlayer, PlayerRegistration>(ReferenceEqualityComparer.Instance);
        if (registrations is { Count: > 0 } && roster.Count > 0)
            foreach (var m in ProvinceMatcher.MatchAll(roster, registrations))
                if (m.Kind is MatchKind.Exact or MatchKind.Probable && m.Entry is not null) byPlayer[m.Player] = m.Entry;

        var result = new List<Athlete>();
        int k = 0;
        foreach (var (cat, players) in all)
            foreach (var p in players)
            {
                var rp = roster[k++];
                byPlayer.TryGetValue(rp, out var entry);
                var prov = province?.Invoke(p.Name) ?? (entry is null ? null : Provinces.Print(entry.Province)) ?? p.Province;
                var club = string.IsNullOrWhiteSpace(p.Club) ? null : p.Club!.Trim();
                var line3 = line3Province ? prov ?? "" : club ?? prov ?? "";
                var display = DisplayName(p.Name);
                var name = titleInName && !string.IsNullOrWhiteSpace(p.Title) ? $"{p.Title!.Trim()} {display}" : display;
                int? elo = entry?.Elo is > 0 ? entry.Elo : p.Rating is > 0 ? p.Rating : null;
                int? ukd = entry?.Ukd is > 0 ? entry.Ukd : null;
                result.Add(new Athlete(name, cat, line3, elo, ukd));
            }
        return result;
    }

    /// <summary>chess-results'taki "SOYAD, AD" → kartta "AD SOYAD" (virgülsüz adlar olduğu gibi).</summary>
    public static string DisplayName(string name)
    {
        var n = (name ?? "").Trim();
        int comma = n.IndexOf(',');
        if (comma <= 0 || comma == n.Length - 1) return n;
        var surname = n[..comma].Trim();
        var given = n[(comma + 1)..].Trim();
        return given.Length == 0 ? surname : $"{given} {surname}";
    }

    /// <summary>TSF kayıt listesinden (chess-results listesi henüz yoksa): ad, kategori, il, ELO, UKD.</summary>
    public static List<Athlete> FromRegistrations(IEnumerable<PlayerRegistration> entries)
        => entries.Where(e => !string.IsNullOrWhiteSpace(e.FullName))
                  .Select(e => new Athlete(e.FullName, e.Category.Trim(), Provinces.Print(e.Province),
                                           e.Elo is > 0 ? e.Elo : null, e.Ukd is > 0 ? e.Ukd : null))
                  .ToList();
}
