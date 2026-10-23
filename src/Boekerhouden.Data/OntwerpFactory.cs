using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Boekerhouden.Data;

/// <summary>Alleen voor <c>dotnet ef migrations</c>: een database is daarvoor niet nodig.</summary>
public sealed class OntwerpFactory : IDesignTimeDbContextFactory<BoekerhoudenDbContext>
{
    public BoekerhoudenDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BoekerhoudenDbContext>()
            .UseNpgsql("Host=localhost;Database=boekerhouden;Username=boekerhouden")
            .Options);
}
