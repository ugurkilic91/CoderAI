using System.Reflection;

namespace DevAgents.Web.Infrastructure;

public static class WebApplicationExtensions
{
    /// <summary>
    /// <paramref name="assembly"/> içindeki tüm <see cref="IEndpointGroup"/> uygulamalarını bulur ve her birini
    /// aynı adlı OpenAPI etiketiyle bir route grubu olarak kaydeder.
    /// </summary>
    public static WebApplication MapEndpoints(this WebApplication app, Assembly assembly)
    {
        var endpointGroupTypes = assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                     && t.IsAssignableTo(typeof(IEndpointGroup)));

        foreach (var type in endpointGroupTypes)
        {
            var groupName = type.Name;
            var routePrefix = type.GetProperty(nameof(IEndpointGroup.RoutePrefix))
                ?.GetValue(null) as string ?? $"/api/{groupName}";
            var group = app.MapGroup(routePrefix).WithTags(groupName);
            type.GetMethod(nameof(IEndpointGroup.Map))!.Invoke(null, [group]);
        }

        return app;
    }
}
