namespace KlavLor.Application.Features.DropRates;

// A collection-log item that currently has no linked drop rate, with the collection-log
// tab(s)/source(s) it belongs to (so an admin knows which source to fetch).
public sealed record ClogMissingRate(string Name, string? Sources);
