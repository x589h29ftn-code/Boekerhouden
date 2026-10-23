using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Data;

public sealed class BoekerhoudenDbContext(DbContextOptions<BoekerhoudenDbContext> options)
    : IdentityDbContext<Gebruiker, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Klant> Klanten => Set<Klant>();

    public DbSet<Dossier> Dossiers => Set<Dossier>();

    public DbSet<Grootboekrekening> Grootboekrekeningen => Set<Grootboekrekening>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<Saldo> Saldi => Set<Saldo>();

    public DbSet<AuditRegel> AuditRegels => Set<AuditRegel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.Entity<Gebruiker>(e =>
        {
            e.Property(g => g.Naam).HasMaxLength(200);
            e.Property(g => g.Initialen).HasMaxLength(10);
            e.Property(g => g.Titel).HasMaxLength(20);
        });

        builder.Entity<Klant>(e =>
        {
            e.ToTable("Klanten");
            e.HasIndex(k => k.Klantnummer).IsUnique();
            e.Property(k => k.Klantnummer).HasMaxLength(50);
            e.Property(k => k.Naam).HasMaxLength(300);
            e.Property(k => k.Rechtsvorm).HasConversion<string>().HasMaxLength(30);
            e.Property(k => k.KvkNummer).HasMaxLength(20);
            e.Property(k => k.Plaats).HasMaxLength(100);
            e.Property(k => k.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<Dossier>(e =>
        {
            e.ToTable("Dossiers");
            e.HasIndex(d => new { d.KlantId, d.Boekjaar, d.SoortOpdracht }).IsUnique();
            e.HasOne(d => d.Klant).WithMany(k => k.Dossiers).HasForeignKey(d => d.KlantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(d => d.SoortOpdracht).HasConversion<string>().HasMaxLength(40);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(d => d.Naam).HasMaxLength(200);
            e.Property(d => d.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<Grootboekrekening>(e =>
        {
            e.ToTable("Grootboekrekeningen");
            e.HasIndex(g => new { g.KlantId, g.Nummer }).IsUnique();
            e.HasOne<Klant>().WithMany().HasForeignKey(g => g.KlantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(g => g.Nummer).HasMaxLength(50);
            e.Property(g => g.Omschrijving).HasMaxLength(300);
            e.Property(g => g.RgsCode).HasMaxLength(40);
        });

        builder.Entity<ImportBatch>(e =>
        {
            e.ToTable("ImportBatches");
            // Per klant en boekjaar is hooguit één batch actief, ook als twee gebruikers tegelijk importeren.
            e.HasIndex(b => new { b.KlantId, b.Boekjaar }).IsUnique().HasFilter("\"Actief\"");
            e.HasOne<Klant>().WithMany().HasForeignKey(b => b.KlantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(b => b.Bestandsnaam).HasMaxLength(260);
            e.Property(b => b.Sha256).HasMaxLength(64);
            e.Property(b => b.TotaalDebet).HasPrecision(18, 2);
            e.Property(b => b.TotaalCredit).HasPrecision(18, 2);
            e.Property(b => b.GeimporteerdDoor).HasMaxLength(200);
        });

        builder.Entity<Saldo>(e =>
        {
            e.ToTable("Saldi");
            e.HasIndex(s => new { s.ImportBatchId, s.GrootboekrekeningId }).IsUnique();
            e.HasOne<ImportBatch>().WithMany(b => b.Saldi).HasForeignKey(s => s.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Grootboekrekening).WithMany().HasForeignKey(s => s.GrootboekrekeningId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.Bedrag).HasPrecision(18, 2);
        });

        builder.Entity<AuditRegel>(e =>
        {
            e.ToTable("AuditRegels");
            e.HasIndex(a => a.Tijdstip);
            e.Property(a => a.Gebruiker).HasMaxLength(200);
            e.Property(a => a.Actie).HasMaxLength(100);
            e.Property(a => a.Onderwerp).HasMaxLength(300);
        });
    }
}
