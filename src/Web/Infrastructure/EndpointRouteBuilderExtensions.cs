using System.Diagnostics.CodeAnalysis;

namespace DevAgents.Web.Infrastructure;

/// <summary>
/// <see cref="IEndpointGroup.Map"/> içinde kullanılan kısayollar (şablondaki düzen). Uç noktanın adı, işleyici metodun
/// adından türetilir ve OpenAPI'de <c>operationId</c> olur; bu yüzden işleyiciler isimli metot olmalıdır (lambda değil).
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
    {
        Guard.Against.AnonymousMethod(handler);

        return builder.MapGet(pattern, handler)
            .WithName(handler.Method.Name);
    }

    public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder builder, Delegate handler, [StringSyntax("Route")] string pattern = "")
    {
        Guard.Against.AnonymousMethod(handler);

        return builder.MapPost(pattern, handler)
            .WithName(handler.Method.Name);
    }
}
