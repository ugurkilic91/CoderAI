using System.Text.RegularExpressions;

namespace DevAgents.Application.Common.Text;

/// <summary>
/// Metnin Türkçe olup olmadığını kabaca tahmin eder. İstek zaten İngilizceyse çeviri ajanını hiç çağırmıyoruz:
/// hem bir model çağrısı kazanılıyor hem de çeviri sırasında kod tanımlayıcılarının bozulma riski ortadan kalkıyor.
/// </summary>
internal static partial class TurkishText
{
    // Sık geçen, İngilizcede neredeyse hiç görülmeyen Türkçe kelimeler (Türkçe karakter içermeyenler).
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ve", "bir", "bu", "icin", "ile", "ekle", "yap", "nedir", "nasil", "olsun", "kodu", "kod", "gelistir",
        "olustur", "sinif", "metot", "metod", "hata", "duzelt", "yaz", "goster", "mi", "mu", "da", "de", "gibi",
        "ama", "veya", "ya", "ne", "neden", "hangi", "tum", "yeni", "sil", "guncelle", "listele", "kaydet", "kaydeden",
        "eden", "olan", "icinde", "projeye", "projede", "proje", "testleri", "testlerini", "yazar", "misin", "mısın"
    };

    public static bool IsLikelyTurkish(string text)
    {
        if (TurkishLetters().IsMatch(text))
            return true;
        return Words().Matches(text).Any(word => CommonWords.Contains(word.Value));
    }

    [GeneratedRegex("[çğıöşüÇĞİÖŞÜ]")]
    private static partial Regex TurkishLetters();

    [GeneratedRegex("[A-Za-z]+")]
    private static partial Regex Words();
}
