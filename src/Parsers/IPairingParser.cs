using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>
/// Tüm parser'ların ortak arayüzü. Ham dosyayı tek bir <see cref="Tournament"/> modeline çevirir.
/// Kütüphane/format değiştirmek kolay olsun diye arayüz arkasına alındı.
/// </summary>
public interface IPairingParser
{
    /// <summary>Bu parser verilen dosyayı işleyebilir mi? (genelde uzantıya bakar)</summary>
    bool CanParse(string filePath);

    /// <summary>Dosyayı oku ve Tournament üret. Hata olursa <see cref="ParseException"/> fırlatır.</summary>
    Tournament Parse(string filePath, AppConfig config);
}

/// <summary>Parser'ların kullanıcıya gösterilebilir, anlaşılır hata mesajı için kullandığı istisna.</summary>
public sealed class ParseException : Exception
{
    public ParseException(string message, Exception? inner = null) : base(message, inner) { }
}
