using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

// Admin-configured multiplier applied to a source's expected kills-to-drop, on top of whatever
// the code-based source model already computes. Lets us hand-correct sources whose stored wiki
// rates don't reflect real per-player odds (raids, Perilous Moons, etc.) without a code change.
//
// A multiplier of 2 means "expect twice as many kills as the raw rate implies" (twice as dry);
// 0.5 means half. ItemName is the empty string for a source-wide modifier, or a specific item
// name to override a single item. (SourceName, ItemName) is the business key.
public sealed class SourceRateModifier : Entity
{
    [Required, StringLength(100)]
    public string SourceName { get; set; } = "";

    // Empty string = applies to every item from the source. A specific name overrides just that item.
    [StringLength(100)]
    public string ItemName { get; set; } = "";

    public double Multiplier { get; set; } = 1.0;
}
