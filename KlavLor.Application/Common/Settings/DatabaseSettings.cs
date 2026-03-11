namespace KlavLor.Application.Common.Settings;

public class DatabaseSettings()
{
    public string? Host { get; init; }
    public string? Port { get; init; }
    public string? Database { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool IncludeErrorDetail { get; init; }

    public string ToConnectionString()
    {
        var conn = $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
        if (IncludeErrorDetail) conn += ";Include Error Detail=true";
        return conn;
    }
}
