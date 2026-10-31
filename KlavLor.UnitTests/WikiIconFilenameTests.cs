using KlavLor.Infrastructure.ExternalServices.OsrsWiki;

namespace KlavLor.UnitTests;

// The icon backfill derives an item's inventory sprite from its wiki page. For a pet the page image
// is the follower render, hundreds of pixels tall, which a square chip squashes flat. Every case
// below is a real page, checked against the wiki.
public sealed class WikiIconFilenameTests
{
    [Theory]
    [InlineData("Olmlet", "Olmlet_(follower).png", "Olmlet.png")]
    // The render is named after the monster; only the page title finds the pet's sprite.
    [InlineData("Pet Smoke Devil", "Smoke_Devil_(follower).png", "Pet_Smoke_Devil.png")]
    // Stripping the render name gives "Gull.png", the seagull NPC at 550x236.
    [InlineData("Gull (pet)", "Gull_(follower).png", "Gull_(pet).png")]
    public void A_pet_tries_the_sprite_named_after_its_page_first(string title, string pageImage, string expected) =>
        Assert.Equal(expected, OsrsWikiClient.DeriveIconFilenames(title, pageImage)[0]);

    [Theory]
    // A pet with several forms has no sprite under its own name, only one per form.
    [InlineData("Rift guardian", "Rift_guardian_(follower,_fire).png", "Rift_guardian_(fire).png")]
    [InlineData("Muphin", "Muphin_(follower,_ranged).png", "Muphin_(ranged).png")]
    [InlineData("Olmlet", "Olmlet_(follower).png", "Olmlet.png")]
    public void Then_falls_back_to_the_render_name_without_follower(string title, string pageImage, string expected) =>
        Assert.Equal(expected, OsrsWikiClient.DeriveIconFilenames(title, pageImage)[1]);

    [Theory]
    [InlineData("Twisted bow", "Twisted_bow_detail.png", "Twisted_bow.png")]
    [InlineData("Tumeken's shadow", "Tumeken's_shadow_detail_animated.gif", "Tumeken's_shadow.png")]
    public void An_ordinary_items_detail_image_resolves_to_its_one_sprite(string title, string pageImage, string expected) =>
        Assert.Equal([expected], OsrsWikiClient.DeriveIconFilenames(title, pageImage));

    [Fact]
    public void With_no_page_image_the_title_names_the_sprite() =>
        Assert.Equal(["Infernal_cape.png"], OsrsWikiClient.DeriveIconFilenames("Infernal cape", null));
}
