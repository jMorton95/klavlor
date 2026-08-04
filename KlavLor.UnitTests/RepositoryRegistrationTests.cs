using System.Reflection;
using KlavLor.Application.Common.DependencyInjection;
using KlavLor.Application.Common.Settings;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KlavLor.UnitTests;

// Repositories are registered by REFLECTION, not by hand: AddDomainRepositories scans Infrastructure
// for classes implementing an interface from the Domain assembly whose name ends in "Repository",
// and AddApplicationRepositories does the same against the Application assembly. Nothing fails loudly
// when the scan misses a class — the interface just has no registration and the first request for it
// throws at runtime, in production, on whichever page happens to need it.
//
// That makes any change to a repository's file, name or interface list a silent-breakage risk, which
// is exactly what splitting the 2,768-line LootLogRepository into five was. These tests pin the
// scan's outcome.
//
// No database is touched: the DbContext options factory runs on resolve but Npgsql opens no
// connection until a query, and nothing here queries.
public sealed class RepositoryRegistrationTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationDependencyConfiguration).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureDependencyConfiguration).Assembly;

    // The five repositories LootLogRepository was split into (KL-2). Named explicitly rather than
    // discovered, so deleting one, renaming it out of the scanner's convention, or pointing an
    // interface at the wrong class all fail here.
    public static TheoryData<string, string> SplitLootLogRepositories => new()
    {
        { nameof(ILootLogSearchRepository), "LootLogSearchRepository" },
        { nameof(ILootSourceDetailRepository), "LootSourceDetailRepository" },
        { nameof(ILootSessionRepository), "LootSessionRepository" },
        { nameof(ILootFeedRepository), "LootFeedRepository" },
        { nameof(ILootProfileRepository), "LootProfileRepository" }
    };

    [Theory]
    [MemberData(nameof(SplitLootLogRepositories))]
    public void Each_split_loot_log_repository_is_registered_against_its_own_implementation(
        string interfaceName, string expectedImplementationName)
    {
        var serviceType = ApplicationAssembly.GetTypes().Single(t => t.Name == interfaceName);

        var descriptors = BuildRegistrations()
            .Where(d => d.ServiceType == serviceType)
            .ToList();

        var descriptor = Assert.Single(descriptors);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationType);
        Assert.Equal(expectedImplementationName, descriptor.ImplementationType!.Name);
    }

    [Fact]
    public void Every_repository_interface_with_an_implementation_is_registered()
    {
        var registered = BuildRegistrations().Select(d => d.ServiceType).ToHashSet();

        var missing = new List<string>();
        foreach (var (implementation, serviceInterface) in ImplementedRepositoryInterfaces())
        {
            if (!registered.Contains(serviceInterface))
                missing.Add($"{serviceInterface.Name} (implemented by {implementation.Name})");
        }

        Assert.True(missing.Count == 0,
            "These repository interfaces have an Infrastructure implementation but no DI registration, "
            + "so the first request for one throws at runtime. The scanner matches an interface from the "
            + "Domain or Application assembly whose name ends in \"Repository\" - check the interface "
            + "name and which assembly it lives in: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void No_repository_class_implements_two_repository_interfaces_from_one_assembly()
    {
        // The scanner uses FirstOrDefault, so a class implementing two Application repository
        // interfaces gets exactly ONE of them registered - and which one depends on reflection
        // ordering. The second interface silently has no registration.
        var offenders = new List<string>();

        foreach (var type in ConcreteInfrastructureTypes())
        {
            foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly })
            {
                var matches = type.GetInterfaces()
                    .Where(i => i.Assembly == assembly && i.Name.EndsWith("Repository", StringComparison.Ordinal))
                    .ToList();

                if (matches.Count > 1)
                    offenders.Add($"{type.Name} -> {string.Join(" + ", matches.Select(i => i.Name))}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Each repository class must implement at most one repository interface per assembly. The "
            + "registration scanner takes FirstOrDefault, so the others are silently unregistered: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void No_repository_interface_has_two_competing_implementations()
    {
        // Two classes implementing the same repository interface both get AddScoped'd, and the last
        // registration wins - so which one serves requests depends on reflection ordering.
        var duplicates = ImplementedRepositoryInterfaces()
            .GroupBy(x => x.ServiceInterface)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} <- {string.Join(", ", g.Select(x => x.Implementation.Name))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Two Infrastructure classes claim the same repository interface; the last one scanned wins: "
            + string.Join("; ", duplicates));
    }

    [Fact]
    public void Every_registered_repository_resolves_from_a_request_scope()
    {
        // The real end-to-end check: build the container the app builds and pull every repository out
        // of a scope. A missing collaborator, a lifetime mismatch (captive dependency) or an absent
        // registration all throw here rather than on a page in production.
        var services = BuildServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var failures = new List<string>();
        foreach (var (implementation, serviceInterface) in ImplementedRepositoryInterfaces())
        {
            try
            {
                Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceInterface));
            }
            catch (Exception ex)
            {
                failures.Add($"{serviceInterface.Name} (impl {implementation.Name}): {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "These repositories could not be resolved from a request scope:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_repository_scan_finds_a_realistic_number_of_repositories()
    {
        // Guards every assertion above against passing over an empty set.
        var found = ImplementedRepositoryInterfaces().Count;
        Assert.True(found > 20, $"expected the scan to find many repositories, found {found}");
    }

    // ------------------------------------------------------------------------------ helpers

    // Mirrors the predicate in InfrastructureDependencyConfiguration.AddDomainRepositories /
    // AddApplicationRepositories: a concrete Infrastructure class plus the first Domain- or
    // Application-assembly interface it implements whose name ends in "Repository".
    private static List<(Type Implementation, Type ServiceInterface)> ImplementedRepositoryInterfaces()
    {
        var result = new List<(Type, Type)>();

        foreach (var type in ConcreteInfrastructureTypes())
        {
            foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly })
            {
                var match = type.GetInterfaces()
                    .FirstOrDefault(i => i.Assembly == assembly && i.Name.EndsWith("Repository", StringComparison.Ordinal));
                if (match is not null) result.Add((type, match));
            }
        }

        return result;
    }

    private static IEnumerable<Type> ConcreteInfrastructureTypes() =>
        InfrastructureAssembly.DefinedTypes
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Select(t => t.AsType());

    private static IServiceCollection BuildRegistrations() => BuildServices();

    private static IServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Required by AuthKeyDerivation for the Data Protection key ring. Not a secret and
                // never used to protect anything here - nothing in this test encrypts or connects.
                ["AuthKey"] = "unit-test-auth-key-0000000000000000",
                ["DatabaseSettings:Host"] = "localhost",
                ["DatabaseSettings:Port"] = "5432",
                ["DatabaseSettings:Database"] = "klavlor_unit_tests_never_connected",
                ["DatabaseSettings:Username"] = "postgres",
                ["DatabaseSettings:Password"] = "postgres"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<DatabaseSettings>(configuration.GetSection(nameof(DatabaseSettings)));
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services;
    }
}
