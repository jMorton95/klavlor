using KlavLor.Application.Features.Loot.Summary;

namespace KlavLor.Web.Application.Features.Loot.Summary;

/// <summary>Every URL the monthly summary builds, so the shell, the panels and the widget agree.</summary>
public static class SummaryLinks
{
    public static string Query(SummaryMonth month, int? characterId = null) =>
        characterId is { } id ? $"?month={month.Slug}&characterId={id}" : $"?month={month.Slug}";

    public static string Page(SummaryMonth month, int? characterId = null) =>
        AppRoutes.LootSummary + Query(month, characterId);

    public static string PageApi(SummaryMonth month, int? characterId = null) =>
        AppRoutes.LootSummary.FromApi() + Query(month, characterId);

    public static string Panel(string route, SummaryMonth month, int? characterId) =>
        route.FromApi() + Query(month, characterId);

    /// <summary>
    /// The character a link to another month should name. Only one the user picked: an unnamed
    /// summary follows the primary character month to month, which is what someone stepping back
    /// through their year expects, while a picked alt stays picked.
    /// </summary>
    public static int? CarriedCharacter(SummaryContext context) =>
        context.CharacterRequested ? context.Character?.CharacterId : null;

    /// <summary>
    /// A day of the month, opened from the summary. The day page is shared with the profile's
    /// heatmap and names no summary of its own, so the month rides along for its back link.
    /// </summary>
    public static string Day(int characterId, DateOnly day, SummaryMonth month) =>
        AppRoutes.LootLogCharacterDay.WithId(characterId).WithDate(day) + DayOrigin(month);

    public static string DayApi(int characterId, DateOnly day, SummaryMonth month) =>
        AppRoutes.LootLogCharacterDay.WithId(characterId).WithDate(day).FromApi() + DayOrigin(month);

    /// <summary>The summary a day page was opened from, if its query names a valid one.</summary>
    public static SummaryMonth? Origin(string? summary) =>
        SummaryMonth.TryParse(summary, out var month) ? month : null;

    private static string DayOrigin(SummaryMonth month) => $"?summary={month.Slug}";

    public static string Source(int characterId, string sourceName) =>
        AppRoutes.LootLogSource.WithId(characterId) + "?name=" + Uri.EscapeDataString(sourceName);

    public static string SourceApi(int characterId, string sourceName) =>
        AppRoutes.LootLogSource.WithId(characterId).FromApi() + "?name=" + Uri.EscapeDataString(sourceName);

    public static string ItemIcon(string itemName) => AppRoutes.ItemIcon.FromApi() + "?name=" + Uri.EscapeDataString(itemName);

    public static string SourceIcon(string sourceName) => AppRoutes.SourceIcon.FromApi() + "?name=" + Uri.EscapeDataString(sourceName);

    public static string PageTitle(SummaryMonth month) => $"KlavLor - {month.Label} Summary";

    /// <summary>The admin section listing every character's month.</summary>
    public static string AdminRoster => $"{AppRoutes.AdminSettings}/summaries";
}
