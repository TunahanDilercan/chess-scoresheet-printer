namespace NotasyonOtomasyonu.App.Update;

/// <summary>
/// Güncelleme penceresi: denetler (ya da verilen sürümü gösterir), sürüm notlarını başlıklı ve maddeli gösterir;
/// "Şimdi güncelle" paketi indirir (yüzde, boyut ve kalan süreyle), doğrular, kurar ve programı yeni sürümle açar.
/// İndirme sürerken "Daha sonra" düğmesi "İptal" olur.
/// </summary>
public sealed class UpdateForm : AppForm
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Ink = Color.FromArgb(40, 44, 36);

    private readonly Label _lblHead = new() { AutoSize = true, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0) };
    private readonly Label _lblSub = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(1, 4, 0, 0) };
    private readonly RichTextBox _notes = new()
    {
        ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Dock = DockStyle.Fill, DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical, Font = new Font("Segoe UI", 9.75f), AccessibleName = "Sürüm notları", TabStop = false
    };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Bottom, Height = 6, Visible = false, Style = ProgressBarStyle.Continuous };
    private readonly Label _lblStatus = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 30, ForeColor = Ink, Padding = new Padding(16, 7, 16, 0), AutoEllipsis = true };
    private readonly Button _btnUpdate = Icons.Set(new Button { Text = "Şimdi güncelle", Width = 190, Height = 38, BackColor = Green, ForeColor = Color.White, Visible = false }, Ico.Update);
    private readonly Button _btnClose = new() { Text = "Kapat", Width = 110, Height = 38 };
    private readonly LinkLabel _lnkSkip = new() { Text = "Bu sürümü atla", AutoSize = true, Visible = false, Margin = new Padding(14, 14, 4, 0) };
    private readonly LinkLabel _lnkWeb = new() { Text = "Tüm sürüm notları", AutoSize = true, Margin = new Padding(4, 14, 4, 0) };
    private readonly Core.UpdateConfig _cfg;
    private readonly Action _save;
    private ReleaseInfo? _release;
    private CancellationTokenSource? _cts;
    private VeloPlan? _plan;
    private readonly System.Diagnostics.Stopwatch _clock = new();

    /// <summary>Güncelleme yolu: kurulu sürüm (yalnız değişenler), kuruluma geçiş (Setup) ya da eski usul ZIP.</summary>
    internal enum Route { Installed, Migrate, Zip }

    /// <summary>Hangi yoldan güncellenecek (test edilebilsin diye ayrı).</summary>
    internal static Route RouteFor(bool installed, bool portableMode, ReleaseInfo r)
        => installed ? Route.Installed : (!portableMode && r.HasSetup ? Route.Migrate : Route.Zip);

    private Route CurrentRoute => RouteFor(Velo.IsInstalled, AppPaths.IsPortableMode, _release!);

    /// <summary>
    /// Yapılacak bir şey var mı: daha yeni sürüm ya da (taşınabilir exe aynı sürümdeyken) kurulu sürüme geçiş.
    /// v1.6.x'ten ZIP ile güncellenen exe, bir sonraki sürümü beklemeden hemen kuruluma geçebilsin.
    /// </summary>
    internal static bool Actionable(ReleaseInfo r, Version current, bool installed, bool portableMode)
        => r.Version > current || RouteFor(installed, portableMode, r) == Route.Migrate && r.Version >= current;

    /// <summary><paramref name="release"/> verilirse denetim atlanır (açılıştaki bildirimden gelindi).</summary>
    public UpdateForm(Core.UpdateConfig cfg, Action save, ReleaseInfo? release = null)
    {
        _cfg = cfg;
        _save = save;
        _release = release;
        Text = "Güncelleme — Notasyon Yazdırıcı";
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 500);
        MinimumSize = new Size(480, 380);
        BackColor = Color.FromArgb(250, 250, 247);
        MinimizeBox = false;

        // Üst: başlık ve sürüm satırı
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                                         Padding = new Padding(16, 14, 16, 10), BackColor = Color.White };
        head.Controls.AddRange(new Control[] { _lblHead, _lblSub });
        var headLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(226, 228, 220) };

        // Orta: sürüm notları
        var notesHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 6), BackColor = Color.White };
        notesHost.Controls.Add(_notes);

        // Alt: bağlantılar solda, düğmeler sağda
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.FromArgb(242, 242, 236), Padding = new Padding(10, 12, 12, 12) };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        right.Controls.AddRange(new Control[] { _btnUpdate, _btnClose });
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        left.Controls.AddRange(new Control[] { _lnkWeb, _lnkSkip });
        bottom.Controls.Add(right);
        bottom.Controls.Add(left);
        foreach (var b in new[] { _btnUpdate, _btnClose })
        {
            b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand; b.Margin = new Padding(4, 0, 4, 0);
            b.FlatAppearance.BorderColor = b == _btnUpdate ? GreenDark : Color.FromArgb(208, 208, 198);
            if (b != _btnUpdate) b.BackColor = Color.White;
        }
        _btnUpdate.Font = new Font(Font, FontStyle.Bold);

        Controls.Add(notesHost);
        Controls.Add(headLine);
        Controls.Add(head);
        Controls.Add(_lblStatus);
        Controls.Add(_bar);
        Controls.Add(bottom);
        CancelButton = _btnClose;

        _lnkWeb.LinkClicked += (_, _) => Open(_release?.HtmlUrl ?? Updater.ReleasesPage);
        _lnkSkip.LinkClicked += (_, _) => { if (_release is not null) { _cfg.SkippedVersion = _release.Version.ToString(); _save(); } Close(); };
        _btnUpdate.Click += async (_, _) => await UpdateAsync();
        // İndirme sürerken bu düğme "İptal"dir: pencereyi kapatmaz, indirmeyi durdurur.
        _btnClose.Click += (_, _) => { if (_cts is not null) _cts.Cancel(); else Close(); };
        FormClosing += (_, e) =>
        {
            if (_cts is not null && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(this, "İndirme sürüyor. İptal edilsin mi?", "Güncelleme", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) e.Cancel = true;
                else _cts.Cancel();
            }
        };
        Shown += async (_, _) => { if (_release is null) await CheckAsync(); else ShowRelease(); };
    }

    private async Task CheckAsync()
    {
        _lblHead.Text = "Güncellemeler denetleniyor…";
        _lblSub.Text = $"Kullandığınız sürüm: v{Updater.Current}";
        _notes.Clear();
        try
        {
            _release = await Updater.CheckAsync();
            _cfg.LastCheck = DateTime.Now;
            _save();
            if (IsDisposed) return;
            ShowRelease();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblHead.Text = "Güncelleme denetlenemedi";
            _lblSub.Text = $"Kullandığınız sürüm: v{Updater.Current}";
            RenderNotes(ex is HttpRequestException or TaskCanceledException
                ? "GitHub'a ulaşılamadı. İnternet bağlantısını denetleyip yeniden deneyin."
                : ex.Message, null);
        }
    }

    private void ShowRelease()
    {
        var cur = Updater.Current;
        if (_release is null)
        {
            _lblHead.Text = "Sürüm bilgisi bulunamadı";
            _lblSub.Text = $"Kullandığınız sürüm: v{cur}";
            return;
        }
        bool isNewer = Updater.IsNewer(_release);
        bool actionable = Actionable(_release, cur, Velo.IsInstalled, AppPaths.IsPortableMode);
        var date = _release.Published is { } d ? $" • {d:d MMMM yyyy}" : "";
        if (isNewer)
        {
            _lblHead.Text = "Yeni sürüm hazır";
            _lblSub.Text = $"v{cur}  →  v{_release.Version}{date}";
        }
        else if (actionable)
        {
            _lblHead.Text = "Yeni güncelleme sistemine geçiş";
            _lblSub.Text = $"v{cur}{date}";
        }
        else
        {
            _lblHead.Text = "Güncelsiniz";
            _lblSub.Text = $"v{cur} en son sürüm{date} • Bu sürümde neler var:";
        }
        RenderNotes(_release.Notes, null);
        _btnUpdate.Visible = _btnUpdate.Enabled = actionable;
        _lnkSkip.Visible = isNewer;
        _btnClose.Text = actionable ? "Daha sonra" : "Kapat";
        _lblStatus.Text = "";
        if (!actionable) return;
        AcceptButton = _btnUpdate;
        switch (CurrentRoute)
        {
            case Route.Installed:
                _lblStatus.Text = "İndirilecek boyut hesaplanıyor…";
                _btnUpdate.Enabled = false;
                _ = PlanAsync();
                break;
            case Route.Migrate:
                _btnUpdate.Text = "Kur ve yeniden başlat";
                _lblStatus.Text = $"Tek seferlik kurulum ({_release.SetupSize / 1048576.0:0} MB) • ayarlarınız ve şablonlarınız korunur.";
                RenderNotes(_release.Notes,
                    "Bu güncellemede program bilgisayara kurulur.\n" +
                    "- Masaüstüne ve Başlat menüsüne \"Notasyon Yazdırıcı\" kısayolu eklenir.\n" +
                    "- Sonraki güncellemelerde yalnız değişen kısımlar (genelde birkaç MB) iner.\n" +
                    "- Ayarlarınız, şablonlarınız ve çıktılarınız olduğu gibi kalır.");
                break;
            default:
                _lblStatus.Text = $"İndirilecek: {_release.ZipSize / 1048576.0:0} MB • ayarlarınız ve şablonlarınız korunur.";
                break;
        }
    }

    /// <summary>
    /// Sürüm notu (Markdown) başlıklı ve maddeli gösterilir: "## Başlık" kalın ve yeşil, "- madde" madde imli.
    /// İngilizce bölüm ("---" sonrası) gösterilmez. <paramref name="intro"/> notların üstünde vurgulu kutu gibi yazılır.
    /// </summary>
    private void RenderNotes(string md, string? intro)
    {
        _notes.Clear();
        var bold = new Font(_notes.Font.FontFamily, 10.5f, FontStyle.Bold);
        void Line(string text, Font font, Color color, bool bullet, int before)
        {
            _notes.SelectionStart = _notes.TextLength;
            _notes.SelectionLength = 0;
            if (before > 0 && _notes.TextLength > 0) _notes.AppendText("\n");
            _notes.SelectionFont = font;
            _notes.SelectionColor = color;
            _notes.SelectionBullet = bullet;
            _notes.SelectionIndent = bullet ? 6 : 0;
            _notes.SelectionHangingIndent = bullet ? 14 : 0;
            _notes.AppendText(text + "\n");
            _notes.SelectionBullet = false;
        }
        if (intro is not null)
        {
            foreach (var l in intro.Replace("\r\n", "\n").Split('\n'))
                if (l.StartsWith("- ")) Line(l[2..], _notes.Font, Ink, true, 0);
                else if (l.Length > 0) Line(l, bold, Color.FromArgb(150, 100, 20), false, 0);
        }
        md = md.Replace("\r\n", "\n");
        int en = md.IndexOf("\n---", StringComparison.Ordinal);
        if (en >= 0) md = md[..en];
        foreach (var raw in md.Split('\n'))
        {
            var t = raw.Trim().Replace("**", "").Replace("`", "");
            if (t.Length == 0) continue;
            if (t.StartsWith('#')) Line(t.TrimStart('#', ' '), bold, GreenDark, false, 1);
            else if (t.StartsWith("- ") || t.StartsWith("* ")) Line(t[2..], _notes.Font, Ink, true, 0);
            else Line(t, _notes.Font, Ink, false, 0);
        }
        _notes.SelectionStart = 0;
        _notes.ScrollToCaret();
    }

    /// <summary>Kurulu sürümde: GitHub'daki paket listesinden ne kadar indirileceği.</summary>
    private async Task PlanAsync()
    {
        try
        {
            var plan = Velo.LastPlan is { } lp && lp.Version == _release?.Version ? lp : await Task.Run(Velo.PlanAsync);
            if (IsDisposed) return;
            _plan = plan;
            _lblStatus.Text = plan is null
                ? "Güncelleme paketi henüz hazır değil; biraz sonra yeniden deneyin."
                : plan.UsesDelta
                    ? $"İndirilecek: {Mb(plan.DownloadBytes)} (yalnız değişen kısımlar) • ayarlarınız ve şablonlarınız korunur."
                    : $"İndirilecek: {Mb(plan.DownloadBytes)} • ayarlarınız ve şablonlarınız korunur.";
            _btnUpdate.Enabled = plan is not null;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblStatus.Text = "Paket bilgisi alınamadı: " + (ex is HttpRequestException ? "bağlantı yok." : ex.Message);
        }
    }

    private static string Mb(long bytes) => bytes < 1048576 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / 1048576.0:0.0} MB";

    /// <summary>Kalan süre (geçen süre ve ilerlemeden): "yaklaşık 12 sn kaldı".</summary>
    private string Remaining(double fraction)
    {
        if (fraction <= 0.02 || fraction >= 1 || _clock.Elapsed.TotalSeconds < 1) return "";
        double left = _clock.Elapsed.TotalSeconds * (1 - fraction) / fraction;
        return left < 60 ? $" • yaklaşık {Math.Max(1, (int)left)} sn kaldı" : $" • yaklaşık {(int)Math.Ceiling(left / 60)} dk kaldı";
    }

    /// <summary>İndirme başlar: düğmeler kilitlenir, "Daha sonra" → "İptal".</summary>
    private void BeginDownload()
    {
        _btnUpdate.Enabled = false;
        _lnkSkip.Enabled = false;
        _btnClose.Text = "İptal";
        _bar.Visible = true;
        _bar.Value = 0;
        _cts = new CancellationTokenSource();
        _clock.Restart();
    }

    private void EndDownload()
    {
        _cts = null;
        _clock.Stop();
        if (IsDisposed) return;
        _bar.Visible = false;
        _btnUpdate.Enabled = _lnkSkip.Enabled = true;
        _btnClose.Text = "Daha sonra";
    }

    private void ShowProgress(string what, long done, long total)
    {
        if (IsDisposed) return;
        double f = total > 0 ? Math.Min(1, (double)done / total) : 0;
        _bar.Value = (int)(f * 100);
        _lblStatus.Text = $"{what} %{(int)(f * 100)} • {Mb(done)} / {Mb(Math.Max(total, 1))}{Remaining(f)}";
    }

    private async Task UpdateAsync()
    {
        if (_release is null) return;
        var route = CurrentRoute;
        if (route == Route.Installed) { await UpdateInstalledAsync(); return; }
        if (route == Route.Migrate) { await MigrateAsync(); return; }
        if (!Updater.CanWriteAppDir())
        {
            MessageBox.Show(this, "Programın bulunduğu klasöre yazılamıyor (ör. Program Files). Yeni sürümü GitHub'dan indirip " +
                                  "elle çıkarın ya da programı yazılabilir bir klasöre taşıyın.", "Güncelleme",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Open(_release.HtmlUrl);
            return;
        }
        BeginDownload();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p => ShowProgress("İndiriliyor…", p.Done, p.Total));
            var dir = await Updater.DownloadAsync(_release, progress, _cts!.Token);
            _cts = null;
            _lblStatus.Text = "Paket doğrulandı; kuruluyor…";
            _save(); // ayarlar yeni sürüm açılmadan yazılsın
            Updater.Install(dir);
            _lblStatus.Text = "Yeni sürüm açılıyor…";
            Application.Exit();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _lblStatus.Text = "İndirme iptal edildi; program değişmedi."; }
        catch (Exception ex) { Fail(ex); }
        finally { EndDownload(); }
    }

    /// <summary>Kurulu sürüm: değişen kısımları indirir, Velopack'e uygulatır, program yeniden açılır.</summary>
    private async Task UpdateInstalledAsync()
    {
        BeginDownload();
        try
        {
            _plan ??= await Task.Run(Velo.PlanAsync);
            if (_plan is null) { _lblStatus.Text = "Güncelleme paketi bulunamadı."; return; }
            var plan = _plan;
            long size = plan.DownloadBytes;
            void Report(int pct)
            {
                if (IsDisposed) return;
                BeginInvoke(() => ShowProgress(plan.UsesDelta ? "İndiriliyor (yalnız değişenler)…" : "İndiriliyor…", size * Math.Clamp(pct, 0, 100) / 100, size));
            }
            await Task.Run(() => Velo.DownloadAsync(plan, Report, _cts!.Token));
            _cts = null;
            _bar.Value = 100;
            _lblStatus.Text = "İndirildi ve doğrulandı; yeni sürüm kuruluyor…";
            _save();
            Velo.ApplyAndRestart(plan);
            Application.Exit();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _lblStatus.Text = "İndirme iptal edildi; program değişmedi."; }
        catch (Exception ex) { Fail(ex); }
        finally { EndDownload(); }
    }

    /// <summary>Taşınabilir exe → kurulu sürüm: Setup indirilir, doğrulanır ve çalıştırılır.</summary>
    private async Task MigrateAsync()
    {
        BeginDownload();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p => ShowProgress("Kurulum indiriliyor…", p.Done, p.Total));
            var setup = await Updater.DownloadSetupAsync(_release!, progress, _cts!.Token);
            _cts = null;
            _lblStatus.Text = "Doğrulandı; kurulum başlıyor…";
            _save();
            Updater.RunSetup(setup);
            Application.Exit();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _lblStatus.Text = "İndirme iptal edildi; program değişmedi."; }
        catch (Exception ex) { Fail(ex); }
        finally { EndDownload(); }
    }

    private void Fail(Exception ex)
    {
        if (IsDisposed) return;
        _lblStatus.Text = "Güncelleme yapılamadı; program değişmedi.";
        MessageBox.Show(this, "Güncelleme yapılamadı: " + (ex is HttpRequestException ? "indirme sırasında bağlantı kesildi." : ex.Message) +
                              Environment.NewLine + Environment.NewLine + "Program değişmedi; yeni sürümü GitHub'dan elle de indirebilirsiniz.",
                        "Güncelleme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
