using KlavLor.Infrastructure.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace KlavLor.IntegrationTests;

// Spins up a real PostgreSQL via Testcontainers and applies the full EF migration set
// (including the LootDrop projection + backfill), so tests exercise the real schema and
// raw SQL rather than an in-memory fake. Shared across a test collection.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    internal DataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new DataContext(options);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
