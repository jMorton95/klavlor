using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Summary;

/// <summary>One of the signed-in user's characters and what it did in one month.</summary>
/// <param name="HourBuckets">Distinct clock hours containing a kill; see <see cref="ActiveHours"/>.</param>
/// <param name="ActiveDays">Distinct Europe/London dates containing a kill.</param>
public sealed record SummaryCharacter(
    int CharacterId,
    string Name,
    int Kills,
    int HourBuckets,
    int ActiveDays,
    long Gp)
{
    public double ActiveHours => Log.ActiveHours.FromHourBuckets(HourBuckets);
    public bool WasActive => Kills > 0;
}

/// <summary>
/// Every character the user owns, with its activity in one month, ordered MOST ACTIVE FIRST — so the
/// primary character is simply the first one that played at all. See
/// <c>ILootMonthlySummaryRepository.GetActivity</c> for what "most active" means.
/// </summary>
public sealed record SummaryActivity(
    IReadOnlyList<SummaryCharacter> Characters,
    DateTimeOffset? FirstActivityAt)
{
    public SummaryCharacter? Primary => Characters.FirstOrDefault(c => c.WasActive);
}

/// <summary>
/// Which month and which character a summary panel is about, plus what its navigation needs.
/// Every panel carries one, because every panel resolves it independently in its own request.
/// </summary>
/// <param name="Character">Null when the user played nothing at all that month.</param>
/// <param name="CharacterRequested">
/// True when the request named a character (and it was the user's own). Navigation then keeps
/// naming it; otherwise each month resolves its own primary.
/// </param>
/// <param name="ActiveCharacters">The user's characters that played this month, most active first.</param>
/// <param name="AdminViewOf">
/// The owner's name when an admin is looking at another user's character; null on anyone's own
/// summary. Everything else in the context is then the OWNER's — their characters, their history.
/// </param>
public sealed record SummaryContext(
    SummaryMonth Month,
    SummaryMonth CurrentMonth,
    SummaryMonth? EarliestMonth,
    SummaryCharacter? Character,
    bool IsPrimary,
    bool CharacterRequested,
    IReadOnlyList<SummaryCharacter> ActiveCharacters,
    string? AdminViewOf = null)
{
    public bool IsInProgress => Month == CurrentMonth;
    public bool HasPrevious => EarliestMonth is { } earliest && Month > earliest;
    public bool HasNext => Month < CurrentMonth;
    public bool IsAdminView => AdminViewOf is not null;
}

/// <summary>Whose character it is, for an admin opening someone else's summary.</summary>
public sealed record SummaryOwner(int UserId, string Name);

/// <summary>One character in the admin roster: its month, and whose it is.</summary>
public sealed record SummaryRosterEntry(SummaryCharacter Character, int OwnerUserId, string OwnerName, bool IsLeagues);

/// <summary>
/// Every character on the site with its activity in one month, most active first by the same rule
/// as <see cref="SummaryActivity"/>, plus when the site's history begins.
/// </summary>
public sealed record SummaryRosterActivity(IReadOnlyList<SummaryRosterEntry> Characters, DateTimeOffset? FirstActivityAt);

/// <summary>The admin area's way into any character's summary for one month.</summary>
public sealed record SummaryRoster(
    SummaryMonth Month,
    SummaryMonth CurrentMonth,
    SummaryMonth? EarliestMonth,
    IReadOnlyList<SummaryRosterEntry> Characters)
{
    public bool IsInProgress => Month == CurrentMonth;
    public bool HasPrevious => EarliestMonth is { } earliest && Month > earliest;
    public bool HasNext => Month < CurrentMonth;
    public IReadOnlyList<SummaryRosterEntry> Played => Characters.Where(c => c.Character.WasActive).ToList();
    public IReadOnlyList<SummaryRosterEntry> Idle => Characters.Where(c => !c.Character.WasActive).ToList();
}

/// <param name="PreviousMonth">The same character's figures one month earlier, for the deltas.</param>
/// <param name="Days">Only days with kills, as the activity calendar returns them.</param>
public sealed record SummaryOverview(
    SummaryContext Context,
    SummaryCharacter? PreviousMonth,
    IReadOnlyList<DayBucket> Days)
{
    public int NewClogs => Days.Sum(d => d.Clogs);
    public DayBucket? BusiestDay => SummaryHighlights.BusiestDay(Days);
    public int LongestStreak => SummaryHighlights.LongestStreak(Days);
}

/// <param name="KillOrdinal">The character's Nth kill of the source, admin baseline included.</param>
public sealed record SummaryClogEntry(
    DateTimeOffset OccurredAt,
    string SourceName,
    int ItemId,
    string ItemName,
    int Quantity,
    long Value,
    int? KillCount,
    int KillOrdinal,
    bool IsPet = false);

/// <param name="Total">Every new slot that month; Entries is capped.</param>
public sealed record SummaryClogPage(IReadOnlyList<SummaryClogEntry> Entries, int Total);

public sealed record SummaryCollection(SummaryContext Context, SummaryClogPage Page);

/// <summary>
/// One source's month. KC follows the site's canonical definition — the greater of the in-game
/// count RuneLite reported and our own count plus any admin baseline — evaluated at the END of
/// the month, and the start is the end minus the month's kills.
///
/// The in-game figure is the month's highest rather than the lifetime highest because in-game KC
/// only rises, so the two are the same number; reading the lifetime one would mean fetching
/// KillCount for every kill the character ever logged at the source.
/// </summary>
public sealed record SummarySourceRow(
    string SourceName,
    LootSourceType SourceType,
    int Kills,
    long Gp,
    int? MaxKillCount,
    int KillsBefore,
    int Baseline)
{
    public int EndKc => Math.Max(MaxKillCount ?? 0, KillsBefore + Kills + Baseline);
    public int StartKc => EndKc - Kills;
    public bool IsFirstEver => StartKc == 0;
    public int? Milestone => KcMilestones.HighestCrossed(StartKc, EndKc);
}

/// <param name="Total">Every source played that month; Rows is capped.</param>
public sealed record SummarySourcePage(IReadOnlyList<SummarySourceRow> Rows, int Total);

public sealed record SummarySources(SummaryContext Context, SummarySourcePage Page);

/// <summary>The best single receipt of one item in the month.</summary>
public sealed record SummaryDrop(
    int ItemId,
    string ItemName,
    int Quantity,
    long Value,
    string SourceName,
    DateTimeOffset OccurredAt)
{
    public LootFeedTier? Tier => ILootFeedService.GetDropTier(Value);
}

/// <param name="Floor">The smallest value a drop needed to count as notable.</param>
public sealed record SummaryDrops(SummaryContext Context, IReadOnlyList<SummaryDrop> Drops, long Floor);

public sealed record SummaryWidget(SummaryMonth Month, SummaryCharacter Character)
{
    /// <summary>
    /// The month the header button offers, or null when the user dismissed it. A dismissal names the
    /// month it dismissed, so it lapses on its own at midnight London time on the 1st, when the
    /// default month moves on.
    /// </summary>
    public static SummaryMonth? MonthToOffer(string? dismissedMonth, DateTimeOffset now)
    {
        var month = SummaryMonth.DefaultFor(now);
        return string.Equals(dismissedMonth, month.Slug, StringComparison.Ordinal) ? null : month;
    }
}

public static class SummaryHighlights
{
    /// <summary>Most GP; ties go to more kills, then the earlier day, so the pick is stable.</summary>
    public static DayBucket? BusiestDay(IReadOnlyList<DayBucket> days) =>
        days.Where(d => d.Kills > 0)
            .OrderByDescending(d => d.Gp)
            .ThenByDescending(d => d.Kills)
            .ThenBy(d => d.Day)
            .FirstOrDefault();

    /// <summary>The longest run of consecutive calendar days with at least one kill.</summary>
    public static int LongestStreak(IReadOnlyList<DayBucket> days)
    {
        var active = days.Where(d => d.Kills > 0).Select(d => d.Day.DayNumber).Distinct().Order().ToList();
        var longest = 0;
        var run = 0;
        for (var i = 0; i < active.Count; i++)
        {
            run = i > 0 && active[i] == active[i - 1] + 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    /// <summary>Change from <paramref name="previous"/> as a fraction, or null when there is nothing to compare against.</summary>
    public static double? Change(double current, double previous) =>
        previous > 0 ? (current - previous) / previous : null;
}
