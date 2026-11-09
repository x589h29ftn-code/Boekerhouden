using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Loon;
using Boekerhouden.Documenten.Tests;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Het loonjournaal uit het salarispakket inlezen, koppelen en de koppelingen onthouden.</summary>
public sealed class LoonjournaalTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Inlezen_koppelen_onthouden_en_boeken()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Loon Voorbeeld B.V.");
        var dienst = app.Dienst<LoonjournaalDienst>();

        var gelezen = await dienst.Lees(wie, id, new MemoryStream(FictiefLoonjournaal.Pdf()), "journaal.pdf");
        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        var voorstel = gelezen.Waarde!;
        Assert.Empty(voorstel.Signalen);

        // 4000 bestaat in het standaardschema; 1500, 4010 en 2100 hebben er geen (passende) rekening met dat nummer of die naam.
        Assert.Equal(("4000", Loonkoppelbron.ZelfdeNummer), (voorstel.Koppelingen[0].Rekening, voorstel.Koppelingen[0].Bron));
        Assert.Equal(Loonkoppelbron.Geen, voorstel.Koppelingen.Single(k => k.Regel.Nummer == "2100").Bron);

        // De gebruiker kiest; de koppelingen worden onthouden voor de volgende keer.
        var keuzes = voorstel.Koppelingen.Select(k => (k.Regel, Rekening: k.Regel.Nummer switch { "4000" => "4000", "4010" => "4010", "1500" => "1520", _ => "1800" })).ToList();
        Assert.True((await dienst.Onthoud(wie, id, keuzes)).IsGelukt);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.LoonkoppelingGewijzigd && r.Details!.Contains("2100 Netto loon → 1800", StringComparison.Ordinal));
        var tweede = (await dienst.Lees(wie, id, new MemoryStream(FictiefLoonjournaal.Pdf()), "journaal.pdf")).Waarde!;
        Assert.Equal(("1800", Loonkoppelbron.Onthouden), (tweede.Koppelingen.Single(k => k.Regel.Nummer == "2100").Rekening, tweede.Koppelingen.Single(k => k.Regel.Nummer == "2100").Bron));

        // De boeking is in balans en gaat via het memoriaal.
        var post = Loonkoppelingen.Boeking("MEM", new(2026, 3, 31), "Loonjournaal 2026-1 t/m 2026-3", keuzes.Select(k => (k.Rekening, k.Regel))) with { Boekstuk = "2026-0001" };
        var geboekt = await app.Dienst<GrootboekDienst>().Boek(wie, id, post);
        Assert.True(geboekt.IsGelukt, string.Join("; ", geboekt.Fouten));

        Assert.False((await dienst.Lees(wie, id, new MemoryStream("geen pdf"u8.ToArray()), "x.pdf")).IsGelukt);
        Assert.False((await dienst.Onthoud(wie, id, [(keuzes[0].Regel, "9999")])).IsGelukt);

        var ondernemer = await app.Gebruiker("Otto Loon");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lees(ondernemer, id, new MemoryStream(FictiefLoonjournaal.Pdf()), "j.pdf"));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Onthoud(ondernemer, id, keuzes));
        var buiten = await app.Gebruiker("Buiten Loon");
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lees(buiten, id, new MemoryStream(FictiefLoonjournaal.Pdf()), "j.pdf"));
    }

    [Fact]
    public async Task Een_andere_opbouw_en_een_nieuwe_looncomponent()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Loon Lijst B.V.");
        var dienst = app.Dienst<LoonjournaalDienst>();

        // Een lijst met kolommen Debet en Credit; de keuzes worden onthouden.
        var eerste = (await dienst.Lees(wie, id, new MemoryStream(FictiefLoonjournaal.Lijst(FictiefLoonjournaal.Lijstsoort.Kolommen)), "maart.pdf")).Waarde!;
        Assert.Equal((7_000m, 7_000m), (eerste.Journaal.Debet, eerste.Journaal.Credit));
        var keuzes = eerste.Koppelingen.Select(k => (k.Regel, Rekening: k.Rekening ?? (k.Regel.Nummer == "2100" ? "1800" : "1520"))).ToList();
        Assert.True((await dienst.Onthoud(wie, id, keuzes)).IsGelukt);

        // De volgende maand staat er een reiskostenvergoeding via de verloning bij: die wordt gesignaleerd.
        var tweede = (await dienst.Lees(wie, id, new MemoryStream(FictiefLoonjournaal.Lijst(FictiefLoonjournaal.Lijstsoort.DebetCredit, nieuw: true)), "april.pdf")).Waarde!;
        var nieuw = tweede.Koppelingen.Single(k => k.Regel.Nummer == "4062");
        Assert.Equal((Loonkoppelbron.Nieuw, (string?)null), (nieuw.Bron, nieuw.Rekening));
        Assert.Equal("Nieuw in het salarispakket: 4062 Reiskostenvergoeding via verloning. Kies bij welke rekening die hoort.", Assert.Single(tweede.Signalen));
        Assert.All(tweede.Koppelingen.Where(k => k.Regel.Nummer != "4062"), k => Assert.Equal(Loonkoppelbron.Onthouden, k.Bron));
    }

    [Fact]
    public async Task Koppelingen_bij_de_instellingen_vooraf_instellen_wijzigen_en_weghalen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Loon Instellen B.V.");
        var dienst = app.Dienst<LoonjournaalDienst>();

        // Vooraf ingesteld, zonder loonjournaal.
        Assert.True((await dienst.Zet(wie, id, "2100", "Netto loon", "1800")).IsGelukt);
        Assert.True((await dienst.Zet(wie, id, "1500", "Af te dragen loonheffing", "1520")).IsGelukt);
        Assert.True((await dienst.Zet(wie, id, "2100", "Netto loon", "1700")).IsGelukt);
        Assert.Equal([("1500", "1520"), ("2100", "1700")], (await dienst.Koppelingen(wie, id)).Select(k => (k.Loonrekening, k.Rekeningnummer)));
        Assert.False((await dienst.Zet(wie, id, "", "Leeg", "1800")).IsGelukt);
        Assert.False((await dienst.Zet(wie, id, "3000", "Onbekend", "9999")).IsGelukt);

        // Bij het inlezen gelden ze meteen; wat niet is ingesteld, is nieuw.
        var voorstel = (await dienst.Lees(wie, id, new MemoryStream(FictiefLoonjournaal.Lijst(FictiefLoonjournaal.Lijstsoort.Teken)), "maart.pdf")).Waarde!;
        Assert.Equal(("1700", Loonkoppelbron.Onthouden), (voorstel.Koppelingen.Single(k => k.Regel.Nummer == "2100").Rekening, voorstel.Koppelingen.Single(k => k.Regel.Nummer == "2100").Bron));
        Assert.Equal(Loonkoppelbron.Nieuw, voorstel.Koppelingen.Single(k => k.Regel.Nummer == "4000").Bron);

        Assert.True((await dienst.Verwijder(wie, id, "2100")).IsGelukt);
        Assert.False((await dienst.Verwijder(wie, id, "2100")).IsGelukt);
        Assert.Equal(["1500"], (await dienst.Koppelingen(wie, id)).Select(k => k.Loonrekening));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.LoonkoppelingGewijzigd && r.Details == "2100 Netto loon weggehaald (was 1700)");

        var ondernemer = await app.Gebruiker("Otto Loon Instellen");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Koppelingen(ondernemer, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Zet(ondernemer, id, "2100", "Netto loon", "1800"));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Verwijder(ondernemer, id, "1500"));
    }
}
