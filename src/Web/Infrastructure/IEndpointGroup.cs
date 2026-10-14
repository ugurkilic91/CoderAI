namespace DevAgents.Web.Infrastructure;

/// <summary>
/// Birbiriyle ilgili Minimal API uç noktalarından oluşan bir grup (şablondaki düzen).
/// Uygulamaları otomatik bulunur ve aynı adlı OpenAPI etiketiyle bir route grubu olarak kaydedilir.
/// Varsayılan önek <c>/api/{SınıfAdı}</c>'dır; farklı bir yol için <see cref="RoutePrefix"/>'i ezin.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>Grubun route öneki. Varsayılan <c>/api/{SınıfAdı}</c>.</summary>
    static virtual string? RoutePrefix => null;

    static abstract void Map(RouteGroupBuilder groupBuilder);
}
