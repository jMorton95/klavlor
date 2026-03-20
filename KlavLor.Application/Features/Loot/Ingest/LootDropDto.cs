namespace KlavLor.Application.Features.Loot.Ingest;

public sealed class LootDropDto
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
}
