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

    public static string Source(int characterId, string sourceName) =>
        AppRoutes.LootLogSource.WithId(characterId) + "?name=" + Uri.EscapeDataString(sourceName);

    public static string SourceApi(int characterId, string sourceName) =>
        AppRoutes.LootLogSource.WithId(characterId).FromApi() + "?name=" + Uri.EscapeDataString(sourceName);

    public static string ItemIcon(string itemName) => AppRoutes.ItemIcon.FromApi() + "?name=" + Uri.EscapeDataString(itemName);

    public static string SourceIcon(string sourceName) => AppRoutes.SourceIcon.FromApi() + "?name=" + Uri.EscapeDataString(sourceName);

    public static string PageTitle(SummaryMonth month) => $"KlavLor - {month.Label} Summary";
}
