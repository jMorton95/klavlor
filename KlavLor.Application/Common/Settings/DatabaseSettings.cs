namespace KlavLor.Application.Common.Settings;

public class DatabaseSettings()
{
    public string? Host { get; init; }
    public string? Port { get; init; }
    public string? Database { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public string ToConnectionString() => $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};Include Error Detail=true";
}
