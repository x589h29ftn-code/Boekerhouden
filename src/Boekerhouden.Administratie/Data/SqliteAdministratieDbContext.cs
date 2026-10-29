using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Boekerhouden.Administratie.Data;

/// <summary>
/// Dezelfde database in één SQLite-bestand, voor de Windows-app (geen databaseserver). Eigen migraties
/// (<c>Data/MigratiesSqlite</c>). SQLite kent geen decimaal type en kan tijdstippen met tijdzone niet sorteren, dus
/// bedragen staan er exact als geheel getal (× 10.000) en tijdstippen als getal dat op tijd sorteert.
/// </summary>
public sealed class SqliteAdministratieDbContext(DbContextOptions<SqliteAdministratieDbContext> options) : AdministratieDbContext(options)
{
    /// <summary>Bedragen in tienduizendsten: exact voor alles met hoogstens vier decimalen.</summary>
    public const decimal Schaal = 10_000m;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<decimal>().HaveConversion<DecimaalAlsGeheel>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    private sealed class DecimaalAlsGeheel() : ValueConverter<decimal, long>(d => (long)decimal.Round(d * Schaal), l => l / Schaal);
}

/// <summary>Voor <c>dotnet ef migrations add &lt;Naam&gt; --context SqliteAdministratieDbContext --output-dir Data/MigratiesSqlite</c>.</summary>
public sealed class SqliteOntwerpFactory : IDesignTimeDbContextFactory<SqliteAdministratieDbContext>
{
    public SqliteAdministratieDbContext CreateDbContext(string[] args)
    {
        var diensten = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();
        return new(new DbContextOptionsBuilder<SqliteAdministratieDbContext>()
            .UseSqlite("Data Source=ontwerp.db")
            .UseApplicationServiceProvider(diensten)
            .Options);
    }
}
