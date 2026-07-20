using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceA.Api.Data;
using ServiceB.Api.Data;

namespace Sync.UnitTests.Support;

public sealed class ProducerDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public ProducerDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = Create();
        context.Database.EnsureCreated();
    }

    public ProductDbContext Create() =>
        new(new DbContextOptionsBuilder<ProductDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

public sealed class ConsumerDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public ConsumerDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = Create();
        context.Database.EnsureCreated();
    }

    public SyncDbContext Create() =>
        new(new DbContextOptionsBuilder<SyncDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
