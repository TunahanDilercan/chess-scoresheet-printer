using System.Runtime.InteropServices;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>
/// Kurulu Microsoft Word üzerinden .docx → PDF ve yazdırma (geç bağlama/COM). Word görünmez
/// çalışır, ayrı bir STA iş parçacığında koşar (arayüz donmaz) ve iş bitince kapatılır.
/// Word yoksa çağıran .docx'i kaydedip varsayılan programla açar.
/// </summary>
public static class WordExport
{
    private const int WdExportFormatPdf = 17;

    public static bool IsAvailable => Type.GetTypeFromProgID("Word.Application") is not null;

    public static Task ToPdfAsync(string docxPath, string pdfPath) => RunSta(() =>
        WithDocument(docxPath, null, doc => doc.ExportAsFixedFormat(pdfPath, WdExportFormatPdf)));

    /// <param name="printerName">Boşsa Word'ün varsayılan yazıcısı. Word'ün etkin yazıcısı iş bitince geri alınır.</param>
    public static Task PrintAsync(string docxPath, string? printerName, int copies) => RunSta(() =>
        WithDocument(docxPath, printerName, doc => doc.PrintOut(false, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Math.Max(1, copies)))); // Background=false: bitmeden kapatılmasın

    private static void WithDocument(string path, string? printer, Action<dynamic> action)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
                   ?? throw new InvalidOperationException("Bu bilgisayarda Microsoft Word bulunamadı.");
        dynamic app = Activator.CreateInstance(type)!;
        string? oldPrinter = null;
        try
        {
            app.Visible = false;
            app.DisplayAlerts = 0;
            if (!string.IsNullOrWhiteSpace(printer))
            {
                oldPrinter = app.ActivePrinter; // Word bunu Windows varsayılanı yapabiliyor → sonra geri al
                app.ActivePrinter = printer;
            }
            dynamic doc = app.Documents.Open(Path.GetFullPath(path), false, true); // ConfirmConversions, ReadOnly
            try { action(doc); }
            finally { doc.Close(false); Marshal.FinalReleaseComObject(doc); }
        }
        finally
        {
            try { if (oldPrinter is not null) app.ActivePrinter = oldPrinter; } catch { /* geri alınamadıysa sorun değil */ }
            try { app.Quit(false); } catch { /* zaten kapanmış olabilir */ }
            Marshal.FinalReleaseComObject(app);
        }
    }

    private static Task RunSta(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var th = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "WordExport" };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        return tcs.Task;
    }
}
