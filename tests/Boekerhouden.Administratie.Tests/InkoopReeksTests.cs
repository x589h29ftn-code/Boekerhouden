using System.Globalization;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Regels;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Een reeks facturen van één leverancier (verzonnen PDF's van "Telecom Noord B.V." aan "Test BV"): na de drempel boekt
/// het pakket zelf, binnen de bandbreedte, met hetzelfde IBAN en adres; een afwijking komt ter controle terug.
/// </summary>
public sealed class InkoopReeksTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private const string Iban = "NL36INGB0007654321";
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Een maandfactuur als PDF: leverancier links, klant "Test BV" rechts, regels, btw 21% en totaal.</summary>
    internal static byte[] Telecom(string nummer, DateOnly datum, decimal exclusief, string iban = Iban, string postcode = "9711 AB")
    {
        var btw = Math.Round(exclusief * 0.21m, 2, MidpointRounding.AwayFromZero);
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var pagina = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var y = 800.0;
        void Regel(string links, string? rechts = null)
        {
            pagina.AddText(links, 10, new UglyToad.PdfPig.Core.PdfPoint(40, y), font);
            if (rechts is not null)
            {
                pagina.AddText(rechts, 10, new UglyToad.PdfPig.Core.PdfPoint(400, y), font);
            }

            y -= 16;
        }

        Regel("Telecom Noord B.V.", "Test BV");
        Regel("Stationsweg 1", "Industrieweg 5");
        Regel($"{postcode} Groningen", "1234 AB Amersfoort");
        Regel($"IBAN {iban}");
        Regel("KvK 12345678");
        Regel("BTW NL800000006B01");
        Regel($"Factuurnummer: {nummer}");
        Regel($"Factuurdatum: {datum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}");
        Regel("Omschrijving", "Bedrag");
        Regel($"Zakelijk abonnement {datum.ToString("MMMM yyyy", Nl)}", exclusief.ToString("N2", Nl));
        Regel("Subtotaal", $"EUR {exclusief.ToString("N2", Nl)}");
        Regel("BTW 21%", $"EUR {btw.ToString("N2", Nl)}");
        Regel("Totaal", $"EUR {(exclusief + btw).ToString("N2", Nl)}");
        return builder.Build();
    }

    private static DateOnly Maand(int m) => new(2026, m, 1);

    private async Task<Inleesuitkomst> Lees(Uitvoerder wie, Guid id, string nummer, int maand, decimal exclusief, string iban = Iban, string postcode = "9711 AB") =>
        await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(Telecom(nummer, Maand(maand), exclusief, iban, postcode)), $"{nummer}.pdf", "application/pdf");

    /// <summary>Boekt zoals de gebruiker het voorstel bevestigt: de herkende gegevens, rekening 4310 en hoog tarief.</summary>
    private async Task Bevestig(Uitvoerder wie, Guid id, Guid document, bool nieuw = false)
    {
        var inkoop = app.Dienst<InkoopDienst>();
        var verwerking = (await inkoop.Verwerking(wie, id, document))!;
        var h = verwerking.Herkend!;
        var invoer = new Factuurinvoer(nieuw ? null : verwerking.Voorstel?.Leverancier?.Relatie.Code, nieuw ? new NieuweRelatie(h.Naam!, h.Iban, h.BtwNummer, h.KvkNummer) : null,
            h.Factuurnummer!, h.Factuurdatum!.Value, h.Factuurdatum.Value.AddDays(14), "Telefoon #maand",
            [new Factuurregel("", "4310", "I21", h.TotaalExclusief!.Value, h.TotaalBtw)]);
        var uitkomst = await inkoop.Boek(wie, id, document, invoer);
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
    }

    [Fact]
    public async Task Vijf_facturen_in_een_keer_ingelezen_na_twee_keer_zelf_boeken_gaat_de_rest_vanzelf()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        var bedragen = new[] { 20.65m, 21.90m, 20.65m, 21.00m, 20.65m };
        var gelezen = new List<Inleesuitkomst>();
        for (var i = 0; i < 5; i++)
        {
            gelezen.Add(await Lees(wie, id, $"TN-2026-{i + 1:000}", i + 1, bedragen[i]));
        }

        Assert.All(gelezen, g => Assert.Equal(Documentstatus.TeVerwerken, g.Status));
        var herkend = (await app.Dienst<InkoopDienst>().Verwerking(wie, id, gelezen[0].DocumentId!.Value))!.Herkend!;
        Assert.Equal(("Telecom Noord B.V.", Iban, "NL800000006B01", "TN-2026-001", 20.65m, 4.34m, 24.99m),
            (herkend.Naam, herkend.Iban, herkend.BtwNummer, herkend.Factuurnummer, herkend.TotaalExclusief, herkend.TotaalBtw, herkend.TotaalInclusief));

        // De eerste: nieuwe leverancier; de tweede herkent het pakket en stelt de vorige keuze voor.
        await Bevestig(wie, id, gelezen[0].DocumentId!.Value, nieuw: true);
        var tweede = (await app.Dienst<InkoopDienst>().Verwerking(wie, id, gelezen[1].DocumentId!.Value))!;
        Assert.Equal(("4310", "I21", false), (tweede.Voorstel!.Advies.Rekeningnummer, tweede.Voorstel.Advies.BtwCode, tweede.Voorstel.Automatisch));
        await Bevestig(wie, id, gelezen[1].DocumentId!.Value);

        var opnieuw = await app.Dienst<InkoopDienst>().VerwerkOpnieuw(wie, id);

        Assert.Equal(new Herverwerking(3, 3), opnieuw);
        Assert.Empty(await app.Dienst<InkoopDienst>().Lijst(wie, id, Documentstatus.TeVerwerken));
        var proef = await app.Dienst<GrootboekDienst>().Proefbalans(wie, id, 2026);
        Assert.Equal(bedragen.Sum(), proef.Regels.Single(r => r.Rekening.Nummer == "4310").Saldo);
        await using var db = await app.Dienst<Toegang>().Open(wie, id, Recht.Lezen);
        Assert.Equal(3, await db.AuditRegels.CountAsync(a => a.AdministratieId == id && a.Actie == AuditActies.AutomatischGeboekt));
        Assert.Equal("Telefoon maart", (await db.Boekingen.SingleAsync(b => b.Boekstuk == "2026-0003")).Omschrijving);
    }

    [Fact]
    public async Task Met_drempel_drie_gaat_de_vierde_factuur_bij_het_inlezen_vanzelf()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        await ZetDrempel(wie, id, 3);

        for (var i = 1; i <= 3; i++)
        {
            var gelezen = await Lees(wie, id, $"D3-{i}", i, 20.65m);
            Assert.Equal(Documentstatus.TeVerwerken, gelezen.Status);
            await Bevestig(wie, id, gelezen.DocumentId!.Value, nieuw: i == 1);
        }

        var vierde = await Lees(wie, id, "D3-4", 4, 21.50m);
        var vijfde = await Lees(wie, id, "D3-5", 5, 20.65m);

        Assert.Equal((Documentstatus.Geboekt, Documentstatus.Geboekt), (vierde.Status, vijfde.Status));
        Assert.Contains("automatisch geboekt", vierde.Melding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hoger_bedrag_ander_iban_of_ander_adres_komt_ter_controle_en_daarna_begint_de_telling_opnieuw()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        for (var i = 1; i <= 2; i++)
        {
            await Bevestig(wie, id, (await Lees(wie, id, $"AF-{i}", i, 20.65m)).DocumentId!.Value, nieuw: i == 1);
        }

        Assert.Equal(Documentstatus.Geboekt, (await Lees(wie, id, "AF-3", 3, 21.00m)).Status);

        // Vier keer zo duur: boven de bandbreedte (1,25 x het hoogste bevestigde bedrag).
        var duur = await Lees(wie, id, "AF-4", 4, 82.60m);
        Assert.Equal(Documentstatus.TeVerwerken, duur.Status);
        Assert.Contains("hoger", duur.Melding, StringComparison.OrdinalIgnoreCase);

        // Een ander IBAN (factuurfraude?) en een ander adres.
        var anderIban = await Lees(wie, id, "AF-5", 5, 20.65m, iban: "NL20INGB0001234567");
        Assert.Equal(Documentstatus.TeVerwerken, anderIban.Status);
        Assert.Contains("IBAN", anderIban.Melding, StringComparison.Ordinal);
        var anderAdres = await Lees(wie, id, "AF-6", 6, 20.65m, postcode: "1011 AB");
        Assert.Equal(Documentstatus.TeVerwerken, anderAdres.Status);
        Assert.Contains("adres", anderAdres.Melding, StringComparison.OrdinalIgnoreCase);

        // De dure factuur toch bevestigen: de telling begint opnieuw, de volgende gewone komt eerst ter controle.
        await Bevestig(wie, id, duur.DocumentId!.Value);
        var daarna = await Lees(wie, id, "AF-7", 7, 20.65m);
        Assert.Equal(Documentstatus.TeVerwerken, daarna.Status);
        await Bevestig(wie, id, daarna.DocumentId!.Value);
        Assert.Equal(Documentstatus.Geboekt, (await Lees(wie, id, "AF-8", 8, 20.65m)).Status);
    }

    [Fact]
    public async Task Dezelfde_factuur_opnieuw_gescand_wordt_als_dubbel_herkend()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Test BV");
        await Bevestig(wie, id, (await Lees(wie, id, "DUB-1", 1, 20.65m)).DocumentId!.Value, nieuw: true);

        // Een andere PDF (andere bytes) met hetzelfde factuurnummer.
        var opnieuw = await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(Telecom("DUB-1", Maand(1), 20.65m, postcode: "9711 AB")
            .Concat(" "u8.ToArray()).ToArray()), "scan.pdf", "application/pdf");

        Assert.Equal(Documentstatus.TeVerwerken, opnieuw.Status);
        Assert.Contains("al geboekt", opnieuw.Melding, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ZetDrempel(Uitvoerder wie, Guid id, int drempel)
    {
        var inrichting = app.Dienst<InrichtingDienst>();
        var huidig = await inrichting.Instellingen(wie, id);
        var uitkomst = await inrichting.WijzigInstellingen(wie, id, huidig with { Afwijkend = huidig.Afwijkend with { Drempel = drempel } });
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
    }
}
