using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Badges;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Lists;
using NotasyonOtomasyonu.App.Registration;
using NotasyonOtomasyonu.App.Reports;
using NotasyonOtomasyonu.App.Update;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;
using Xunit.Abstractions;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Ekran ölçeği (%125, %150) benzetimi: Windows'un yaptığı gibi yazı tipleri büyütülür, pencere de
/// <see cref="AppForm"/>'un gerçek ekranda yaptığı gibi ölçeklenir; sonra kesilen yazı ve taşan denetim aranır.
/// CSP_FORM_SHOTS verilirse ölçekli pencerelerin görüntüsü kaydedilir.
/// </summary>
public class DpiScalingTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-dpi-" + Guid.NewGuid().ToString("N"));
    public DpiScalingTests(ITestOutputHelper o) { _out = o; Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Ağa çıkmadan hemen hata veren bağlantı (pencereler açılışta veri çekmeye çalışır).</summary>
    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("çevrimdışı deneme"));
    }

    private Dictionary<string, Func<Form>> Forms()
    {
        var cfg = new AppConfig();
        var cats = new[] { new CategoryRef(1, "12 Yaş", true), new CategoryRef(2, "14 Yaş Kızlar", false) };
        var svc = new ChessResultsService(new ChessResultsClient(new HttpClient(new Offline())));
        var release = new ReleaseInfo(new Version(9, 9, 9), "v9.9.9", "Deneme notları", "https://example.invalid", DateTime.Now,
                                      "https://example.invalid/z.zip", "z.zip", 1000, null);
        return new()
        {
            ["athletes"] = () => new AthletesForm(new AthletesContext("Deneme Turnuvası", null, cats, null, cfg, () => { }, _dir, _dir)),
            ["badges"] = () => new BadgesForm(new BadgesContext("Deneme Turnuvası",
                () => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()), cfg, () => { }, _dir, _dir)),
            ["cards"] = () => new CardsForm(new CardsContext("Deneme Turnuvası", cats, _ => Task.FromResult((8, "deneme")), cfg, () => { }, _dir, _dir)),
            ["settings"] = () => new SettingsForm(cfg, _ => { }, _ => { }),
            ["templates"] = () => new TemplatesForm(cfg),
            ["log"] = () => new LogWindow(() => "Basıldı: deneme", () => { }, () => "ayrıntı"),
            ["update"] = () => new UpdateForm(cfg.Updates, () => { }, release),
            ["exclusions"] = () => new ExclusionsForm(cfg, new[] { 1, 2, 3 }),
            ["meta"] = () => new MetaEditForm(cfg),
            ["printer"] = () => new NotasyonOtomasyonu.App.Overlay.PrinterSettingsForm(cfg.Printing),
            ["incidents"] = () => new IncidentsForm(new IncidentsContext(1, "Deneme", new[] { "12 Yaş" }, "12 Yaş", 1, null, null, cfg, () => { })),
            ["arbiter"] = () => new ArbiterSheetsForm(new ArbiterSheetsContext("Deneme", cats, (_, _) => Task.FromResult<ArbiterSheet?>(null),
                _ => Task.FromResult(new List<ArbiterSheet>()), cfg.ArbiterSheets, () => { }, _dir)),
            ["registration"] = () => new RegistrationForm(new RegistrationContext(svc, 1, "Deneme", cats, "Isparta", cfg, () => { })),
            ["report"] = () => new ReportForm(new ReportContext(svc, 1, "Deneme Turnuvası", cats, "Isparta",
                _ => Task.FromResult((7, TournamentSystem.Swiss)), cfg, () => { }, _dir, _dir)),
            ["fields"] = () => new FieldListForm(null),
        };
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void ScaledWindows_HaveNoClippedText(float factor)
    {
        var shots = Environment.GetEnvironmentVariable("CSP_FORM_SHOTS");
        var all = new List<string>();
        foreach (var (name, make) in Forms())
        {
            // "eski" (ölçeklemesiz) görünüm yalnız karşılaştırma görüntüsü istenince çizilir
            foreach (bool fixScale in string.IsNullOrEmpty(shots) ? new[] { true } : new[] { false, true })
            {
                List<string>? problems = null;
                Exception? err = null;
                var t = new Thread(() =>
                {
                    try
                    {
                        UiScale.Override = fixScale ? factor : 1f;
                        using var f = make();
                        SimulateSystemFonts(f, factor);
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new Point(-6000, -6000);
                        f.ShowInTaskbar = false;
                        f.WindowState = FormWindowState.Normal;
                        f.Show();
                        for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(20); }
                        problems = Problems(f).ToList();
                        if (!string.IsNullOrEmpty(shots))
                        {
                            using var bmp = new Bitmap(f.Width, f.Height);
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                            bmp.Save(Path.Combine(shots, $"dpi{factor * 100:0}_{(fixScale ? "yeni" : "eski")}_{name}.png"));
                        }
                        f.Close();
                    }
                    catch (Exception ex) { err = ex; }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start(); t.Join();
                if (err is not null) throw new Exception(name, err);
                _out.WriteLine($"{name} %{factor * 100:0} {(fixScale ? "yeni" : "eski")}: {problems!.Count} sorun");
                foreach (var p in problems.Take(12)) _out.WriteLine("    " + p);
                if (fixScale) all.AddRange(problems.Select(p => $"{name}: {p}"));
            }
        }
        Assert.True(all.Count == 0, "Ölçekli pencerelerde sorun:\n" + string.Join("\n", all));
    }

    /// <summary>Windows'un ekran ölçeğinde yaptığı gibi: açıkça verilmiş bütün yazı tipleri (ve pencereninki) büyür.</summary>
    internal static void SimulateSystemFonts(Form form, float f)
    {
        var explicitFonts = new List<Control> { form };
        void Collect(Control c) { foreach (Control k in c.Controls) { if (!ReferenceEquals(k.Font, c.Font)) explicitFonts.Add(k); Collect(k); } }
        Collect(form);
        var grids = new List<DataGridView>();
        void Grids(Control c) { foreach (Control k in c.Controls) { if (k is DataGridView g) grids.Add(g); Grids(k); } }
        Grids(form);
        // tablo hücre stillerinin yalnız açıkça verilmiş yazı tipleri (tablonun kendi yazı tipini taşıyanlar onunla büyür)
        var styleFonts = grids.SelectMany(g => new[] { g.DefaultCellStyle, g.ColumnHeadersDefaultCellStyle, g.RowsDefaultCellStyle, g.AlternatingRowsDefaultCellStyle }
                                                  .Where(st => st.Font is { } sf && !sf.Equals(g.Font)).Select(st => (st, st.Font!))).ToList();
        var sizes = explicitFonts.Select(c => (c, c.Font)).ToList();
        foreach (var (c, font) in sizes) c.Font = new Font(font.FontFamily, font.Size * f, font.Style, font.Unit);
        foreach (var (st, sf) in styleFonts) st.Font = new Font(sf.FontFamily, sf.Size * f, sf.Style, sf.Unit);
    }

    /// <summary>Görünür denetimlerde kesilen yazı, taşan denetim ve dar tablo sütunu.</summary>
    internal static IEnumerable<string> Problems(Form form)
    {
        foreach (var c in All(form))
        {
            if (!c.Visible || c.Width <= 0) continue;
            string id = $"{c.GetType().Name} '{Short(c.Text)}'";
            if (c is ButtonBase b && !b.AutoSize && !string.IsNullOrWhiteSpace(b.Text) && !(b is Button { AutoEllipsis: true }))
            {
                int need = TextRenderer.MeasureText(b.Text, b.Font).Width;
                int glyph = b is CheckBox or RadioButton ? 20 : 0;
                int avail = b.ClientSize.Width - b.Padding.Horizontal - 6 - glyph;
                if (need > avail) yield return $"{id}: yazı kesik ({need} > {avail} px)";
            }
            if (c is Label l && !l.AutoSize && !l.AutoEllipsis && !string.IsNullOrWhiteSpace(l.Text) && c is not LinkLabel { Dock: DockStyle.Fill })
            {
                var sz = TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(1, l.ClientSize.Width - l.Padding.Horizontal), 0), TextFormatFlags.WordBreak);
                if (sz.Height > l.ClientSize.Height - l.Padding.Vertical + 3) yield return $"{id}: yazı sığmıyor ({sz.Height} > {l.ClientSize.Height} px yükseklik)";
            }
            // taşan denetim: kendi boyunu almayan, kaydırmayan kapta kalan kısmı görünmez
            if (c.Parent is { } p && c.Dock == DockStyle.None && !p.AutoSize && !(p is ScrollableControl { AutoScroll: true }))
            {
                if (c.Right > p.ClientSize.Width + 2) yield return $"{id}: sağdan taşıyor ({c.Right} > {p.ClientSize.Width})";
                if (c.Bottom > p.ClientSize.Height + 2 && p is not FlowLayoutPanel) yield return $"{id}: alttan taşıyor ({c.Bottom} > {p.ClientSize.Height})";
            }
            if (c is DataGridView g && g.ColumnHeadersVisible)
            {
                // başlık satırı yazıya yetecek yükseklikte mi
                var hfont = g.ColumnHeadersDefaultCellStyle.Font ?? g.Font;
                if (g.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize && g.ColumnHeadersHeight < hfont.Height + 4)
                    yield return $"tablo başlık satırı alçak ({g.ColumnHeadersHeight} < {hfont.Height + 4})";
                // yatay kaydırma: sütunlar tabloya sığmıyor
                int cols = g.Columns.Cast<DataGridViewColumn>().Where(k => k.Visible).Sum(k => k.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill ? k.MinimumWidth : k.Width);
                if (cols > g.ClientSize.Width - 4) yield return $"tablo yatay kayıyor (sütunlar {cols} > {g.ClientSize.Width})";
            }
            if (c is DataGridView g2)
                foreach (DataGridViewColumn col in g2.Columns)
                {
                    if (!col.Visible || string.IsNullOrWhiteSpace(col.HeaderText)) continue;
                    var hf = g2.ColumnHeadersDefaultCellStyle.Font ?? g2.Font;
                    // başlık satır kaydırıyorsa en uzun kelimenin sığması yeter
                    bool wraps = g2.ColumnHeadersDefaultCellStyle.WrapMode == DataGridViewTriState.True;
                    int need = wraps ? col.HeaderText.Split(' ').Max(w => UiScale.HeaderWidth(w, hf)) : UiScale.HeaderWidth(col.HeaderText, hf) - 2;
                    if (need > col.Width) yield return $"tablo sütunu '{col.HeaderText}' dar ({need} > {col.Width})";
                }
        }
    }

    private static string Short(string s) => s.Length > 28 ? s[..28] + "…" : s.Replace("\n", " ");

    private static IEnumerable<Control> All(Control c)
    {
        foreach (Control k in c.Controls) { yield return k; foreach (var d in All(k)) yield return d; }
    }
}
