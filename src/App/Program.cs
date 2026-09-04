namespace NotasyonOtomasyonu.App;

internal static class Program
{
    /// <summary>Uygulama giriş noktası.</summary>
    [STAThread]
    private static void Main()
    {
        // WinForms yüksek DPI ve varsayılan font ayarları.
        ApplicationConfiguration.Initialize();

        // Beklenmeyen hatalarda uygulama çökmesin, anlaşılır mesaj göster.
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Beklenmeyen bir hata oluştu:\n\n" + e.Exception.Message,
                "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
    }
}
