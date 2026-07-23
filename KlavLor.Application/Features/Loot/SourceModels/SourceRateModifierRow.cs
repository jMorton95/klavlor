namespace KlavLor.Application.Features.Loot.SourceModels;

// One row in the admin source-rate-modifier panel. ItemName is the empty string for a
// source-wide modifier, or a specific item name. Multiplier scales the expected kills-to-drop
// (2 = twice as dry, 0.5 = twice as common); 1.0 means no adjustment.
public sealed record SourceRateModifierRow(string SourceName, string ItemName, double Multiplier);
