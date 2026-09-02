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

        // Not a *Handler, so the convention scan above doesn't see it. Shared by every admin panel
        // that edits an input to the luck maths — see RecomputeTrigger.
        services.TryAddScoped<Features.Maintenance.RecomputeTrigger>();

        // Not a *Handler, so the assembly scan above does not pick it up. Shared by the startup
        // seeder and the item-value admin — see FeedBufferSeeder for why the admin needs it.
        services.TryAddScoped<Features.Loot.Feed.FeedBufferSeeder>();

        // Source loot strategies (PVT strategy convention). One line per strategy against the
        // shared interface so SourceLootService receives them all as IEnumerable and dispatches
        // by source name. The default (empty key) covers every ordinary source; add a new
        // edge-case source by registering one more strategy — consumers are untouched.
        services.AddSingleton<ISourceLootStrategy, DefaultSourceLootStrategy>();
        services.AddSingleton<ISourceLootStrategy, DoomLootStrategy>();
        services.AddSingleton<ISourceLootStrategy, ChambersOfXericStrategy>();
        services.AddSingleton<ISourceLootStrategy, TombsOfAmascutStrategy>();
        services.AddSingleton<ISourceLootStrategy, TheatreOfBloodStrategy>();
        services.AddSingleton<SourceLootService>();
    }
}
