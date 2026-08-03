namespace KlavLor.Domain.Entities;

// Admin-set average delve depth for a character at a depth-modelled source (Doom of Mokhaiotl).
//
// We cannot read delve depth from the loot payload, and the only in-payload signal — Demon tear
// quantity — turns out not to be depth-proportional (the wiki gives 50 guaranteed plus a 100–300
// roll), so inferring it produced numbers that read systematically low. Rather than guess, the
// strategy assumes a stated default and an admin can correct it per character from the admin hub.
//
// Keyed by (GameCharacterId, SourceName), mirroring CharacterSourceBaseline.
public sealed class CharacterDelveDepth
{
    public int GameCharacterId { get; set; }
    public string SourceName { get; set; } = "";

    /// <summary>Average delve levels cleared per run. Overrides the assumed default for this character.</summary>
    public int AverageDepth { get; set; }
}
