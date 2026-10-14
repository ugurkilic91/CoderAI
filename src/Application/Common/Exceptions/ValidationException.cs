using FluentValidation.Results;

namespace DevAgents.Application.Common.Exceptions;

/// <summary>
/// Bir komut ya da sorgu doğrulamadan geçemedi (FluentValidation). Web katmanı bunu 400 olarak döndürür.
/// Şablondakinden farkı: mesaj, doğrulama hatalarının kendisidir (Türkçe). Akış (stream) uç noktaları
/// hatayı tek bir mesajla bildirdiği için kullanıcı "Soru boş olamaz." gibi anlamlı bir metin görür.
/// </summary>
public class ValidationException : Exception
{
    private const string DefaultMessage = "Bir veya daha fazla doğrulama hatası oluştu.";

    public ValidationException()
        : base(DefaultMessage)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this(failures.ToList())
    {
    }

    private ValidationException(List<ValidationFailure> failures)
        : base(failures.Count == 0 ? DefaultMessage : string.Join(" ", failures.Select(f => f.ErrorMessage).Distinct()))
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
    }

    /// <summary>Alan adı → o alanın hata mesajları.</summary>
    public IDictionary<string, string[]> Errors { get; }
}
