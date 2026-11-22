using System.Diagnostics;
using System.Text;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA2;
using Xunit;
using Xunit.Abstractions;

namespace NotasyonOtomasyonu.UiTests;

/// <summary>
/// Gerçek programı açar ve her pencereyi tarar:
///  • Axe.Windows (Microsoft Accessibility Insights kuralları),
///  • geometri: üst üste binen ("arkada kalan") denetimler, kutusundan taşan/kırpılan denetimler,
///  • ekran görüntüsü.
/// Programın ayarları geçici bir klasörde tutulur (CSP_CONFIG_DIR); gerçek ayarlara dokunulmaz.
/// Çalıştırmak: set CSP_UI_E2E=1 &amp; dotnet test tests\UiTests  (rapor: CSP_UI_OUT klasörü ya da %TEMP%\csp-ui-scan)
/// </summary>
public class AppScanTests
{
    private readonly ITestOutputHelper _out;
    public AppScanTests(ITestOutputHelper o) => _out = o;

    private static string ExePath()
    {
        var env = Environment.GetEnvironmentVariable("CSP_UI_EXE");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NotasyonOtomasyonu.sln"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Çözüm klasörü bulunamadı; CSP_UI_EXE verin.");
        return Directory.GetFiles(Path.Combine(dir.FullName, "src", "App", "bin", "Debug"), "ChessScoresheetPrinter.exe", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTime).First();
    }

    /// <summary>Ana pencereden açılan pencereler: düğme adı (joker *), beklenen pencere başlığı.</summary>
    private static readonly (string Button, string Title)[] Windows =
    {
        ("Ayarlar", "Ayarlar"),
        ("Sporcu Kartları", "Sporcu Yaka*"),
        ("Yaka Kartları", "Hakem Yaka*"),
        ("Masa Kartları", "Kategori Masa*"),
        ("Yönerge / Tutanak", "*Yönerge*"),
        ("Hakem eşleştirme kağıtları…", "*Hakem*kağıt*"),
        ("Ceza / itiraz kayıtları…", "*Ceza*"),
        ("Düzenle", "Turnuva Bilgisini*"),
        ("Hariç Tut", "Hariç Tut*"),
        ("*değiştir", "*Yazıcı*"),
    };

    [Fact]
    public void ScanAllWindows()
    {
        if (Environment.GetEnvironmentVariable("CSP_UI_E2E") != "1") return;
        var outDir = Environment.GetEnvironmentVariable("CSP_UI_OUT") is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "csp-ui-scan");
        Directory.CreateDirectory(outDir);
        var cfgDir = Path.Combine(Path.GetTempPath(), "csp-ui-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "config.json"), "{\"Updates\":{\"AutoCheck\":false},\"Online\":{\"Province\":\"Isparta\"}}");

        var report = new StringBuilder($"# Arayüz taraması ({DateTime.Now:dd.MM.yyyy HH:mm})\n\n");
        int geometryProblems = 0, axeErrors = 0;
        using var automation = new UIA2Automation();
        try
        {
            // Her pencere için program ayrı açılır; pencereyi program kendisi açar (CSP_UI_OPEN):
            // modal pencereyi dışarıdan tetiklemek UI Automation'ı kilitliyordu.
            var only = Environment.GetEnvironmentVariable("CSP_UI_ONLY");   // yalnız bu düğmenin penceresi (hızlı yineleme)
            foreach (var (button, title) in Windows.Prepend((Button: "", Title: "")).Where(w => string.IsNullOrEmpty(only) || w.Button == only || (only == "ana" && w.Button == "")))
            {
                var psi = new ProcessStartInfo(ExePath()) { UseShellExecute = false };
                psi.Environment["CSP_CONFIG_DIR"] = cfgDir;
                psi.Environment["CSP_NO_REDIRECT"] = "1";
                if (button.Length > 0) psi.Environment["CSP_UI_OPEN"] = button;
                var app = FlaUI.Core.Application.Launch(psi);
                try
                {
                    var main = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))!;
                    if (button.Length == 0)
                    {
                        // turnuva ve kategoriler yüklenene kadar (hızlı erişim düğmeleri)
                        Wait(() => main.FindAllDescendants(c => c.ByControlType(ControlType.Button)).Any(b => b.Name.Contains("· T")), 60);
                        Thread.Sleep(1500);
                        (geometryProblems, axeErrors) = Scan("Ana pencere", main, app.ProcessId, outDir, report, (geometryProblems, axeErrors));
                        continue;
                    }
                    Window? win = null;
                    Wait(() => (win = AllWindows(automation, app.ProcessId).FirstOrDefault(w => w.Properties.NativeWindowHandle.ValueOrDefault != main.Properties.NativeWindowHandle.ValueOrDefault
                                                                                              && Like(w.Title, title))) is not null, 75);
                    if (win is null) { report.AppendLine($"## {button}\n\nPencere açılmadı (beklenen başlık: {title}).\n"); geometryProblems++; continue; }
                    Thread.Sleep(3000);   // pencerenin kendi yüklemesi (chess-results) bitsin
                    (geometryProblems, axeErrors) = Scan(win.Title, win, app.ProcessId, outDir, report, (geometryProblems, axeErrors));
                }
                finally { try { app.Kill(); app.Dispose(); } catch { } Thread.Sleep(500); }
            }
        }
        finally
        {
            try { Directory.Delete(cfgDir, true); } catch { }
            File.WriteAllText(Path.Combine(outDir, "rapor.md"), report.ToString());
            _out.WriteLine(report.ToString());
        }
        _out.WriteLine($"Toplam: {geometryProblems} geometri sorunu, {axeErrors} Axe.Windows hatası. Rapor: {Path.Combine(outDir, "rapor.md")}");
        Assert.True(geometryProblems == 0 && axeErrors == 0, $"{geometryProblems} geometri sorunu, {axeErrors} Axe.Windows hatası (rapor: {outDir})");
    }

    private (int Geometry, int Axe) Scan(string name, Window win, int pid, string outDir, StringBuilder report, (int Geometry, int Axe) totals)
    {
        var file = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ')).Trim().Replace(' ', '_');
        if (file.Length > 40) file = file[..40];
        // pencerenin kendi içeriği (PrintWindow): önünde başka bir program olsa da yalnız bu pencere kaydedilir
        try { SaveWindow((IntPtr)win.Properties.NativeWindowHandle.Value, Path.Combine(outDir, file + ".png")); } catch { }

        var cells = CellProbe.TruncatedCells(win, (IntPtr)win.Properties.NativeWindowHandle.Value).ToList();
        var geo = Geometry(win).Concat(cells.Where(c => !c.StartsWith("(bilgi)"))).ToList();
        var axe = new List<string>();
        try
        {
            var scanner = ScannerFactory.CreateScanner(Config.Builder.ForProcessId(pid).WithOutputFileFormat(OutputFileFormat.None).Build());
            var output = scanner.Scan(new ScanOptions(Guid.NewGuid().ToString("N"), (IntPtr)win.Properties.NativeWindowHandle.Value));
            foreach (var w in output.WindowScanOutputs)
                foreach (var e in w.Errors)
                {
                    e.Element.Properties.TryGetValue("Name", out var en);
                    e.Element.Properties.TryGetValue("ControlType", out var ct);
                    // pencerenin Windows'a ait sistem menüsü (simge menüsü) konumsuz görünür: programla ilgisi yok
                    if (e.Rule.ID.ToString() == "BoundingRectangleNotNull" && Equals(en, "Sistem")) continue;
                    e.Element.Properties.TryGetValue("BoundingRectangle", out var br);
                    e.Element.Properties.TryGetValue("ClassName", out var cn);
                    axe.Add($"{e.Rule.ID}: {e.Rule.Description} — {ct} '{en}' [{cn} {br}]");
                }
        }
        catch (Exception ex) { axe.Add("Axe.Windows taraması yapılamadı: " + ex.Message); }

        report.AppendLine($"## {name}\n");
        report.AppendLine($"Görüntü: `{file}.png` • {geo.Count} geometri sorunu • {axe.Count} Axe.Windows bulgusu\n");
        foreach (var info in cells.Where(c => c.StartsWith("(bilgi)"))) report.AppendLine("- " + info);
        foreach (var g in geo) report.AppendLine("- " + g);
        if (axe.Count > 0)
        {
            report.AppendLine("\nAxe.Windows:");
            foreach (var group in axe.GroupBy(a => a).OrderByDescending(g => g.Count()))
                report.AppendLine($"- {group.Key}" + (group.Count() > 1 ? $" (×{group.Count()})" : ""));
        }
        report.AppendLine();
        return (totals.Geometry + geo.Count, totals.Axe + axe.Count);
    }

    private static void SaveWindow(IntPtr h, string path)
    {
        WindowShot.GetWindowRect(h, out var r);
        using var bmp = new System.Drawing.Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T));
        using (var g = System.Drawing.Graphics.FromImage(bmp)) { var dc = g.GetHdc(); WindowShot.PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
        bmp.Save(path);
    }

    private static readonly ControlType[] Visual =
    {
        ControlType.Button, ControlType.Text, ControlType.Edit, ControlType.ComboBox, ControlType.CheckBox,
        ControlType.RadioButton, ControlType.Hyperlink, ControlType.Spinner, ControlType.ProgressBar, ControlType.Image,
        ControlType.Group, ControlType.DataGrid, ControlType.Table, ControlType.List, ControlType.Tab
    };

    /// <summary>Üst üste binen kardeş denetimler, kutusundan taşan/kırpılan denetimler.</summary>
    private static IEnumerable<string> Geometry(AutomationElement root)
    {
        var problems = new List<string>();
        void Walk(AutomationElement parent, System.Drawing.Rectangle clip)
        {
            AutomationElement[] kids;
            try { kids = parent.FindAllChildren(); } catch { return; }
            var pr = parent.Properties.BoundingRectangle.ValueOrDefault;
            bool scrolls = kids.Any(k => Ct(k) == ControlType.ScrollBar);
            var items = new List<(AutomationElement E, System.Drawing.Rectangle R)>();
            foreach (var k in kids)
            {
                ControlType ct; System.Drawing.Rectangle r; bool off;
                try { if (Ct(k) is not { } t) { Walk(k, clip); continue; } ct = t; r = k.Properties.BoundingRectangle.ValueOrDefault; off = k.Properties.IsOffscreen.ValueOrDefault; } catch { continue; }
                if (off || r.Width <= 0 || r.Height <= 0) continue;
                // tablo/liste satırları ve kaydırılan içerik kaba sığmak zorunda değil
                if (ct is ControlType.DataItem or ControlType.ListItem or ControlType.TreeItem or ControlType.HeaderItem or ControlType.Header
                       or ControlType.Custom or ControlType.ScrollBar or ControlType.Thumb or ControlType.TitleBar or ControlType.MenuBar
                       or ControlType.Window or ControlType.Menu or ControlType.MenuItem || scrolls)
                {
                    if (ct is not (ControlType.DataItem or ControlType.ListItem or ControlType.Custom or ControlType.Window or ControlType.TitleBar)) Walk(k, clip);
                    continue;
                }
                string id = $"{Tr(ct)} '{Short(Nm(k))}'";
                if (pr.Width > 0 && (r.Left < pr.Left - 2 || r.Top < pr.Top - 2 || r.Right > pr.Right + 2 || r.Bottom > pr.Bottom + 2))
                    problems.Add($"**taşıyor/kırpılıyor:** {id} ({Rect(r)}), kabı {Tr(Ct(parent) ?? ControlType.Unknown)} '{Short(Nm(parent))}' ({Rect(pr)})");
                if (Visual.Contains(ct)) items.Add((k, r));
                Walk(k, System.Drawing.Rectangle.Intersect(clip, r));
            }
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    var a = items[i].R; var b = items[j].R;
                    var x = System.Drawing.Rectangle.Intersect(a, b);
                    if (x.Width <= 2 || x.Height <= 2 || a.Contains(b) || b.Contains(a)) continue;
                    problems.Add($"**üst üste biniyor:** {Tr(Ct(items[i].E) ?? ControlType.Unknown)} '{Short(Nm(items[i].E))}' ({Rect(a)}) ile " +
                                 $"{Tr(Ct(items[j].E) ?? ControlType.Unknown)} '{Short(Nm(items[j].E))}' ({Rect(b)}) — {x.Width}×{x.Height} px");
                }
        }
        Walk(root, root.Properties.BoundingRectangle.ValueOrDefault);
        return problems;
    }

    private static ControlType? Ct(AutomationElement e) { try { return e.Properties.ControlType.TryGetValue(out var v) ? v : null; } catch { return null; } }
    private static string Nm(AutomationElement e) { try { return e.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; } }

    private static string Tr(ControlType t) => t switch
    {
        ControlType.Button => "düğme", ControlType.Text => "yazı", ControlType.Edit => "kutu", ControlType.ComboBox => "açılır kutu",
        ControlType.CheckBox => "onay kutusu", ControlType.RadioButton => "seçenek", ControlType.Hyperlink => "bağlantı",
        ControlType.Group => "grup", ControlType.Pane => "panel", ControlType.DataGrid or ControlType.Table => "tablo", _ => t.ToString()
    };

    private static string Rect(System.Drawing.Rectangle r) => $"{r.X},{r.Y} {r.Width}×{r.Height}";
    private static string Short(string? s) => s is null ? "" : (s.Length > 40 ? s[..40] + "…" : s).Replace("\n", " ").Replace("\r", "");

    private static bool Like(string? text, string pattern)
        => text is not null && System.Text.RegularExpressions.Regex.IsMatch(text,
               "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
               System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>Sürecin bütün pencereleri: masaüstü çocukları ve (sahipli pencereler) onların içindeki pencereler.</summary>
    private static List<Window> AllWindows(UIA2Automation a, int pid)
    {
        var list = new List<Window>();
        try
        {
            foreach (var top in a.GetDesktop().FindAllChildren(c => c.ByProcessId(pid)))
            {
                list.Add(top.AsWindow());
                foreach (var w in top.FindAllChildren(c => c.ByControlType(ControlType.Window))) list.Add(w.AsWindow());
            }
        }
        catch { }
        return list;
    }

    private static void Wait(Func<bool> cond, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            try { if (cond()) return; } catch { }
            Thread.Sleep(300);
        }
    }
}

internal static class WindowShot
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
}
