using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Kostenplaatsen: de module aan of uit (kantoor), kostenplaatsen beheren, op een boekingsregel kiezen en het
/// overzicht per kostenplaats. Niet verplicht; een gebruikte kostenplaats vervalt in plaats van te verdwijnen.
/// </summary>
public sealed class KostenplaatsTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private async Task<(Uitvoerder Wie, Guid Id)> MetModule()
    {
        var wie = await app.Beheerder();
        Assert.True((await app.Dienst<KantoorDienst>().WijzigModules(wie, new Kantoormodules(true))).IsGelukt);
        return (wie, await app.Administratie());
    }

    private static Journaalpost Post(string boekstuk, DateOnly datum, params (string Rekening, decimal Bedrag, string? Kostenplaats)[] regels) =>
        new("MEM", boekstuk, datum, "Memoriaal " + boekstuk, [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag, Kostenplaats: r.Kostenplaats))]);

    [Fact]
    public async Task Zonder_module_geen_kostenplaatsen()
    {
        var wie = await app.Beheerder();
        var kantoor = app.Dienst<KantoorDienst>();
        Assert.True((await kantoor.WijzigModules(wie, new Kantoormodules(false))).IsGelukt);
        var id = await app.Administratie();
        var dienst = app.Dienst<KostenplaatsDienst>();

        Assert.Equal(["De module kostenplaatsen staat uit (Beheer, Instellingen)."], (await dienst.VoegToe(wie, id, "AUTO1", "Auto")).Fouten);
        Assert.Empty(await dienst.Keuze(wie, id));
        var kop = (await app.Dienst<AdministratieDienst>().Open(wie, id))!;
        Assert.False(kop.Kostenplaatsen);
        Assert.DoesNotContain(Administratiemenu.Groepen.SelectMany(g => g.Items), i => i.Pad == "/kostenplaatsen" && i.Zichtbaar(kop));
        Assert.Contains(Administratiemenu.Groepen.SelectMany(g => g.Items), i => i.Pad == "/kostenplaatsen" && i.Zichtbaar(kop with { Kostenplaatsen = true }));

        // Alleen de beheerder zet een module aan; dat komt in het wijzigingslog.
        var medewerker = await app.Gebruiker("Mees Medewerker");
        await Assert.ThrowsAsync<GeenToegangException>(() => kantoor.WijzigModules(medewerker, new Kantoormodules(true)));
        Assert.True((await kantoor.WijzigModules(wie, new Kantoormodules(true))).IsGelukt);
        Assert.True((await app.Dienst<AdministratieDienst>().Open(wie, id))!.Kostenplaatsen);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.KantoorinstellingenGewijzigd && r.Details == "kostenplaatsen aan");
    }

    [Fact]
    public async Task Kostenplaatsen_beheren()
    {
        var (wie, id) = await MetModule();
        var dienst = app.Dienst<KostenplaatsDienst>();

        Assert.Equal(["Vul de code in, bijv. AUTO1.", "Vul de naam in."], (await dienst.VoegToe(wie, id, " ", "")).Fouten);
        Assert.Equal(["Een code is hooguit 20 tekens, zonder spaties."], (await dienst.VoegToe(wie, id, "AUTO 1", "Auto")).Fouten);
        var auto = await dienst.VoegToe(wie, id, "AUTO1", "Auto AB-123-C");
        Assert.True(auto.IsGelukt, string.Join("; ", auto.Fouten));
        Assert.Equal(["Er is al een kostenplaats AUTO1."], (await dienst.VoegToe(wie, id, "AUTO1", "Nog een auto")).Fouten);
        Assert.True((await dienst.VoegToe(wie, id, "WERK", "Werkplaats")).IsGelukt);

        Assert.True((await dienst.Wijzig(wie, id, auto.Waarde, "Auto Tesla", vervallen: false)).IsGelukt);
        Assert.Equal(["AUTO1 Auto Tesla", "WERK Werkplaats"], (await dienst.Lijst(wie, id)).Select(k => k.Weergave));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.KostenplaatsGewijzigd && r.Onderwerp == "AUTO1" && r.Details == "naam Auto AB-123-C → Auto Tesla");

        // Niet gebruikt: weg. Vervallen: niet meer te kiezen.
        var werk = (await dienst.Lijst(wie, id)).Single(k => k.Code == "WERK");
        Assert.True((await dienst.Wijzig(wie, id, werk.Id, werk.Naam, vervallen: true)).IsGelukt);
        Assert.Equal(["AUTO1"], (await dienst.Keuze(wie, id)).Select(k => k.Code));
        Assert.True((await dienst.Verwijder(wie, id, werk.Id)).IsGelukt);
        Assert.Equal(["AUTO1"], (await dienst.Lijst(wie, id)).Select(k => k.Code));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.KostenplaatsVerwijderd && r.Onderwerp == "WERK Werkplaats");
    }

    [Fact]
    public async Task Boekingen_op_een_kostenplaats_en_het_overzicht()
    {
        var (wie, id) = await MetModule();
        var dienst = app.Dienst<KostenplaatsDienst>();
        var grootboek = app.Dienst<GrootboekDienst>();
        var auto = (await dienst.VoegToe(wie, id, "AUTO1", "Auto")).Waarde;
        await dienst.VoegToe(wie, id, "WINKEL", "Winkel");

        // Kosten op de auto (twee regels in één boeking, één zonder kostenplaats), opbrengst op de winkel.
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0001", new(2026, 3, 15), ("4300", 100m, "AUTO1"), ("4310", 50m, "AUTO1"), ("4320", 25m, null), ("1100", -175m, null)))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0002", new(2026, 4, 1), ("1100", 300m, null), ("8000", -300m, "WINKEL")))).IsGelukt);
        Assert.True((await grootboek.Boek(wie, id, Post("2026-0003", new(2025, 12, 31), ("4300", 999m, "AUTO1"), ("1100", -999m, null)))).IsGelukt);
        Assert.Equal(["Kostenplaats ONBEKEND bestaat niet."], (await grootboek.Boek(wie, id, Post("2026-0004", new(2026, 5, 1), ("4300", 1m, "ONBEKEND"), ("1100", -1m, null)))).Fouten);

        var overzicht = await dienst.Overzicht(wie, id, new(2026, 1, 1), new(2026, 12, 31));
        var a = overzicht.Single(t => t.Kostenplaats.Code == "AUTO1");
        var w = overzicht.Single(t => t.Kostenplaats.Code == "WINKEL");
        Assert.Equal((150m, 0m, -150m, 2), (a.Kosten, a.Opbrengsten, a.Resultaat, a.Regels));
        Assert.Equal((0m, 300m, 300m, 1), (w.Kosten, w.Opbrengsten, w.Resultaat, w.Regels));

        var boekingen = await dienst.Boekingen(wie, id, "AUTO1", new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal([("2026-0001", "4300", 100m), ("2026-0001", "4310", 50m)], boekingen.Select(b => (b.Boekstuk, b.Rekeningnummer, b.Bedrag)));
        Assert.Equal("Memoriaal 2026-0001", boekingen[0].Omschrijving);

        // Gebruikt: niet te verwijderen, wel te laten vervallen. Een nieuwe boeking kan er dan niet meer op;
        // een aanpassing van een boeking die er al op stond, wel.
        Assert.Equal(["Op AUTO1 is geboekt; laat hem vervallen in plaats van verwijderen."], (await dienst.Verwijder(wie, id, auto)).Fouten);
        Assert.True((await dienst.Wijzig(wie, id, auto, "Auto", vervallen: true)).IsGelukt);
        Assert.Equal(["Kostenplaats AUTO1 is vervallen."], (await grootboek.Boek(wie, id, Post("2026-0005", new(2026, 5, 1), ("4300", 1m, "AUTO1"), ("1100", -1m, null)))).Fouten);
        var aangepast = await grootboek.PasAan(wie, id, "MEM", "2026-0001", Post("x", new(2026, 3, 15), ("4300", 120m, "AUTO1"), ("4310", 50m, "AUTO1"), ("4320", 5m, null), ("1100", -175m, null)));
        Assert.True(aangepast.IsGelukt, string.Join("; ", aangepast.Fouten));
        Assert.Equal(170m, (await dienst.Overzicht(wie, id, new(2026, 1, 1), new(2026, 12, 31))).Single(t => t.Kostenplaats.Code == "AUTO1").Kosten);

        // Op de boeking zelf staat de kostenplaats, en de keten is heel.
        Assert.Equal("AUTO1", (await grootboek.Boeking(wie, id, "MEM", "2026-0001"))!.Boeking.Post.Regels[0].Kostenplaats);
        Assert.Empty(await grootboek.ControleerKeten(wie, id));
    }

    [Fact]
    public async Task Toegang_tot_kostenplaatsen()
    {
        var (wie, id) = await MetModule();
        var dienst = app.Dienst<KostenplaatsDienst>();
        await dienst.VoegToe(wie, id, "AUTO1", "Auto");

        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        Assert.Single(await dienst.Lijst(ondernemer, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.VoegToe(ondernemer, id, "AUTO2", "Auto 2"));

        var buitenstaander = await app.Gebruiker("Bram Buiten");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lijst(buitenstaander, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Overzicht(buitenstaander, id, new(2026, 1, 1), new(2026, 12, 31)));

        // Een kostenplaats van de ene administratie bestaat niet in de andere.
        var andere = await app.Administratie("Andere B.V.");
        Assert.Empty(await dienst.Lijst(wie, andere));
        Assert.Equal(["Kostenplaats AUTO1 bestaat niet."],
            (await app.Dienst<GrootboekDienst>().Boek(wie, andere, Post("2026-0001", new(2026, 3, 1), ("4300", 1m, "AUTO1"), ("1100", -1m, null)))).Fouten);
    }
}
