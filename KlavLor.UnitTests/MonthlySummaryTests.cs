using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.Summary;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.UnitTests;

// The monthly summary's pure rules: which instants belong to which month, which month an
// unqualified URL means, and the figures derived from a month's aggregates. The SQL side is pinned
// in KlavLor.IntegrationTests/MonthlySummaryTests.cs.
public sealed class SummaryMonthTests
{
    [Fact]
    public void A_BST_month_starts_at_23_00_UTC_the_evening_before()
    {
        var october = new SummaryMonth(2026, 10);

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero), october.StartUtc);
        // The clocks go back on 25 October, so October ENDS on GMT midnight.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), october.EndUtc);
        Assert.Equal(TimeSpan.Zero, october.StartUtc.Offset);
    }

    [Fact]
    public void A_GMT_month_that_runs_into_BST_ends_at_23_00_UTC()
    {
        var march = new SummaryMonth(2026, 3);

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), march.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 23, 0, 0, TimeSpan.Zero), march.EndUtc);
    }

    [Fact]
    public void Consecutive_months_share_a_boundary_so_no_kill_falls_between_them()
    {
        var month = new SummaryMonth(2025, 11);
        for (var i = 0; i < 24; i++)
        {
            Assert.Equal(month.EndUtc, month.Next().StartUtc);
            month = month.Next();
        }
    }

    [Theory]
    [InlineData("2026-09-30T22:59:59Z", 2026, 9)]
    [InlineData("2026-09-30T23:00:00Z", 2026, 10)]   // midnight BST on 1 October
    [InlineData("2026-10-31T23:59:59Z", 2026, 10)]   // GMT by then, so still October
    [InlineData("2026-11-01T00:00:00Z", 2026, 11)]
    [InlineData("2026-12-31T23:59:59Z", 2026, 12)]
    [InlineData("2027-01-01T00:00:00Z", 2027, 1)]
    public void An_instant_belongs_to_its_London_month(string utc, int year, int month)
    {
        Assert.Equal(new SummaryMonth(year, month), SummaryMonth.Containing(DateTimeOffset.Parse(utc)));
    }

    [Fact]
    public void With_no_month_asked_for_the_summary_opens_on_the_last_complete_month()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new SummaryMonth(2026, 10), SummaryMonth.Resolve(null, now));

        var newYear = new DateTimeOffset(2027, 1, 1, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(new SummaryMonth(2026, 12), SummaryMonth.Resolve("", newYear));
    }

    [Theory]
    [InlineData("2026-10", 2026, 10)]
    [InlineData("2025-01", 2025, 1)]
    [InlineData("2026-11", 2026, 11)]    // the month in progress is allowed
    [InlineData("2027-03", 2026, 11)]    // a future month clamps to the current one
    [InlineData("garbage", 2026, 10)]    // unparseable falls back to the default
    [InlineData("2026-13", 2026, 10)]
    [InlineData("2026-1", 2026, 10)]
    public void A_requested_month_is_honoured_clamped_or_defaulted(string slug, int year, int month)
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new SummaryMonth(year, month), SummaryMonth.Resolve(slug, now));
    }

    [Fact]
    public void Slug_round_trips_and_stepping_crosses_year_boundaries()
    {
        var january = new SummaryMonth(2027, 1);

        Assert.Equal("2027-01", january.Slug);
        Assert.True(SummaryMonth.TryParse(january.Slug, out var parsed));
        Assert.Equal(january, parsed);
        Assert.Equal(new SummaryMonth(2026, 12), january.Previous());
        Assert.Equal(new SummaryMonth(2027, 2), january.Next());
        Assert.True(january.Previous() < january);
        Assert.Equal(31, january.DayCount);
        Assert.Equal(new DateOnly(2027, 1, 31), january.LastDay);
    }

    [Theory]
    [InlineData("2026-11-02T12:00:00Z", false)]  // the day after it was dismissed
    [InlineData("2026-11-30T23:59:59Z", false)]  // last second of November in London (GMT)
    [InlineData("2026-12-01T00:00:00Z", true)]   // midnight on the 1st: November's summary is offered
    [InlineData("2027-01-15T12:00:00Z", true)]
    public void A_dismissed_header_button_stays_hidden_until_the_1st_of_the_next_month(string utc, bool offered)
    {
        var month = SummaryWidget.MonthToOffer("2026-10", DateTimeOffset.Parse(utc));

        Assert.Equal(offered, month is not null);
    }

    [Theory]
    [InlineData("2027-04-30T22:59:59Z", false)]  // 23:59:59 BST on 30 April
    [InlineData("2027-04-30T23:00:00Z", true)]   // midnight BST on 1 May
    public void A_dismissal_lapses_at_London_midnight_in_summer_time(string utc, bool offered)
    {
        Assert.Equal(offered, SummaryWidget.MonthToOffer("2027-03", DateTimeOffset.Parse(utc)) is not null);
    }

    [Fact]
    public void Without_a_dismissal_the_last_complete_month_is_offered()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(new SummaryMonth(2026, 10), SummaryWidget.MonthToOffer(null, now));
        Assert.Equal(new SummaryMonth(2026, 10), SummaryWidget.MonthToOffer("2026-09", now));
    }

    [Fact]
    public void Navigation_stops_at_the_first_month_with_data_and_at_the_current_month()
    {
        var current = new SummaryMonth(2026, 11);
        SummaryContext Context(SummaryMonth month) =>
            new(month, current, new SummaryMonth(2026, 6), null, false, false, []);

        Assert.False(Context(new SummaryMonth(2026, 6)).HasPrevious);
        Assert.True(Context(new SummaryMonth(2026, 7)).HasPrevious);
        Assert.True(Context(new SummaryMonth(2026, 10)).HasNext);
        Assert.False(Context(current).HasNext);
        Assert.True(Context(current).IsInProgress);

        var neverPlayed = new SummaryContext(new SummaryMonth(2026, 10), current, null, null, false, false, []);
        Assert.False(neverPlayed.HasPrevious);
    }
}

public sealed class SummaryFigureTests
{
    [Theory]
    [InlineData(0, 49, null)]
    [InlineData(0, 50, 50)]
    [InlineData(49, 50, 50)]
    [InlineData(50, 99, null)]      // 50 was passed BEFORE the month
    [InlineData(80, 1_100, 1_000)]  // several crossed: only the highest is reported
    [InlineData(999, 1_000, 1_000)]
    [InlineData(1_000, 1_001, null)]
    [InlineData(9_000, 120_000, 100_000)]
    public void Only_the_highest_milestone_crossed_within_the_month_is_reported(int start, int end, int? expected)
    {
        Assert.Equal(expected, KcMilestones.HighestCrossed(start, end));
    }

    [Fact]
    public void Kill_count_is_tracked_kills_plus_baseline_when_RuneLite_reports_none()
    {
        var row = new SummarySourceRow("Vorkath", LootSourceType.Npc, Kills: 30, Gp: 0,
            MaxKillCount: null, KillsBefore: 960, Baseline: 20);

        Assert.Equal(1_010, row.EndKc);
        Assert.Equal(980, row.StartKc);
        Assert.Equal(1_000, row.Milestone);
        Assert.False(row.IsFirstEver);
    }

    [Fact]
    public void The_in_game_kill_count_wins_when_it_is_higher_than_ours()
    {
        // Tracking began long after the character started: we have 10 kills, the game says 2,507.
        var row = new SummarySourceRow("Zulrah", LootSourceType.Npc, Kills: 10, Gp: 0,
            MaxKillCount: 2_507, KillsBefore: 0, Baseline: 0);

        Assert.Equal(2_507, row.EndKc);
        Assert.Equal(2_497, row.StartKc);
        Assert.Equal(2_500, row.Milestone);
        Assert.False(row.IsFirstEver);
    }

    [Fact]
    public void A_source_never_killed_before_the_month_is_a_first()
    {
        var row = new SummarySourceRow("Scurrius", LootSourceType.Npc, Kills: 3, Gp: 0,
            MaxKillCount: null, KillsBefore: 0, Baseline: 0);

        Assert.True(row.IsFirstEver);
        Assert.Equal(0, row.StartKc);
    }

    [Fact]
    public void The_longest_streak_counts_consecutive_days_only()
    {
        var days = new[] { 1, 2, 3, 5, 6, 7, 8, 12 }
            .Select(d => new DayBucket(new DateOnly(2026, 10, d), Kills: 1, Gp: 0))
            .ToList();

        Assert.Equal(4, SummaryHighlights.LongestStreak(days));
        Assert.Equal(0, SummaryHighlights.LongestStreak([]));
        Assert.Equal(1, SummaryHighlights.LongestStreak([new DayBucket(new DateOnly(2026, 10, 9), 4, 0)]));
    }

    [Fact]
    public void Consecutive_means_consecutive_dates_not_consecutive_day_numbers()
    {
        var days = new[] { new DateOnly(2026, 2, 27), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 1) }
            .Select(d => new DayBucket(d, 1, 0))
            .ToList();

        Assert.Equal(3, SummaryHighlights.LongestStreak(days));
    }

    [Fact]
    public void The_busiest_day_is_the_richest_with_a_stable_tie_break()
    {
        var days = new List<DayBucket>
        {
            new(new DateOnly(2026, 10, 3), Kills: 50, Gp: 9_000_000),
            new(new DateOnly(2026, 10, 9), Kills: 80, Gp: 9_000_000),
            new(new DateOnly(2026, 10, 1), Kills: 80, Gp: 9_000_000),
            new(new DateOnly(2026, 10, 20), Kills: 900, Gp: 2_000_000)
        };

        Assert.Equal(new DateOnly(2026, 10, 1), SummaryHighlights.BusiestDay(days)!.Day);
        Assert.Null(SummaryHighlights.BusiestDay([]));
    }

    [Theory]
    [InlineData(150, 100, 0.5)]
    [InlineData(50, 100, -0.5)]
    [InlineData(100, 100, 0.0)]
    public void Change_is_relative_to_the_previous_month(double current, double previous, double expected)
    {
        Assert.Equal(expected, SummaryHighlights.Change(current, previous)!.Value, precision: 6);
    }

    [Fact]
    public void There_is_no_change_to_report_from_a_month_with_nothing_in_it()
    {
        Assert.Null(SummaryHighlights.Change(40, 0));
    }

    [Fact]
    public void Active_hours_on_the_summary_are_the_profiles_estimate()
    {
        var character = new SummaryCharacter(1, "Main", Kills: 100, HourBuckets: 40, ActiveDays: 9, Gp: 0);

        Assert.Equal(ActiveHours.FromHourBuckets(40), character.ActiveHours);
        Assert.Equal(30, character.ActiveHours);
    }

    [Fact]
    public void A_notable_drop_is_coloured_by_the_feeds_own_tier_rule()
    {
        static SummaryDrop Drop(long value) => new(1, "x", 1, value, "src", DateTimeOffset.UnixEpoch);

        Assert.Equal(ILootFeedService.GetDropTier(250_000), Drop(250_000).Tier);
        Assert.Equal(LootFeedTier.Uncommon, Drop(250_000).Tier);
        Assert.Equal(LootFeedTier.Standard, Drop(MonthlySummaryHandler.NotableFloor).Tier);
    }
}
