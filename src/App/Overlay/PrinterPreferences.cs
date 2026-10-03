using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Yazıcının kendi sürücü ayarları (DEVMODE): sessiz mod, baskı kalitesi, tepsi gibi üreticiye özel
/// seçenekler Windows'un ortak ayarlarında yoktur; yazıcının "Yazdırma Tercihleri" penceresinde durur.
/// Bu pencere programdan açılır, seçilenler saklanır ve her baskıda o yazıcıya uygulanır.
/// </summary>
public static class PrinterPreferences
{
    private const int DM_OUT_BUFFER = 2, DM_IN_PROMPT = 4, DM_IN_BUFFER = 8, IDOK = 1;

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string pDeviceName,
                                                 IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr hMem);
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>
    /// Yazıcının tercihler penceresini açar (kayıtlı ayarlar önceden yüklenir). Tamam'a basılırsa yeni
    /// ayarları (base64) döndürür; vazgeçilirse null.
    /// </summary>
    public static string? Edit(IWin32Window owner, string printerName, string? savedBase64)
    {
        if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero)) return null;
        IntPtr hIn = IntPtr.Zero, outBuf = IntPtr.Zero;
        try
        {
            int size = DocumentProperties(owner.Handle, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0) return null;
            hIn = Load(printerName, savedBase64);
            outBuf = Marshal.AllocHGlobal(size);
            var inPtr = GlobalLock(hIn);
            int ret;
            try { ret = DocumentProperties(owner.Handle, hPrinter, printerName, outBuf, inPtr, DM_IN_BUFFER | DM_IN_PROMPT | DM_OUT_BUFFER); }
            finally { GlobalUnlock(hIn); }
            if (ret != IDOK) return null;
            var bytes = new byte[size];
            Marshal.Copy(outBuf, bytes, 0, size);
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            if (outBuf != IntPtr.Zero) Marshal.FreeHGlobal(outBuf);
            if (hIn != IntPtr.Zero) GlobalFree(hIn);
            ClosePrinter(hPrinter);
        }
    }

    /// <summary>Kayıtlı ayarları belgeye uygular (yazıcı adı aynıysa). Sonra kağıt/yön program tarafından ayarlanır.</summary>
    public static void Apply(PrintDocument doc, string? savedBase64)
    {
        if (string.IsNullOrWhiteSpace(savedBase64)) return;
        IntPtr h = IntPtr.Zero;
        try
        {
            h = FromBytes(Convert.FromBase64String(savedBase64));
            doc.PrinterSettings.SetHdevmode(h);
            doc.DefaultPageSettings.SetHdevmode(h);
        }
        catch { /* sürücü değiştiyse ya da ayar bozuksa varsayılanlarla basılır */ }
        finally { if (h != IntPtr.Zero) GlobalFree(h); }
    }

    /// <summary>Kayıtlı ayar varsa onu, yoksa yazıcının varsayılan ayarlarını içeren GlobalAlloc bloğu.</summary>
    private static IntPtr Load(string printerName, string? savedBase64)
    {
        if (!string.IsNullOrWhiteSpace(savedBase64))
            try { return FromBytes(Convert.FromBase64String(savedBase64)); } catch { }
        var ps = new PrinterSettings { PrinterName = printerName };
        return ps.GetHdevmode();
    }

    private static IntPtr FromBytes(byte[] bytes)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        var p = GlobalLock(h);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        GlobalUnlock(h);
        return h;
    }
}
