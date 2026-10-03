using System.Globalization;
using System.Text.RegularExpressions;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>Kontrol sonucu.</summary>
public enum RuleLevel { Ok, Info, Warning, Error }

/// <summary>Bir kural kontrolü: sonuç, açıklama, kaynak madde ve (varsa) ilgili program satırı.</summary>
public sealed record RuleCheck(RuleLevel Level, string Title, string Detail, string Source, int? ProgramRow = null);

/// <summary>Kategori bilgisi: sporcu (takımda takım) sayısı, en yüksek rating, zaman kontrolü metni.</summary>
public sealed record CategoryStats(int Players, int MaxRating, string? TimeControl);

/// <summary>Programdaki bir kalem (tarih metni, saat metni, etkinlik).</summary>
public sealed record ScheduleItem(string Date, string Time, string Event);

/// <summary>
/// TSF-TK Yarışma Yönergeleri Hazırlama ve Uygulama Prosedürü (TSF YK 11.09.2025 tarih ve 64/16
/// sayılı karar) kuralları: tempoya göre ardışık iki tur arası en az süre (EK-B), günlük tur sınırı ve
/// gün uzunluğu (Madde 7/22), kayıt kontrol süresi (Madde 7/19), sporcu sayısına göre sistem (EK-C),
/// az katılımlı kategori (Madde 7/10).
/// </summary>
public static class TsfRules
{
    public const string ProcedureName = "TSF-TK Yarışma Yönergeleri Hazırlama ve Uygulama Prosedürü";
    public const string ProcedureVersion = "YK 11.09.2025 / 64-16";
    public const string ProcedureUrl = "https://www.tsf.org.tr/kaynaklar/belgeler/1929-tsf-tk-yarisma-yonergeleri-hazirlama-ve-uygulama-proseduru.pdf";

    /// <summary>EK-B: tempo → ardışık iki tur başlangıcı arası en az süre (dakika).</summary>
    public static readonly IReadOnlyList<(int Minutes, int Increment, int Gap)> RoundGaps = new[]
    {
        (3, 2, 30), (5, 3, 45), (10, 5, 60), (15, 10, 75), (20, 10, 90), (25, 10, 90),
        (35, 30, 150), (45, 30, 180), (60, 30, 210), (90, 30, 300)
    };

    /// <summary>
    /// Tempo için en az tur arası. Tabloda yoksa, oyuncu başına toplam süresi (60 hamle) en yakın
    /// büyük ya da eşit olan tablo temposu alınır (güvenli taraf); Exact=false döner.
    /// </summary>
    public static (int Gap, bool Exact, (int Minutes, int Increment) Basis) MinRoundGap(int minutes, int increment)
    {
        foreach (var r in RoundGaps)
            if (r.Minutes == minutes && r.Increment == increment) return (r.Gap, true, (r.Minutes, r.Increment));
        double total = TimeControls.Total(minutes, increment);
        var up = RoundGaps.OrderBy(r => TimeControls.Total(r.Minutes, r.Increment))
                          .FirstOrDefault(r => TimeControls.Total(r.Minutes, r.Increment) >= total);
        if (up == default)
        {
            // Tablonun üstü (ör. 120'+30''): en uzun tempodaki oran korunur.
            var last = RoundGaps[^1];
            int gap = (int)Math.Ceiling(total / TimeControls.Total(last.Minutes, last.Increment) * last.Gap / 15.0) * 15;
            return (gap, false, (minutes, increment));
        }
        return (up.Gap, false, (up.Minutes, up.Increment));
    }

    /// <summary>Bir oyunun en uzun süresi tahmini (iki oyuncu, 60 hamle): 2 × (temel + artış).</summary>
    public static int EstimatedGameMinutes(int minutes, int increment) => 2 * (minutes + increment);

    /// <summary>Madde 7/22: UKD'de günde en fazla 4, ELO'da 3 tur.</summary>
    public static int MaxRoundsPerDay(bool elo) => elo ? 3 : 4;
    public const int MaxDayHours = 12;
    public const int MinCheckInMinutes = 30, MaxCheckInMinutes = 60;
    public const int MinPlayers = 5;   // Madde 7/10: 4 ve altı iptal

    /// <summary>EK-C: sporcu sayısına göre sistem ve tur sayısı.</summary>
    public static (TournamentSystem System, int Rounds, string Text) ExpectedSystem(int players, bool team = false)
    {
        if (team)
            return players switch
            {
                <= 4 => (TournamentSystem.TeamRoundRobin, 2 * (players - 1 + players % 2), "Çift tur döner sistem"),
                <= 8 => (TournamentSystem.TeamRoundRobin, players - 1 + players % 2, "Tek tur döner sistem"),
                <= 16 => (TournamentSystem.TeamSwiss, 5, "5 tur İsviçre sistemi"),
                <= 24 => (TournamentSystem.TeamSwiss, 7, "7 tur İsviçre sistemi"),
                _ => (TournamentSystem.TeamSwiss, 9, "9 tur İsviçre sistemi")
            };
        return players switch
        {
            <= 4 => (TournamentSystem.RoundRobin, 2 * (players - 1 + players % 2), "Çift tur döner sistem"),
            <= 8 => (TournamentSystem.RoundRobin, players - 1 + players % 2, "Tek tur döner sistem"),
            <= 32 => (TournamentSystem.Swiss, 5, "5 tur İsviçre sistemi"),
            <= 64 => (TournamentSystem.Swiss, 6, "6 tur İsviçre sistemi"),
            <= 128 => (TournamentSystem.Swiss, 7, "7 tur İsviçre sistemi"),
            <= 256 => (TournamentSystem.Swiss, 9, "9 tur İsviçre sistemi"),
            _ => (TournamentSystem.Swiss, 11, "11 tur İsviçre sistemi")
        };
    }

    // ================= Program =================

    /// <summary>"10.00", "10:00", "09.00-09.30" → başlangıç (ve varsa bitiş) dakikası.</summary>
    public static (int Start, int? End)? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"(\d{1,2})[.:](\d{2})(?:\s*[-–]\s*(\d{1,2})[.:](\d{2}))?");
        if (!m.Success) return null;
        int start = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
        int? end = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) * 60 + int.Parse(m.Groups[4].Value) : null;
        return (start, end);
    }

    public static string FormatTime(int minutes) => $"{minutes / 60 % 24:00}.{minutes % 60:00}";

    private static int? RoundNo(string ev)
    {
        var m = Regex.Match(ev ?? "", @"^\s*(\d+)\s*\.\s*Tur\s*$", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// Programı kontrol eder. <paramref name="items"/> rapordaki program satırları (tur satırları "N. Tur");
    /// açılış kalemleri (kayıt, teknik toplantı) ilk güne aittir.
    /// </summary>
    public static List<RuleCheck> CheckSchedule(IReadOnlyList<ScheduleItem> items, (int Minutes, int Increment)? tempo,
                                                bool elo, string? checkIn, int? maxPerDayOverride = null, string? maxPerDaySource = null)
    {
        var list = new List<RuleCheck>();
        const string ekB = "EK-B", m22 = "Madde 7/22", m19 = "Madde 7/19";

        if (checkIn is { Length: > 0 } && ParseTime(checkIn) is { } ci)
        {
            if (ci.End is int e)
            {
                int d = e - ci.Start;
                list.Add(d is >= MinCheckInMinutes and <= MaxCheckInMinutes
                    ? new RuleCheck(RuleLevel.Ok, $"Kayıt kontrol süresi {d} dk", "30–60 dk arasında olmalı.", m19)
                    : new RuleCheck(RuleLevel.Error, $"Kayıt kontrol süresi {d} dk", $"En az {MinCheckInMinutes}, en fazla {MaxCheckInMinutes} dakika olmalı.", m19));
            }
            else list.Add(new RuleCheck(RuleLevel.Info, "Kayıt kontrol bitiş saati yok", "Başlangıç-bitiş olarak yazın (ör. 09.00-09.30); süre 30–60 dk olmalı.", m19));
        }

        // Tur satırlarını günlere ayır (tarih metni boşsa önceki günün devamı)
        var rounds = new List<(int Index, string Day, int Round, int Start)>();
        string day = "";
        for (int i = 0; i < items.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(items[i].Date)) day = items[i].Date.Trim();
            if (RoundNo(items[i].Event) is int rn && ParseTime(items[i].Time) is { } t)
                rounds.Add((i, day, rn, t.Start));
        }
        if (rounds.Count == 0) return list;

        if (tempo is { } tc)
        {
            var (gap, exact, basis) = MinRoundGap(tc.Minutes, tc.Increment);
            var basisText = exact ? "" : $" (tabloda yok; en yakın üst tempo {TimeControls.Short(basis.Minutes, basis.Increment)} esas alındı)";
            list.Add(new RuleCheck(RuleLevel.Info, $"{TimeControls.Short(tc.Minutes, tc.Increment)}: iki tur arası en az {gap} dk{basisText}",
                $"Bir oyun en fazla ~{EstimatedGameMinutes(tc.Minutes, tc.Increment)} dk sürer.", ekB));
            bool allOk = true;
            for (int k = 1; k < rounds.Count; k++)
            {
                var (prevIdx, prevDay, prevRound, prevStart) = rounds[k - 1];
                var (idx, d2, r2, start) = rounds[k];
                if (d2 != prevDay) continue; // gün değişti
                int diff = start - prevStart;
                if (diff < gap)
                {
                    allOk = false;
                    list.Add(new RuleCheck(RuleLevel.Error, $"{r2}. Tur {FormatTime(start)}: {prevRound}. turdan {diff} dk sonra",
                        $"En az {gap} dk olmalı; en erken {FormatTime(prevStart + gap)}.", ekB, idx));
                }
            }
            if (allOk) list.Add(new RuleCheck(RuleLevel.Ok, "Tur aralıkları uygun", $"Aynı gündeki ardışık turlar arasında en az {gap} dk var.", ekB));
        }
        else list.Add(new RuleCheck(RuleLevel.Warning, "Düşünme süresi okunamadı", "Tur aralığı kontrolü için düşünme süresini girin (ör. 35 DAKİKA + HAMLE BAŞINA 30 SANİYE).", ekB));

        // Günlük tur sayısı ve gün uzunluğu
        int maxPerDay = maxPerDayOverride ?? MaxRoundsPerDay(elo);
        foreach (var g in rounds.GroupBy(r => r.Day))
        {
            int n = g.Count();
            if (n > maxPerDay)
                list.Add(new RuleCheck(RuleLevel.Error, $"{g.Key}: {n} tur",
                    maxPerDayOverride is null ? $"{(elo ? "ELO" : "UKD")} hesaplamalı etkinlikte günde en fazla {maxPerDay} tur."
                                              : $"Bu etkinlikte günde en fazla {maxPerDay} tur.", maxPerDaySource ?? m22, g.Last().Index));
            if (tempo is { } t2)
            {
                int first = g.Min(r => r.Start);
                // İlk gün: açılış kalemleri (kayıt kontrol) günün başlangıcıdır
                if (ReferenceEquals(g.Key, rounds[0].Day) || g.Key == rounds[0].Day)
                    for (int i = 0; i < items.Count && i <= g.First().Index; i++)
                        if (ParseTime(items[i].Time) is { } ot) first = Math.Min(first, ot.Start);
                int end = g.Max(r => r.Start) + EstimatedGameMinutes(t2.Minutes, t2.Increment);
                double hours = (end - first) / 60.0;
                if (hours > MaxDayHours)
                    list.Add(new RuleCheck(RuleLevel.Error, $"{g.Key}: gün ~{hours:0.#} saat sürüyor",
                        $"Bir günde 12 saati aşan etkinlik düzenlenemez (son tur ~{FormatTime(end)}'de biter).", m22, g.Last().Index));
                else
                    list.Add(new RuleCheck(RuleLevel.Ok, $"{g.Key}: {n} tur, {FormatTime(first)}–~{FormatTime(end)}",
                        $"Günde en fazla {maxPerDay} tur ve 12 saat sınırına uygun.", m22));
            }
        }
        return list;
    }

    /// <summary>
    /// Tempoya göre tur saatleri önerir: her gün <paramref name="dayStarts"/>'taki saatte başlar,
    /// turlar en az aralıkla (15 dakikaya yuvarlanmış) dizilir; günlük sınır aşılmaz.
    /// </summary>
    public static List<(int Day, int Round, int Start)> SuggestSchedule(int rounds, int days, int firstStart, (int Minutes, int Increment) tempo, bool elo)
    {
        int gap = MinRoundGap(tempo.Minutes, tempo.Increment).Gap;
        gap = (gap + 14) / 15 * 15;
        days = Math.Max(1, days);
        int perDayLimit = MaxRoundsPerDay(elo);
        // 12 saat sınırı: ilk turdan son turun bitişine kadar (kayıt payı 60 dk)
        int game = EstimatedGameMinutes(tempo.Minutes, tempo.Increment);
        int byHours = Math.Max(1, (MaxDayHours * 60 - 60 - game) / gap + 1);
        int perDay = Math.Min(perDayLimit, byHours);
        // Turları günlere olabildiğince eşit dağıt (ilk günler dolu)
        var result = new List<(int, int, int)>();
        int remaining = rounds, round = 1;
        for (int d = 0; d < days && remaining > 0; d++)
        {
            int todays = Math.Min(perDay, (int)Math.Ceiling(remaining / (double)(days - d)));
            for (int k = 0; k < todays; k++) result.Add((d, round++, firstStart + k * gap));
            remaining -= todays;
        }
        // Gün yetmediyse kalanlar son güne eklenir (kontrolde hata olarak görünür)
        while (remaining-- > 0) result.Add((days - 1, round++, firstStart + result.Count(r => r.Item1 == days - 1) * gap));
        return result;
    }

    /// <summary>Kategori sporcu sayısı ve sistemini EK-C ve Madde 7/10'a göre kontrol eder.</summary>
    public static RuleCheck CheckCategory(string name, int players, TournamentSystem system, int rounds)
    {
        const string ekC = "EK-C", m10 = "Madde 7/10";
        if (players <= 0) return new RuleCheck(RuleLevel.Info, $"{name}: sporcu sayısı bilinmiyor", "Eşleştirme yayımlanınca kontrol edilir.", ekC);
        if (players < MinPlayers)
            return new RuleCheck(RuleLevel.Error, $"{name}: {players} sporcu", "Kayıt kontrol sonrası 4 ve altı sporculu kategori iptal edilmeli (yarışmalar talimatına göre birleştirilir).", m10);
        var exp = ExpectedSystem(players, system.IsTeam());
        bool sysOk = system == TournamentSystem.Unknown || exp.System.IsRoundRobin() == system.IsRoundRobin();
        bool roundsOk = rounds <= 0 || rounds == exp.Rounds;
        var actual = system == TournamentSystem.Unknown ? $"{rounds} tur" : $"{system.DisplayName()} {rounds} tur";
        return sysOk && roundsOk
            ? new RuleCheck(RuleLevel.Ok, $"{name}: {players} sporcu → {exp.Text}", $"chess-results: {actual}.", ekC)
            : new RuleCheck(RuleLevel.Warning, $"{name}: {players} sporcu → EK-C'ye göre {exp.Text} ({exp.Rounds} tur)",
                $"chess-results'ta {actual}. Başhakem katılıma göre değiştirebilir (Madde 8/8); teknik toplantıda duyurulmalı.", ekC);
    }

    /// <summary>
    /// Madde 7/3: yaş kategorileri 7 yaştan başlayarak üst kategoriye dahil edilir ve çift yaşta biter
    /// ("10 Yaş ve Altı" doğru, "9 Yaş ve Altı" yanlış). Tek yaş kategorileri ("9 Yaş") serbesttir.
    /// </summary>
    public static RuleCheck? CheckAgeCategory(string name)
    {
        var m = Regex.Match(EventGrouping.Fold(name), @"(\d{1,2})\s*yas\s*(?:ve\s*)?alti");
        if (!m.Success) return null;
        int age = int.Parse(m.Groups[1].Value);
        return age % 2 == 0 && age >= 8
            ? new RuleCheck(RuleLevel.Ok, $"{name}: kategori aralığı uygun", $"7–{age} yaş sporcuları kapsar (çift yaşta biter).", "Madde 7/3")
            : new RuleCheck(RuleLevel.Warning, $"{name}: kategori çift yaşta bitmeli",
                $"\"Yaş ve Altı\" kategorileri 7 yaştan başlar ve çift yaşta biter (ör. {age + 1} Yaş ve Altı).", "Madde 7/3");
    }

    /// <summary>Madde 7/8: aynı salondaki kategorilerde zaman temposu aynı olmalı.</summary>
    public static RuleCheck? CheckTempoUnity(IReadOnlyList<(string Category, string? TimeControl)> categories)
    {
        var parsed = categories.Select(c => (c.Category, Tc: TimeControls.Parse(c.TimeControl))).Where(c => c.Tc is not null).ToList();
        if (parsed.Count < 2) return null;
        var groups = parsed.GroupBy(c => c.Tc!.Value).ToList();
        if (groups.Count == 1)
            return new RuleCheck(RuleLevel.Ok, $"Tüm kategorilerde tempo {TimeControls.Short(groups[0].Key.Minutes, groups[0].Key.Increment)}",
                "Aynı salondaki kategorilerde zaman temposu aynı olmalı.", "Madde 7/8");
        var detail = string.Join("; ", groups.Select(g => $"{TimeControls.Short(g.Key.Minutes, g.Key.Increment)}: {string.Join(", ", g.Select(x => x.Category))}"));
        return new RuleCheck(RuleLevel.Warning, "Kategorilerde farklı tempo var",
            $"{detail}. Kategoriler aynı salondaysa tempo aynı olmalı.", "Madde 7/8");
    }

    /// <summary>
    /// EK-A (ELO kriterli kategoriler): 2400 ve üzeri ELO'lu sporcu katılıyorsa 90'+30'' ve günde en fazla
    /// 2 tur; diğer durumlarda 60'+30'' ve günde en fazla 3 tur. ELO hesaplamalı değilse null.
    /// </summary>
    public static (RuleCheck Check, int MaxPerDay)? CheckEloTempo(bool elo, int maxRating, (int Minutes, int Increment)? tempo)
    {
        if (!elo) return null;
        bool strong = maxRating >= 2400;
        var (em, ei, perDay) = strong ? (90, 30, 2) : (60, 30, 3);
        string who = strong ? $"2400+ ELO'lu sporcu var (en yüksek {maxRating})" : (maxRating > 0 ? $"en yüksek rating {maxRating}" : "rating bilinmiyor");
        if (tempo is { } t && t.Minutes == em && t.Increment == ei)
            return (new RuleCheck(RuleLevel.Ok, $"ELO etkinliği: tempo {TimeControls.Short(em, ei)}", $"{who}; günde en fazla {perDay} tur.", "EK-A"), perDay);
        return (new RuleCheck(RuleLevel.Warning, $"ELO etkinliği: tempo {TimeControls.Short(em, ei)} olmalı",
            $"{who}. EK-A'ya göre ELO kategorilerinde {TimeControls.Short(em, ei)} tempo ve günde en fazla {perDay} tur" +
            (tempo is { } tt ? $" (şu an {TimeControls.Short(tt.Minutes, tt.Increment)})." : "."), "EK-A"), perDay);
    }

    /// <summary>chess-results "Rating hesaplama" bilgisinden ELO hesaplamalı mı.</summary>
    public static bool IsElo(string? ratingCalc)
    {
        var f = EventGrouping.Fold(ratingCalc ?? "");
        return f.Contains("elo") || f.Contains("fide") || f.Contains("uluslararasi") || f.Contains("international");
    }
}
