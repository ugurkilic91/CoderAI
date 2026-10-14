using System.Reflection;

namespace DevAgents.Web.Infrastructure;

public static class MethodInfoExtensions
{
    // Derleyicinin ürettiği anonim metotların (lambda, yerel fonksiyon) adlarında '<' ve '>' bulunur.
    private static readonly char[] AnonymousMethodChars = ['<', '>'];

    /// <summary>Metot bir lambda ya da yerel fonksiyondan mı üretildi (OpenAPI için anlamlı bir adı yok).</summary>
    public static bool IsAnonymous(this MethodInfo method) =>
        method.Name.Any(AnonymousMethodChars.Contains);

    /// <summary>İşleyici anonimse hata fırlatır: uç nokta adları metot adından türetiliyor.</summary>
    public static void AnonymousMethod(this IGuardClause guardClause, Delegate input)
    {
        if (input.Method.IsAnonymous())
            throw new ArgumentException("Uç nokta işleyicisi isimli bir metot olmalı (lambda kullanılamaz).");
    }
}
