using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Tussentijds bewaren van een inkoopfactuur of bankmutatie, en weer verder gaan.</summary>
public sealed class ConceptTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private sealed record Formulier(string Leverancier, IReadOnlyList<string> Regels);

    [Fact]
    public async Task Bewaren_lezen_overschrijven_en_weg_met_log()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var dienst = app.Dienst<ConceptDienst>();
        var document = Guid.NewGuid();

        Assert.Null(await dienst.Lees<Formulier>(wie, id, Conceptsoort.Inkoop, document));
        await dienst.Bewaar(wie, id, Conceptsoort.Inkoop, document, new Formulier("BELNET", ["4310"]), "Belnet BN-1");
        await dienst.Bewaar(wie, id, Conceptsoort.Inkoop, document, new Formulier("BELNET", ["4310", "4330"]), "Belnet BN-1");

        var gelezen = await dienst.Lees<Formulier>(wie, id, Conceptsoort.Inkoop, document);
        Assert.NotNull(gelezen);
        Assert.Equal(["4310", "4330"], gelezen.Inhoud.Regels);
        Assert.Equal(wie.Naam, gelezen.BewaardDoor);
        Assert.Equal([document], await dienst.Met(wie, id, Conceptsoort.Inkoop));
        Assert.Empty(await dienst.Met(wie, id, Conceptsoort.Bank));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ConceptBewaard && r.Onderwerp == "Belnet BN-1");

        await dienst.Verwijder(wie, id, Conceptsoort.Inkoop, document);
        Assert.Null(await dienst.Lees<Formulier>(wie, id, Conceptsoort.Inkoop, document));
    }

    [Fact]
    public async Task Een_concept_hoort_bij_een_administratie_en_bewaren_mag_wie_mag_invoeren()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Concept A");
        var ander = await app.Administratie("Concept B");
        var dienst = app.Dienst<ConceptDienst>();
        var mutatie = Guid.NewGuid();
        await dienst.Bewaar(wie, id, Conceptsoort.Bank, mutatie, new Formulier("x", []), "mutatie");

        Assert.Null(await dienst.Lees<Formulier>(wie, ander, Conceptsoort.Bank, mutatie));
        var inzien = await app.Gebruiker("Inge Concept");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        Assert.NotNull(await dienst.Lees<Formulier>(inzien, id, Conceptsoort.Bank, mutatie));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Bewaar(inzien, id, Conceptsoort.Bank, mutatie, new Formulier("y", []), "mutatie"));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwijder(inzien, id, Conceptsoort.Bank, mutatie));
        var buiten = await app.Gebruiker("Buiten Concept");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lees<Formulier>(buiten, id, Conceptsoort.Bank, mutatie));
    }
}
