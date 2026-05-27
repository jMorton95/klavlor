namespace KlavLor.Application.Common;

/// <summary>
/// Shared, abbreviated formatting for OSRS gold values (e.g. 1.25B, 3.4M, 950K).
/// Single source of truth so every profile/feed surface renders gold identically.
/// </summary>
public static class GoldFormat
{
    public static string Format(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000.0:F1}B";
        if (value >= 1_000_000) return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000) return $"{value / 1_000.0:F1}K";
        return value.ToString("N0");
    }
}
