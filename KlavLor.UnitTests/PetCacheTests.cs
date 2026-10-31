using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Services;

namespace KlavLor.UnitTests;

// IPetCache decides which zero-value drops reach the Legendary lane. It matches the way
// ICollectionLogCache does - id OR name, case-insensitive - because the three vocabularies that name
// an item (the wiki, RuneLite, Temple) disagree on both.
public sealed class PetCacheTests
{
    private const int Baron = 28_250;
    private const int Gull = 31_285;

    private static PetCache With(params CollectionLogEntryRef[] pets)
    {
        var cache = new PetCache();
        cache.Replace(pets);
        return cache;
    }

    [Fact]
    public void Nothing_is_a_pet_before_the_cache_is_primed()
    {
        var cache = new PetCache();

        Assert.False(cache.IsPet(Baron, "Baron"));
        Assert.Empty(cache.ItemIds);
        Assert.Empty(cache.LowerNames);
    }

    [Fact]
    public void A_pet_matches_on_its_id_whatever_name_it_arrives_under()
    {
        var cache = With(new CollectionLogEntryRef(Baron, "Baron"));

        Assert.True(cache.IsPet(Baron));
        Assert.True(cache.IsPet(Baron, "something else entirely"));
    }

    [Theory]
    [InlineData("Lil' Zik")]
    [InlineData("Lil' zik")]
    [InlineData("LIL' ZIK")]
    [InlineData(" Lil' zik ")]
    public void A_pet_logged_under_another_id_still_matches_by_name_in_any_case(string reported)
    {
        var cache = With(new CollectionLogEntryRef(22_473, "Lil' Zik"));

        Assert.True(cache.IsPet(999_999, reported));
    }

    [Fact]
    public void The_wikis_disambiguating_suffix_does_not_stop_the_in_game_name_matching()
    {
        var cache = With(new CollectionLogEntryRef(Gull, "Gull (pet)"));

        Assert.True(cache.IsPet(999_999, "Gull"));
        Assert.True(cache.IsPet(999_999, "Gull (pet)"));
    }

    [Fact]
    public void An_ordinary_drop_is_not_a_pet()
    {
        var cache = With(new CollectionLogEntryRef(Baron, "Baron"));

        Assert.False(cache.IsPet(4_151, "Abyssal whip"));
        Assert.False(cache.IsPet(4_151));
        Assert.False(cache.IsPet(4_151, ""));
    }

    [Fact]
    public void An_unnamed_pet_still_matches_on_its_id_and_contributes_no_name()
    {
        var cache = With(new CollectionLogEntryRef(Baron, ""));

        Assert.True(cache.IsPet(Baron));
        Assert.Empty(cache.LowerNames);
    }

    [Fact]
    public void Replace_swaps_the_whole_list_so_a_removed_pet_stops_matching()
    {
        var cache = With(new CollectionLogEntryRef(Baron, "Baron"));

        cache.Replace([new CollectionLogEntryRef(Gull, "Gull (pet)")]);

        Assert.False(cache.IsPet(Baron, "Baron"));
        Assert.True(cache.IsPet(Gull));
        Assert.Equal([Gull], cache.ItemIds);
    }

    [Fact]
    public void The_sets_handed_to_sql_are_lowered_so_they_compare_against_lower_name()
    {
        var cache = With(new CollectionLogEntryRef(Baron, "Baron"), new CollectionLogEntryRef(22_473, "Lil' Zik"));

        Assert.Equal(["baron", "lil' zik"], cache.LowerNames.Order());
    }
}
