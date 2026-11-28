using System.Drawing.Printing;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Tüm baskıların (notasyon, masa kartı, yaka kartı) yazıcıya gidişi: ayarlardaki hedef yazıcı,
/// kağıt kaynağı (tepsi) ve kopya sayısı uygulanır. Sessiz modda yazdırma penceresi açılmadan
/// doğrudan hedef yazıcıya gönderilir; değilse pencere bu ayarlarla önceden seçili açılır.
/// </summary>
/// <summary>
/// Bir baskının seçenekleri. <paramref name="Bulk"/>: toplu baskı (tüm kategoriler, tüm listeler, çok sayfalı kartlar);
/// "doğrudan yazdır" açık olsa da (ayar açıksa) önce yazıcı penceresi gelir. <paramref name="Printer"/>: bu baskının
/// yazıcısı (çoklu yazıcı modu). <paramref name="NoDialog"/>: pencere hiç açılmaz (seçimler önceden onaylandı).
/// </summary>
public sealed record PrintOptions(bool Bulk = false, string? Printer = null, bool NoDialog = false);

public static class PrintRouter
{
    /// <summary>Uygulamanın yazıcı ayarları (ana pencere yükler, Ayarlar günceller).</summary>
    public static PrintConfig Config { get; set; } = new();

    public static IReadOnlyList<string> InstalledPrinters()
    {
        try { return PrinterSettings.InstalledPrinters.Cast<string>().ToList(); }
        catch { return Array.Empty<string>(); }
    }

    public static string DefaultPrinter()
    {
        try { return new PrinterSettings().PrinterName; } catch { return ""; }
    }

    /// <summary>Ayarlı yazıcı kurulu mu (boşsa Windows varsayılanı kullanılır).</summary>
    public static bool TargetAvailable =>
        string.IsNullOrWhiteSpace(Config.PrinterName) || InstalledPrinters().Contains(Config.PrinterName!, StringComparer.OrdinalIgnoreCase);

    /// <summary>"Doğrudan yazdır" etkin ve hedef yazıcı kullanılabilir mi.</summary>
    public static bool IsSilent => Config.Silent && TargetAvailable;

    /// <summary>Baskının gideceği yazıcının adı (kullanıcıya göstermek için).</summary>
    public static string TargetName => string.IsNullOrWhiteSpace(Config.PrinterName) ? DefaultPrinter() : Config.PrinterName!;

    /// <summary>Yazıcının kağıt kaynakları (tepsiler).</summary>
    public static IReadOnlyList<string> PaperSources(string? printer)
    {
        try
        {
            var ps = new PrinterSettings();
            if (!string.IsNullOrWhiteSpace(printer)) ps.PrinterName = printer;
            return ps.PaperSources.Cast<PaperSource>().Select(s => s.SourceName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Belgeyi basar. Sessiz modda doğrudan; değilse yazıcı penceresiyle. <paramref name="preparePaper"/>
    /// seçilen yazıcıya göre kağıdı ayarlar (false = vazgeç).
    /// </summary>
    /// <returns>true = yazıcıya gönderildi.</returns>
    public static bool Print(PrintDocument doc, IWin32Window owner, Func<PrintDocument, IWin32Window, bool>? preparePaper = null,
                             PrintOptions? options = null)
    {
        if (!Prepare(doc, owner, preparePaper, options)) return false;
        doc.Print();
        return true;
    }

    /// <summary>
    /// Baskıya hazırlar (arayüzde): hedef yazıcı, yazıcı penceresi ("doğrudan yazdır" kapalıysa), kâğıt ve tepsi.
    /// false = kullanıcı vazgeçti. Ardından <c>doc.Print()</c> arka planda çağrılabilir.
    /// </summary>
    public static bool Prepare(PrintDocument doc, IWin32Window owner, Func<PrintDocument, IWin32Window, bool>? preparePaper = null,
                               PrintOptions? options = null)
    {
        ApplyTarget(doc, options?.Printer);
        if (ShowsDialog(options))
        {
            using var pd = new PrintDialog { Document = doc, UseEXDialog = true, AllowSomePages = false };
            if (pd.ShowDialog(owner) != DialogResult.OK) return false;
        }
        if (preparePaper is not null && !preparePaper(doc, owner)) return false;
        ApplySource(doc);
        doc.PrintController = new StandardPrintController(); // önizleme değil, gerçek baskı; durum penceresi yok (ilerleme pencerede)
        return true;
    }

    /// <summary>Çoklu yazıcı modunun kurulu ve farklı yazıcıları (en az 2 değilse mod kapalı sayılır).</summary>
    public static IReadOnlyList<string> MultiTargets()
    {
        if (!Config.MultiPrinter) return Array.Empty<string>();
        var installed = InstalledPrinters();
        var list = Config.MultiPrinters.Where(p => installed.Contains(p, StringComparer.OrdinalIgnoreCase))
                                       .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count >= 2 ? list : Array.Empty<string>();
    }

    /// <summary>
    /// Yazıcı penceresi açılacak mı: "doğrudan yazdır" kapalıysa her zaman; açıksa yalnız toplu baskıda
    /// ("Toplu yazdırmada önce yazıcı penceresi" ayarı açıksa). Seçimler önceden onaylandıysa hiç.
    /// </summary>
    public static bool ShowsDialog(PrintOptions? options)
        => options?.NoDialog != true && (!IsSilent || (options?.Bulk == true && Config.ConfirmBulk));

    /// <summary>Önizleme atlanır mı: doğrudan yazdırda (toplu onayda yalnız yazıcı penceresi açılır) ya da seçimler onaylandıysa.</summary>
    public static bool SkipsPreview(PrintOptions? options) => IsSilent || options?.NoDialog == true;

    /// <summary>Hedef yazıcıyı ve kopya sayısını belgeye uygular (pencere açılacaksa önceden seçili gelir).</summary>
    public static void ApplyTarget(PrintDocument doc, string? printer = null)
    {
        if (!string.IsNullOrWhiteSpace(printer) && InstalledPrinters().Contains(printer, StringComparer.OrdinalIgnoreCase))
            doc.PrinterSettings.PrinterName = printer;
        else if (!string.IsNullOrWhiteSpace(Config.PrinterName) && TargetAvailable)
            doc.PrinterSettings.PrinterName = Config.PrinterName;
        // Yazıcının kendi tercihleri (sessiz mod vb.); kağıt ve yön sonra programca ayarlanır.
        if (Config.DevModes.TryGetValue(doc.PrinterSettings.PrinterName, out var devmode))
            PrinterPreferences.Apply(doc, devmode);
        doc.PrinterSettings.Copies = (short)Math.Clamp(Config.Copies, 1, 99);
    }

    /// <summary>Ayarlı tepsi, seçilen yazıcıda varsa uygulanır; A5 belgede (notasyon) varsa A5 tepsisi.</summary>
    private static void ApplySource(PrintDocument doc)
    {
        var source = Config.PaperSource;
        try
        {
            if (doc.DefaultPageSettings.PaperSize.Kind == PaperKind.A5 && !string.IsNullOrWhiteSpace(Config.PaperSourceA5))
                source = Config.PaperSourceA5;
        }
        catch (InvalidPrinterException) { }
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            foreach (PaperSource s in doc.PrinterSettings.PaperSources)
                if (string.Equals(s.SourceName, source, StringComparison.OrdinalIgnoreCase))
                { doc.DefaultPageSettings.PaperSource = s; return; }
        }
        catch (InvalidPrinterException) { }
    }
}
