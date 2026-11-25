using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Een factuur die eerst los en daarna nog eens in een verzamelbetaling wordt betaald: Sifer boekt de verzamelbetaling
/// niet vanzelf, want het bedrag klopt niet meer met wat openstaat.
/// </summary>
public sealed class VerzamelbetalingTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Een_al_betaalde_factuur_in_een_verzamelbetaling()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Verzamel B.V.");
        var inrichting = app.Dienst<InrichtingDienst>();
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, await inrichting.Instellingen(wie, id) with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = ["NL20INGB0001234567"],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", "NL20INGB0001234567", "1100")).IsGelukt);
        var p = Praktijkset.Hotel;
        var nieuw = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), p.Iban);
        var klant = (await inrichting.Relaties(wie, id)).Single(r => r.Id == nieuw.Waarde).Code;
        var verkoop = app.Dienst<VerkoopDienst>();
        async Task<string> Factuur(DateOnly datum, decimal bedrag)
        {
            var c = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, datum, datum.AddDays(14), "Levering", [new Verkoopregel("Fietsen", 1m, bedrag, "8000", "V21")]));
            return (await verkoop.MaakDefinitief(wie, id, c.Waarde)).Waarde!;
        }

        var a = await Factuur(new DateOnly(2024, 9, 9), 1000m);   // 1210
        var b = await Factuur(new DateOnly(2024, 9, 22), 2000m);  // 2420
        var bank = app.Dienst<BankDienst>();
        var mutaties = new List<Bankmutatie>
        {
            new(new DateOnly(2024, 10, 5), 2420m, $"Factuur {b}", p.Iban, p.Naam),
            new(new DateOnly(2024, 10, 12), 3630m, $"Facturen {a} en {b}", p.Iban, p.Naam),
        };
        var afschrift = new Bankafschrift("NL20INGB0001234567", "202410", "EUR", new DateOnly(2024, 10, 1), 0m, new DateOnly(2024, 10, 31), 6050m, mutaties);
        Assert.True((await bank.Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, "BNK202410"))), "oktober.mt940")).IsGelukt);

        var lijst = await bank.Lijst(wie, id, ookGeboekt: true);
        Assert.Equal(Verwerkingswijze.Afgeletterd, lijst.Single(m => m.Bedrag == 2420m).Verwerking);
        var dubbel = lijst.Single(m => m.Bedrag == 3630m);
        Assert.Equal(Verwerkingswijze.Onbekend, dubbel.Verwerking);
        Assert.NotEqual("Geboekt", dubbel.Status.ToString());

        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        var posten = await db.OpenstaandePosten.AsNoTracking().ToDictionaryAsync(p => p.Factuurnummer!, p => p.Openstaand);
        Assert.Equal(1210m, posten[a]);
        Assert.Equal(0m, posten[b]);
    }
}
