using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Boekerhouden.Administratie.Data;

/// <summary>
/// Voor <c>dotnet ef migrations add</c>; maakt geen verbinding. Met Identity-schemaversie 3 (passkeys), zoals de app
/// die instelt in <see cref="Opstart"/>.
/// </summary>
public sealed class OntwerpFactory : IDesignTimeDbContextFactory<AdministratieDbContext>
{
    public AdministratieDbContext CreateDbContext(string[] args)
    {
        var diensten = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();
        return new(new DbContextOptionsBuilder<AdministratieDbContext>()
            .UseNpgsql("Host=localhost;Database=boekerhouden_administratie;Username=boekerhouden")
            .UseApplicationServiceProvider(diensten)
            .Options);
    }
}
