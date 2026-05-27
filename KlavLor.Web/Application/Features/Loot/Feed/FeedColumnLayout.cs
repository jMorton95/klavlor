namespace KlavLor.Web.Application.Features.Loot.Feed;

/// <summary>
/// Responsive Tailwind classes for the loot-feed column layout, shared by the live feed
/// (<c>LootFeedGrid</c>) and the per-day feed page so their column widths stay in sync.
/// </summary>
public static class FeedColumnLayout
{
    public static string ContainerClass(int columnCount) => columnCount >= 5
        ? "flex flex-wrap gap-3 justify-center xl:justify-start"
        : "flex flex-wrap gap-3 justify-center";

    public static string ColumnClass(int columnCount) => columnCount switch
    {
        >= 5 => "w-full md:w-[calc(50%-0.375rem)] xl:w-[calc(20%-0.6rem)]",
        4    => "w-full md:w-[calc(50%-0.375rem)] xl:w-[calc(25%-0.5625rem)]",
        3    => "w-full md:w-[calc(50%-0.375rem)] xl:w-[calc(33.333%-0.5rem)]",
        2    => "w-full md:w-[calc(50%-0.375rem)] xl:w-[calc(40%-0.375rem)]",
        _    => "w-full md:w-[calc(50%-0.375rem)] xl:w-[min(50%,32rem)]"
    };
}
