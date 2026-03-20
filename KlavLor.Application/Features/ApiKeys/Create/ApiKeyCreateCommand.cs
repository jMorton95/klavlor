namespace KlavLor.Application.Features.ApiKeys.Create;

public sealed class ApiKeyCreateCommand
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
}
