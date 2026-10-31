using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Data;

/// <summary>
/// De database van het administratiepakket. Alle gegevens van een administratie (<see cref="IVanAdministratie"/>)
/// zijn gefilterd op <see cref="Geopend"/>: zonder geopende administratie geeft een query niets. Openen gebeurt
/// alleen via <c>Toegang</c>, na controle van het lidmaatschap.
/// </summary>
public class AdministratieDbContext : IdentityDbContext<Gebruiker, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    public AdministratieDbContext(DbContextOptions<AdministratieDbContext> options)
        : base(options)
    {
    }

    /// <summary>Voor <see cref="SqliteAdministratieDbContext"/>, met eigen migraties.</summary>
    protected AdministratieDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>
    /// Laat andere boekingen in dezelfde administratie wachten tot het eind van deze transactie (volgnummers en de
    /// hash-keten). Op PostgreSQL met een rij-lock; SQLite laat sowieso maar één schrijvende transactie tegelijk toe.
    /// </summary>
    public async Task Vergrendel(Guid administratie)
    {
        if (Database.IsNpgsql())
        {
            await Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Administraties\" WHERE \"Id\" = {administratie} FOR UPDATE");
        }
    }

    /// <summary>De administratie waarvan deze context gegevens laat zien; leeg = geen.</summary>
    public Guid? Geopend { get; set; }

    public DbSet<Administratie> Administraties => Set<Administratie>();

    public DbSet<Lidmaatschap> Lidmaatschappen => Set<Lidmaatschap>();

    public DbSet<Grootboekrekening> Rekeningen => Set<Grootboekrekening>();

    public DbSet<Dagboekdefinitie> Dagboeken => Set<Dagboekdefinitie>();

    public DbSet<Boeking> Boekingen => Set<Boeking>();

    public DbSet<Boekingsregel> Boekingsregels => Set<Boekingsregel>();

    public DbSet<AuditRegel> AuditRegels => Set<AuditRegel>();

    /// <summary>De boekingen die gelden: van elke boeking de laatste versie (niet vervangen door een nieuwere).</summary>
    public IQueryable<Boeking> Geldend => Boekingen.Where(b => !Boekingen.Any(v => v.Vervangt == b.Id));

    /// <summary>De regels van de boekingen die gelden.</summary>
    public IQueryable<Boekingsregel> GeldendeRegels => Boekingsregels.Where(r => !Boekingen.Any(v => v.Vervangt == r.BoekingId));

    public DbSet<Handelsrelatie> Relaties => Set<Handelsrelatie>();

    public DbSet<Document> Documenten => Set<Document>();

    public DbSet<DocumentInhoud> DocumentInhouden => Set<DocumentInhoud>();

    public DbSet<OpenstaandePost> OpenstaandePosten => Set<OpenstaandePost>();

    public DbSet<Leerkeuze> Leerkeuzes => Set<Leerkeuze>();

    public DbSet<Automatiseringsregel> Regels => Set<Automatiseringsregel>();

    public DbSet<Kantoorinstellingen> Kantoorinstellingen => Set<Kantoorinstellingen>();

    public DbSet<Zoekplek> Zoekplekken => Set<Zoekplek>();

    public DbSet<Afschrift> Afschriften => Set<Afschrift>();

    public DbSet<Btwcodedefinitie> Btwcodes => Set<Btwcodedefinitie>();

    public DbSet<Btwtariefrecord> Btwtarieven => Set<Btwtariefrecord>();

    public DbSet<Banktransactie> Banktransacties => Set<Banktransactie>();

    public DbSet<Verkoopdocument> Verkoopdocumenten => Set<Verkoopdocument>();

    public DbSet<Verkoopdocumentregel> Verkoopdocumentregels => Set<Verkoopdocumentregel>();

    /// <summary>De sleutels van ASP.NET Core Data Protection (cookies, tokens).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.Entity<Gebruiker>(e =>
        {
            e.Property(g => g.Naam).HasMaxLength(200);
            e.Property(g => g.Initialen).HasMaxLength(10);
        });

        builder.Entity<Administratie>(e =>
        {
            e.ToTable("Administraties");
            e.Property(a => a.Naam).HasMaxLength(300);
            e.Property(a => a.Ondernemingsvorm).HasConversion<string>().HasMaxLength(30);
            e.Property(a => a.KvkNummer).HasMaxLength(20);
            e.Property(a => a.BtwNummer).HasMaxLength(20);
            e.Property(a => a.AangemaaktDoor).HasMaxLength(200);
            e.Property(a => a.LimietAutomatisch).HasPrecision(18, 2);
            e.Property(a => a.Bandbreedte).HasPrecision(6, 2);
            e.Property(a => a.Documentmap).HasMaxLength(400);
            e.Property(a => a.Straat).HasMaxLength(200);
            e.Property(a => a.Postcode).HasMaxLength(20);
            e.Property(a => a.Plaats).HasMaxLength(100);
            e.Property(a => a.Land).HasMaxLength(2);
            e.Property(a => a.Email).HasMaxLength(200);
            e.Property(a => a.Telefoon).HasMaxLength(50);
            e.Property(a => a.Website).HasMaxLength(200);
        });

        builder.Entity<Kantoorinstellingen>(e =>
        {
            e.ToTable("Kantoorinstellingen");
            e.Property(k => k.Id).ValueGeneratedNever();
            e.Property(k => k.LimietAutomatisch).HasPrecision(18, 2);
            e.Property(k => k.Bandbreedte).HasPrecision(6, 2);
            e.Property(k => k.OverzichtLetter).HasDefaultValue(100);
            e.Property(k => k.Documentmap).HasMaxLength(400);
        });

        builder.Entity<Lidmaatschap>(e =>
        {
            e.ToTable("Lidmaatschappen");
            e.HasKey(l => new { l.AdministratieId, l.GebruikerId });
            e.HasIndex(l => l.GebruikerId);
            e.Property(l => l.Rol).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Administratie>().WithMany().HasForeignKey(l => l.AdministratieId);
            e.HasOne<Gebruiker>().WithMany().HasForeignKey(l => l.GebruikerId);
        });

        builder.Entity<Grootboekrekening>(e =>
        {
            e.ToTable("Rekeningen");
            e.HasIndex(r => new { r.AdministratieId, r.Nummer }).IsUnique();
            e.Property(r => r.Nummer).HasMaxLength(20);
            e.Property(r => r.Naam).HasMaxLength(200);
            e.Property(r => r.Soort).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.RgsCode).HasMaxLength(30);
            e.Property(r => r.Functie).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.BtwCode).HasMaxLength(10);
            e.HasOne<Administratie>().WithMany().HasForeignKey(r => r.AdministratieId);
        });

        builder.Entity<Dagboekdefinitie>(e =>
        {
            e.ToTable("Dagboeken");
            e.HasIndex(d => new { d.AdministratieId, d.Code }).IsUnique();
            e.Property(d => d.Code).HasMaxLength(10);
            e.Property(d => d.Naam).HasMaxLength(100);
            e.Property(d => d.Soort).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Rekeningnummer).HasMaxLength(20);
            e.Property(d => d.Iban).HasMaxLength(34);
            e.HasOne<Administratie>().WithMany().HasForeignKey(d => d.AdministratieId);
        });

        builder.Entity<Boeking>(e =>
        {
            e.ToTable("Boekingen");
            e.HasIndex(b => new { b.AdministratieId, b.Volgnummer }).IsUnique();
            e.HasIndex(b => new { b.AdministratieId, b.Dagboek, b.Boekstuk, b.Versie }).IsUnique();
            e.Property(b => b.Versie).HasDefaultValue(1);
            e.Property(b => b.Dagboek).HasMaxLength(10);
            e.Property(b => b.Boekstuk).HasMaxLength(30);
            e.Property(b => b.Omschrijving).HasMaxLength(500);
            e.Property(b => b.GeboektDoor).HasMaxLength(200);
            e.Property(b => b.Hash).HasMaxLength(64).IsFixedLength();
            e.Property(b => b.VorigeHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(b => b.Herstelt);
            e.HasIndex(b => b.Vervangt);
            e.HasMany(b => b.Regels).WithOne().HasForeignKey(r => r.BoekingId);
            e.HasOne<Administratie>().WithMany().HasForeignKey(b => b.AdministratieId);
        });

        builder.Entity<Boekingsregel>(e =>
        {
            e.ToTable("Boekingsregels");
            e.HasIndex(r => new { r.AdministratieId, r.Rekeningnummer });
            e.Property(r => r.Rekeningnummer).HasMaxLength(20);
            e.Property(r => r.Bedrag).HasPrecision(18, 2);
            e.Property(r => r.Omschrijving).HasMaxLength(500);
            e.Property(r => r.BtwCode).HasMaxLength(10);
            e.Property(r => r.Relatie).HasMaxLength(50);
            e.Property(r => r.Factuurnummer).HasMaxLength(50);
        });

        builder.Entity<AuditRegel>(e =>
        {
            e.ToTable("AuditRegels");
            e.HasIndex(a => a.Tijdstip);
            e.HasIndex(a => a.AdministratieId);
            e.Property(a => a.Gebruiker).HasMaxLength(200);
            e.Property(a => a.Actie).HasMaxLength(100);
            e.Property(a => a.Onderwerp).HasMaxLength(500);
            e.Property(a => a.Details).HasMaxLength(4000);
        });

        builder.Entity<Handelsrelatie>(e =>
        {
            e.ToTable("Relaties");
            e.HasIndex(r => new { r.AdministratieId, r.Code }).IsUnique();
            e.Property(r => r.Code).HasMaxLength(50);
            e.Property(r => r.Naam).HasMaxLength(300);
            e.Property(r => r.BtwNummer).HasMaxLength(20);
            e.Property(r => r.KvkNummer).HasMaxLength(20);
            e.Property(r => r.Tegenrekening).HasMaxLength(20);
            e.Property(r => r.ControleVanaf).HasPrecision(18, 2);
            e.Property(r => r.ControleAfwijking).HasPrecision(6, 2);
            e.Property(r => r.Straat).HasMaxLength(200);
            e.Property(r => r.Postcode).HasMaxLength(20);
            e.Property(r => r.Plaats).HasMaxLength(100);
            e.Property(r => r.Land).HasMaxLength(2);
            e.Property(r => r.Email).HasMaxLength(200);
            e.Property(r => r.Contactpersoon).HasMaxLength(200);
            e.Property(r => r.Omzetrekening).HasMaxLength(20);
            e.Property(r => r.Verkoopbtwcode).HasMaxLength(10);
            e.HasOne<Administratie>().WithMany().HasForeignKey(r => r.AdministratieId);
        });

        builder.Entity<Document>(e =>
        {
            e.ToTable("Documenten");
            e.HasIndex(d => new { d.AdministratieId, d.Status });
            e.HasIndex(d => new { d.AdministratieId, d.Sha256 });
            e.Property(d => d.Bestandsnaam).HasMaxLength(260);
            e.Property(d => d.Inhoudstype).HasMaxLength(100);
            e.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Soort).HasConversion<string>().HasMaxLength(20);
            if (Database.IsNpgsql())
            {
                e.Property(d => d.Herkend).HasColumnType("jsonb");
            }
            e.Property(d => d.Melding).HasMaxLength(2000);
            e.Property(d => d.IngelezenDoor).HasMaxLength(200);
            e.HasOne<Administratie>().WithMany().HasForeignKey(d => d.AdministratieId);
        });

        builder.Entity<DocumentInhoud>(e =>
        {
            e.ToTable("DocumentInhouden");
            e.HasKey(d => d.DocumentId);
            e.HasOne<Document>().WithOne().HasForeignKey<DocumentInhoud>(d => d.DocumentId);
        });

        builder.Entity<OpenstaandePost>(e =>
        {
            e.ToTable("OpenstaandePosten");
            e.HasIndex(p => new { p.AdministratieId, p.Relatie, p.Factuurnummer });
            e.Property(p => p.Soort).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Relatie).HasMaxLength(50);
            e.Property(p => p.Factuurnummer).HasMaxLength(50);
            e.Property(p => p.Bedrag).HasPrecision(18, 2);
            e.Property(p => p.Openstaand).HasPrecision(18, 2);
            e.Property(p => p.Betalingskenmerk).HasMaxLength(50);
            e.HasOne<Boeking>().WithMany().HasForeignKey(p => p.BoekingId);
        });

        builder.Entity<Leerkeuze>(e =>
        {
            e.ToTable("Leerkeuzes");
            e.HasIndex(k => new { k.AdministratieId, k.Sleutel, k.Id });
            e.Property(k => k.Sleutel).HasMaxLength(400);
            e.Property(k => k.Rekeningnummer).HasMaxLength(20);
            e.Property(k => k.BtwCode).HasMaxLength(10);
            e.Property(k => k.Bedrag).HasPrecision(18, 2);
            e.Property(k => k.Iban).HasMaxLength(34);
            e.Property(k => k.Omschrijving).HasMaxLength(200);
        });

        builder.Entity<Automatiseringsregel>(e =>
        {
            e.ToTable("Regels");
            e.Property(r => r.Naam).HasMaxLength(200);
            e.Property(r => r.Rekeningnummer).HasMaxLength(20);
            e.Property(r => r.BtwCode).HasMaxLength(10);
            e.Property(r => r.Soort).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Relatie).HasMaxLength(50);
            e.Property(r => r.NaamBevat).HasMaxLength(200);
            e.Property(r => r.BedragVan).HasPrecision(18, 2);
            e.Property(r => r.BedragTot).HasPrecision(18, 2);
            e.Property(r => r.Omschrijving).HasMaxLength(200);
            e.Property(r => r.OmschrijvingBevat).HasMaxLength(200);
            e.Property(r => r.TegenIban).HasMaxLength(34);
            e.Property(r => r.Richting).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Administratie>().WithMany().HasForeignKey(r => r.AdministratieId);
        });

        // Alleen de gegevens van de geopende administratie; zonder geopende administratie niets.
        builder.Entity<Grootboekrekening>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Dagboekdefinitie>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Boeking>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Boekingsregel>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Handelsrelatie>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Document>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<DocumentInhoud>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<OpenstaandePost>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Zoekplek>(e =>
        {
            e.ToTable("Zoekplekken");
            e.HasIndex(z => new { z.AdministratieId, z.Relatie, z.Veld }).IsUnique();
            e.Property(z => z.Relatie).HasMaxLength(50);
            e.Property(z => z.Veld).HasConversion<string>().HasMaxLength(30);
        });

        builder.Entity<Afschrift>(e =>
        {
            e.ToTable("Afschriften");
            e.HasIndex(a => new { a.AdministratieId, a.Iban, a.Einddatum });
            e.Property(a => a.Dagboek).HasMaxLength(10);
            e.Property(a => a.Iban).HasMaxLength(34);
            e.Property(a => a.Nummer).HasMaxLength(50);
            e.Property(a => a.Beginsaldo).HasPrecision(18, 2);
            e.Property(a => a.Eindsaldo).HasPrecision(18, 2);
            e.Property(a => a.Bestandsnaam).HasMaxLength(260);
            e.Property(a => a.Melding).HasMaxLength(1000);
            e.Property(a => a.IngelezenDoor).HasMaxLength(200);
            e.HasOne<Administratie>().WithMany().HasForeignKey(a => a.AdministratieId);
        });

        builder.Entity<Banktransactie>(e =>
        {
            e.ToTable("Banktransacties");
            e.HasIndex(t => new { t.AdministratieId, t.Sleutel }).IsUnique();
            e.HasIndex(t => new { t.AdministratieId, t.Status });
            e.Property(t => t.Dagboek).HasMaxLength(10);
            e.Property(t => t.Iban).HasMaxLength(34);
            e.Property(t => t.Bedrag).HasPrecision(18, 2);
            e.Property(t => t.Omschrijving).HasMaxLength(1000);
            e.Property(t => t.TegenIban).HasMaxLength(34);
            e.Property(t => t.TegenNaam).HasMaxLength(300);
            e.Property(t => t.Betalingskenmerk).HasMaxLength(50);
            e.Property(t => t.EndToEndId).HasMaxLength(100);
            e.Property(t => t.Sleutel).HasMaxLength(64).IsFixedLength();
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Melding).HasMaxLength(1000);
            e.HasOne<Afschrift>().WithMany().HasForeignKey(t => t.AfschriftId);
        });

        // De btw-tabel van het kantoor, gevuld met de standaardcodes en de Nederlandse tarieven.
        builder.Entity<Btwcodedefinitie>(e =>
        {
            e.ToTable("Btwcodes");
            e.HasKey(c => c.Code);
            e.Property(c => c.Code).HasMaxLength(10);
            e.Property(c => c.Omschrijving).HasMaxLength(200);
            e.Property(c => c.Soort).HasConversion<string>().HasMaxLength(30);
            e.Property(c => c.Tarief).HasConversion<string>().HasMaxLength(10);
            e.HasData(Boekhouding.Btw.BtwCodes.Definities.Select((c, i) => new Btwcodedefinitie
            {
                Code = c.Code,
                Omschrijving = c.Omschrijving,
                Soort = c.Soort,
                Tarief = c.Tarief,
                Actief = true,
                Volgorde = (i + 1) * 10,
            }));
        });

        builder.Entity<Btwtariefrecord>(e =>
        {
            e.ToTable("Btwtarieven");
            e.HasIndex(t => new { t.Tarief, t.Vanaf }).IsUnique();
            e.Property(t => t.Tarief).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Percentage).HasPrecision(6, 3);
            e.HasData(Boekhouding.Btw.Btwtarieven.Standaard.Regels.Select((t, i) => new Btwtariefrecord
            {
                Id = i + 1,
                Tarief = t.Tarief,
                Vanaf = t.Vanaf,
                Percentage = t.Percentage,
            }));
        });

        builder.Entity<Verkoopdocument>(e =>
        {
            e.ToTable("Verkoopdocumenten");
            e.HasIndex(v => new { v.AdministratieId, v.Nummer }).IsUnique();
            e.HasIndex(v => new { v.AdministratieId, v.Status, v.Datum });
            e.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.Nummer).HasMaxLength(30);
            e.Property(v => v.Relatie).HasMaxLength(50);
            e.Property(v => v.Klantgegevens).HasMaxLength(4000);
            e.Property(v => v.Omschrijving).HasMaxLength(500);
            e.Property(v => v.Referentie).HasMaxLength(100);
            e.Property(v => v.Tekst).HasMaxLength(4000);
            e.Property(v => v.Betalingskenmerk).HasMaxLength(50);
            e.Property(v => v.Verdeling).HasMaxLength(4000);
            e.Property(v => v.AangemaaktDoor).HasMaxLength(200);
            e.Property(v => v.DefinitiefDoor).HasMaxLength(200);
            e.HasMany(v => v.Regels).WithOne().HasForeignKey(r => r.VerkoopdocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Administratie>().WithMany().HasForeignKey(v => v.AdministratieId);
        });

        builder.Entity<Verkoopdocumentregel>(e =>
        {
            e.ToTable("Verkoopdocumentregels");
            e.Property(r => r.Omschrijving).HasMaxLength(1000);
            e.Property(r => r.Aantal).HasPrecision(18, 4);
            e.Property(r => r.Eenheid).HasMaxLength(20);
            e.Property(r => r.Prijs).HasPrecision(18, 4);
            e.Property(r => r.Korting).HasPrecision(6, 2);
            e.Property(r => r.Rekeningnummer).HasMaxLength(20);
            e.Property(r => r.BtwCode).HasMaxLength(10);
        });

        builder.Entity<Verkoopdocument>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Verkoopdocumentregel>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Afschrift>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Banktransactie>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Leerkeuze>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Zoekplek>().HasQueryFilter(e => e.AdministratieId == Geopend);
        builder.Entity<Automatiseringsregel>().HasQueryFilter(e => e.AdministratieId == Geopend);
    }

    /// <summary>
    /// Nieuwe gegevens van een administratie mogen alleen in de geopende administratie worden opgeslagen, zodat een
    /// fout in de code nooit iets in een andere administratie zet.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ControleerAdministratie();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ControleerAdministratie();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void ControleerAdministratie()
    {
        foreach (var entry in ChangeTracker.Entries<IVanAdministratie>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && entry.Entity.AdministratieId != Geopend)
            {
                throw new InvalidOperationException("Gegevens horen niet bij de geopende administratie.");
            }
        }
    }
}
