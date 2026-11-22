namespace NotasyonOtomasyonu.App;

/// <summary>
/// Dönen bekleme imleci yerine: durum yazısının altında ince, kayan bir çubuk. İş arka planda sürerken pencere
/// kullanılabilir kalır; ne yapıldığı durum yazısında okunur. <c>using (StatusBusy.Show(lbl)) { await ... }</c>
/// </summary>
internal static class StatusBusy
{
    public static IDisposable Show(Label status)
    {
        int h = UiScale.Px(3);
        var pad = status.Padding;
        status.Padding = new Padding(pad.Left, pad.Top, pad.Right, pad.Bottom + h + 2);   // yazı çubuğun üstünde kalsın
        var bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, AccessibleName = "İşlem sürüyor",
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        void Place() => bar.SetBounds(pad.Left, status.Height - h - 1, Math.Max(1, status.Width - pad.Left - pad.Right), h);
        status.Controls.Add(bar);
        Place();
        status.SizeChanged += OnSize;
        return new Undo(() =>
        {
            status.SizeChanged -= OnSize;
            bar.Dispose();
            if (!status.IsDisposed) status.Padding = pad;
        });

        void OnSize(object? s, EventArgs e) => Place();
    }

    private sealed class Undo(Action a) : IDisposable
    {
        private Action? _a = a;
        public void Dispose() { _a?.Invoke(); _a = null; }
    }
}
