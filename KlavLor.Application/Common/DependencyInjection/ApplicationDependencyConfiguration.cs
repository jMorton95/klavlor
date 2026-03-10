using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlavLor.Application.Common.DependencyInjection;

public static class ApplicationDependencyConfiguration
{
    public static void AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationDependencyConfiguration).Assembly;

        var handlerTypes = assembly.DefinedTypes
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && t.Name.EndsWith("Handler"))
            .Select(t => t.AsType());

        foreach (var handlerType in handlerTypes)
        {
            services.TryAddScoped(handlerType);
        }

        services.AddValidatorsFromAssembly(typeof(ApplicationDependencyConfiguration).Assembly);
    }
}
