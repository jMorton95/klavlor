namespace KlavLor.Application.Features.Loot.Ingest;

public sealed class LootIngestCommand
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int KillCount { get; set; }
    public string Type { get; set; } = "";
    public List<LootDropDto> Drops { get; set; } = [];
    public string Date { get; set; } = "";
    public string? ContentHash { get; set; }
    public bool Imported { get; set; }
}
