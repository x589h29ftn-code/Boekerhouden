using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Memoriaalposten kopiëren naar een andere maand (bijv. de loonjournaalpost).</summary>
public sealed class MemoriaalKopieTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Een_post_kopieren_naar_een_maand_geeft_een_nieuwe_boeking_met_de_maand_in_de_omschrijving()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 10, 31), "Lonen oktober 2026",
            [new("4000", 3000m), new("1520", -800m), new("1800", -2200m)]))).IsGelukt);

        var kopie = await grootboek.KopieerNaarMaand(wie, id, "MEM", "2026-0001", 2026, 11);

        Assert.True(kopie.IsGelukt, string.Join("; ", kopie.Fouten));
        var nieuw = (await grootboek.Posten(wie, id, "MEM")).Single(p => p.Post.Boekstuk == kopie.Waarde);
        Assert.Equal((new DateOnly(2026, 11, 30), "Lonen november 2026", 3000m), (nieuw.Post.Datum, nieuw.Post.Omschrijving, nieuw.Post.TotaalDebet));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.Geboekt && r.Onderwerp == $"MEM {kopie.Waarde}");
        Assert.False((await grootboek.KopieerNaarMaand(wie, id, "MEM", "9999-9999", 2026, 11)).IsGelukt);

        var ondernemer = await app.Gebruiker("Otto Memoriaal");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.KopieerNaarMaand(ondernemer, id, "MEM", "2026-0001", 2026, 12));
    }

    [Fact]
    public async Task Een_kopie_hoort_niet_bij_dezelfde_factuur()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await grootboek.Boek(wie, id, new Journaalpost("MEM", "2026-0001", new(2026, 10, 31), "Verrekening oktober",
            [new("1300", -100m, Relatie: "KLANT", Factuurnummer: "F12"), new("4300", 100m)]))).IsGelukt);

        var kopie = await grootboek.KopieerNaarMaand(wie, id, "MEM", "2026-0001", 2026, 11);
        var herhaald = await grootboek.BoekPerMaand(wie, id, new Journaalpost("MEM", "2026-0010", new(2026, 1, 31), "Verrekening",
            [new("1300", -10m, Relatie: "KLANT", Factuurnummer: "F13"), new("4300", 10m)]), 2);

        Assert.True(kopie.IsGelukt && herhaald.IsGelukt);
        var posten = (await grootboek.Posten(wie, id, "MEM")).Select(p => p.Post).ToList();
        Assert.Null(posten.Single(p => p.Boekstuk == kopie.Waarde).Regels.Single(r => r.Rekeningnummer == "1300").Factuurnummer);
        Assert.Equal(["F13", null], posten.Where(p => p.Omschrijving == "Verrekening").OrderBy(p => p.Datum).Select(p => p.Regels.Single(r => r.Rekeningnummer == "1300").Factuurnummer));
    }
}
