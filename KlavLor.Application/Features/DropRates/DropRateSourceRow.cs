namespace KlavLor.Application.Features.DropRates;

// A source in the admin drop-rate panel: how many rates it currently has, plus a
// transient note set right after a manual fetch.
public sealed record DropRateSourceRow(string SourceName, int RateCount, string? Note = null);
