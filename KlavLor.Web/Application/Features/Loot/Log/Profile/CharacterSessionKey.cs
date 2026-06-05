using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Web.Application.Features.Loot.Log.Profile;

internal static class CharacterSessionKey
{
    // Globally-unique-per-page DOM key for a cross-source session card. (SourceName, session
    // index) is unique, so a slug of the source name plus the per-source session index
    // disambiguates same-numbered sessions from different sources — including across the
    // paginated "show more" appends, where a plain list index would collide.
    public static string For(CharacterSession cs) =>
        new string(cs.SourceName.Where(char.IsLetterOrDigit).ToArray()) + "-" + cs.Session.Index;
}
