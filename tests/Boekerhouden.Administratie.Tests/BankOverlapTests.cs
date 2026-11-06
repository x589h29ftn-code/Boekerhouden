using System.Text;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;

namespace Boekerhouden.Administratie.Tests;

/// <summary>
/// Afschriften die elkaar overlappen: eerst 1 t/m 10 maart, dan per ongeluk 8 t/m 20 maart. Uit hetzelfde formaat
/// herkent de sleutel de mutaties van 8 t/m 10 maart; uit een ander formaat (andere omschrijving) komen ze als mogelijk
/// dubbel binnen, worden ze niet automatisch geboekt en kan de gebruiker ze kiezen en verwijderen.
/// </summary>
public sealed class BankOverlapTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private const string Eigen = "NL20INGB0001234567";

    private static readonly Bankmutatie[] Maart =
    [
        new(new(2026, 3, 2), -25m, "Factuur BN-2026-0311", "NL63TRIO0212345678", "Belnet B.V."),
        new(new(2026, 3, 9), 1210m, "Factuur 20260001", "NL44RABO0123456789", "J. de Vries"),
        new(new(2026, 3, 10), -4.58m, "Webwinkel pas 4382", null, null),
        new(new(2026, 3, 16), -199.65m, "Betaling inkoop", "NL36INGB0007654321", "Kantoorhandel Jansen"),
        new(new(2026, 3, 20), 605m, "Factuur 20260002", "NL44RABO0123456789", "J. de Vries"),
    ];

    /// <summary>Een MT940 met de mutaties van <paramref name="van"/> t/m <paramref name="tot"/>; met <paramref name="anders"/> een andere omschrijving (ander formaat).</summary>
    private static MemoryStream Afschrift(DateOnly van, DateOnly tot, bool anders = false)
    {
        var begin = 5000m + Maart.Where(m => m.Datum < van).Sum(m => m.Bedrag);
        var mutaties = Maart.Where(m => m.Datum >= van && m.Datum <= tot).Select(m => anders ? m with { Omschrijving = m.Omschrijving.ToUpperInvariant() + " (PDF)" } : m).ToList();
        var afschrift = new Bankafschrift(Eigen, "1", "EUR", van.AddDays(-1), begin, tot, begin + mutaties.Sum(m => m.Bedrag), mutaties);
        return new MemoryStream(Encoding.UTF8.GetBytes(Mt940Schrijver.Schrijf(afschrift)));
    }

    [Fact]
    public async Task Hetzelfde_formaat_overlapt_de_bekende_mutaties_worden_overgeslagen_met_een_melding()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.Lees(wie, id, Afschrift(new(2026, 3, 1), new(2026, 3, 10)), "maart-1.mt940")).IsGelukt);

        var tweede = (await bank.Lees(wie, id, Afschrift(new(2026, 3, 8), new(2026, 3, 20)), "maart-2.mt940")).Waarde!;

        Assert.Equal((2, 2, 0), (tweede.Nieuw, tweede.Dubbel, tweede.MogelijkDubbel));
        var melding = Assert.Single(tweede.Meldingen);
        Assert.Contains("overlapt met maart-1.mt940 (28-02-2026 t/m 10-03-2026)", melding, StringComparison.Ordinal);
        Assert.Contains("2 mutaties waren al ingelezen en zijn overgeslagen", melding, StringComparison.Ordinal);
        Assert.Equal(5, (await bank.Lijst(wie, id, ookGeboekt: true)).Count);

        // Het saldo voor en na de boeking klopt ook bij het tweede afschrift, waar de eerste mutaties ontbreken.
        var jansen = (await bank.Lijst(wie, id)).Single(m => m.Bedrag == -199.65m);
        var saldo = (await bank.Verwerking(wie, id, jansen.Id))!.Saldo!;
        Assert.Equal((5000m - 25m + 1210m - 4.58m, 5000m - 25m + 1210m - 4.58m - 199.65m), (saldo.VoorBoeking, saldo.NaBoeking));
    }

    [Fact]
    public async Task Een_ander_formaat_geeft_mogelijk_dubbel_niet_automatisch_en_kiezen_en_verwijderen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.Lees(wie, id, Afschrift(new(2026, 3, 1), new(2026, 3, 10)), "maart.pdf.mt940")).IsGelukt);
        var eerst = await bank.Lijst(wie, id);
        // De mutaties van het eerste afschrift zijn geboekt (de pinbetaling niet): dan zou een dubbele er vanzelf weer bij komen.
        Assert.True((await bank.OpRekening(wie, id, [.. eerst.Where(m => m.TegenIban is not null).Select(m => m.Id)], "4300", null)).IsGelukt);

        var gelezen = (await bank.Lees(wie, id, Afschrift(new(2026, 3, 8), new(2026, 3, 20), anders: true), "maart.mt940")).Waarde!;

        Assert.Equal((4, 0, 2), (gelezen.Nieuw, gelezen.Dubbel, gelezen.MogelijkDubbel));
        Assert.Contains("2 mutaties lijken dubbel", Assert.Single(gelezen.Meldingen), StringComparison.Ordinal);
        var lijst = await bank.Lijst(wie, id);
        var dubbel = lijst.Where(m => m.MogelijkDubbel).ToList();
        Assert.Equal([1210m, -4.58m], dubbel.OrderBy(m => m.Datum).Select(m => m.Bedrag));
        Assert.All(dubbel, m => Assert.StartsWith("Mogelijk dubbel: op ", m.Melding, StringComparison.Ordinal));
        Assert.Contains("ingelezen uit maart.pdf.mt940", dubbel.Single(m => m.Bedrag == 1210m).Melding, StringComparison.Ordinal);

        // Opnieuw herkennen boekt ze ook niet.
        await bank.VerwerkOpnieuw(wie, id);
        Assert.Equal(2, (await bank.Lijst(wie, id)).Count(m => m.MogelijkDubbel));

        // Een geboekte mutatie kan niet weg; dan blijft alles staan.
        var geboekt = (await bank.Lijst(wie, id, ookGeboekt: true)).First(m => m.Status == Banktransactiestatus.Geboekt);
        Assert.False((await bank.Verwijder(wie, id, [geboekt.Id, dubbel[0].Id])).IsGelukt);
        Assert.Equal(2, (await bank.Lijst(wie, id)).Count(m => m.MogelijkDubbel));

        var weg = await bank.Verwijder(wie, id, [.. dubbel.Select(m => m.Id)]);

        Assert.Equal(2, weg.Waarde);
        Assert.DoesNotContain(await bank.Lijst(wie, id, ookGeboekt: true), m => dubbel.Any(d => d.Id == m.Id));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.BankmutatiesVerwijderd && r.Onderwerp == "2 mutaties");
        // Hetzelfde bestand nog eens: alles al ingelezen, ook de verwijderde.
        Assert.Equal((0, 4), ((await bank.Lees(wie, id, Afschrift(new(2026, 3, 8), new(2026, 3, 20), anders: true), "maart.mt940")).Waarde! is var w ? (w.Nieuw, w.Dubbel) : default));
    }

    [Fact]
    public async Task Niet_dubbel_dan_gewoon_verwerken()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(new(2026, 3, 1), new(2026, 3, 10)), "a.mt940");
        await bank.Lees(wie, id, Afschrift(new(2026, 3, 8), new(2026, 3, 20), anders: true), "b.mt940");
        var pin = (await bank.Lijst(wie, id)).Single(m => m.MogelijkDubbel && m.Bedrag == -4.58m);

        Assert.True((await bank.GeenDubbel(wie, id, pin.Id)).IsGelukt);

        var na = (await bank.Lijst(wie, id)).Single(m => m.Id == pin.Id);
        Assert.False(na.MogelijkDubbel);
        Assert.DoesNotContain("Mogelijk dubbel", na.Melding ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aansluitende_afschriften_geven_geen_melding()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(new(2026, 3, 1), new(2026, 3, 10)), "a.mt940");

        var tweede = (await bank.Lees(wie, id, Afschrift(new(2026, 3, 11), new(2026, 3, 20)), "b.mt940")).Waarde!;

        Assert.Equal((2, 0, 0), (tweede.Nieuw, tweede.Dubbel, tweede.MogelijkDubbel));
        Assert.Empty(tweede.Meldingen);
    }

    [Fact]
    public async Task Zonder_invoerrecht_niets_verwijderen_of_als_niet_dubbel_markeren()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, Afschrift(new(2026, 3, 1), new(2026, 3, 10)), "a.mt940");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        var buiten = await app.Gebruiker("Bo Buiten");
        var alle = (await bank.Lijst(wie, id)).Select(m => m.Id).ToList();

        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Verwijder(inzien, id, alle));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Verwijder(buiten, id, alle));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.GeenDubbel(inzien, id, alle[0]));
        Assert.Equal(3, (await bank.Lijst(wie, id)).Count);
    }
}
