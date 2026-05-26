namespace KlavLor.Application.Features.Loot.Log;

public static class SessionInference
{
    public readonly record struct WindowResult(DateTimeOffset WindowStart, long Total, int Count);

    // Sliding-window aggregation over time-ordered events: find the contiguous
    // `window` span with the highest sum of `value`. Events are expected to be
    // sorted ascending by time; caller is responsible for ordering. Returns the
    // window aligned to its earliest event (not arbitrary timestamps), which
    // matches the natural OSRS framing of "best hour from event X".
    public static WindowResult? BestRollingWindow(
        IReadOnlyList<(DateTimeOffset At, long Value)> events,
        TimeSpan window)
    {
        if (events.Count == 0) return null;

        long bestTotal = long.MinValue;
        var bestStartIdx = 0;
        var bestEndIdxExclusive = 1;

        long runningTotal = 0;
        var left = 0;
        for (var right = 0; right < events.Count; right++)
        {
            runningTotal += events[right].Value;
            while (events[right].At - events[left].At > window)
            {
                runningTotal -= events[left].Value;
                left++;
            }

            if (runningTotal > bestTotal)
            {
                bestTotal = runningTotal;
                bestStartIdx = left;
                bestEndIdxExclusive = right + 1;
            }
        }

        return new WindowResult(
            events[bestStartIdx].At,
            bestTotal,
            bestEndIdxExclusive - bestStartIdx);
    }
}
