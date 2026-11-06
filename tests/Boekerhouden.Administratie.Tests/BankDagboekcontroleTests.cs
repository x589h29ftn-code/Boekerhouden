using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// De aansluiting vergelijkt elke ingelezen bankmutatie met het bankdagboek: een boeking die (per ongeluk) verwijderd
/// of vervallen is, of met een ander bedrag op de bank staat, komt als verschil in beeld en de mutatie kan terug naar
/// Te verwerken. Het verzonnen ING-afschrift van maart 2026: Belnet -25,00, J. de Vries 1.210,00, Jansen -199,65.
/// </summary>
public sealed class BankDagboekcontroleTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    /// <summary>Een administratie met beginbalans en het afschrift van maart, alle mutaties geboekt op 9000.</summary>
    private async Task<(Uitvoerder Wie, Guid Id)> Geboekt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var beginbalans = new Journaalpost("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans", [new Journaalregel("1100", 5000m), new Journaalregel("0500", -5000m)]);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, beginbalans)).IsGelukt);
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        foreach (var m in await bank.Lijst(wie, id))
        {
            Assert.True((await bank.OpRekening(wie, id, m.Id, "9000", null, null)).IsGelukt);
        }

        return (wie, id);
    }

    private async Task<Aansluiting> Bank(Uitvoerder wie, Guid id) =>
        (await app.Dienst<AansluitingDienst>().Aansluitingen(wie, id)).Single(a => a.Soort == Aansluitingsoort.Bank);

    /// <summary>Wijzigt de boeking van de mutatie met dit bedrag buiten de schermen om: zonder regels (vervallen) of met een ander bedrag.</summary>
    private async Task<string> Wijzig(Uitvoerder wie, Guid id, decimal bedrag, decimal? nieuwBedrag = null)
    {
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Boekhouden);
        var mutatie = await db.Banktransacties.SingleAsync(t => t.Bedrag == bedrag);
        var oud = await db.Boekingen.Include(b => b.Regels).SingleAsync(b => b.Id == mutatie.BoekingId);
        var stuk = $"{oud.Dagboek} {oud.Boekstuk}";
        if (nieuwBedrag is { } n)
        {
            await app.Dienst<GrootboekDienst>().NieuweVersie(db, id, oud,
                new Journaalpost(oud.Dagboek, oud.Boekstuk, oud.Datum, oud.Omschrijving, [new Journaalregel("1100", n), new Journaalregel("9000", -n)]), wie);
            await db.SaveChangesAsync();
        }
        else
        {
            await app.Dienst<GrootboekDienst>().LaatVervallen(db, id, [stuk], wie);
        }

        return stuk;
    }

    [Fact]
    public async Task Alles_geboekt_dan_geen_verschillen()
    {
        var (wie, id) = await Geboekt();

        var bank = await Bank(wie, id);

        Assert.True(bank.Sluit);
        Assert.Empty(bank.Verschillen!);
    }

    [Fact]
    public async Task Een_verwijderde_boeking_in_het_bankdagboek_komt_als_verschil_en_de_mutatie_kan_terug()
    {
        var (wie, id) = await Geboekt();
        var stuk = await Wijzig(wie, id, -199.65m);

        var bank = await Bank(wie, id);

        Assert.False(bank.Sluit);
        Assert.Equal(199.65m, bank.Verschil);
        var verschil = Assert.Single(bank.Verschillen!);
        Assert.Equal((Bankverschilsoort.NietInGrootboek, new DateOnly(2026, 3, 20), -199.65m, 0m, stuk), (verschil.Soort, verschil.Datum, verschil.Bedrag, verschil.Grootboek, verschil.Boekstuk));
        Assert.Contains("Kantoorhandel Jansen", verschil.Omschrijving, StringComparison.Ordinal);
        Assert.Contains("1 mutatie niet in het bankdagboek", bank.Toelichting, StringComparison.Ordinal);

        // Een mutatie waarvan de boeking nog op de bank staat, kan niet terug.
        var dienst = app.Dienst<BankDienst>();
        var andere = (await dienst.Lijst(wie, id, ookGeboekt: true)).First(m => m.Bedrag == 1210m);
        Assert.False((await dienst.TerugNaarTeVerwerken(wie, id, andere.Id)).IsGelukt);

        Assert.True((await dienst.TerugNaarTeVerwerken(wie, id, verschil.Mutatie!.Value)).IsGelukt);

        var terug = Assert.Single(await dienst.Lijst(wie, id));
        Assert.Equal(-199.65m, terug.Bedrag);
        Assert.Contains($"De boeking {stuk} stond niet meer op de bank", terug.Melding, StringComparison.Ordinal);
        var na = await Bank(wie, id);
        Assert.Empty(na.Verschillen!);
        Assert.Contains("1 mutatie te verwerken", na.Toelichting, StringComparison.Ordinal);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BankmutatieTerug);

        // Opnieuw boeken: weer sluitend.
        Assert.True((await dienst.OpRekening(wie, id, terug.Id, "9000", null, null)).IsGelukt);
        Assert.True((await Bank(wie, id)).Sluit);
    }

    [Fact]
    public async Task Een_ander_bedrag_op_de_bank_en_een_boeking_zonder_mutatie()
    {
        var (wie, id) = await Geboekt();
        var stuk = await Wijzig(wie, id, -25m, nieuwBedrag: -20m);
        var memoriaal = new Journaalpost("MEM", "2026-0002", new(2026, 3, 15), "Kasopname", [new Journaalregel("1100", -50m), new Journaalregel("9000", 50m)]);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, memoriaal)).IsGelukt);

        var bank = await Bank(wie, id);

        Assert.Equal(
            [(Bankverschilsoort.Afwijkend, -25m, -20m, stuk), (Bankverschilsoort.ZonderMutatie, -50m, -50m, "MEM 2026-0002")],
            bank.Verschillen!.Select(v => (v.Soort, v.Bedrag, v.Grootboek, v.Boekstuk)));
        Assert.Single(bank.Ontbrekend);
        Assert.False((await app.Dienst<BankDienst>().TerugNaarTeVerwerken(wie, id, bank.Verschillen![0].Mutatie!.Value)).IsGelukt);
    }

    [Fact]
    public async Task Met_alleen_inzien_of_zonder_lidmaatschap_geen_mutatie_terugzetten()
    {
        var (wie, id) = await Geboekt();
        await Wijzig(wie, id, -199.65m);
        var mutatie = (await Bank(wie, id)).Verschillen!.Single().Mutatie!.Value;
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        var buiten = await app.Gebruiker("Bo Buiten");

        Assert.Single((await app.Dienst<AansluitingDienst>().Aansluitingen(inzien, id)).Single(a => a.Soort == Aansluitingsoort.Bank).Verschillen!);
        await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<BankDienst>().TerugNaarTeVerwerken(inzien, id, mutatie));
        await Assert.ThrowsAsync<GeenToegangException>(() => app.Dienst<BankDienst>().TerugNaarTeVerwerken(buiten, id, mutatie));
        Assert.Empty(await app.Dienst<BankDienst>().Lijst(wie, id));
    }
}
