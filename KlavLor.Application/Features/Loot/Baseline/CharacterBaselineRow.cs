namespace KlavLor.Application.Features.Loot.Baseline;

// One configured baseline in the admin panel: which character, which source, and the seed KC.
public sealed record CharacterBaselineRow(int CharacterId, string CharacterName, string SourceName, int BaselineKc);
