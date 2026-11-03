using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Documenten.Tests;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Een rekeningafschrift dat alleen als PDF bestaat: omzetten naar MT940 en direct inlezen.</summary>
public sealed class BankPdfTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    private static MemoryStream Pdf(decimal? nieuwSaldo = null) => new(FictiefAfschrift.OktoberPdf(nieuwSaldo));

    [Fact]
    public async Task Omzetten_geeft_een_mt940_dat_hetzelfde_afschrift_bevat_en_leest_niets_in()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();

        var uitkomst = await bank.PdfNaarMt940(wie, id, Pdf(), "afschrift-oktober.pdf");

        Assert.True(uitkomst.IsGelukt, string.Join("; ", uitkomst.Fouten));
        Assert.Equal("afschrift-oktober.mt940", uitkomst.Waarde!.Bestandsnaam);
        Assert.Equal(FictiefAfschrift.Oktober.Length, uitkomst.Waarde.Afschrift.Mutaties.Count);
        var terug = Assert.Single(Bankbestand.Lees(new MemoryStream(uitkomst.Waarde.Mt940)));
        Assert.Equal(uitkomst.Waarde.Afschrift.Mutaties, terug.Mutaties);
        Assert.Empty(await bank.Lijst(wie, id, ookGeboekt: true));
    }

    [Fact]
    public async Task Een_pdf_afschrift_wordt_bij_het_inlezen_omgezet_en_ingelezen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();

        var gelezen = await bank.Lees(wie, id, Pdf(), "afschrift.pdf");

        Assert.True(gelezen.IsGelukt, string.Join("; ", gelezen.Fouten));
        Assert.Equal(FictiefAfschrift.Oktober.Length, gelezen.Waarde!.Nieuw);
        Assert.Contains(await bank.Lijst(wie, id, ookGeboekt: true), m => m.Bedrag == -199.65m && m.TegenNaam == "Kantoorhandel Jansen");
        Assert.Equal(0, (await bank.Lees(wie, id, Pdf(), "nog-eens.pdf")).Waarde!.Nieuw);
    }

    [Fact]
    public async Task Een_pdf_die_niet_sluit_of_geen_afschrift_is_wordt_geweigerd()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();

        Assert.Contains("sluit niet", string.Join(" ", (await bank.PdfNaarMt940(wie, id, Pdf(nieuwSaldo: 1m), "fout.pdf")).Fouten), StringComparison.Ordinal);
        Assert.Contains("geen PDF", string.Join(" ", (await bank.PdfNaarMt940(wie, id, new MemoryStream("geen pdf"u8.ToArray()), "x.txt")).Fouten), StringComparison.Ordinal);
        Assert.False((await bank.Lees(wie, id, Pdf(nieuwSaldo: 1m), "fout.pdf")).IsGelukt);
        Assert.Empty(await bank.Lijst(wie, id, ookGeboekt: true));
    }

    [Fact]
    public async Task Zonder_lidmaatschap_of_met_alleen_inzien_niet_omzetten()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var buiten = await app.Gebruiker("Buiten Staander");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        var bank = app.Dienst<BankDienst>();

        await Assert.ThrowsAsync<GeenToegangException>(() => bank.PdfNaarMt940(buiten, id, Pdf(), "a.pdf"));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.PdfNaarMt940(inzien, id, Pdf(), "a.pdf"));
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.Lees(inzien, id, Pdf(), "a.pdf"));
    }
}

/// <summary>Meer bankmutaties tegelijk op één rekening met een btw-code boeken.</summary>
public sealed class BankSelectieTests(AdministratieFixture app) : IClassFixture<AdministratieFixture>
{
    [Fact]
    public async Task Gekozen_mutaties_worden_elk_met_hun_eigen_omschrijving_op_de_rekening_geboekt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        Assert.True((await bank.Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        var lijst = await bank.Lijst(wie, id);
        var betalingen = lijst.Where(m => m.Bedrag < 0).Select(m => m.Id).ToList();

        var uitkomst = await bank.OpRekening(wie, id, betalingen, "4300", "I21");

        Assert.Equal(2, uitkomst.Waarde);
        Assert.Equal([1210m], (await bank.Lijst(wie, id)).Select(m => m.Bedrag));
        var kaart = await app.Dienst<GrootboekDienst>().Kaart(wie, id, "4300", new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(2, kaart.Mutaties.Count);
        Assert.Contains(kaart.Mutaties, r => r.Omschrijving.Contains("Kantoorhandel Jansen - Betaling inkoop", StringComparison.Ordinal));
        Assert.True(Math.Abs((25m + 199.65m) / 1.21m - kaart.Eindsaldo) <= 0.02m, $"{kaart.Eindsaldo}");

        // Al geboekt: niets meer te doen; zonder rekening een fout.
        Assert.False((await bank.OpRekening(wie, id, betalingen, "4300", "I21")).IsGelukt);
        Assert.False((await bank.OpRekening(wie, id, [lijst.Single(m => m.Bedrag > 0).Id], "", null)).IsGelukt);
    }

    [Fact]
    public async Task Met_alleen_inzien_niet_meer_tegelijk_boeken()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie();
        var bank = app.Dienst<BankDienst>();
        await bank.Lees(wie, id, BankTests.Afschrift(), "ing.mt940");
        var inzien = await app.Gebruiker("Inge Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);

        var alle = (await bank.Lijst(wie, id)).Select(m => m.Id).ToList();
        await Assert.ThrowsAsync<GeenToegangException>(() => bank.OpRekening(inzien, id, alle, "4300", null));
        Assert.Equal(3, (await bank.Lijst(wie, id)).Count);
    }
}
