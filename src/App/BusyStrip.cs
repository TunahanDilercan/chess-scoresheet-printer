namespace NotasyonOtomasyonu.App;

/// <summary>
/// Uzun işler için sade ilerleme şeridi: çubuk + "PDF (18 sayfa): 12 / 18 sayfa" yazısı + İptal. İş arka planda
/// yürür; pencere ve program kullanılmaya devam eder (dönen bekleme imleci yerine).
/// </summary>
public sealed class BusyStrip : FlowLayoutPanel
{
    private readonly ProgressBar _bar = new() { AccessibleName = "İşlemin ilerlemesi", Width = 220, Height = 18, Margin = new Padding(0, 3, 8, 0) };
    private readonly Label _text = new() { AccessibleName = "İşlem durumu", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 3, 8, 0) };
    private readonly LinkLabel _cancel = new() { Text = "İptal", AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
    private CancellationTokenSource? _cts;

    /// <summary>İş başlayınca true, bitince false (pencere düğmeleri kapatıp açar).</summary>
    public event Action<bool>? BusyChanged;

    public bool IsBusy => _cts is not null;

    public BusyStrip()
    {
        AutoSize = true;
        WrapContents = false;
        Visible = false;
        Margin = new Padding(12, 8, 0, 0);
        Controls.AddRange(new Control[] { _bar, _text, _cancel });
        _cancel.LinkClicked += (_, _) => Cancel();
    }

    public void Cancel()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _text.Text = "İptal ediliyor…";
    }

    /// <summary>
    /// İşi çalıştırır; ilerleme şeritte görünür. İptal ya da hata olursa <c>default</c> döner (hata <paramref name="onError"/>'a).
    /// Aynı anda tek iş: şerit doluyken çağrılırsa hemen <c>default</c> döner.
    /// </summary>
    public async Task<T?> RunAsync<T>(string title, string unit, Func<IProgress<(int Done, int Total)>, CancellationToken, Task<T>> work,
                                      Action<Exception>? onError = null)
    {
        if (_cts is not null) return default;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        Visible = true;
        _bar.Style = ProgressBarStyle.Marquee;
        _text.Text = title + "…";
        BusyChanged?.Invoke(true);
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            if (IsDisposed || p.Total <= 0 || cts.IsCancellationRequested) return;
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Maximum = p.Total;
            _bar.Value = Math.Clamp(p.Done, 0, p.Total);
            _text.Text = $"{title}: {p.Done} / {p.Total} {unit}";
        });
        try { return await work(progress, cts.Token); }
        catch (OperationCanceledException) { return default; }
        catch (Exception ex) { if (!IsDisposed) onError?.Invoke(ex); return default; }
        finally
        {
            _cts = null;
            if (!IsDisposed)
            {
                Visible = false;
                BusyChanged?.Invoke(false);
            }
            ReleaseMemory();
        }
    }

    /// <summary>
    /// Yazdırma/PDF gibi işlerde sayfa başına ~26 MB'lık resimler büyük nesne yığınında birikir; iş bitince
    /// bellek sıkıştırılıp sisteme geri verilir (program uzun açık kalınca RAM şişmesin).
    /// </summary>
    internal static void ReleaseMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: true);
    }

    /// <summary>Kullanıcıya gösterilecek kısa hata açıklaması.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException => "internet bağlantısını kontrol edin.",
        IOException => "dosya başka bir programda açık olabilir (PDF görüntüleyiciyi kapatıp yeniden deneyin).",
        System.ComponentModel.Win32Exception or System.Drawing.Printing.InvalidPrinterException => "yazıcıya ulaşılamadı: " + ex.Message,
        _ => ex.Message
    };
}
