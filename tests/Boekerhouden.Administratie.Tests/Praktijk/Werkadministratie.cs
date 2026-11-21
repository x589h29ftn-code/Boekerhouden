using System.Text;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Een ingerichte administratie voor de stresstests (gelijktijdig, willekeurige reeksen): eigen gegevens, bank, een
/// beginbalans, een klant en een leverancier, en korte wegen om te factureren, in te kopen en bankmutaties in te lezen.
/// </summary>
internal sealed class Werkadministratie(AdministratieFixture app, Uitvoerder wie, Guid id, string klant)
{
    public const string Iban = "NL20INGB0001234567";

    private int afschrift;

    public Uitvoerder Wie { get; } = wie;

    public Guid Id { get; } = id;

    public string Klant { get; } = klant;

    public static async Task<Werkadministratie> Maak(AdministratieFixture app, string naam)
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie(naam);
        var inrichting = app.Dienst<InrichtingDienst>();
        var nu = await inrichting.Instellingen(wie, id);
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, nu with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [Iban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", Iban, "1100")).IsGelukt);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Journaalpost("MEM", "2025-0001", new(2025, 1, 1), "Beginbalans",
            [new("1100", 10_000m), new("0500", -10_000m)]))).IsGelukt);
        var p = Praktijkset.Fietsplan;
        var klant = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), p.Iban);
        Assert.True(klant.IsGelukt, string.Join("; ", klant.Fouten));
        var code = (await inrichting.Relaties(wie, id)).Single(r => r.Id == klant.Waarde).Code;
        return new Werkadministratie(app, wie, id, code);
    }

    /// <summary>Een concept-verkoopfactuur (nog niet definitief).</summary>
    public async Task<Guid> Concept(DateOnly datum, decimal bedrag, Uitvoerder? door = null)
    {
        var concept = await app.Dienst<VerkoopDienst>().Bewaar(door ?? Wie, Id, null,
            new Verkoopinvoer(Klant, datum, datum.AddDays(14), "Fietsen", [new Verkoopregel("Fiets", 1, bedrag, "8000", "V21")]));
        Assert.True(concept.IsGelukt, string.Join("; ", concept.Fouten));
        return concept.Waarde;
    }

    /// <summary>Een inkoopfactuur als e-factuur inlezen (nog niet geboekt als het pakket hem niet zelf boekt).</summary>
    public async Task<Guid> InkoopDocument(string nummer, DateOnly datum, decimal bedrag)
    {
        var f = new Factuurdata(true, Praktijkset.Groothandel, nummer, datum, datum.AddDays(14), "Levering", [new Regeldata("Levering", 1, bedrag, "V21", "7000", "I21")], Formaat.Ubl, Betaalwijze.Nummer);
        var gelezen = await app.Dienst<InkoopDienst>().Lees(Wie, Id, new MemoryStream(Praktijkset.Document(f, f.Partij, Praktijkset.Eigen)), f.Bestandsnaam, null);
        return gelezen.DocumentId ?? throw new InvalidOperationException(gelezen.Melding);
    }

    /// <summary>Een inkoopdocument boeken zoals op het scherm.</summary>
    public async Task<Uitkomst> BoekInkoop(Guid document, string nummer, DateOnly datum, decimal bedrag, Uitvoerder? door = null)
    {
        var p = Praktijkset.Groothandel;
        var code = (await app.Dienst<InrichtingDienst>().Relaties(Wie, Id)).FirstOrDefault(r => r.Naam == p.Naam)?.Code;
        return await app.Dienst<InkoopDienst>().Boek(door ?? Wie, Id, document, new Factuurinvoer(code, code is null ? new NieuweRelatie(p.Naam, p.Iban, p.BtwNummer, p.Kvk) : null,
            nummer, datum, datum.AddDays(14), "Levering", [new Factuurregel("Levering", "7000", "I21", bedrag, Math.Round(bedrag * 0.21m, 2))], null));
    }

    /// <summary>Bankmutaties inlezen in een afschrift dat aansluit op het vorige; geeft de mutaties die blijven staan.</summary>
    public async Task<IReadOnlyList<BankmutatieRegel>> Bank(params Bankmutatie[] mutaties)
    {
        var bank = app.Dienst<BankDienst>();
        var stand = (await bank.Stand(Wie, Id)).Single(b => b.Dagboek == "BNK");
        var begin = stand.Saldo ?? 10_000m;
        var nummer = ++afschrift;
        var a = new Bankafschrift(Iban, $"{nummer}", "EUR", mutaties.Min(m => m.Datum), begin, mutaties.Max(m => m.Datum), begin + mutaties.Sum(m => m.Bedrag), mutaties);
        var gelezen = await bank.Lees(Wie, Id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(a, $"AF{nummer}"))), $"afschrift {nummer}.mt940");
        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        return await bank.Lijst(Wie, Id);
    }
}
