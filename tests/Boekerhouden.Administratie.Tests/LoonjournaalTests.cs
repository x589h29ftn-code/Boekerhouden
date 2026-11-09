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
}
