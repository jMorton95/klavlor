using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.UnitTests;

// A feed card covers a whole play session, and merging a further kill into it advances the card's
// MaxKillCount. The luck line under a drop must therefore NOT be computed from the card's kill
// count: doing that meant a Venator fang received on rate at kill 20 was re-judged against kill
// 126 as the session ran on, and reported as 6.7x dry when nothing about the drop had changed.
//
// The fix is that each drop carries the kill count it actually landed on. These tests pin that
// merging leaves those per-drop counts alone, which is the property the display depends on.
public sealed class FeedDropKillCountTests
{
    private const string Fang = "Venator fang";

    private static DateTimeOffset At(int hour) => new(new DateTime(2026, 1, 5, hour, 0, 0, DateTimeKind.Utc));

    private static LootFeedEntry Entry(
        DateTimeOffset occurredAt,
        int killCount,
        IReadOnlyList<LootFeedDrop>? drops = null) =>
        new(
            UserName: "player",
            UserId: 1,
            SourceName: "Venator",
            SourceType: LootSourceType.Npc,
            TotalValue: 50_000,
            Drops: (drops ?? [new LootFeedDrop(Fang, 1, 50_000, IsCollectionLogItem: true, ExpectedKc: 19, EffectiveRarity: "1/19", KillCount: killCount)]).ToList(),
            OccurredAt: occurredAt,
            Tier: LootFeedTier.Standard,
            GameCharacterId: 10,
            MinKillCount: killCount,
            MaxKillCount: killCount);

    [Fact]
    public void MergingLaterKills_leavesTheDropsOwnKillCountAlone()
    {
        // The reported bug, at model level: the drop came at 20, the session ran to 126.
        var card = Entry(At(10), killCount: 20);
        var later = Entry(At(12), killCount: 126, drops: [new LootFeedDrop("Bones", 1, 12_000, KillCount: 126)]);

        Assert.True(LootFeedGrouping.CanMerge(card, later));
        var merged = LootFeedGrouping.Merge(card, later);

        // The card now spans the whole session...
        Assert.Equal(126, merged.MaxKillCount);
        // ...but the fang still knows it arrived at 20, which is what its luck must be judged on.
        var fang = merged.Drops.Single(d => d.Name == Fang);
        Assert.Equal(20, fang.KillCount);
    }

    [Fact]
    public void TheCardsMaxKillCount_isNotTheDropsKillCount()
    {
        // Stated as its own assertion because the two being conflated is the entire defect: it is
        // not enough that the drop's count exists, it has to differ from the card's once merged.
        var merged = LootFeedGrouping.Merge(
            Entry(At(10), killCount: 20),
            Entry(At(12), killCount: 126, drops: [new LootFeedDrop("Bones", 1, 12_000, KillCount: 126)]));

        Assert.NotEqual(merged.MaxKillCount, merged.Drops.Single(d => d.Name == Fang).KillCount);
    }

    [Fact]
    public void OnRateAtItsOwnKillCount_wouldReadAsVeryDryAtTheCards()
    {
        // Pins the magnitude of the bug so the arithmetic behind it can't be argued with: at 1/19,
        // arriving on kill 20 is on rate, while the session's 126th kill is well past 3x dry.
        const double expected = 19;
        var merged = LootFeedGrouping.Merge(
            Entry(At(10), killCount: 20),
            Entry(At(12), killCount: 126, drops: [new LootFeedDrop("Bones", 1, 12_000, KillCount: 126)]));

        var atDrop = merged.Drops.Single(d => d.Name == Fang).KillCount!.Value / expected;
        var atCard = merged.MaxKillCount!.Value / expected;

        Assert.InRange(atDrop, 0.5, 1.5);   // "on rate" band
        Assert.True(atCard > 3.0, $"expected the card-wide ratio to look severely dry, got {atCard:0.#}");
    }

    [Fact]
    public void DropsOnOneCard_keepTheirOwnOrdinalsWhenNoKillCountWasReported()
    {
        // The Lunar Chest case. RuneLite reports no kill count for chest sources, so every drop
        // relied on the card's ordinal range — and four uniques spread across rolls 197 to 420 all
        // claimed 197, the session's opening roll, making a drop at 420 look as lucky as one at 197.
        // Each drop now carries its own resolved ordinal.
        var early = new LootFeedDrop("Blood moon chestplate", 1, 5_000_000, IsCollectionLogItem: true, KillOrdinal: 201);
        var late = new LootFeedDrop("Dual macuahuitl", 1, 8_000_000, IsCollectionLogItem: true, KillOrdinal: 415);

        var card = Entry(At(10), killCount: 0, drops: [early, late]) with
        {
            MinKillCount = null,
            MaxKillCount = null,
            MinKillOrdinal = 197,
            MaxKillOrdinal = 420
        };

        // What the display resolves for each drop.
        int? Observed(LootFeedDrop d) => d.KillCount ?? d.KillOrdinal ?? card.MinKillCount ?? card.MinKillOrdinal;

        Assert.Equal(201, Observed(card.Drops[0]));
        Assert.Equal(415, Observed(card.Drops[1]));
        // Specifically NOT the card's opening roll, which is what the old fallback produced.
        Assert.NotEqual(card.MinKillOrdinal, Observed(card.Drops[1]));
    }

    [Fact]
    public void AReportedKillCount_winsOverTheDerivedOrdinal()
    {
        // RuneLite's own number is authoritative when present; the ordinal is only a stand-in.
        var drop = new LootFeedDrop(Fang, 1, 50_000, IsCollectionLogItem: true, KillCount: 20, KillOrdinal: 999);

        Assert.Equal(20, drop.KillCount ?? drop.KillOrdinal);
    }

    [Fact]
    public void ADropWithNoReportedKillCount_leavesTheFigureToTheCardsFirstKill()
    {
        // RuneLite doesn't always report a KC. The fallback is deliberately the session's FIRST
        // kill, not its latest, so the figure still can't drift upward while the session runs.
        var drop = new LootFeedDrop(Fang, 1, 50_000, IsCollectionLogItem: true, ExpectedKc: 19, EffectiveRarity: "1/19");

        Assert.Null(drop.KillCount);

        var card = Entry(At(10), killCount: 20, drops: [drop]) with { MinKillCount = 5, MaxKillCount = 126 };
        var observed = card.Drops[0].KillCount ?? card.MinKillCount ?? card.MaxKillOrdinal;

        Assert.Equal(5, observed);
    }
}
