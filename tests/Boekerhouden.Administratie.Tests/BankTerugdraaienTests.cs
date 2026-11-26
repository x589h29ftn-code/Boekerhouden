using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Administratie.Tests.Praktijk;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Verkoop;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Een geboekte bankmutatie terugdraaien: tegenboeking, de factuur weer open, de mutatie weer te verwerken.</summary>
public sealed class BankTerugdraaienTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL20INGB0001234567";

    [Fact]
    public async Task Een_afgeletterde_betaling_terugdraaien_zet_de_factuur_weer_open()
    {
        var wie = await app.Beheerder();
        var (id, nummer) = await Inrichten(wie, "Terug B.V.");
        var bank = app.Dienst<BankDienst>();
        var m = Assert.Single(await bank.Lijst(wie, id, ookGeboekt: true));
        Assert.Equal(Banktransactiestatus.Geboekt, m.Status);
        Assert.Equal(0m, await Open(wie, id, nummer));

        Assert.True((await bank.Terugdraaien(wie, id, m.Id)).IsGelukt);
        Assert.Equal(1_210m, await Open(wie, id, nummer));
        var terug = Assert.Single(await bank.Lijst(wie, id));
        Assert.Equal((Banktransactiestatus.TeVerwerken, Verwerkingswijze.Onbekend), (terug.Status, terug.Verwerking));
        Assert.StartsWith("Boeking BNK", terug.Melding, StringComparison.Ordinal);
        // Het journaal blijft: de boeking en de tegenboeking heffen elkaar op; de bank staat op het beginsaldo.
        var posten = await app.Dienst<GrootboekDienst>().Posten(wie, id, "BNK");
        Assert.Equal(2, posten.Count);
        Assert.Equal(0m, posten.SelectMany(p => p.Post.Regels).Where(r => r.Rekeningnummer == "1100").Sum(r => r.Bedrag));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BoekingTeruggedraaid && r.Details!.Contains("tegenboeking BNK", StringComparison.Ordinal));
        Assert.False((await bank.Terugdraaien(wie, id, m.Id)).IsGelukt);

        // Opnieuw afletteren kan gewoon.
        var post = (await bank.OpenPosten(wie, id)).Single(p => p.Factuurnummer == nummer);
        Assert.True((await bank.Afletteren(wie, id, m.Id, [post.Id])).IsGelukt);
        Assert.Equal(0m, await Open(wie, id, nummer));
        Assert.Empty(await Invarianten.Controleer(app, wie, id, "na opnieuw afletteren"));
    }

    [Fact]
    public async Task Niet_in_een_afgesloten_periode()
    {
        var wie = await app.Beheerder();
        var (id, nummer) = await Inrichten(wie, "Terug Dicht B.V.");
        var bank = app.Dienst<BankDienst>();
        var m = Assert.Single(await bank.Lijst(wie, id, ookGeboekt: true));
        await app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, new(2024, 10, 1), new(2024, 12, 31));
        await app.Dienst<BtwaangifteDienst>().Vastleggen(wie, id, new(2024, 7, 1), new(2024, 9, 30));
        Assert.True((await app.Dienst<PeriodeDienst>().SluitAf(wie, id, new(2024, 10, 31))).IsGelukt);

        var uitkomst = await bank.Terugdraaien(wie, id, m.Id);
        Assert.False(uitkomst.IsGelukt);
        Assert.Equal(0m, await Open(wie, id, nummer));
    }

    [Fact]
    public async Task Een_afschrift_dat_een_dag_overlapt_geeft_geen_melding_over_het_beginsaldo()
    {
        var wie = await app.Beheerder();
        var (id, _) = await Inrichten(wie, "Terug Overlap B.V.");
        // Het vorige afschrift eindigde op 12-10 met 1.210; dit begint op 12-10 met het saldo van vóór die dag (0).
        var mutaties = new List<Bankmutatie>
        {
            new(new DateOnly(2024, 10, 12), 1_210m, "Overlap", Praktijkset.Hotel.Iban, Praktijkset.Hotel.Naam),
            new(new DateOnly(2024, 10, 20), -10m, "Kosten"),
        };
        var afschrift = new Bankafschrift(Iban, "202410b", "EUR", new DateOnly(2024, 10, 12), 0m, new DateOnly(2024, 10, 31), 1_200m, mutaties);
        var gelezen = await app.Dienst<BankDienst>().Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, "BNK202410b"))), "oktober-2.mt940");
        Assert.True(gelezen.IsGelukt);
        Assert.DoesNotContain(gelezen.Waarde!.Meldingen, m => m.Contains("sluit niet aan", StringComparison.Ordinal));
        Assert.Equal(1, gelezen.Waarde.Dubbel);
    }

    private async Task<decimal> Open(Uitvoerder wie, Guid id, string nummer)
    {
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        return (await db.OpenstaandePosten.AsNoTracking().SingleAsync(p => p.Factuurnummer == nummer)).Openstaand;
    }

    /// <summary>Een administratie met één verkoopfactuur van 1.210 die met het afschrift van oktober vanzelf is afgeletterd.</summary>
    private async Task<(Guid Id, string Nummer)> Inrichten(Uitvoerder wie, string naam)
    {
        var id = await app.Administratie(naam);
        var inrichting = app.Dienst<InrichtingDienst>();
        var e = Praktijkset.Eigen;
        Assert.True((await inrichting.WijzigInstellingen(wie, id, await inrichting.Instellingen(wie, id) with
        {
            BtwNummer = e.BtwNummer,
            KvkNummer = e.Kvk,
            Ibans = [Iban],
            Contact = new Contactgegevens(e.Straat, e.Postcode, e.Plaats, null, "info@despaak.test"),
        })).IsGelukt);
        Assert.True((await app.Dienst<BankDienst>().WijzigBank(wie, id, "BNK", "Betaalrekening", Iban, "1100")).IsGelukt);
        var p = Praktijkset.Hotel;
        var nieuw = await inrichting.VoegRelatieToe(wie, id, new RelatieInvoer(p.Naam, p.BtwNummer, p.Kvk, null,
            new Klantgegevens(true, p.Straat, p.Postcode, p.Plaats, p.Land, Betalingstermijn: 14, Omzetrekening: "8000", Verkoopbtwcode: "V21")), p.Iban);
        var klant = (await inrichting.Relaties(wie, id)).Single(r => r.Id == nieuw.Waarde).Code;
        var verkoop = app.Dienst<VerkoopDienst>();
        var c = await verkoop.Bewaar(wie, id, null, new Verkoopinvoer(klant, new(2024, 9, 9), new(2024, 9, 23), "Levering", [new Verkoopregel("Fietsen", 1m, 1_000m, "8000", "V21")]));
        var nummer = (await verkoop.MaakDefinitief(wie, id, c.Waarde)).Waarde!;
        var afschrift = new Bankafschrift(Iban, "202410", "EUR", new DateOnly(2024, 10, 1), 0m, new DateOnly(2024, 10, 12), 1_210m,
            [new(new DateOnly(2024, 10, 12), 1_210m, "Overlap", p.Iban, p.Naam)]);
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, new MemoryStream(Encoding.Latin1.GetBytes(Mt940Schrijver.Schrijf(afschrift, "BNK202410"))), "oktober.mt940")).IsGelukt);
        // Gewone omschrijving zonder nummer: met de hand afletteren als het niet vanzelf ging.
        var bank = app.Dienst<BankDienst>();
        if (await bank.Lijst(wie, id) is [var m, ..])
        {
            var post = (await bank.OpenPosten(wie, id)).Single(x => x.Factuurnummer == nummer);
            Assert.True((await bank.Afletteren(wie, id, m.Id, [post.Id])).IsGelukt);
        }

        return (id, nummer);
    }
}
