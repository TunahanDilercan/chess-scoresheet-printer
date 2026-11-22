namespace NotasyonOtomasyonu.App.Update;

/// <summary>
/// Güncelleme penceresi: denetler (ya da verilen sürümü gösterir), sürüm notlarını listeler; "Güncelle ve
/// yeniden başlat" paketi indirir, doğrular, kurar ve programı yeni sürümle açar.
/// </summary>
public sealed class UpdateForm : AppForm
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);

    private readonly Label _lblHead = new() { AutoSize = false, Dock = DockStyle.Top, Height = 54, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = GreenDark, Padding = new Padding(14, 12, 14, 0) };
    private readonly TextBox _txtNotes = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.75f) };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Bottom, Height = 8, Visible = false, Style = ProgressBarStyle.Continuous };
    private readonly Label _lblStatus = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.DimGray, Padding = new Padding(14, 4, 14, 0) };
    private readonly Button _btnUpdate = Icons.Set(new Button { Text = "Güncelle ve yeniden başlat", AutoSize = true, Height = 36, BackColor = Green, ForeColor = Color.White, Enabled = false }, Ico.Update);
    private readonly Button _btnSkip = new() { Text = "Bu sürümü atla", AutoSize = true, Height = 36, Visible = false };
    private readonly Button _btnClose = new() { Text = "Kapat", AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };
    private readonly LinkLabel _lnkWeb = new() { Text = "GitHub'da aç", AutoSize = true, Margin = new Padding(8, 11, 8, 0) };
    private readonly Core.UpdateConfig _cfg;
    private readonly Action _save;
    private ReleaseInfo? _release;
    private CancellationTokenSource? _cts;
    private VeloPlan? _plan;

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
        Text = "Güncelleme";
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 520);
        MinimumSize = new Size(480, 360);
        BackColor = Color.FromArgb(250, 250, 247);
        MinimizeBox = false;

        var notesHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 8) };
        notesHost.Controls.Add(_txtNotes);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 10, 10, 0), BackColor = Color.FromArgb(242, 242, 236) };
        bottom.Controls.AddRange(new Control[] { _btnClose, _btnUpdate, _btnSkip, _lnkWeb });
        foreach (var b in new[] { _btnUpdate, _btnSkip, _btnClose })
        {
            b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand; b.Margin = new Padding(4, 0, 4, 0);
            b.FlatAppearance.BorderColor = b == _btnUpdate ? GreenDark : Color.FromArgb(208, 208, 198);
            if (b != _btnUpdate) b.BackColor = Color.White;
        }
        _btnUpdate.Font = new Font(Font, FontStyle.Bold);
        Controls.Add(notesHost);
        Controls.Add(_lblHead);
        Controls.Add(_lblStatus);
        Controls.Add(_bar);
        Controls.Add(bottom);
        CancelButton = _btnClose;

        _lnkWeb.LinkClicked += (_, _) => Open(_release?.HtmlUrl ?? Updater.ReleasesPage);
        _btnSkip.Click += (_, _) => { if (_release is not null) { _cfg.SkippedVersion = _release.Version.ToString(); _save(); } Close(); };
        _btnUpdate.Click += async (_, _) => await UpdateAsync();
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
        _txtNotes.Text = "";
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
            _txtNotes.Text = (ex is HttpRequestException or TaskCanceledException
                ? "GitHub'a ulaşılamadı (internet bağlantısını denetleyin)."
                : ex.Message) + Environment.NewLine + Environment.NewLine + "Sürümler: " + Updater.ReleasesPage;
        }
    }

    private void ShowRelease()
    {
        var cur = Updater.Current;
        if (_release is null)
        {
            _lblHead.Text = $"Sürüm bilgisi bulunamadı (kullandığınız: v{cur})";
            return;
        }
        bool isNewer = Updater.IsNewer(_release);
        bool newer = Actionable(_release, cur, Velo.IsInstalled, AppPaths.IsPortableMode);
        _lblHead.Text = isNewer ? $"Yeni sürüm var: v{_release.Version}  (kullandığınız: v{cur})"
                      : newer ? $"Yeni güncelleme sistemine geçiş (v{cur})"
                      : $"Güncelsiniz: v{cur} en son sürüm.";
        var date = _release.Published is { } d ? $"Yayımlanma: {d:dd.MM.yyyy}" + Environment.NewLine + Environment.NewLine : "";
        _txtNotes.Text = date + PlainNotes(_release.Notes);
        _btnUpdate.Enabled = newer;
        _btnUpdate.Visible = newer;
        _btnSkip.Visible = isNewer;
        _lblStatus.Text = "";
        if (!newer) return;
        AcceptButton = _btnUpdate;
        switch (CurrentRoute)
        {
            case Route.Installed:
                _lblStatus.Text = "İndirilecek boyut hesaplanıyor…";
                _ = PlanAsync();
                break;
            case Route.Migrate:
                _btnUpdate.Text = "Kur ve yeniden başlat";
                _lblStatus.Text = $"Tek seferlik kurulum ({_release.SetupSize / 1048576.0:0} MB): sonraki güncellemelerde yalnız değişen kısımlar iner. Ayarlarınız korunur.";
                _txtNotes.Text = "BU GÜNCELLEMEDE PROGRAM BİLGİSAYARA KURULUR" + Environment.NewLine +
                    "• Bir kez kurulur; masaüstüne ve Başlat menüsüne \"Chess Scoresheet Printer\" kısayolu eklenir." + Environment.NewLine +
                    "• Bundan sonraki güncellemelerde paketin tamamı değil, yalnız değişen kısımlar (genelde birkaç MB) iner." + Environment.NewLine +
                    "• Ayarlarınız, şablonlarınız ve çıktılarınız olduğu gibi kalır. Bu klasördeki eski exe'yi sonra silebilirsiniz." + Environment.NewLine +
                    Environment.NewLine + _txtNotes.Text;
                break;
            default:
                _lblStatus.Text = $"Paket {_release.ZipSize / 1048576.0:0} MB • ayarlarınız, şablonlarınız ve çıktılarınız korunur.";
                break;
        }
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
                    ? $"İndirilecek: {Mb(plan.DownloadBytes)} (yalnız değişen kısımlar; tam paket {Mb(plan.FullBytes)}) • ayarlarınız korunur."
                    : $"İndirilecek: {Mb(plan.DownloadBytes)} (tam paket) • ayarlarınız korunur.";
            _btnUpdate.Enabled = plan is not null;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblStatus.Text = "Paket bilgisi alınamadı: " + (ex is HttpRequestException ? "bağlantı yok." : ex.Message);
        }
    }

    private static string Mb(long bytes) => bytes < 1048576 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / 1048576.0:0.0} MB";

    /// <summary>Markdown sürüm notunu düz metne yaklaştırır (başlık/kalın işaretleri atılır).</summary>
    private static string PlainNotes(string md)
    {
        md = md.Replace("\r\n", "\n");
        int en = md.IndexOf("\n---", StringComparison.Ordinal); // İngilizce bölüm gösterilmez
        if (en > 0) md = md[..en];
        var lines = md.Split('\n').Select(l =>
        {
            var t = l.TrimEnd();
            if (t.StartsWith("#")) t = t.TrimStart('#', ' ').ToUpperInvariant();
            if (t.StartsWith("- ")) t = "• " + t[2..];
            return t.Replace("**", "").Replace("`", "");
        });
        return string.Join(Environment.NewLine, lines).Trim();
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
        _btnUpdate.Enabled = _btnSkip.Enabled = false;
        _bar.Visible = true;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (IsDisposed) return;
                _bar.Value = p.Total > 0 ? (int)Math.Min(100, p.Done * 100 / p.Total) : 0;
                _lblStatus.Text = $"İndiriliyor… {p.Done / 1048576.0:0.0} / {Math.Max(p.Total, 1) / 1048576.0:0.0} MB";
            });
            var dir = await Updater.DownloadAsync(_release, progress, _cts.Token);
            _cts = null;
            _lblStatus.Text = "Paket doğrulandı; kuruluyor…";
            _save(); // ayarlar yeni sürüm açılmadan yazılsın
            Updater.Install(dir);
            _lblStatus.Text = "Yeni sürüm açılıyor…";
            Application.Exit();
        }
        catch (OperationCanceledException) { _lblStatus.Text = "İndirme iptal edildi."; }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblStatus.Text = "Güncelleme yapılamadı.";
            MessageBox.Show(this, "Güncelleme yapılamadı: " + (ex is HttpRequestException ? "indirme sırasında bağlantı kesildi." : ex.Message) +
                                  Environment.NewLine + Environment.NewLine + "Program değişmedi; yeni sürümü GitHub'dan elle de indirebilirsiniz.",
                            "Güncelleme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _cts = null;
            if (!IsDisposed) { _bar.Visible = false; _btnUpdate.Enabled = _btnSkip.Enabled = true; }
        }
    }

    /// <summary>Kurulu sürüm: değişen kısımları indirir, Velopack'e uygulatır, program yeniden açılır.</summary>
    private async Task UpdateInstalledAsync()
    {
        _btnUpdate.Enabled = _btnSkip.Enabled = false;
        _bar.Visible = true;
        _bar.Value = 0;
        _cts = new CancellationTokenSource();
        try
        {
            _plan ??= await Task.Run(Velo.PlanAsync);
            if (_plan is null) { _lblStatus.Text = "Güncelleme paketi bulunamadı."; return; }
            var plan = _plan;
            long size = plan.DownloadBytes;
            void Report(int pct)
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    _bar.Value = Math.Clamp(pct, 0, 100);
                    _lblStatus.Text = $"İndiriliyor… %{pct}  ({Mb(size * pct / 100)} / {Mb(size)}{(plan.UsesDelta ? ", yalnız değişen kısımlar" : "")})";
                });
            }
            await Task.Run(() => Velo.DownloadAsync(plan, Report, _cts.Token));
            _cts = null;
            _bar.Value = 100;
            _lblStatus.Text = "İndirildi ve doğrulandı; yeni sürüm kuruluyor…";
            _save();
            Velo.ApplyAndRestart(plan);
            Application.Exit();
        }
        catch (OperationCanceledException) { _lblStatus.Text = "İndirme iptal edildi."; }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            _cts = null;
            if (!IsDisposed) { _bar.Visible = false; _btnUpdate.Enabled = _btnSkip.Enabled = true; }
        }
    }

    /// <summary>Taşınabilir exe → kurulu sürüm: Setup indirilir, doğrulanır ve çalıştırılır.</summary>
    private async Task MigrateAsync()
    {
        _btnUpdate.Enabled = _btnSkip.Enabled = false;
        _bar.Visible = true;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (IsDisposed) return;
                _bar.Value = p.Total > 0 ? (int)Math.Min(100, p.Done * 100 / p.Total) : 0;
                _lblStatus.Text = $"Kurulum indiriliyor… {p.Done / 1048576.0:0.0} / {Math.Max(p.Total, 1) / 1048576.0:0.0} MB";
            });
            var setup = await Updater.DownloadSetupAsync(_release!, progress, _cts.Token);
            _cts = null;
            _lblStatus.Text = "Doğrulandı; kurulum başlıyor…";
            _save();
            Updater.RunSetup(setup);
            Application.Exit();
        }
        catch (OperationCanceledException) { _lblStatus.Text = "İndirme iptal edildi."; }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            _cts = null;
            if (!IsDisposed) { _bar.Visible = false; _btnUpdate.Enabled = _btnSkip.Enabled = true; }
        }
    }

    private void Fail(Exception ex)
    {
        if (IsDisposed) return;
        _lblStatus.Text = "Güncelleme yapılamadı.";
        MessageBox.Show(this, "Güncelleme yapılamadı: " + (ex is HttpRequestException ? "indirme sırasında bağlantı kesildi." : ex.Message) +
                              Environment.NewLine + Environment.NewLine + "Program değişmedi; yeni sürümü GitHub'dan elle de indirebilirsiniz.",
                        "Güncelleme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
