using Velopack;

namespace NotasyonOtomasyonu.App;

internal static class Program
{
    /// <summary>Güncellemeyle açıldıysa önceki sürümün numarası ("--updated 1.5.2"); yoksa null.</summary>
    internal static string? UpdatedFrom { get; private set; }

    /// <summary>Açılışta "internetten indirildi" işareti kaldırılan dosya sayısı (taşınabilir exe).</summary>
    internal static int Unblocked { get; private set; }

    /// <summary>Uygulama giriş noktası.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack: kurulum, kaldırma ve güncelleme adımlarında program bu argümanlarla çağrılır; o durumda
        // gerekeni yapıp çıkar. Olağan açılışta hemen döner.
        VelopackApp.Build().SetArgs(args).Run();

        // Güncellemeden sonra: eski sürüm kapanana kadar bekle (config.json'u ikisi aynı anda yazmasın), eski exe'yi sil.
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--updated") UpdatedFrom = args[i + 1];
            if (args[i] == "--wait" && int.TryParse(args[i + 1], out var pid))
                try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(15000); } catch { /* çoktan kapandı */ }
        }
        Update.Updater.CleanupOld();
        AppPaths.Init(AppContext.BaseDirectory);   // ayarlar ve kullanıcı dosyaları: %APPDATA%\ChessScoresheetPrinter

        if (!Update.Velo.IsInstalled && Environment.ProcessPath is { } exe)
        {
            // Taşınabilir exe (ZIP'ten): Windows her açılışta "çalıştırılsın mı?" diye sormasın.
            Unblocked = Update.Updater.Unblock(AppContext.BaseDirectory, exe);
            // Bilgisayarda kurulu sürüm varsa eski kopya onu açar (eski kısayollar da yeni sürüme gitsin).
            if (OpenInstalledInstead(exe, args)) return;
        }

        // WinForms yüksek DPI ve varsayılan font ayarları.
        ApplicationConfiguration.Initialize();

        // İkinci pencere açılmasın: ikisi aynı ayar dosyasını yazarsa biri ötekinin değişikliklerini ezer.
        if (!AppPaths.AcquireSingleInstance(afterUpdate: UpdatedFrom is not null)) return;

        // Beklenmeyen hatalarda uygulama çökmesin, anlaşılır mesaj göster.
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Beklenmeyen bir hata oluştu:\n\n" + e.Exception.Message,
                "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
    }

    /// <summary>
    /// Taşınabilir exe açıldı ama program bilgisayara kurulmuş (Setup): kurulu olanı aç ve çık. Taşınabilir
    /// kullanımda ("portable" dosyası), deneme ortamında (CSP_CONFIG_DIR / CSP_NO_REDIRECT) ya da kurulu sürüm
    /// daha eskiyse bu kopya açılır.
    /// </summary>
    private static bool OpenInstalledInstead(string exe, string[] args)
    {
        try
        {
            if (AppPaths.IsPortableMode) return false;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CSP_CONFIG_DIR"))) return false;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CSP_NO_REDIRECT"))) return false;
            var installed = Update.Updater.InstalledExe;
            if (!File.Exists(installed) || string.Equals(Path.GetFullPath(installed), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return false;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(installed);
            var v = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            if (v < Update.Updater.Current) return false;
            var psi = new System.Diagnostics.ProcessStartInfo(installed) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installed)! };
            foreach (var a in args) psi.ArgumentList.Add(a);
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
