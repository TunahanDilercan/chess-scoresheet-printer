using Velopack;
using Velopack.Sources;

namespace NotasyonOtomasyonu.App.Update;

/// <summary>İndirilecek güncelleme: hedef sürüm, indirilecek boyut (yalnız değişen kısımlar ya da tam paket).</summary>
public sealed record VeloPlan(UpdateInfo Info, Version Version, long DownloadBytes, long FullBytes, int DeltaCount)
{
    public bool UsesDelta => DeltaCount > 0;
}

/// <summary>
/// Kurulu sürümün güncellenmesi (Velopack). Program Setup ile kurulduysa (%LocalAppData%\ChessScoresheetPrinter)
/// güncellemeler GitHub sürümlerinden alınır: önceki sürümden bu yana yalnız değişen dosyaların farkı (delta
/// paket) indirilir, olmazsa tam pakete düşülür; paketler SHA ile doğrulanır. Uygulama sırasında program kapanır,
/// Velopack yeni sürümü yerine koyup programı yeniden açar. Ayarlar %APPDATA%'da olduğundan dokunulmaz.
/// </summary>
public static class Velo
{
    /// <summary>
    /// Güncelleme kaynağı: CSP_VELOPACK_SOURCE (yerel klasör ya da adres; deneme için) yoksa GitHub sürümleri.
    /// </summary>
    public static UpdateManager Manager()
    {
        var env = Environment.GetEnvironmentVariable("CSP_VELOPACK_SOURCE");
        if (!string.IsNullOrWhiteSpace(env)) return new UpdateManager(env);
        return new UpdateManager(new GithubSource("https://github.com/" + Updater.Repo, null, false));
    }

    private static bool? _installed;

    /// <summary>Program Velopack kurulumuyla mı çalışıyor (Setup ile kurulmuş, Update.exe yanında).</summary>
    public static bool IsInstalled
    {
        get
        {
            if (_installed is { } v) return v;
            try { _installed = Manager().IsInstalled; }
            catch { _installed = false; }
            return _installed.Value;
        }
    }

    /// <summary>Son denetimde bulunan plan (güncelleme penceresi yeniden sormasın).</summary>
    public static VeloPlan? LastPlan { get; set; }

    /// <summary>Plan → sürüm bilgisi (notlar paketin içinden).</summary>
    public static ReleaseInfo ToRelease(VeloPlan p)
        => new(p.Version, "v" + p.Version, p.Info.TargetFullRelease.NotesMarkdown ?? "", Updater.ReleasesPage, null, "", "", p.FullBytes, null);

    /// <summary>Yeni sürüm varsa indirme planı; yoksa null.</summary>
    public static async Task<VeloPlan?> PlanAsync()
    {
        var mgr = Manager();
        var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null) return null;
        return Plan(info);
    }

    /// <summary>İndirilecek boyut: delta paketlerin toplamı tam paketten küçükse onlar, değilse tam paket.</summary>
    public static VeloPlan Plan(UpdateInfo info)
    {
        long full = info.TargetFullRelease.Size;
        var deltas = info.DeltasToTarget ?? Array.Empty<VelopackAsset>();
        long deltaSum = deltas.Sum(d => d.Size);
        bool useDelta = deltas.Length > 0 && info.BaseRelease is not null && deltaSum < full;
        var v = info.TargetFullRelease.Version;
        return new VeloPlan(info, new Version(v.Major, v.Minor, v.Patch), useDelta ? deltaSum : full, full, useDelta ? deltas.Length : 0);
    }

    /// <summary>İndirir ve hazırlar (delta olmazsa Velopack tam pakete düşer). progress: 0–100.</summary>
    public static Task DownloadAsync(VeloPlan plan, Action<int> progress, CancellationToken ct)
        => Manager().DownloadUpdatesAsync(plan.Info, progress, ct);

    /// <summary>
    /// Program kapandıktan sonra güncellemeyi uygulatır ve yeni sürümü "--updated eski" ile açtırır.
    /// Çağıran hemen ardından programı kapatmalı.
    /// </summary>
    public static void ApplyAndRestart(VeloPlan plan)
        => Manager().WaitExitThenApplyUpdates(plan.Info.TargetFullRelease, silent: false, restart: true,
                                              restartArgs: new[] { "--updated", Updater.Current.ToString() });
}
