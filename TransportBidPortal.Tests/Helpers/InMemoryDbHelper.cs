using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Data;

namespace TransportBidPortal.Tests.Helpers;

public static class InMemoryDbHelper
{
    public static AppDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? $"Test_{Guid.NewGuid():N}")
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
