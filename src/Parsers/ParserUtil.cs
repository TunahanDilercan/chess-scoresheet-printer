using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>Parser'lar arası ortak Tournament kurma yardımcısı (meta veriyi config'ten doldurur).</summary>
internal static class ParserUtil
{
    public static Tournament BuildTournament(AppConfig config, IReadOnlyList<Pairing> pairings)
        => new(
            Name: config.Tournament.Name,
            RoundNo: config.Tournament.LastRound,
            Pairings: pairings,
            Location: config.Tournament.Location,
            Date: config.Tournament.Date,
            TimeControl: config.Tournament.TimeControl,
            Arbiter: config.Tournament.Arbiter);
}
