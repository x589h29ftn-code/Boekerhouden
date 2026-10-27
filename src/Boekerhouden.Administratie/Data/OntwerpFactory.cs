using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Boekerhouden.Administratie.Data;

/// <summary>Voor <c>dotnet ef migrations add</c>; maakt geen verbinding.</summary>
public sealed class OntwerpFactory : IDesignTimeDbContextFactory<AdministratieDbContext>
{
    public AdministratieDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AdministratieDbContext>()
            .UseNpgsql("Host=localhost;Database=boekerhouden_administratie;Username=boekerhouden")
            .Options);
}
