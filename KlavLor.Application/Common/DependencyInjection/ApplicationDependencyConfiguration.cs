using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using KlavLor.Application.Features.Loot.SourceModels;

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

        // Source loot strategies (PVT strategy convention). One line per strategy against the
        // shared interface so SourceLootService receives them all as IEnumerable and dispatches
        // by source name. The default (empty key) covers every ordinary source; add a new
        // edge-case source by registering one more strategy — consumers are untouched.
        services.AddSingleton<ISourceLootStrategy, DefaultSourceLootStrategy>();
        services.AddSingleton<ISourceLootStrategy, DoomLootStrategy>();
        services.AddSingleton<SourceLootService>();
    }
}
