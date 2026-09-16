using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rebelgent.Core.Domain;
using Rebelgent.Persistence.Repositories;

namespace Rebelgent.Persistence.Tests;

/// <summary>
/// SQLite integration tests for the <c>GetAllAsync</c> methods added to
/// <see cref="EfReleaseRepository"/> and <see cref="EfPackageRepository"/> for M9 metrics.
/// </summary>
public class ReleaseAndPackageGetAllAsyncSqliteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RebelgentDbContext _db;
    private readonly EfReleaseRepository _releaseRepository;
    private readonly EfPackageRepository _packageRepository;

    public ReleaseAndPackageGetAllAsyncSqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RebelgentDbContext(options);
        _db.Database.EnsureCreated();
        _releaseRepository = new EfReleaseRepository(_db);
        _packageRepository = new EfPackageRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Release_GetAllAsync_ReturnsAllPersistedReleases_UsingSQLite()
    {
        await _releaseRepository.AddAsync(new Release(Guid.NewGuid(), "1.0.0", "Release 1.0.0", "notes", false, new string('a', 40)));
        await _releaseRepository.AddAsync(new Release(Guid.NewGuid(), "1.1.0", "Release 1.1.0", "notes", false, new string('b', 40)));

        var all = await _releaseRepository.GetAllAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Release_GetAllAsync_Empty_ReturnsEmptyList_UsingSQLite()
    {
        var all = await _releaseRepository.GetAllAsync();

        Assert.Empty(all);
    }

    [Fact]
    public async Task Package_GetAllAsync_ReturnsAllPersistedPackages_UsingSQLite()
    {
        await _packageRepository.AddAsync(new Package(Guid.NewGuid(), "MyLib", "1.0.0", @"C:\out\MyLib.1.0.0.nupkg"));
        await _packageRepository.AddAsync(new Package(Guid.NewGuid(), "MyLib", "1.1.0", @"C:\out\MyLib.1.1.0.nupkg"));

        var all = await _packageRepository.GetAllAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Package_GetAllAsync_Empty_ReturnsEmptyList_UsingSQLite()
    {
        var all = await _packageRepository.GetAllAsync();

        Assert.Empty(all);
    }
}
