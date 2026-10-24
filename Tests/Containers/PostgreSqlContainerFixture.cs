using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.Containers;

/// <summary>
/// Starts one PostgreSQL container per test collection and applies the real EF Core migrations to a template
/// database. Each test then clones that template (<see cref="CreateDatabaseAsync"/>), so tests see the production
/// schema and provider behaviour (string lengths, indexes, query translation) that the SQLite suites cannot.
/// When Docker is unavailable the tests are skipped rather than failed, so <c>dotnet test</c> still runs without it.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    // Overridable so a registry mirror can be used where Docker Hub is rate-limited.
    private static readonly string Image =
        Environment.GetEnvironmentVariable("TESTCONTAINERS_POSTGRES_IMAGE") ?? "postgres:16-alpine";

    private const string TemplateDatabase = "migrated_template";

    private PostgreSqlContainer? _container;
    private string? _unavailableReason;

    public async ValueTask InitializeAsync()
    {
        var container = new PostgreSqlBuilder(Image).Build();
        try
        {
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            _unavailableReason = $"PostgreSQL container could not start (is Docker running?): {ex.GetBaseException().Message}";
            await container.DisposeAsync();
            return;
        }

        _container = container;

        await ExecuteAsync($"CREATE DATABASE {TemplateDatabase}");
        await using (var db = CreateDbContext(ConnectionStringFor(TemplateDatabase)))
        {
            await db.Database.MigrateAsync();
        }

        // CREATE DATABASE ... TEMPLATE needs the template to have no open connections.
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Creates an empty, fully migrated database for one test and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        Assert.SkipWhen(_container is null, _unavailableReason ?? "PostgreSQL container is not available.");

        var name = $"test_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}");
        return ConnectionStringFor(name);
    }

    public static ApplicationDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container!.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container!.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "PostgreSQL container";
}
