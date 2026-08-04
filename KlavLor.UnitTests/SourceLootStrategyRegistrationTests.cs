using System.Reflection;
using KlavLor.Application.Common.DependencyInjection;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KlavLor.UnitTests;

// Commit abf0996 records the failure mode: strategies are matched by interface dispatch, so a
// strategy that is written but never registered with SourceLootService silently never engages.
// Nothing fails, nothing logs, the source just quietly uses the default flat-rate model.
//
// This test scans the Application assembly for every ISourceLootStrategy implementation and asserts
// each one is both registered in DI and reachable through SourceLootService. Adding a strategy
// without adding its AddSingleton line must turn this red.
public sealed class SourceLootStrategyRegistrationTests
{
    private static readonly Assembly ApplicationAssembly = typeof(ISourceLootStrategy).Assembly;

    // Concrete, instantiable strategies — the ones that are supposed to be registered. Abstract
    // bases (SourceLootStrategy, RaidUniqueShareStrategy) are excluded by design.
    private static List<Type> DeclaredStrategies() =>
        ApplicationAssembly.DefinedTypes
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                        && typeof(ISourceLootStrategy).IsAssignableFrom(t))
            .Select(t => t.AsType())
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    private static List<Type> RegisteredStrategies()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        return services
            .Where(d => d.ServiceType == typeof(ISourceLootStrategy))
            .Select(d => d.ImplementationType
                         ?? d.ImplementationInstance?.GetType()
                         ?? throw new InvalidOperationException(
                             "ISourceLootStrategy is registered via a factory, so this test can no longer "
                             + "determine which concrete strategy it produces. Register strategies by type."))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void There_is_at_least_one_strategy_to_check()
    {
        // Guards against the scan silently matching nothing and the whole file passing vacuously.
        Assert.True(DeclaredStrategies().Count >= 5,
            $"expected the Application assembly to declare several strategies, found {DeclaredStrategies().Count}");
    }

    [Fact]
    public void Every_strategy_in_the_application_assembly_is_registered_in_DI()
    {
        var declared = DeclaredStrategies();
        var registered = RegisteredStrategies();

        var unregistered = declared.Except(registered).ToList();

        Assert.True(unregistered.Count == 0,
            "These ISourceLootStrategy implementations exist but are not registered, so they will "
            + "silently never engage (see commit abf0996). Add an AddSingleton<ISourceLootStrategy, T>() "
            + "line in ApplicationDependencyConfiguration.AddApplication for each: "
            + string.Join(", ", unregistered.Select(t => t.Name)));

        // The reverse direction too: a registration pointing at a type that no longer exists in this
        // assembly means the scan and the registration list have drifted apart.
        var strangers = registered.Except(declared).ToList();
        Assert.True(strangers.Count == 0,
            "These types are registered as ISourceLootStrategy but were not found by the assembly scan: "
            + string.Join(", ", strangers.Select(t => t.FullName)));
    }

    [Fact]
    public void Every_strategy_is_reachable_through_SourceLootService()
    {
        var strategies = RegisteredStrategies()
            .Select(t => (ISourceLootStrategy)Activator.CreateInstance(t)!)
            .ToList();

        // Constructing the service is itself part of the contract: it throws if no default strategy
        // (the empty-string key) is registered.
        var service = new SourceLootService(strategies, new NoRateModifiers());

        foreach (var strategy in strategies.Where(s => !string.IsNullOrEmpty(s.SourceName)))
        {
            Assert.True(service.HasSpecialModel(strategy.SourceName),
                $"{strategy.GetType().Name} declares SourceName '{strategy.SourceName}' but "
                + "SourceLootService does not dispatch to it.");
            Assert.Contains(strategy.SourceName, service.SpecialSourceNames);
        }

        // Every declared strategy's flags have to be observable through the facade, or a consumer
        // reading the facade sees the default's answer instead of the strategy's.
        foreach (var strategy in strategies.Where(s => !string.IsNullOrEmpty(s.SourceName)))
        {
            Assert.Equal(strategy.HasDepthModel, service.HasDepthModel(strategy.SourceName));
            Assert.Equal(strategy.OverridesStoredRates, service.OverridesStoredRates(strategy.SourceName));
            Assert.Equal(strategy.IncludeInLeaderboard, service.IncludeInLeaderboard(strategy.SourceName));
        }
    }

    [Fact]
    public void Exactly_one_default_strategy_is_registered()
    {
        var defaults = RegisteredStrategies()
            .Select(t => (ISourceLootStrategy)Activator.CreateInstance(t)!)
            .Where(s => string.IsNullOrEmpty(s.SourceName))
            .ToList();

        // SourceLootService takes `.First(s => SourceName is empty)`, so a second default would be
        // picked non-deterministically by registration order.
        Assert.Single(defaults);
    }

    [Fact]
    public void No_two_strategies_claim_the_same_source_name()
    {
        var byName = RegisteredStrategies()
            .Select(t => (ISourceLootStrategy)Activator.CreateInstance(t)!)
            .Where(s => !string.IsNullOrEmpty(s.SourceName))
            .GroupBy(s => s.SourceName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        // The dispatch dictionary is built with ToDictionary, which throws on a duplicate key — this
        // turns that startup crash into a test failure instead.
        Assert.True(byName.Count == 0,
            "Duplicate strategy source names: "
            + string.Join("; ", byName.Select(g => $"{g.Key} -> {string.Join(", ", g.Select(s => s.GetType().Name))}")));
    }

    [Fact]
    public void Every_strategy_can_be_constructed_without_arguments()
    {
        // The DI registrations are AddSingleton<ISourceLootStrategy, T>() with no factory, so a
        // strategy that grows a constructor dependency breaks resolution at startup.
        foreach (var type in DeclaredStrategies())
        {
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        }
    }
}
