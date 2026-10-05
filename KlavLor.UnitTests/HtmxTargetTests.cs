using System.Text.RegularExpressions;

namespace KlavLor.UnitTests;

public sealed class HtmxTargetTests
{
    private static readonly Regex RelativeTarget = new(
        """hx-(?:target|include|indicator|disabled-elt|select)="(?:closest|find|next|previous) (?<selector>[^"]+)""",
        RegexOptions.Compiled);

    private static readonly Regex IdTarget = new(
        """hx-(?:target|include|indicator|disabled-elt|select)="#(?<id>[A-Za-z0-9_-]+)(?=")""",
        RegexOptions.Compiled);

    [Fact]
    public void Relative_targets_select_something_declared_in_the_same_file()
    {
        var broken = new List<string>();

        foreach (var file in RazorFiles())
        {
            var source = File.ReadAllText(file);
            foreach (Match match in RelativeTarget.Matches(source))
            {
                var selector = match.Groups["selector"].Value;
                if (!SelectorIsDeclared(selector, source))
                    broken.Add($"{Relative(file)}: {match.Value}\"");
            }
        }

        Assert.True(broken.Count == 0,
            "These htmx targets select a class or attribute that no element in the component carries. "
            + "Target a semantic hook (e.g. data-swap-panel), never a styling utility:\n"
            + string.Join("\n", broken));
    }

    [Fact]
    public void Id_targets_refer_to_an_id_defined_somewhere()
    {
        var sources = RazorFiles()
            .Concat(Directory.EnumerateFiles(WebProjectDirectory, "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(WebProjectDirectory, "wwwroot"), "*.js"))
            .Where(NotBuildOutput)
            .ToDictionary(f => f, File.ReadAllText);
        var everything = string.Join("\n", sources.Values);

        var broken = new List<string>();
        foreach (var (file, source) in sources.Where(s => s.Key.EndsWith(".razor")))
        {
            foreach (Match match in IdTarget.Matches(source))
            {
                var id = Regex.Escape(match.Groups["id"].Value);
                if (!Regex.IsMatch(everything, $"""id=["']{id}["']|\.id\s*=\s*["']{id}["']"""))
                    broken.Add($"{Relative(file)}: {match.Value}\"");
            }
        }

        Assert.True(broken.Count == 0,
            "These htmx targets name an id nothing defines:\n" + string.Join("\n", broken));
    }

    [Theory]
    [InlineData("div.bg-slate-50", """<div class="bg-slate-900 p-5">""", false)]
    [InlineData("div.bg-slate-900", """<div class="bg-slate-900 p-5">""", true)]
    [InlineData("[data-swap-panel]", """<div data-swap-panel class="p-5">""", true)]
    [InlineData("[data-swap-panel]", """<div class="p-5">""", false)]
    [InlineData(".drate-row", """<tr class="drate-row @Css">""", true)]
    public void The_selector_check_matches_classes_and_attributes(string selector, string markup, bool expected)
    {
        Assert.Equal(expected, SelectorIsDeclared(selector, markup));
    }

    private static bool SelectorIsDeclared(string selector, string source)
    {
        var attribute = Regex.Match(selector, @"\[(?<name>[A-Za-z0-9_-]+)");
        if (attribute.Success)
            return Regex.IsMatch(source, $@"<[^>]*\s{Regex.Escape(attribute.Groups["name"].Value)}[\s=>]");

        var classes = Regex.Matches(selector, @"\.(?<name>[A-Za-z0-9_-]+)").Select(m => m.Groups["name"].Value).ToList();
        if (classes.Count == 0) return true;

        return Regex.Matches(source, "class=\"(?<list>[^\"]*)\"")
            .Select(m => m.Groups["list"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(list => classes.All(list.Contains));
    }

    private static List<string> RazorFiles() =>
        Directory.EnumerateFiles(WebProjectDirectory, "*.razor", SearchOption.AllDirectories)
            .Where(NotBuildOutput)
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
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KlavLor.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate KlavLor.slnx walking up from {AppContext.BaseDirectory}.");
    }
}
