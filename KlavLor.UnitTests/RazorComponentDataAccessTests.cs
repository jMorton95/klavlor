using System.Reflection;
using System.Text.RegularExpressions;
using KlavLor.Web.Application;
using Microsoft.AspNetCore.Components;

namespace KlavLor.UnitTests;

// Architecture tests for the Razor Component Data-Access Rules in CLAUDE.md.
//
// A June 2026 production incident (fixed in effdbd6) was caused by the layout sidebar querying the
// DB during static SSR. Blazor static SSR runs sibling components' OnInitializedAsync CONCURRENTLY
// on the same request scope. EF's "second operation on this context" guard never fired, because many
// repositories execute raw ADO commands directly on Database.GetDbConnection() — so instead of a
// clean exception the Npgsql wire protocol was corrupted ("Received backend message BindComplete
// while expecting ReadyForQueryMessage"), poisoning the pooled connection and surfacing as
// intermittent 500s in completely unrelated requests.
//
// The rules were documented but nothing enforced them, so a future component could reintroduce the
// exact outage. These tests are that enforcement.
public sealed class RazorComponentDataAccessTests
{
    private static readonly Assembly WebAssembly = typeof(AppRoutes).Assembly;

    // ------------------------------------------------------------------------------------------
    // Rule: "Components must never inject repositories (I*Repository). When a page component loads
    // its own data, it goes through a *Handler. There are no exceptions."
    //
    // Compiled-metadata check (the robust one): Razor's `@inject T Name` generates a property
    // carrying [Inject], and a code-behind component can also declare one or take one through a
    // constructor. All three shapes are covered here.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void No_razor_component_injects_a_repository()
    {
        var violations = new List<string>();

        foreach (var component in ComponentTypes())
        {
            foreach (var member in InjectedMemberTypes(component))
            {
                if (IsRepositoryType(member.Type))
                    violations.Add($"{component.FullName}.{member.MemberName} : {member.Type.Name}");
            }
        }

        Assert.True(violations.Count == 0,
            "Razor components must never inject a repository - they go through a *Handler instead. "
            + "A component that queries the DB during static SSR can race a sibling component on the "
            + "shared request scope and corrupt the Npgsql connection (see CLAUDE.md, 'Razor Component "
            + "Data-Access Rules'). Offending injections:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_component_scan_actually_finds_components()
    {
        // Without this, a refactor that moved components to another assembly would leave
        // No_razor_component_injects_a_repository passing over an empty set forever.
        var components = ComponentTypes();

        Assert.True(components.Count > 50,
            $"expected the Web assembly to contain many components, found {components.Count}");
        // And it must actually be looking at injected members, not just types.
        Assert.Contains(components, c => InjectedMemberTypes(c).Count > 0);
    }

    [Fact]
    public void The_repository_type_test_recognises_the_naming_convention()
    {
        // Self-check for IsRepositoryType, so the rule test above cannot pass because its predicate
        // silently stopped matching anything.
        Assert.True(IsRepositoryType(typeof(IFakeLootRepository)));
        Assert.True(IsRepositoryType(typeof(IFakeLootQueryRepository)));
        Assert.True(IsRepositoryType(typeof(IFakeLootLogRepository)));

        Assert.False(IsRepositoryType(typeof(IFakeHandlerLikeThing)));
        Assert.False(IsRepositoryType(typeof(FakeConcreteRepository)));
        Assert.False(IsRepositoryType(typeof(string)));
    }

    // ------------------------------------------------------------------------------------------
    // Supplementary source scan over the .razor files. Cheap, and catches @inject declarations that
    // the metadata check could miss depending on how the generated member is expressed.
    // ------------------------------------------------------------------------------------------

    private static readonly Regex InjectDirective =
        new(@"^\s*@inject\s+(?<type>[A-Za-z0-9_.<>,\[\]\?]+)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void No_razor_file_injects_a_repository_type()
    {
        var violations = new List<string>();

        foreach (var file in RazorFiles())
        {
            foreach (Match match in InjectDirective.Matches(File.ReadAllText(file)))
            {
                var typeName = SimpleTypeName(match.Groups["type"].Value);
                if (LooksLikeRepositoryName(typeName))
                    violations.Add($"{Relative(file)} : @inject {match.Groups["type"].Value}");
            }
        }

        Assert.True(violations.Count == 0,
            "These .razor files @inject a repository, which components must never do (CLAUDE.md, "
            + "'Razor Component Data-Access Rules'). Go through a *Handler, or - for layout and shared "
            + "components - fetch via the component's own hx-trigger=\"load\" request so it runs in its "
            + "own request scope:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_razor_source_scan_finds_files_and_parses_their_inject_directives()
    {
        var files = RazorFiles();
        Assert.True(files.Count > 100, $"expected to find the Web project's .razor files, found {files.Count}");

        var injectedTypes = files
            .SelectMany(f => InjectDirective.Matches(File.ReadAllText(f)).Select(m => m.Groups["type"].Value))
            .ToList();

        // The regex has to be actually matching, or the scan above is vacuous.
        Assert.NotEmpty(injectedTypes);
        Assert.Contains("NavigationManager", injectedTypes);
        // And it must be able to spot a repository name if one turned up.
        Assert.True(LooksLikeRepositoryName(SimpleTypeName("KlavLor.Domain.Interfaces.ILootLogRepository")));
    }

    // ------------------------------------------------------------------------------------------
    // Rule: "Multiple loads inside one component must be awaited sequentially — never Task.WhenAll
    // over handler or repository calls."
    //
    // Deliberately conservative: this flags ANY Task.WhenAll inside a component rather than trying
    // to classify the awaited operands. A cleverer check produces false negatives, and a false
    // negative here is an outage; a false positive is one line of human review.
    // ------------------------------------------------------------------------------------------

    private static readonly Regex WhenAll = new(@"Task\s*\.\s*WhenAll", RegexOptions.Compiled);

    [Fact]
    public void No_component_uses_Task_WhenAll()
    {
        var violations = new List<string>();

        foreach (var file in ComponentSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in WhenAll.Matches(text))
            {
                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                violations.Add($"{Relative(file)}:{line}");
            }
        }

        Assert.True(violations.Count == 0,
            "Task.WhenAll appears inside a Razor component. This check is deliberately conservative "
            + "and does not inspect the operands, because concurrent work on the shared request scope "
            + "is what caused the June 2026 incident: two loads in one render pass race on the same "
            + "scoped DbContext, and the raw-ADO repositories bypass EF's second-operation guard, so "
            + "the failure is a corrupted Npgsql connection rather than a clean exception. Await the "
            + "loads sequentially. If this Task.WhenAll provably touches neither a handler nor the "
            + "database, it still needs a human decision - not a silent exemption in this test:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_when_all_scan_looks_at_real_files_and_would_match_the_pattern()
    {
        Assert.True(ComponentSourceFiles().Count > 100,
            $"expected to scan the Web project's component sources, found {ComponentSourceFiles().Count}");

        // Self-check: the regex tolerates the formatting variations someone might actually write.
        Assert.Matches(WhenAll, "await Task.WhenAll(a, b);");
        Assert.Matches(WhenAll, "await Task . WhenAll(a, b);");
        Assert.Matches(WhenAll, "return Task.WhenAll(tasks);");
        Assert.DoesNotMatch(WhenAll, "await Task.WhenAny(a, b);");
    }

    // ------------------------------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------------------------------

    private static List<Type> ComponentTypes() =>
        WebAssembly.DefinedTypes
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ComponentBase).IsAssignableFrom(t))
            .Select(t => t.AsType())
            .ToList();

    private static List<(string MemberName, Type Type)> InjectedMemberTypes(Type component)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.DeclaredOnly;
        var members = new List<(string, Type)>();

        // `@inject` generates a property with [Inject]; a code-behind component may declare one
        // directly. [Inject] is not inherited, so DeclaredOnly is walked up the hierarchy explicitly.
        for (var type = component; type is not null && type != typeof(ComponentBase); type = type.BaseType)
        {
            members.AddRange(type.GetProperties(all)
                .Where(p => p.IsDefined(typeof(InjectAttribute), inherit: true))
                .Select(p => (p.Name, p.PropertyType)));

            // Belt and braces: a field-backed injection or a [Inject]-marked field.
            members.AddRange(type.GetFields(all)
                .Where(f => f.IsDefined(typeof(InjectAttribute), inherit: true))
                .Select(f => (f.Name, f.FieldType)));

            // Primary/explicit constructor parameters — how a .cs component would take a dependency.
            foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public
                                                      | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                members.AddRange(ctor.GetParameters()
                    .Where(p => p.Name is not null)
                    .Select(p => ($"ctor({p.Name})", p.ParameterType)));
            }
        }

        return members;
    }

    // The repository auto-registration convention: I*Repository in Domain, I*QueryRepository /
    // I*LogRepository in Application. All of them end in "Repository", so that is the discriminator.
    private static bool IsRepositoryType(Type type) =>
        type.IsInterface && LooksLikeRepositoryName(type.Name);

    private static bool LooksLikeRepositoryName(string simpleName) =>
        simpleName.Length > "IRepository".Length
        && simpleName.StartsWith('I')
        && char.IsUpper(simpleName.ElementAtOrDefault(1))
        && simpleName.EndsWith("Repository", StringComparison.Ordinal);

    private static string SimpleTypeName(string declared)
    {
        var name = declared.Split('<')[0].TrimEnd('?');
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static List<string> RazorFiles() =>
        Directory.EnumerateFiles(WebProjectDirectory, "*.razor", SearchOption.AllDirectories)
            .Where(NotBuildOutput)
            .ToList();

    // Everything a component's behaviour can live in: the .razor markup and any code-behind.
    private static List<string> ComponentSourceFiles() =>
        RazorFiles()
            .Concat(Directory.EnumerateFiles(WebProjectDirectory, "*.razor.cs", SearchOption.AllDirectories)
                .Where(NotBuildOutput))
            .ToList();

    private static bool NotBuildOutput(string path)
    {
        var normalised = path.Replace('\\', '/');
        return !normalised.Contains("/obj/") && !normalised.Contains("/bin/");
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

    private static string WebProjectDirectory => Path.Combine(RepositoryRoot, "KlavLor.Web");

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        // The source scans need the repository on disk, not just the compiled assembly. Walk up from
        // the test binaries to the directory holding the solution file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KlavLor.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate KlavLor.slnx walking up from {AppContext.BaseDirectory}. The .razor "
            + "source scans in RazorComponentDataAccessTests need the repository checkout on disk.");
    }

    // Fixtures for the self-checks above. Named so they exercise the predicate without pretending to
    // be real repositories.
    private interface IFakeLootRepository;

    private interface IFakeLootQueryRepository;

    private interface IFakeLootLogRepository;

    private interface IFakeHandlerLikeThing;

    private sealed class FakeConcreteRepository;
}
