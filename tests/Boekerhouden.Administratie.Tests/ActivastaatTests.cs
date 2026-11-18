using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Activa;
using ClosedXML.Excel;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Een staat vaste activa van vorig jaar inlezen (Excel) en overnemen, met de aansluiting op het grootboek.</summary>
public sealed class ActivastaatTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    /// <summary>Een verzonnen staat over 2025: inventaris (een oven en een volledig afgeschreven kassa) en een bestelbus.</summary>
    private static MemoryStream Staat()
    {
        using var boek = new XLWorkbook();
        var blad = boek.AddWorksheet("Staat");
        string[] kop = ["Omschrijving", "Datum", "Aanschaffingen t/m 31-12-2025", "Afschrijvingen tot 01-01-2025", "Afschrijvingen 2025", "Afschrijvingen t/m 31-12-2025", "Boekwaarde per 31-12-2025", "Restwaarde", "%"];
        for (var c = 0; c < kop.Length; c++)
        {
            blad.Cell(1, c + 1).Value = kop[c];
        }

        void Regel(int r, string omschrijving, DateTime? datum, decimal aanschaf, decimal begin, decimal jaar, decimal rest, decimal procent)
        {
            blad.Cell(r, 1).Value = omschrijving;
            if (datum is { } d)
            {
                blad.Cell(r, 2).Value = d;
            }

            (blad.Cell(r, 3).Value, blad.Cell(r, 4).Value, blad.Cell(r, 5).Value, blad.Cell(r, 6).Value, blad.Cell(r, 7).Value, blad.Cell(r, 8).Value, blad.Cell(r, 9).Value) =
                (aanschaf, begin, jaar, begin + jaar, aanschaf - begin - jaar, rest, procent);
        }

        blad.Cell(2, 1).Value = "Inventaris";
        Regel(3, "Oven", new DateTime(2022, 2, 11), 3000m, 1450m, 600m, 0m, 20m);
        Regel(4, "Kassa", null, 800m, 800m, 0m, 0m, 20m);
        blad.Cell(5, 1).Value = "Vervoermiddelen";
        Regel(6, "Bestelbus", new DateTime(2024, 7, 1), 24000m, 0m, 2200m, 2000m, 20m);
        blad.Cell(7, 1).Value = "Totaal";
        (blad.Cell(7, 3).Value, blad.Cell(7, 6).Value, blad.Cell(7, 7).Value) = (27800m, 5050m, 22750m);
        var stream = new MemoryStream();
        boek.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Een_staat_inlezen_aansluiten_en_overnemen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Staat B.V.");
        var dienst = app.Dienst<ActivastaatDienst>();

        var voorstel = (await dienst.Lees(wie, id, Staat(), "staat-2025.xlsx")).Waarde!;

        Assert.True(voorstel.Staat.Sluit, string.Join(" | ", voorstel.Staat.Signalen));
        Assert.Equal(new DateOnly(2026, 1, 1), voorstel.Begin);
        Assert.Equal([new Staatgroepkeuze("Inventaris", "0200", "0210", voorstel.Groepen[0].Kostenrekening), new Staatgroepkeuze("Vervoermiddelen", "0300", "0310", voorstel.Groepen[1].Kostenrekening)], voorstel.Groepen);
        Assert.All(voorstel.Groepen, g => Assert.NotNull(g.Kostenrekening));

        // Nog geen beginbalans: de staat sluit niet op het grootboek.
        var voor = await dienst.Aansluiting(wie, id, voorstel, voorstel.Groepen);
        Assert.Equal([("0200", 3800m, 0m), ("0210", -2850m, 0m), ("0300", 24000m, 0m), ("0310", -2200m, 0m)], voor.Select(a => (a.Rekening, a.Staat, a.Grootboek)));

        // Met de beginbalans per 1 januari 2026 sluit het.
        var beginbalans = new Boekhouding.Grootboek.Journaalpost("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans 2026",
            [new("0200", 3800m), new("0210", -2850m), new("0300", 24000m), new("0310", -2200m), new("0500", -22750m)]);
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, beginbalans)).IsGelukt);
        Assert.All(await dienst.Aansluiting(wie, id, voorstel, voorstel.Groepen), a => Assert.Equal(0m, a.Verschil));

        // Overnemen: de kassa zonder datum krijgt de ingevulde datum.
        var kassa = voorstel.Staat.Regels.Single(r => r.Omschrijving == "Kassa").Rij;
        var uitkomst = await dienst.NeemOver(wie, id, voorstel, voorstel.Groepen, new Dictionary<int, DateOnly> { [kassa] = new(2020, 5, 1) });
        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal(3, uitkomst.Waarde);

        var lijst = await app.Dienst<ActivaDienst>().Lijst(wie, id);
        Assert.Equal([("Oven", 950m), ("Kassa", 0m), ("Bestelbus", 21800m)], lijst.OrderBy(a => a.Nummer).Select(a => (a.Omschrijving, a.Boekwaarde)));
        var bus = (await app.Dienst<ActivaDienst>().Open(wie, id, lijst.Single(a => a.Omschrijving == "Bestelbus").Id))!.Invoer;
        Assert.Equal((new DateOnly(2024, 7, 1), 2000m, 2200m, 54, "0300", "0310"), (bus.Aanschafdatum, bus.Restwaarde, bus.AfgeschrevenVoorBegin, bus.Looptijd, bus.Activarekening, bus.Afschrijvingsrekening));
        Assert.Equal(new DateOnly(2020, 5, 1), (await app.Dienst<ActivaDienst>().Open(wie, id, lijst.Single(a => a.Omschrijving == "Kassa").Id))!.Invoer.Aanschafdatum);

        // Nog een keer inlezen: alles bestaat al en wordt overgeslagen.
        var opnieuw = (await dienst.Lees(wie, id, Staat(), "staat-2025.xlsx")).Waarde!;
        Assert.Equal(3, opnieuw.Bestaand.Count);
        Assert.Equal(0, (await dienst.NeemOver(wie, id, opnieuw, opnieuw.Groepen)).Waarde);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.ActivastaatIngelezen && r.Onderwerp == "staat-2025.xlsx");
    }

    [Fact]
    public async Task Zonder_rekeningen_of_als_geen_staat_een_melding_en_alleen_het_kantoor()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Staat Melding B.V.");
        var dienst = app.Dienst<ActivastaatDienst>();
        var voorstel = (await dienst.Lees(wie, id, Staat(), "staat.xlsx")).Waarde!;

        var zonder = await dienst.NeemOver(wie, id, voorstel, [.. voorstel.Groepen.Select(g => g with { Kostenrekening = null })]);
        Assert.Contains("Kies bij Inventaris", zonder.Fouten[0], StringComparison.Ordinal);
        Assert.Empty(await app.Dienst<ActivaDienst>().Lijst(wie, id));
        Assert.Contains("geen Excel-bestand", (await dienst.Lees(wie, id, new MemoryStream("hallo"u8.ToArray()), "x.txt")).Fouten[0], StringComparison.Ordinal);

        var inzien = await app.Gebruiker("Inge Staat");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Lees(inzien, id, Staat(), "staat.xlsx"));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.NeemOver(inzien, id, voorstel, voorstel.Groepen));
    }

    [Fact]
    public async Task Het_sjabloon_downloaden_invullen_en_overnemen_met_rekeningen_en_privegebruik_per_actief()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Sjabloon B.V.");
        var dienst = app.Dienst<ActivastaatDienst>();
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, new Boekhouding.Grootboek.Journaalpost("MEM", "2026-0001", new(2026, 1, 1), "Beginbalans 2026",
            [new("0300", 40000m), new("0310", -15750m), new("0500", -24250m)]))).IsGelukt);

        var (inhoud, jaar) = await dienst.Sjabloon(wie, id);
        Assert.Equal(2025, jaar);
        using var boek = new XLWorkbook(new MemoryStream(inhoud));
        Assert.Contains(boek.Worksheet("Rekeningen").RowsUsed().Select(r => r.Cell(1).GetString()), n => n == "0300");
        var blad = boek.Worksheet("Vaste activa");
        (blad.Cell(2, 1).Value, blad.Cell(2, 2).Value, blad.Cell(2, 3).Value, blad.Cell(2, 4).Value, blad.Cell(2, 5).Value, blad.Cell(2, 6).Value, blad.Cell(2, 7).Value) =
            ("Personenauto", "Auto's", new DateTime(2023, 3, 1), 40000, 5000, 20, 15750);
        blad.Cell(2, 9).SetValue("0300");
        blad.Cell(2, 10).SetValue("0310");
        (blad.Cell(2, 12).Value, blad.Cell(2, 13).Value, blad.Cell(2, 14).Value) = ("ja", 45000, "Lease afgelost");
        var ingevuld = new MemoryStream();
        boek.SaveAs(ingevuld);
        ingevuld.Position = 0;

        var voorstel = (await dienst.Lees(wie, id, ingevuld, "sjabloon.xlsx")).Waarde!;

        // De rekeningen uit het sjabloon; de kostenrekening (leeg) volgt uit RGS bij de activarekening.
        var keuze = Assert.Single(voorstel.Groepen);
        Assert.Equal(("Auto's", "0300", "0310"), (keuze.Groep, keuze.Activarekening, keuze.Afschrijvingsrekening));
        Assert.NotNull(keuze.Kostenrekening);
        Assert.All(await dienst.Aansluiting(wie, id, voorstel, voorstel.Groepen), a => Assert.Equal(0m, a.Verschil));
        Assert.True((await dienst.NeemOver(wie, id, voorstel, voorstel.Groepen)).IsGelukt);

        var auto = (await app.Dienst<ActivaDienst>().Lijst(wie, id)).Single();
        var invoer = (await app.Dienst<ActivaDienst>().Open(wie, id, auto.Id))!.Invoer;
        Assert.Equal((24250m, Privegebruik.JaForfait, (decimal?)45000m, 15750m), (auto.Boekwaarde, invoer.Privegebruik, invoer.Cataloguswaarde, invoer.AfgeschrevenVoorBegin));
        Assert.StartsWith("Lease afgelost; Overgenomen van de staat sjabloon.xlsx", invoer.Notitie, StringComparison.Ordinal);
    }
}
