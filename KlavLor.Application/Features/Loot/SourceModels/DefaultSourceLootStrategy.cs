namespace KlavLor.Application.Features.Loot.SourceModels;

// Every ordinary source: one loot-table roll per kill, so a claim is one effective kill.
// Keyed on the empty string; SourceLootService hands it out for any unmapped source.
public sealed class DefaultSourceLootStrategy() : SourceLootStrategy(string.Empty)
{
    public override int EffectiveKills(IReadOnlyList<ClaimDrop> drops) => 1;
}
