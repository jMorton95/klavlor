using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.UnitTests;

// LootFeedGrouping's MaxGap / SessionBreakGap / PlayDayStart constants define session boundaries for
// several surfaces at once — the feed cards here in C#, and the session/trend SQL queries which
// mirror the same numbers by hand. The merge rule is pure, so it is unit-testable: no DB, no feed
// service, no host.
public sealed class LootFeedGroupingTests
{
    // Europe/London is UTC+0 in January, so these local wall-clock times are also UTC and the
    // play-day arithmetic is easy to read. PlayDayStart is 06:00, so 02:00 belongs to the previous
    // play-day.
    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(new DateTime(2026, 1, day, hour, minute, 0, DateTimeKind.Utc));

    private static LootFeedEntry Entry(
        DateTimeOffset occurredAt,
        LootFeedTier tier = LootFeedTier.Standard,
        DateTimeOffset? groupStartedAt = null,
        string sourceName = "Vorkath",
        int userId = 1,
        int? characterId = 10,
        long totalValue = 50_000,
        int runCount = 1,
        int? minKillCount = null,
        int? maxKillCount = null) =>
        new(
            UserName: "player",
            UserId: userId,
            SourceName: sourceName,
            SourceType: LootSourceType.Npc,
            TotalValue: totalValue,
            Drops: [new LootFeedDrop("Rune sword", 1, (int)totalValue)],
            OccurredAt: occurredAt,
            Tier: tier,
            GameCharacterId: characterId,
            RunCount: runCount,
            GroupStartedAt: groupStartedAt,
            MinKillCount: minKillCount,
            MaxKillCount: maxKillCount);

    // ------------------------------------------------------------------------- constants

    [Fact]
    public void The_session_constants_are_the_ones_the_SQL_mirrors()
    {
        // The session/trend SQL hard-codes these same numbers (`- INTERVAL '6 hours'` for the
        // play-day rollover), so changing one here without the other silently desynchronises the
        // feed cards from the session history.
        Assert.Equal(TimeSpan.FromHours(16), LootFeedGrouping.MaxGap);
        Assert.Equal(TimeSpan.FromHours(6), LootFeedGrouping.SessionBreakGap);
        Assert.Equal(TimeSpan.FromHours(6), LootFeedGrouping.PlayDayStart);
        Assert.Equal(TimeSpan.FromDays(3), LootFeedGrouping.WideMaxGap);

        // The break gap has to be strictly inside the outer cap or it could never fire.
        Assert.True(LootFeedGrouping.SessionBreakGap < LootFeedGrouping.MaxGap);
        Assert.True(LootFeedGrouping.MaxGap < LootFeedGrouping.WideMaxGap);
    }

    [Fact]
    public void Only_rare_and_epic_get_the_widened_multi_day_merge_window()
    {
        // Rare/Epic drops land far less often, so their cards keep accumulating across days instead
        // of fragmenting. Legendary deliberately stays on the 16h window despite being rarer still.
        Assert.Equal(LootFeedGrouping.WideMaxGap, LootFeedGrouping.MergeWindowFor(LootFeedTier.Rare));
        Assert.Equal(LootFeedGrouping.WideMaxGap, LootFeedGrouping.MergeWindowFor(LootFeedTier.Epic));

        Assert.Equal(LootFeedGrouping.MaxGap, LootFeedGrouping.MergeWindowFor(LootFeedTier.Standard));
        Assert.Equal(LootFeedGrouping.MaxGap, LootFeedGrouping.MergeWindowFor(LootFeedTier.Uncommon));
        Assert.Equal(LootFeedGrouping.MaxGap, LootFeedGrouping.MergeWindowFor(LootFeedTier.Legendary));
    }

    // --------------------------------------------------------------------- the group key

    [Fact]
    public void Entries_from_different_groups_never_merge()
    {
        var head = Entry(At(10, 12));

        Assert.Null(LootFeedGrouping.TryGetMergeDelta(head, Entry(At(10, 12), sourceName: "Zulrah")));
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(head, Entry(At(10, 12), userId: 2)));
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(head, Entry(At(10, 12), characterId: 11)));
        // Same group, same instant: merges.
        Assert.NotNull(LootFeedGrouping.TryGetMergeDelta(head, Entry(At(10, 12))));
    }

    [Fact]
    public void A_missing_character_id_still_forms_a_stable_group_key()
    {
        var withoutCharacter = Entry(At(10, 12), characterId: null);

        Assert.Equal(withoutCharacter.GroupKey, Entry(At(10, 13), characterId: null).GroupKey);
        Assert.NotEqual(withoutCharacter.GroupKey, Entry(At(10, 13), characterId: 10).GroupKey);
        Assert.True(LootFeedGrouping.CanMerge(withoutCharacter, Entry(At(10, 13), characterId: null)));
    }

    // ------------------------------------------------------------------------- outer cap

    [Fact]
    public void A_session_never_spans_more_than_the_outer_cap()
    {
        // 06:15 to 23:30 on the same play-day is 17h15m — over MaxGap, so it splits on the cap alone,
        // with the overnight rule not involved (both instants are in play-day Jan 10).
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(Entry(At(10, 23, 30)), Entry(At(10, 6, 15))));

        // Just inside the cap, same play-day: merges.
        var delta = LootFeedGrouping.TryGetMergeDelta(Entry(At(10, 21)), Entry(At(10, 6, 15)));
        Assert.Equal(TimeSpan.FromHours(14) + TimeSpan.FromMinutes(45), delta);
    }

    [Fact]
    public void The_cap_measures_the_whole_merged_span_not_just_the_new_gap()
    {
        // A group already spanning 10h (anchor 08:00 -> latest 18:00). A kill 10h after the latest
        // is only 10h from the nearest edge, but the merged span would be 20h — past the cap.
        var head = Entry(At(10, 18), groupStartedAt: At(10, 8));

        Assert.Null(LootFeedGrouping.TryGetMergeDelta(head, Entry(At(11, 4))));

        // 5h after the latest keeps the merged span at 15h, inside the cap.
        Assert.Equal(TimeSpan.FromHours(5), LootFeedGrouping.TryGetMergeDelta(head, Entry(At(10, 23))));
    }

    // -------------------------------------------------------------- overnight break split

    [Fact]
    public void A_long_gap_that_crosses_into_a_new_play_day_starts_a_new_session()
    {
        // Play past midnight (02:00, play-day Jan 9), sleep, resume 09:00 (play-day Jan 10).
        // The 7h gap is >= SessionBreakGap and lands on a different play-day, so it splits.
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(Entry(At(10, 9)), Entry(At(10, 2))));
        // Direction-independent: the same pair the other way round also splits.
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(Entry(At(10, 2)), Entry(At(10, 9))));
    }

    [Fact]
    public void A_long_gap_inside_one_play_day_does_not_split()
    {
        // 13:00 -> 20:00 is 7h, over SessionBreakGap, but both are play-day Jan 10 — a long break in
        // one day's session, not an overnight one.
        Assert.Equal(TimeSpan.FromHours(7), LootFeedGrouping.TryGetMergeDelta(Entry(At(10, 20)), Entry(At(10, 13))));
    }

    [Fact]
    public void A_short_gap_that_crosses_a_play_day_boundary_does_not_split()
    {
        // 03:00 (play-day Jan 9) -> 07:00 (play-day Jan 10) crosses the 06:00 rollover, but the 4h
        // gap is under SessionBreakGap, so it is one continuous late-night session.
        Assert.Equal(TimeSpan.FromHours(4), LootFeedGrouping.TryGetMergeDelta(Entry(At(10, 7)), Entry(At(10, 3))));
    }

    [Fact]
    public void A_late_night_run_across_midnight_stays_one_session()
    {
        // 23:00 Jan 10 -> 02:00 Jan 11: different calendar dates, but PlayDayStart at 06:00 puts both
        // in play-day Jan 10, so the 3h gap merges. This is exactly what PlayDayStart exists for.
        var earlier = Entry(At(10, 23));
        var later = Entry(At(11, 2));

        Assert.Equal(TimeSpan.FromHours(3), LootFeedGrouping.TryGetMergeDelta(later, earlier));

        // ...and an 8h version of the same crossing DOES split, because it is over the break gap and
        // 07:00 is in the next play-day.
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(Entry(At(11, 7)), earlier));
    }

    [Fact]
    public void The_split_is_measured_from_the_groups_nearest_edge()
    {
        // A group anchored at 20:00 Jan 10 whose latest kill is 02:00 Jan 11 (one late-night session).
        // A new kill at 03:00 Jan 11 is 1h from the latest edge and 7h from the anchor: the nearest
        // edge is what counts, so it merges rather than splitting on the anchor distance.
        var head = Entry(At(11, 2), groupStartedAt: At(10, 20));

        Assert.Equal(TimeSpan.FromHours(1), LootFeedGrouping.TryGetMergeDelta(head, Entry(At(11, 3))));
    }

    // ----------------------------------------------------------- the widened Rare/Epic window

    [Fact]
    public void Rare_and_epic_cards_span_days_and_skip_the_overnight_split()
    {
        // Two days apart, which would split twice over on the 16h window. On the widened window the
        // overnight rule is deliberately skipped — it exists precisely so a card can span sleep.
        foreach (var tier in new[] { LootFeedTier.Rare, LootFeedTier.Epic })
        {
            var delta = LootFeedGrouping.TryGetMergeDelta(Entry(At(12, 12), tier), Entry(At(10, 12), tier));
            Assert.Equal(TimeSpan.FromDays(2), delta);
        }

        // Four days apart is past WideMaxGap, so even a Rare card splits.
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(
            Entry(At(14, 12), LootFeedTier.Rare), Entry(At(10, 12), LootFeedTier.Rare)));
    }

    [Fact]
    public void A_standard_card_two_days_apart_still_splits()
    {
        // The contrast that makes the widened window meaningful.
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(Entry(At(12, 12)), Entry(At(10, 12))));
    }

    [Fact]
    public void The_merge_window_is_taken_from_the_head_entrys_tier()
    {
        // TryGetMergeDelta reads MergeWindowFor(head.Tier), so the existing group's tier decides the
        // window. Pinning this makes the asymmetry explicit rather than accidental.
        Assert.NotNull(LootFeedGrouping.TryGetMergeDelta(
            Entry(At(12, 12), LootFeedTier.Rare), Entry(At(10, 12), LootFeedTier.Standard)));
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(
            Entry(At(12, 12), LootFeedTier.Standard), Entry(At(10, 12), LootFeedTier.Rare)));
    }

    [Fact]
    public void CanMerge_agrees_with_TryGetMergeDelta()
    {
        foreach (var (head, next) in new[]
                 {
                     (Entry(At(10, 12)), Entry(At(10, 13))),
                     (Entry(At(10, 9)), Entry(At(10, 2))),
                     (Entry(At(12, 12)), Entry(At(10, 12))),
                     (Entry(At(10, 12)), Entry(At(10, 12), sourceName: "Zulrah"))
                 })
        {
            Assert.Equal(LootFeedGrouping.TryGetMergeDelta(head, next) is not null,
                LootFeedGrouping.CanMerge(head, next));
        }
    }

    // ----------------------------------------------------------------------------- Merge

    [Fact]
    public void Merge_accumulates_the_card_without_moving_its_identity()
    {
        var head = Entry(At(10, 15), totalValue: 60_000, runCount: 3, minKillCount: 120, maxKillCount: 140);
        var next = Entry(At(10, 11), totalValue: 25_000, runCount: 2, minKillCount: 100, maxKillCount: 110);

        var merged = LootFeedGrouping.Merge(head, next);

        Assert.Equal(85_000, merged.TotalValue);
        Assert.Equal(5, merged.RunCount);
        Assert.Equal(head.Drops.Count + next.Drops.Count, merged.Drops.Count);
        // The card spans earliest to latest.
        Assert.Equal(At(10, 15), merged.OccurredAt);
        Assert.Equal(At(10, 11), merged.GroupStartedAt);
        Assert.Equal(At(10, 11), merged.GroupAnchorAt);
        // Kill-count range widens both ways.
        Assert.Equal(100, merged.MinKillCount);
        Assert.Equal(140, merged.MaxKillCount);
        // Identity is the head's, so the DOM node the card already occupies is reused per group.
        Assert.Equal(head.GroupKey, merged.GroupKey);
        Assert.Equal(head.Tier, merged.Tier);
    }

    [Fact]
    public void Merge_keeps_the_head_drops_before_the_newly_merged_ones()
    {
        var head = Entry(At(10, 15));
        var next = Entry(At(10, 11));

        var merged = LootFeedGrouping.Merge(head, next);

        Assert.Equal(head.Drops[0], merged.Drops[0]);
        Assert.Equal(next.Drops[0], merged.Drops[^1]);
        // The originals are untouched — LootFeedEntry is a record and Merge builds a new list.
        Assert.Single(head.Drops);
        Assert.Single(next.Drops);
    }

    [Fact]
    public void Merge_treats_an_absent_kill_count_as_no_constraint_rather_than_zero()
    {
        var head = Entry(At(10, 15), minKillCount: null, maxKillCount: null);
        var next = Entry(At(10, 11), minKillCount: 100, maxKillCount: 110);

        var forwards = LootFeedGrouping.Merge(head, next);
        var backwards = LootFeedGrouping.Merge(next, head);

        // A null must not collapse the range to 0 — it means "unknown", so the known value wins.
        Assert.Equal(100, forwards.MinKillCount);
        Assert.Equal(110, forwards.MaxKillCount);
        Assert.Equal(100, backwards.MinKillCount);
        Assert.Equal(110, backwards.MaxKillCount);

        var neither = LootFeedGrouping.Merge(head, Entry(At(10, 11)));
        Assert.Null(neither.MinKillCount);
        Assert.Null(neither.MaxKillCount);
    }

    [Fact]
    public void Merge_is_order_independent_for_the_span_and_the_totals()
    {
        var a = Entry(At(10, 15), totalValue: 60_000, runCount: 3);
        var b = Entry(At(10, 11), totalValue: 25_000, runCount: 2);

        var forwards = LootFeedGrouping.Merge(a, b);
        var backwards = LootFeedGrouping.Merge(b, a);

        Assert.Equal(forwards.TotalValue, backwards.TotalValue);
        Assert.Equal(forwards.RunCount, backwards.RunCount);
        Assert.Equal(forwards.OccurredAt, backwards.OccurredAt);
        Assert.Equal(forwards.GroupAnchorAt, backwards.GroupAnchorAt);
    }

    // ------------------------------------------------------------------------ play-day model

    [Fact]
    public void The_play_day_rollover_is_expressed_in_the_ingest_timezone_not_UTC()
    {
        // PlayDayOf converts through IngestTimezone before subtracting PlayDayStart, so the rollover
        // follows Europe/London wall-clock time (and its DST shift) rather than UTC. In July the zone
        // is UTC+1, which means the 06:00 local rollover happens at 05:00 UTC.
        var midJuly = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(1), IngestTimezone.Zone.GetUtcOffset(midJuly));

        // Straddling the local rollover: 01:00 UTC is 02:00 local (play-day Jul 9) and 07:00 UTC is
        // 08:00 local (play-day Jul 10), so this 6h gap splits.
        var straddleEarlier = new DateTimeOffset(2026, 7, 10, 1, 0, 0, TimeSpan.Zero);
        var straddleLater = new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero);
        Assert.Null(LootFeedGrouping.TryGetMergeDelta(Entry(straddleLater), Entry(straddleEarlier)));

        // Wholly inside one play-day: 08:00 local -> 14:00 local. Identical 6h gap, so only the
        // rollover position can be the difference.
        var insideEarlier = new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero);
        var insideLater = new DateTimeOffset(2026, 7, 10, 13, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(6),
            LootFeedGrouping.TryGetMergeDelta(Entry(insideLater), Entry(insideEarlier)));
    }
}
