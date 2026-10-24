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

    public DbSet<RgsVersie> RgsVersies => Set<RgsVersie>();

    public DbSet<RgsCodeRegel> RgsCodes => Set<RgsCodeRegel>();

    public DbSet<Koppeling> Koppelingen => Set<Koppeling>();

    public DbSet<KlantEigenPost> EigenPosten => Set<KlantEigenPost>();

    public DbSet<KlantPostNaam> KlantPostNamen => Set<KlantPostNaam>();

    public DbSet<KantoorPostNaam> KantoorPostNamen => Set<KantoorPostNaam>();

    public DbSet<SjabloonRegel> SjabloonRegels => Set<SjabloonRegel>();

    public DbSet<Correctieboeking> Correctieboekingen => Set<Correctieboeking>();

    public DbSet<Correctieregel> Correctieregels => Set<Correctieregel>();

    public DbSet<WerkprogrammaSjabloon> WerkprogrammaSjablonen => Set<WerkprogrammaSjabloon>();

    public DbSet<SjabloonOnderdeel> SjabloonOnderdelen => Set<SjabloonOnderdeel>();

    public DbSet<SjabloonStap> SjabloonStappen => Set<SjabloonStap>();

    public DbSet<DossierOnderdeel> DossierOnderdelen => Set<DossierOnderdeel>();

    public DbSet<DossierStap> DossierStappen => Set<DossierStap>();

    public DbSet<DossierPostKeuze> PostKeuzes => Set<DossierPostKeuze>();

    public DbSet<Reviewpunt> Reviewpunten => Set<Reviewpunt>();

    public DbSet<Bijlage> Bijlagen => Set<Bijlage>();

    public DbSet<BijlageInhoud> BijlageInhouden => Set<BijlageInhoud>();

    public DbSet<Kantoorinstellingen> Kantoorinstellingen => Set<Kantoorinstellingen>();

    public DbSet<RekeningPresentatie> RekeningPresentaties => Set<RekeningPresentatie>();

    public DbSet<PostPresentatie> PostPresentaties => Set<PostPresentatie>();

    public DbSet<DossierToelichting> DossierToelichtingen => Set<DossierToelichting>();

    public DbSet<UitsplitsingRegel> UitsplitsingRegels => Set<UitsplitsingRegel>();

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
            e.Property(d => d.WerkprogrammaVersie).HasMaxLength(40);
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

        builder.Entity<RgsVersie>(e =>
        {
            e.ToTable("RgsVersies");
            e.HasIndex(v => v.Sha256).IsUnique();
            e.HasIndex(v => v.Actief).IsUnique().HasFilter("\"Actief\"");
            e.Property(v => v.Versie).HasMaxLength(20);
            e.Property(v => v.Bestandsnaam).HasMaxLength(260);
            e.Property(v => v.Sha256).HasMaxLength(64);
            e.Property(v => v.IngelezenDoor).HasMaxLength(200);
        });

        builder.Entity<RgsCodeRegel>(e =>
        {
            e.ToTable("RgsCodes");
            e.HasIndex(c => new { c.RgsVersieId, c.Code }).IsUnique();
            e.HasOne<RgsVersie>().WithMany(v => v.Codes).HasForeignKey(c => c.RgsVersieId).OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Code).HasMaxLength(20);
            e.Property(c => c.Ouder).HasMaxLength(20);
            e.Property(c => c.Omslagcode).HasMaxLength(20);
            e.Property(c => c.Omschrijving).HasMaxLength(500);
            e.Property(c => c.OmschrijvingLang).HasMaxLength(1000);
            e.Property(c => c.Kant).HasMaxLength(1);
            e.Property(c => c.Referentienummer).HasMaxLength(20);
        });

        builder.Entity<KlantEigenPost>(e =>
        {
            e.ToTable("EigenPosten");
            e.HasIndex(p => new { p.KlantId, p.OuderCode });
            e.HasOne<Klant>().WithMany().HasForeignKey(p => p.KlantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(p => p.OuderCode).HasMaxLength(20);
            e.Property(p => p.Naam).HasMaxLength(300);
            e.Property(p => p.AangemaaktDoor).HasMaxLength(200);
        });

        builder.Entity<Koppeling>(e =>
        {
            e.ToTable("Koppelingen");
            e.HasIndex(k => k.GrootboekrekeningId).IsUnique();
            e.HasIndex(k => k.KlantId);
            e.HasOne<Klant>().WithMany().HasForeignKey(k => k.KlantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Grootboekrekening>().WithMany().HasForeignKey(k => k.GrootboekrekeningId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<KlantEigenPost>().WithMany().HasForeignKey(k => k.EigenPostId).OnDelete(DeleteBehavior.Restrict);
            e.Property(k => k.Code).HasMaxLength(20);
            e.Property(k => k.RgsVersie).HasMaxLength(20);
            e.Property(k => k.Bron).HasConversion<string>().HasMaxLength(20);
            e.Property(k => k.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<KlantPostNaam>(e =>
        {
            e.ToTable("KlantPostNamen");
            e.HasKey(n => new { n.KlantId, n.Code });
            e.HasOne<Klant>().WithMany().HasForeignKey(n => n.KlantId).OnDelete(DeleteBehavior.Cascade);
            e.Property(n => n.Code).HasMaxLength(60);
            e.Property(n => n.Naam).HasMaxLength(300);
        });

        builder.Entity<KantoorPostNaam>(e =>
        {
            e.ToTable("KantoorPostNamen");
            e.HasKey(n => n.Code);
            e.Property(n => n.Code).HasMaxLength(20);
            e.Property(n => n.Naam).HasMaxLength(300);
        });

        builder.Entity<SjabloonRegel>(e =>
        {
            e.ToTable("SjabloonRegels");
            e.HasIndex(r => r.Rekeningnummer).IsUnique();
            e.Property(r => r.Rekeningnummer).HasMaxLength(50);
            e.Property(r => r.Omschrijving).HasMaxLength(300);
            e.Property(r => r.Code).HasMaxLength(20);
            e.Property(r => r.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<Correctieboeking>(e =>
        {
            e.ToTable("Correctieboekingen");
            e.HasIndex(c => new { c.DossierId, c.Nummer }).IsUnique();
            e.HasIndex(c => c.OvergenomenVanId);
            e.HasOne<Dossier>().WithMany().HasForeignKey(c => c.DossierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Correctieboeking>().WithMany().HasForeignKey(c => c.OvergenomenVanId).OnDelete(DeleteBehavior.SetNull);
            e.Property(c => c.Omschrijving).HasMaxLength(300);
            e.Property(c => c.Soort).HasConversion<string>().HasMaxLength(30);
            e.Property(c => c.AangemaaktDoor).HasMaxLength(200);
            e.Property(c => c.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<Correctieregel>(e =>
        {
            e.ToTable("Correctieregels");
            e.HasIndex(r => r.GrootboekrekeningId);
            e.HasOne<Correctieboeking>().WithMany(c => c.Regels).HasForeignKey(r => r.CorrectieboekingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Grootboekrekening).WithMany().HasForeignKey(r => r.GrootboekrekeningId).OnDelete(DeleteBehavior.Restrict);
            e.Property(r => r.Bedrag).HasPrecision(18, 2);
            e.Property(r => r.Omschrijving).HasMaxLength(300);
        });

        builder.Entity<WerkprogrammaSjabloon>(e =>
        {
            e.ToTable("WerkprogrammaSjablonen");
            e.HasIndex(w => w.SoortOpdracht).IsUnique();
            e.Property(w => w.SoortOpdracht).HasConversion<string>().HasMaxLength(40);
            e.Property(w => w.Naam).HasMaxLength(200);
            e.Property(w => w.Versie).HasMaxLength(40);
            e.Property(w => w.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<SjabloonOnderdeel>(e =>
        {
            e.ToTable("SjabloonOnderdelen");
            e.HasOne<WerkprogrammaSjabloon>().WithMany(w => w.Onderdelen).HasForeignKey(o => o.SjabloonId).OnDelete(DeleteBehavior.Cascade);
            e.Property(o => o.Fase).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Soort).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Titel).HasMaxLength(200);
            e.Property(o => o.HoofdpostCode).HasMaxLength(20);
        });

        builder.Entity<SjabloonStap>(e =>
        {
            e.ToTable("SjabloonStappen");
            e.HasOne<SjabloonOnderdeel>().WithMany(o => o.Stappen).HasForeignKey(s => s.OnderdeelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.Titel).HasMaxLength(300);
            e.Property(s => s.Instructie).HasMaxLength(4000);
            e.Property(s => s.Controle).HasConversion<string>().HasMaxLength(40);
        });

        builder.Entity<DossierOnderdeel>(e =>
        {
            e.ToTable("DossierOnderdelen");
            e.HasIndex(o => o.DossierId);
            e.HasIndex(o => new { o.DossierId, o.HoofdpostCode }).IsUnique().HasFilter("\"HoofdpostCode\" IS NOT NULL");
            e.HasOne<Dossier>().WithMany().HasForeignKey(o => o.DossierId).OnDelete(DeleteBehavior.Restrict);
            e.Property(o => o.Fase).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Soort).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Titel).HasMaxLength(200);
            e.Property(o => o.HoofdpostCode).HasMaxLength(20);
            e.Property(o => o.Notitie).HasMaxLength(20000);
            e.Property(o => o.GereviewdDoor).HasMaxLength(200);
        });

        builder.Entity<DossierStap>(e =>
        {
            e.ToTable("DossierStappen");
            e.HasOne<DossierOnderdeel>().WithMany(o => o.Stappen).HasForeignKey(s => s.OnderdeelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.Titel).HasMaxLength(300);
            e.Property(s => s.Instructie).HasMaxLength(4000);
            e.Property(s => s.Controle).HasConversion<string>().HasMaxLength(40);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(s => s.Antwoord).HasMaxLength(20000);
            e.Property(s => s.Reden).HasMaxLength(1000);
            e.Property(s => s.UitgevoerdDoor).HasMaxLength(200);
            e.Property(s => s.VerwijderdDoor).HasMaxLength(200);
            e.Property(s => s.AangemaaktDoor).HasMaxLength(200);
        });

        builder.Entity<DossierPostKeuze>(e =>
        {
            e.ToTable("DossierPostKeuzes");
            e.HasKey(k => new { k.DossierId, k.HoofdpostCode });
            e.HasOne<Dossier>().WithMany().HasForeignKey(k => k.DossierId).OnDelete(DeleteBehavior.Cascade);
            e.Property(k => k.HoofdpostCode).HasMaxLength(20);
            e.Property(k => k.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<Reviewpunt>(e =>
        {
            e.ToTable("Reviewpunten");
            e.HasIndex(r => r.DossierId);
            e.HasIndex(r => r.OnderdeelId);
            e.HasOne<Dossier>().WithMany().HasForeignKey(r => r.DossierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DossierOnderdeel>().WithMany().HasForeignKey(r => r.OnderdeelId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<DossierStap>().WithMany().HasForeignKey(r => r.StapId).OnDelete(DeleteBehavior.SetNull);
            e.Property(r => r.Tekst).HasMaxLength(4000);
            e.Property(r => r.Reactie).HasMaxLength(4000);
            e.Property(r => r.Door).HasMaxLength(200);
            e.Property(r => r.OpgelostDoor).HasMaxLength(200);
        });

        builder.Entity<Bijlage>(e =>
        {
            e.ToTable("Bijlagen");
            e.HasIndex(b => b.StapId);
            e.HasIndex(b => b.DossierId);
            e.HasOne<Dossier>().WithMany().HasForeignKey(b => b.DossierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DossierStap>().WithMany().HasForeignKey(b => b.StapId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Inhoud).WithOne().HasForeignKey<BijlageInhoud>(i => i.BijlageId).OnDelete(DeleteBehavior.Cascade);
            e.Property(b => b.Bestandsnaam).HasMaxLength(260);
            e.Property(b => b.ContentType).HasMaxLength(200);
            e.Property(b => b.Sha256).HasMaxLength(64);
            e.Property(b => b.Door).HasMaxLength(200);
        });

        builder.Entity<BijlageInhoud>(e =>
        {
            e.ToTable("BijlageInhouden");
            e.HasKey(i => i.BijlageId);
        });

        builder.Entity<Kantoorinstellingen>(e =>
        {
            e.ToTable("Kantoorinstellingen");
            e.Property(k => k.Id).ValueGeneratedNever();
            e.Property(k => k.Naam).HasMaxLength(200);
            e.Property(k => k.Adres).HasMaxLength(200);
            e.Property(k => k.Postcode).HasMaxLength(20);
            e.Property(k => k.Plaats).HasMaxLength(100);
            e.Property(k => k.Kleur).HasMaxLength(7);
            e.Property(k => k.LogoType).HasMaxLength(40);
            e.Property(k => k.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<RekeningPresentatie>(e =>
        {
            e.ToTable("RekeningPresentaties");
            e.HasKey(r => r.GrootboekrekeningId);
            e.HasIndex(r => r.KlantId);
            e.HasOne<Grootboekrekening>().WithOne().HasForeignKey<RekeningPresentatie>(r => r.GrootboekrekeningId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Klant>().WithMany().HasForeignKey(r => r.KlantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(r => r.Omschrijving).HasMaxLength(300);
            e.Property(r => r.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<PostPresentatie>(e =>
        {
            e.ToTable("PostPresentaties");
            e.HasKey(p => new { p.KlantId, p.Sleutel });
            e.HasOne<Klant>().WithMany().HasForeignKey(p => p.KlantId).OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Sleutel).HasMaxLength(60);
            e.Property(p => p.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<DossierToelichting>(e =>
        {
            e.ToTable("DossierToelichtingen");
            e.HasKey(t => new { t.DossierId, t.Sleutel });
            e.HasOne<Dossier>().WithMany().HasForeignKey(t => t.DossierId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Sleutel).HasMaxLength(60);
            e.Property(t => t.Tekst).HasMaxLength(4000);
            e.Property(t => t.GewijzigdDoor).HasMaxLength(200);
        });

        builder.Entity<UitsplitsingRegel>(e =>
        {
            e.ToTable("UitsplitsingRegels");
            e.HasIndex(r => new { r.DossierId, r.GrootboekrekeningId, r.Volgorde });
            e.HasOne<Dossier>().WithMany().HasForeignKey(r => r.DossierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Grootboekrekening>().WithMany().HasForeignKey(r => r.GrootboekrekeningId).OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.Omschrijving).HasMaxLength(200);
            e.Property(r => r.Bedrag).HasPrecision(18, 2);
            e.Property(r => r.BedragVorig).HasPrecision(18, 2);
            e.Property(r => r.GewijzigdDoor).HasMaxLength(200);
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
