using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlavLor.Application.Common.DependencyInjection;

public static class ApplicationDependencyConfiguration
{
    public static void AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationDependencyConfiguration).Assembly;

        var scopedByConvention = assembly.DefinedTypes
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters
                        && (t.Name.EndsWith("Handler") || t.Name.EndsWith("Checker")))
            .Select(t => t.AsType());

        foreach (var type in scopedByConvention)
        {
            services.TryAddScoped(type);
        }

        services.AddValidatorsFromAssembly(typeof(ApplicationDependencyConfiguration).Assembly);
    }
}
