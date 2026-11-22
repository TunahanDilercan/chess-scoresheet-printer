using System.Diagnostics;
using System.Reflection;
using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Uzun süren işlerin hızı ve arka planda yürümesi (sporcu kartları, PDF, önizleme).</summary>
public class PerformanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-perf-" + Guid.NewGuid().ToString("N"));
    public PerformanceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static void OnSta(Action a)
    {
        Exception? err = null;
        var t = new Thread(() => { try { a(); } catch (Exception ex) { err = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw new Exception("STA", err);
    }

    private static void PumpUntil(Func<bool> done, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
    }

    [Fact]
    public void Pdf_ReportsProgress_UsesSmallImages_AndCancels()
    {
        var img = AthleteBenchTests.BigImages();
        using var p = new AthleteSheetPrinter(AthleteBenchTests.Many(20), new AthleteCardConfig { FooterBand = true }, img, "x");
        // baskı görselleri 300 dpi'ye küçültülür (2400 px afiş → 95 mm ≈ 1122 px, 1500 px logo → 24 mm ≈ 284 px)
        Assert.True(p.Images.Banner!.Width <= 1130 && p.Images.Logo1!.Width <= 290);
        Assert.NotSame(img.Logo2, p.Images.Logo2);   // her zaman kopya: pencere görselleri kapansa da PDF sürer

        var reports = new List<(int Done, int Total)>();
        var path = Path.Combine(_dir, "a.pdf");
        var sw = Stopwatch.StartNew();
        p.SavePdf(path, new SyncProgress(reports.Add));
        Assert.True(sw.ElapsedMilliseconds < 15000, $"PDF {sw.ElapsedMilliseconds} ms");
        Assert.Equal((p.PageCount, p.PageCount), reports[^1]);
        Assert.Equal(p.PageCount, reports.Count - 1);   // başta 0, sonra her sayfa
        Assert.StartsWith("%PDF", File.ReadAllText(path)[..8]);

        // iptal: dosya yazılmaz, geçici dosya kalmaz
        using var cts = new CancellationTokenSource();
        var cancelled = Path.Combine(_dir, "b.pdf");
        Assert.ThrowsAny<OperationCanceledException>(() => p.SavePdf(cancelled, new SyncProgress(r => { if (r.Item1 == 2) cts.Cancel(); }), cts.Token));
        Assert.False(File.Exists(cancelled));
        Assert.False(File.Exists(cancelled + ".tmp"));
    }

    private sealed class SyncProgress : IProgress<(int Done, int Total)>
    {
        private readonly Action<(int, int)> _a;
        public SyncProgress(Action<(int, int)> a) => _a = a;
        public void Report((int Done, int Total) v) => _a(v);
    }

    [Fact]
    public void Preview_DrawsOnlyVisiblePages()
    {
        OnSta(() =>
        {
            var drawn = new List<int>();
            var ctor = typeof(PagePreviewDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            using var f = (Form)ctor.Invoke(new object[] { "deneme", 30, new SizeF(595, 842), (Action<Graphics, int>)((g, p) => { lock (drawn) drawn.Add(p); g.FillRectangle(Brushes.Black, 10, 10, 50, 50); }),
                                                            "ipucu", (Func<IWin32Window, bool>)(_ => true) });
            f.WindowState = FormWindowState.Normal; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-5000, -5000); f.Size = new Size(900, 900);
            f.ShowInTaskbar = false;
            f.Show();
            PumpUntil(() => { lock (drawn) return drawn.Count >= 2; }, 5000);
            PumpUntil(() => false, 200);
            lock (drawn)
            {
                Assert.Contains(0, drawn);
                Assert.True(drawn.Distinct().Count() <= 2, "yalnız görünen ve sıradaki sayfa çizilmeli: " + string.Join(",", drawn));
            }
            f.Close();
        });
    }

    private static T Call<T>(object o, string name, params object?[] args)
        => (T)o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args)!;

    [Fact]
    public void AthleteList_ManyRowsAndSelectAll_StaysFast()
    {
        OnSta(() =>
        {
            var cfg = new AppConfig();
            var cfgImg = AthleteBenchTests.BigImages();
            using var f = new AthletesForm(new AthletesContext("Deneme", null, Array.Empty<CategoryRef>(), null, cfg, () => { }, _dir, _dir));
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-5000, -5000); f.ShowInTaskbar = false;
            f.Show();
            var sw = Stopwatch.StartNew();
            Call<object?>(f, "Fill", AthleteBenchTests.Many(400), null);
            Call<object?>(f, "SetChecked", false);
            Call<object?>(f, "SetChecked", true);
            PumpUntil(() => false, 100);
            Assert.True(sw.ElapsedMilliseconds < 4000, $"400 satır doldurma + iki kez tümünü seç: {sw.ElapsedMilliseconds} ms");
            Assert.Equal(400, Call<List<Athlete>>(f, "Athletes", false).Count);
            f.Close();
        });
    }

    /// <summary>
    /// Pencere chess-results'ı beklerken kapatılırsa liste sonradan gelince hata vermemeli (v1.7.1'de
    /// "No row can be added to a DataGridView control that does not have columns" penceresi çıkıyordu:
    /// kapanan pencerenin tablosu sütunsuz kalır).
    /// </summary>
    [Theory]
    [InlineData(true)]    // yükleme iptali dinler
    [InlineData(false)]   // iptali dinlemeyen (eski) yükleme: sonuç pencere kapandıktan sonra gelir
    public void ClosingWhileLoading_DoesNotCrash(bool honorsCancel)
    {
        OnSta(() =>
        {
            Exception? uiError = null;
            ThreadExceptionEventHandler h = (_, e) => uiError = e.Exception;
            Application.ThreadException += h;
            try
            {
                var cats = new[] { new CategoryRef(1, "A", true), new CategoryRef(2, "B", false) };
                async Task<IReadOnlyList<Player>> Load(int tnr, CancellationToken ct)
                {
                    await Task.Delay(400, honorsCancel ? ct : CancellationToken.None);
                    return new List<Player> { new(1, $"Sporcu{tnr}, Deneme") };
                }
                var f = new AthletesForm(new AthletesContext("Deneme", 77, cats, Load, new AppConfig(), () => { }, _dir, _dir));
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-5000, -5000); f.ShowInTaskbar = false;
                f.Show();
                var task = Call<Task>(f, "FromChessResultsAsync");
                PumpUntil(() => false, 50);
                f.Close();
                PumpUntil(() => task.IsCompleted, 3000);
                PumpUntil(() => false, 200);
                Assert.True(task.IsCompleted);
                Assert.Null(task.Exception);
                Assert.Null(uiError);
            }
            finally { Application.ThreadException -= h; }
        });
    }

    [Fact]
    public void AthletesWindow_OpensQuickly()
    {
        OnSta(() =>
        {
            var cfg = new AppConfig();   // TSF il logosu kendiliğinden (varsayılan) çizilir
            var times = new List<long>();
            for (int i = 0; i < 2; i++)
            {
                var sw = Stopwatch.StartNew();
                using var f = new AthletesForm(new AthletesContext("Deneme", null, Array.Empty<CategoryRef>(), null, cfg, () => { }, _dir, _dir));
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-5000, -5000); f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                times.Add(sw.ElapsedMilliseconds);
                f.Close();
            }
            Assert.True(times[^1] < 1500, "pencere açılışı: " + string.Join(", ", times) + " ms");
        });
    }

    [Fact]
    public void ChessResults_CategoriesLoadInParallel_WithProgressBar()
    {
        OnSta(() =>
        {
            var cats = Enumerable.Range(1, 8).Select(i => new CategoryRef(i, $"Kategori {i}", i == 1)).ToArray();
            int active = 0, peak = 0;
            async Task<IReadOnlyList<Player>> Load(int tnr, CancellationToken ct)
            {
                int now = Interlocked.Increment(ref active);
                lock (cats) peak = Math.Max(peak, now);
                await Task.Delay(300, ct);
                Interlocked.Decrement(ref active);
                return new List<Player> { new(1, $"Sporcu{tnr}, Deneme", Rating: 1500) };
            }
            using var f = new AthletesForm(new AthletesContext("Deneme", 99, cats, Load, new AppConfig(), () => { }, _dir, _dir));
            f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-5000, -5000); f.ShowInTaskbar = false;
            f.Show();
            var sw = Stopwatch.StartNew();
            var task = Call<Task>(f, "FromChessResultsAsync");
            bool sawBar = false;
            var busy = (Control)f.GetType().GetField("_busy", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;
            PumpUntil(() => { sawBar |= busy.Visible; return task.IsCompleted; }, 10000);
            Assert.True(task.IsCompletedSuccessfully);
            Assert.True(sawBar, "ilerleme çubuğu görünmedi");
            Assert.False(busy.Visible);
            Assert.InRange(peak, 2, 4);                                  // aynı anda en çok 4 istek
            Assert.True(sw.ElapsedMilliseconds < 1800, $"{sw.ElapsedMilliseconds} ms (sırayla 2400 ms sürerdi)");
            Assert.Equal(8, Call<List<Athlete>>(f, "Athletes", true).Count);
            f.Close();
        });
    }
}
