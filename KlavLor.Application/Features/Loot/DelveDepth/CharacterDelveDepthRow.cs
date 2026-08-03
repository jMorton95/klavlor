namespace KlavLor.Application.Features.Loot.DelveDepth;

// One configured override in the admin panel: which character, which source, and the average depth.
public sealed record CharacterDelveDepthRow(int CharacterId, string CharacterName, string SourceName, int AverageDepth);
