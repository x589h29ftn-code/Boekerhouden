using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Boekerhouden.Administratie.Data;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Hoe een werkplek ervoor staat bij de kantoorserver.</summary>
public enum Werkplekstand
{
    /// <summary>Aangemeld, wacht op de beheerder.</summary>
    Wacht,

    /// <summary>Gekoppeld aan een gebruiker: inloggen kan.</summary>
    Gekoppeld,

    /// <summary>Niet aangenomen: te veel werkplekken wachten al, of de gebruiker is niet meer actief.</summary>
    Geweigerd,
}

/// <summary>Het antwoord op een aanmelding: de stand, de controlecode en (als de werkplek gekoppeld is) een uitdaging om te ondertekenen.</summary>
public sealed record Werkplekantwoord(Werkplekstand Stand, string Code, string? Uitdaging = null);

/// <summary>Een werkplek in de lijst van de beheerder.</summary>
public sealed record Werkplekregel(Guid Id, string WindowsNaam, string Computer, string Code, DateTimeOffset AangevraagdOp, Guid? GebruikerId, string? Gebruiker, DateTimeOffset? LaatstGezien);

/// <summary>
/// Werkplekken van de kantoorserver. Een werkplek is één Windows-gebruiker op één pc, met een eigen sleutelpaar dat de
/// Windows-app daar bewaart (versleuteld met het Windows-account). De werkplek meldt zich aan met zijn publieke
/// sleutel; de beheerder ziet de aanmelding met een controlecode (dezelfde code staat op de werkplek) en koppelt hem
/// aan een gebruiker. Daarna logt de werkplek in door een eenmalige uitdaging te ondertekenen: geen wachtwoord, en een
/// andere pc of een ander Windows-account kan zich niet als deze gebruiker voordoen. Wie achter de ontgrendelde pc zit,
/// werkt onder die naam (besluit van de gebruiker, beveiliging §4.10).
/// </summary>
public sealed class ApparaatDienst(IDbContextFactory<AdministratieDbContext> dbFactory, AuditDienst audit, TimeProvider tijd)
{
    /// <summary>Hoeveel werkplekken tegelijk op de beheerder mogen wachten (tegen vollopen door een vreemde op het netwerk).</summary>
    public const int MaximumWachtend = 25;

    private static readonly TimeSpan Geldig = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Wachttijd = TimeSpan.FromDays(14);
    private readonly ConcurrentDictionary<string, (string Vingerafdruk, DateTimeOffset Tot)> uitdagingen = new();

    /// <summary>
    /// De controlecode van een werkplek: zes cijfers uit zijn sleutel en het certificaat van de server. De werkplek
    /// rekent met het certificaat dat hij zag, de server met zijn eigen: zit er iemand tussen (met een ander
    /// certificaat), dan verschillen de codes en ziet de beheerder dat bij het koppelen.
    /// </summary>
    public static string Controlecode(ReadOnlySpan<byte> sleutel, string? servercertificaat)
    {
        var hash = SHA256.HashData([.. sleutel, .. System.Text.Encoding.ASCII.GetBytes((servercertificaat ?? "").ToUpperInvariant())]);
        var getal = BitConverter.ToUInt32(hash, 0) % 1_000_000;
        return getal.ToString("000 000", CultureInfo.InvariantCulture);
    }

    /// <summary>De controlecode zoals deze server hem toont (met zijn eigen certificaat).</summary>
    public static string Code(ReadOnlySpan<byte> sleutel) => Controlecode(sleutel, Lokaal.Kantoorserver.Certificaatvingerafdruk);

    public static string Vingerafdruk(ReadOnlySpan<byte> sleutel) => Convert.ToHexStringLower(SHA256.HashData(sleutel));

    /// <summary>
    /// Een werkplek meldt zich (bij elke start): onbekend = vastleggen en wachten op de beheerder; gekoppeld = een
    /// uitdaging om mee in te loggen.
    /// </summary>
    public async Task<Werkplekantwoord> Meld(byte[] sleutel, string? windowsNaam, string? computer)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        if (!IsSleutel(sleutel))
        {
            return new(Werkplekstand.Geweigerd, "");
        }

        var code = Code(sleutel);
        var vingerafdruk = Vingerafdruk(sleutel);
        var nu = tijd.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync();
        var apparaat = await db.Apparaten.FirstOrDefaultAsync(a => a.Vingerafdruk == vingerafdruk);
        if (apparaat is null)
        {
            // Oude aanmeldingen waar niets mee gedaan is, verdwijnen vanzelf.
            var verlopen = await db.Apparaten.Where(a => a.GebruikerId == null).ToListAsync();
            db.Apparaten.RemoveRange(verlopen.Where(a => a.AangevraagdOp < nu - Wachttijd));
            if (verlopen.Count(a => a.AangevraagdOp >= nu - Wachttijd) >= MaximumWachtend)
            {
                await db.SaveChangesAsync();
                return new(Werkplekstand.Geweigerd, code);
            }

            db.Apparaten.Add(new Apparaat
            {
                Id = Guid.NewGuid(),
                Sleutel = sleutel,
                Vingerafdruk = vingerafdruk,
                WindowsNaam = Kort(windowsNaam, 100),
                Computer = Kort(computer, 100),
                AangevraagdOp = nu,
            });
            await db.SaveChangesAsync();
            await audit.Log(new Uitvoerder(null, "werkplek"), AuditActies.WerkplekAangemeld, $"{Kort(windowsNaam, 100)} op {Kort(computer, 100)}", $"code {code}");
            return new(Werkplekstand.Wacht, code);
        }

        if (apparaat.GebruikerId is not { } gebruiker)
        {
            return new(Werkplekstand.Wacht, code);
        }

        if (!await db.Users.AnyAsync(g => g.Id == gebruiker && g.Actief))
        {
            return new(Werkplekstand.Geweigerd, code);
        }

        foreach (var oud in uitdagingen.Where(u => u.Value.Tot < nu).Select(u => u.Key).ToList())
        {
            uitdagingen.TryRemove(oud, out _);
        }

        var uitdaging = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        uitdagingen[uitdaging] = (vingerafdruk, nu + Geldig);
        return new(Werkplekstand.Gekoppeld, code, uitdaging);
    }

    /// <summary>
    /// Controleert de ondertekende uitdaging en geeft de gebruiker van de werkplek; leeg als de uitdaging niet (meer)
    /// geldt, de handtekening niet klopt of de gebruiker niet actief is. Een uitdaging werkt één keer.
    /// </summary>
    public async Task<Gebruiker?> Verifieer(string? uitdaging, byte[]? handtekening)
    {
        if (uitdaging is null || handtekening is null || !uitdagingen.TryRemove(uitdaging, out var bekend) || bekend.Tot < tijd.GetUtcNow())
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Apparaten.FirstOrDefaultAsync(a => a.Vingerafdruk == bekend.Vingerafdruk) is not { GebruikerId: { } gebruikerId } apparaat)
        {
            return null;
        }

        using var sleutel = ECDsa.Create();
        try
        {
            sleutel.ImportSubjectPublicKeyInfo(apparaat.Sleutel, out _);
            if (!sleutel.VerifyData(Werkpleksleutel.TeTekenen(uitdaging), handtekening, HashAlgorithmName.SHA256))
            {
                return null;
            }
        }
        catch (CryptographicException)
        {
            return null;
        }

        if (await db.Users.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gebruikerId && g.Actief) is not { } gebruiker)
        {
            return null;
        }

        apparaat.LaatstGezien = tijd.GetUtcNow();
        await db.SaveChangesAsync();
        await audit.Log(new Uitvoerder(gebruiker.Id, gebruiker.Naam), AuditActies.IngelogdMetWerkplek, $"{apparaat.WindowsNaam} op {apparaat.Computer}");
        return gebruiker;
    }

    /// <summary>Alle werkplekken: eerst die wachten, dan de gekoppelde.</summary>
    public async Task<IReadOnlyList<Werkplekregel>> Lijst()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var apparaten = await db.Apparaten.AsNoTracking().ToListAsync();
        var namen = await db.Users.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Naam);
        return [.. apparaten.OrderBy(a => a.GebruikerId is not null).ThenBy(a => a.AangevraagdOp)
            .Select(a => new Werkplekregel(a.Id, a.WindowsNaam, a.Computer, Code(a.Sleutel), a.AangevraagdOp, a.GebruikerId,
                a.GebruikerId is { } g ? namen.GetValueOrDefault(g) : null, a.LaatstGezien))];
    }

    /// <summary>De beheerder koppelt een werkplek aan een gebruiker (of aan een andere gebruiker).</summary>
    public async Task<Uitkomst> Koppel(Uitvoerder wie, Guid apparaat, Guid gebruiker)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Apparaten.FirstOrDefaultAsync(a => a.Id == apparaat) is not { } rij)
        {
            return Uitkomst.Mislukt("Deze werkplek bestaat niet (meer).");
        }

        if (await db.Users.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gebruiker) is not { Actief: true } persoon)
        {
            return Uitkomst.Mislukt("Kies een actieve gebruiker.");
        }

        (rij.GebruikerId, rij.GekoppeldOp, rij.GekoppeldDoor) = (gebruiker, tijd.GetUtcNow(), wie.Naam);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.WerkplekGekoppeld, $"{rij.WindowsNaam} op {rij.Computer}", $"aan {persoon.Naam}; code {Code(rij.Sleutel)}");
        return Uitkomst.Gelukt;
    }

    /// <summary>Een werkplek weghalen: een aanmelding afwijzen, of een gekoppelde werkplek de toegang ontnemen.</summary>
    public async Task<Uitkomst> Verwijder(Uitvoerder wie, Guid apparaat)
    {
        ArgumentNullException.ThrowIfNull(wie);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Apparaten.FirstOrDefaultAsync(a => a.Id == apparaat) is not { } rij)
        {
            return Uitkomst.Mislukt("Deze werkplek bestaat niet (meer).");
        }

        db.Apparaten.Remove(rij);
        await db.SaveChangesAsync();
        await audit.Log(wie, AuditActies.WerkplekVerwijderd, $"{rij.WindowsNaam} op {rij.Computer}");
        return Uitkomst.Gelukt;
    }

    /// <summary>
    /// De eigen werkplek van de kantoor-pc: meteen gekoppeld aan de gebruiker van die pc (de beheerder), zonder
    /// goedkeuring. Alleen de Windows-app zelf roept dit aan, vóór hij de server start.
    /// </summary>
    public async Task ZorgEigen(byte[] sleutel, Guid gebruiker, string windowsNaam, string computer)
    {
        ArgumentNullException.ThrowIfNull(sleutel);
        var vingerafdruk = Vingerafdruk(sleutel);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rij = await db.Apparaten.FirstOrDefaultAsync(a => a.Vingerafdruk == vingerafdruk);
        if (rij is null)
        {
            rij = new Apparaat { Id = Guid.NewGuid(), Sleutel = sleutel, Vingerafdruk = vingerafdruk, AangevraagdOp = tijd.GetUtcNow() };
            db.Apparaten.Add(rij);
        }

        if (rij.GebruikerId != gebruiker)
        {
            (rij.GebruikerId, rij.GekoppeldOp, rij.GekoppeldDoor) = (gebruiker, tijd.GetUtcNow(), "deze pc");
        }

        (rij.WindowsNaam, rij.Computer) = (Kort(windowsNaam, 100), Kort(computer, 100));
        await db.SaveChangesAsync();
    }

    private static bool IsSleutel(byte[] sleutel)
    {
        if (sleutel.Length is < 50 or > 200)
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(sleutel, out var gelezen);
            return gelezen == sleutel.Length && ecdsa.KeySize == 256;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string Kort(string? tekst, int lengte)
    {
        var schoon = new string([.. (tekst ?? "").Where(c => !char.IsControl(c))]).Trim();
        return schoon.Length > lengte ? schoon[..lengte] : schoon;
    }
}

/// <summary>
/// Het sleutelpaar van een werkplek (ECDSA P-256). De Windows-app bewaart de geheime sleutel versleuteld met het
/// Windows-account (DPAPI) en ondertekent er de uitdaging van de kantoorserver mee.
/// </summary>
public sealed class Werkpleksleutel : IDisposable
{
    private readonly ECDsa sleutel;

    private Werkpleksleutel(ECDsa sleutel) => this.sleutel = sleutel;

    public static Werkpleksleutel Nieuw() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <param name="geheim">De geheime sleutel zoals <see cref="Geheim"/> hem gaf (PKCS#8).</param>
    public static Werkpleksleutel Van(ReadOnlySpan<byte> geheim)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(geheim, out _);
        return new(ecdsa);
    }

    /// <summary>De publieke sleutel, zoals de server hem bewaart.</summary>
    public byte[] Publiek => sleutel.ExportSubjectPublicKeyInfo();

    /// <summary>De geheime sleutel (PKCS#8); alleen versleuteld bewaren.</summary>
    public byte[] Geheim => sleutel.ExportPkcs8PrivateKey();

    /// <param name="servercertificaat">De vingerafdruk (SHA-256, hex) van het certificaat dat de server liet zien.</param>
    public string Code(string? servercertificaat) => ApparaatDienst.Controlecode(Publiek, servercertificaat);

    public byte[] Teken(string uitdaging) => sleutel.SignData(TeTekenen(uitdaging), HashAlgorithmName.SHA256);

    /// <summary>Wat er ondertekend wordt: de uitdaging met een vast voorvoegsel, zodat de handtekening nergens anders voor te gebruiken is.</summary>
    public static byte[] TeTekenen(string uitdaging) => System.Text.Encoding.UTF8.GetBytes("sifer-werkplek-inloggen:" + uitdaging);

    public void Dispose() => sleutel.Dispose();
}
