using KlavLor.Application.Interfaces.Services;

namespace KlavLor.UnitTests;

// The rate-modifier cache is a singleton snapshot in production. These stand-ins keep the unit
// tests free of Infrastructure entirely, so nothing here needs a database or a running host.

internal sealed class NoRateModifiers : ISourceRateModifierCache
{
    public double GetMultiplier(string sourceName, string? itemName) => 1.0;
    public void Replace(IEnumerable<SourceRateModifierValue> modifiers) { }
}

// A single admin override, matched exactly the way the real cache matches: item-specific first,
// then source-wide (empty item name), then 1.0.
internal sealed class FixedRateModifier(string source, string? item, double multiplier) : ISourceRateModifierCache
{
    public double GetMultiplier(string sourceName, string? itemName)
    {
        if (!string.Equals(sourceName, source, StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (string.IsNullOrEmpty(item)) return multiplier;
        return string.Equals(itemName, item, StringComparison.OrdinalIgnoreCase) ? multiplier : 1.0;
    }

    public void Replace(IEnumerable<SourceRateModifierValue> modifiers) { }
}
