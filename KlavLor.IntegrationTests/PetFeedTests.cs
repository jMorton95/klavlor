using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.CollectionLog;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// RuneLite's loot logger attaches a pet to the kill that produced it, priced at 0 GP, so by value it
// never clears the feed's 10k floor. IPetCache puts it in the Legendary lane instead. The live path
// is ClassifyDropsByTier (unit-tested); these pin the backfill, which has to FIND such a record
// first - a zero-value record is invisible to the value-ranged candidate fetch every other lane uses.
[Collection("postgres")]
public sealed class PetFeedTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);

    private static LootFeedRepository Repo(DataContext ctx, IPetCache pets) =>
        new(ctx, NullLogger<LootFeedRepository>.Instance, new FakeClogCache(), pets, Fakes.DropReader());

    private static int UniqueId() => 900_000_000 + Random.Shared.Next(1, 99_999_999);

    private static string Tag(string name) => $"{name} {Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task A_zero_value_pet_lands_in_the_legendary_lane_and_nowhere_else()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "pet-lane");
        var baronId = UniqueId();
        var baron = Tag("Baron");
        Seed.AddKill(ctx, userId, charId, "Duke Sucellus", T, 57, [new(baron, baronId, 1, 0, IsFirstTime: true)]);
        await ctx.SaveChangesAsync();

        var tiers = await Repo(ctx, new FakePetCache(baronId)).GetAllFeedTiers(50, gameCharacterId: charId);

        var card = Assert.Single(tiers[LootFeedTier.Legendary]);
        var drop = Assert.Single(card.Drops);
        Assert.Equal(baron, drop.Name);
        Assert.True(drop.IsPet);
        Assert.False(drop.IsSpecial);
        Assert.Equal(57, card.MinKillCount);
        Assert.All(ILootFeedService.AllTiers.Where(t => t != LootFeedTier.Legendary),
            t => Assert.Empty(tiers[t]));
    }

    [Fact]
    public async Task Without_the_pet_list_the_same_kill_reaches_no_lane()
    {
        // The control: it is the cache, not anything about the record, that puts it in Legendary.
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "pet-none");
        Seed.AddKill(ctx, userId, charId, "Duke Sucellus", T, 57, [new(Tag("Baron"), UniqueId(), 1, 0)]);
        await ctx.SaveChangesAsync();

        var tiers = await Repo(ctx, new FakePetCache()).GetAllFeedTiers(50, gameCharacterId: charId);

        Assert.All(tiers.Values, lane => Assert.Empty(lane));
    }

    [Fact]
    public async Task A_pet_logged_under_an_id_the_taxonomy_does_not_carry_is_found_by_name()
    {
        // The name half of the candidate query. RuneLite's id and the taxonomy's id do not always
        // agree, and a pet matched only by name has to be found by SQL before it can be classified.
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "pet-name");
        var name = Tag("Lil' Zik");
        Seed.AddKill(ctx, userId, charId, "Theatre of Blood", T, 40, [new(name, UniqueId(), 1, 0)]);
        await ctx.SaveChangesAsync();

        var pets = new FakePetCache();
        pets.Replace([new CollectionLogEntryRef(UniqueId(), name.ToUpperInvariant())]);
        var tiers = await Repo(ctx, pets).GetAllFeedTiers(50, gameCharacterId: charId);

        Assert.True(Assert.Single(Assert.Single(tiers[LootFeedTier.Legendary]).Drops).IsPet);
    }

    [Fact]
    public async Task A_kill_with_a_pet_and_a_valued_drop_splits_across_both_lanes()
    {
        // Lanes are per drop. The pet goes up; the 50M drop beside it stays in Epic and does not
        // follow the pet into Legendary.
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "pet-split");
        var nexlingId = UniqueId();
        var nexling = Tag("Nexling");
        var vambraces = Tag("Zaryte vambraces");
        Seed.AddKill(ctx, userId, charId, "Nex", T, 200,
            [new(nexling, nexlingId, 1, 0), new(vambraces, UniqueId(), 1, 50_000_000)]);
        await ctx.SaveChangesAsync();

        var tiers = await Repo(ctx, new FakePetCache(nexlingId)).GetAllFeedTiers(50, gameCharacterId: charId);

        Assert.Equal(nexling, Assert.Single(Assert.Single(tiers[LootFeedTier.Legendary]).Drops).Name);
        Assert.Equal(vambraces, Assert.Single(Assert.Single(tiers[LootFeedTier.Epic]).Drops).Name);
    }

    [Fact]
    public async Task GetPets_reads_the_all_pets_category_named_from_the_synced_item_list()
    {
        await using var ctx = fx.CreateContext();
        var named = UniqueId();
        var unnamed = UniqueId();
        var outsider = UniqueId();
        var name = Tag("Baron");
        ctx.CollectionLogCategoryItems.AddRange(
            new CollectionLogCategoryItem { CategorySlug = "all_pets", ItemId = named },
            new CollectionLogCategoryItem { CategorySlug = "all_pets", ItemId = unnamed },
            new CollectionLogCategoryItem { CategorySlug = "duke_sucellus", ItemId = outsider });
        Seed.AddClogItem(ctx, named, name);
        await ctx.SaveChangesAsync();

        var pets = await new CollectionLogRepository(ctx, NullLogger<CollectionLogRepository>.Instance).GetPets();

        Assert.Contains(new CollectionLogEntryRef(named, name), pets);
        // No synced item for this id yet: still a pet, matched on its id alone.
        Assert.Contains(new CollectionLogEntryRef(unnamed, ""), pets);
        Assert.DoesNotContain(pets, p => p.ItemId == outsider);
    }
}
