using Boekerhouden.Administratie.Data;
using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;
using Microsoft.Extensions.DependencyInjection;

namespace Boekerhouden.Administratie.Tests;

/// <summary>Het administratiepakket op een vaste dag (15 oktober 2026), zodat de btw-periode en de kerncijfers vastliggen.</summary>
public sealed class VasteDagFixture : AdministratieFixture
{
    public static readonly DateTimeOffset Nu = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    protected override void Diensten(IServiceCollection diensten) => diensten.AddSingleton<TimeProvider>(new VasteKlok());

    private sealed class VasteKlok : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Nu;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

/// <summary>
/// Het dashboard: de indeling per gebruiker (standaard, bewaren, onbekende widgets overslaan) en de cijfers van de
/// widgets, gelijk aan wat de andere schermen laten zien.
/// </summary>
public sealed class DashboardDienstTests(VasteDagFixture app) : IClassFixture<VasteDagFixture>
{
    private static Journaalpost Memoriaal(string boekstuk, DateOnly datum, params (string Rekening, decimal Bedrag)[] regels) =>
        new("MEM", boekstuk, datum, "Memoriaal", [.. regels.Select(r => new Journaalregel(r.Rekening, r.Bedrag))]);

    private static readonly IReadOnlyList<Widgetkeuze> Alles = [.. Enum.GetValues<Widget>().Select(w => new Widgetkeuze(w))];

    [Fact]
    public async Task Na_een_auditfile_tellen_bank_en_kas_en_liquiditeit_dezelfde_rekeningen_en_de_grafiek_toont_vorig_jaar()
    {
        // Een administratie die tot vorig jaar loopt, als auditfile ingelezen: de bank heeft daarna de functie bank maar
        // (nog) geen bankdagboek met die rekening.
        var wie = await app.Beheerder();
        var bron = await app.Administratie("Dashboard Auditfile Bron B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        Assert.True((await app.Dienst<BankDienst>().VoegBankToe(wie, bron, "SPA", "Spaarrekening", null, "1110")).IsGelukt);
        foreach (var post in new[]
                 {
                     Memoriaal("2025-0001", new(2025, 1, 1), ("1100", 5000m), ("1110", 2500m), ("0500", -7500m)),
                     Memoriaal("2025-0002", new(2025, 6, 10), ("1100", 1000m), ("8000", -1000m)),
                     Memoriaal("2025-0003", new(2025, 9, 1), ("4100", 300m), ("1100", -300m)),
                 })
        {
            Assert.True((await grootboek.Boek(wie, bron, post)).IsGelukt);
        }

        var auditfiles = app.Dienst<AuditfileDienst>();
        var bestand = await auditfiles.Maak(wie, bron, 2025);
        var ingelezen = await auditfiles.LeesIn(wie, new MemoryStream(bestand.Inhoud), bestand.Bestandsnaam, Ondernemingsvorm.BV);
        Assert.True(ingelezen.IsGelukt, string.Join("; ", ingelezen.Fouten));
        var id = ingelezen.Waarde!.Administratie;

        var dashboard = await app.Dienst<DashboardDienst>().Laad(wie, id, [new(Widget.Liquide), new(Widget.Liquiditeit), new(Widget.Maandgrafiek)]);
        Assert.Equal(8200m, dashboard.Liquide!.Sum(r => r.Saldo));
        Assert.Equal(8200m, dashboard.Liquiditeit![^1].Saldo);
        Assert.Equal(["1100", "1110"], dashboard.Liquide!.Select(r => r.Rekening).Order(StringComparer.Ordinal));

        // In 2026 is er nog niets geboekt: de grafiek toont 2025 in plaats van een lege grafiek.
        var (jaar, maanden) = dashboard.Maandgrafiek!.Value;
        Assert.Equal(2025, jaar);
        Assert.Equal(1000m, maanden.Sum(m => m.Omzet));
    }

    [Fact]
    public async Task Bank_btw_maandgrafiek_en_kerncijfers()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Cijfers B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        Journaalpost[] posten =
        [
            Memoriaal("2025-0001", new(2025, 1, 1), ("1100", 5000m), ("0500", -5000m)),
            Memoriaal("2025-0002", new(2025, 3, 10), ("1100", 1000m), ("8000", -1000m)),
            Memoriaal("2025-0003", new(2025, 11, 1), ("1100", 500m), ("8000", -500m)),
            Memoriaal("2026-0001", new(2026, 2, 10), ("1300", 1210m), ("8000", -1000m), ("1510", -210m)),
            Memoriaal("2026-0002", new(2026, 2, 20), ("7000", 400m), ("1500", 84m), ("1600", -484m)),
            Memoriaal("2026-0003", new(2026, 7, 20), ("1510", 210m), ("1500", -84m), ("1100", -126m)),
            Memoriaal("2026-0004", new(2026, 8, 5), ("1100", 2420m), ("8000", -2000m), ("1510", -420m)),
            Memoriaal("2026-0005", new(2026, 8, 6), ("4100", 300m), ("1100", -300m)),
            Memoriaal("2026-0006", new(2026, 10, 2), ("1100", 1210m), ("8000", -1000m), ("1510", -210m)),
            Memoriaal("2026-0007", new(2026, 12, 1), ("1100", 1000m), ("8000", -1000m)),
        ];
        foreach (var post in posten)
        {
            Assert.True((await grootboek.Boek(wie, id, post)).IsGelukt);
        }

        var dienst = app.Dienst<DashboardDienst>();
        var dashboard = await dienst.Laad(wie, id, Alles);

        Assert.Equal(new DateOnly(2026, 10, 15), dashboard.Vandaag);
        // Per vandaag: de boeking van 1 december telt nog niet mee.
        var bank = Assert.Single(dashboard.Liquide!);
        Assert.Equal(("BNK", "1100", 9704m, false), (bank.Dagboek, bank.Rekening, bank.Saldo, bank.Afschrift.HasValue));

        // Liquiditeit: de laatste twaalf maanden (november 2025 tot en met oktober 2026, die tot vandaag), cumulatief.
        var liquiditeit = dashboard.Liquiditeit!;
        Assert.Equal(12, liquiditeit.Count);
        Assert.Equal((new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 30), 6500m), (liquiditeit[0].Maand, liquiditeit[0].Tot, liquiditeit[0].Saldo));
        Assert.Equal([6500m, 6500m, 6500m, 6500m, 6500m, 6500m, 6500m, 6500m, 6374m, 8494m, 8494m, 9704m], liquiditeit.Select(m => m.Saldo));
        Assert.Equal((new DateOnly(2026, 10, 15), "Bank", 9704m), (liquiditeit[11].Tot, liquiditeit[11].PerRekening.Single().Naam, liquiditeit[11].PerRekening.Single().Saldo));

        // Btw t/m het 3e kwartaal: 210 + 420 af te dragen, het 1e kwartaal (210 - 84) in juli betaald; in oktober 210 erbij.
        var btw = dashboard.Btw!;
        Assert.Equal(("3e kwartaal 2026", 420m, "4e kwartaal 2026", 210m), (btw.Afgelopen.Naam, btw.TeBetalen, btw.Lopend.Naam, btw.Lopende));
        var perMaand = await dienst.Laad(wie, id, [new Widgetkeuze(Widget.Btw, Widgetkeuze.Maand)]);
        Assert.Equal(("september 2026", 420m, 210m), (perMaand.Btw!.Afgelopen.Naam, perMaand.Btw.TeBetalen, perMaand.Btw.Lopende));

        var (jaar, maanden) = dashboard.Maandgrafiek!.Value;
        Assert.Equal((2026, 12), (jaar, maanden.Count));
        Assert.Equal((1000m, 400m, 600m), (maanden[1].Omzet, maanden[1].Inkoop, maanden[1].Resultaat));
        Assert.Equal((2000m, 0m, 300m, 1700m), (maanden[7].Omzet, maanden[7].Inkoop, maanden[7].OverigeKosten, maanden[7].Resultaat));
        Assert.Equal(1000m, maanden[11].Omzet);

        // Kerncijfers t/m vandaag naast dezelfde periode vorig jaar (de omzet van november 2025 telt niet mee).
        var (dit, vorig) = dashboard.Kerncijfers!.Value;
        Assert.Equal((new DateOnly(2026, 10, 15), 4000m, 700m, 3300m), (dit.Tot, dit.Omzet, dit.Kosten, dit.Resultaat));
        Assert.Equal((new DateOnly(2025, 10, 15), 1000m, 0m, 1000m), (vorig.Tot, vorig.Omzet, vorig.Kosten, vorig.Resultaat));

        Assert.Equal((0, 0), dashboard.Verkoop!.Value);
        Assert.Equal((0, 0, (DateOnly?)null), dashboard.Bankmutaties!.Value);
        Assert.Equal(new Openstaand(0, 0m, 0, 0m), dashboard.Debiteuren);
    }

    [Fact]
    public async Task Kerncijfers_vanaf_vandaag_ook_als_er_dit_jaar_nog_niets_is_geboekt_de_grafiek_dan_vorig_jaar()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Vorig Jaar B.V.");
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, Memoriaal("2025-0001", new(2025, 6, 1), ("1100", 800m), ("8000", -800m)))).IsGelukt);

        var dashboard = await app.Dienst<DashboardDienst>().Laad(wie, id, Alles);

        // De grafiek is in 2026 nog leeg: dan die van 2025 (met het jaar in de titel), niet een lege grafiek.
        Assert.Equal(2025, dashboard.Maandgrafiek!.Value.Jaar);
        Assert.Equal(800m, dashboard.Maandgrafiek.Value.Maanden.Sum(m => m.Omzet));
        var (dit, vorig) = dashboard.Kerncijfers!.Value;
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 15), 0m), (dit.Van, dit.Tot, dit.Omzet));
        Assert.Equal((new DateOnly(2025, 10, 15), 800m), (vorig.Tot, vorig.Omzet));
        Assert.All(dashboard.Liquiditeit!, m => Assert.Equal(800m, m.Saldo));
    }

    [Fact]
    public async Task Brutomarge_per_margegroep_tegen_vorig_jaar_en_per_kwartaal()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Marge B.V.");
        var grootboek = app.Dienst<GrootboekDienst>();
        foreach (var (rekening, groep) in new[] { ("8010", "Product X"), ("7000", "Product X"), ("8000", "Diensten") })
        {
            Assert.True((await grootboek.ZetMargegroep(wie, id, rekening, groep)).IsGelukt);
        }

        Journaalpost[] posten =
        [
            Memoriaal("2025-0001", new(2025, 3, 10), ("1300", 1000m), ("8010", -1000m)),
            Memoriaal("2025-0002", new(2025, 3, 11), ("7000", 600m), ("1600", -600m)),
            Memoriaal("2025-0003", new(2025, 11, 1), ("1300", 500m), ("8010", -500m)),
            Memoriaal("2026-0001", new(2026, 2, 10), ("1300", 2000m), ("8010", -2000m)),
            Memoriaal("2026-0002", new(2026, 2, 11), ("7000", 1200m), ("1600", -1200m)),
            Memoriaal("2026-0003", new(2026, 5, 1), ("1300", 300m), ("8000", -300m)),
            Memoriaal("2026-0004", new(2026, 8, 1), ("1300", 1000m), ("8010", -1000m)),
            Memoriaal("2026-0005", new(2026, 8, 2), ("7000", 500m), ("1600", -500m)),
            Memoriaal("2026-0006", new(2026, 10, 2), ("1300", 400m), ("8010", -400m)),
            Memoriaal("2026-0007", new(2026, 11, 2), ("1300", 900m), ("8010", -900m)),
        ];
        foreach (var post in posten)
        {
            Assert.True((await grootboek.Boek(wie, id, post)).IsGelukt);
        }

        var marge = (await app.Dienst<DashboardDienst>().Laad(wie, id, [new Widgetkeuze(Widget.Marge)])).Marge!;

        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 15), new DateOnly(2025, 10, 15)), (marge.Van, marge.Tot, marge.VorigTot));
        Assert.Equal(["Diensten", "Product X"], marge.Groepen.Select(g => g.Groep));
        var x = marge.Groepen[1];
        Assert.Equal((3400m, 1700m, 1700m, 50.0m), (x.Dit.Omzet, x.Dit.Inkoop, x.Dit.Brutomarge, x.Dit.Percentage));
        Assert.Equal((1000m, 600m, 40.0m, 1300m), (x.Vorig.Omzet, x.Vorig.Inkoop, x.Vorig.Percentage, x.Afwijking));
        Assert.Equal([800m, 0m, 500m, 400m], x.Kwartalen.Select(k => k.Brutomarge));
        Assert.Equal([300m, 0m], marge.Groepen[0].Kwartalen.Skip(1).Take(2).Select(k => k.Omzet));
        Assert.Equal((3700m, 1700m, 2000m), (marge.Totaal.Dit.Omzet, marge.Totaal.Dit.Inkoop, marge.Totaal.Dit.Brutomarge));
        Assert.Equal("Product X", (await grootboek.Schema(wie, id)).Zoek("7000")!.Margegroep);
    }

    [Fact]
    public async Task Een_margegroep_alleen_op_resultaatrekeningen_en_met_het_recht_boekhouden()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Margegroep B.V.");
        var ondernemer = await app.Gebruiker("Otto Ondernemer");
        await app.Dienst<AdministratieDienst>().ZetLid(id, ondernemer.Id!.Value, Lidmaatschapsrol.Ondernemer, wie);
        var grootboek = app.Dienst<GrootboekDienst>();

        Assert.Contains("balansrekening", (await grootboek.ZetMargegroep(wie, id, "1100", "Product X")).Fouten[0], StringComparison.Ordinal);
        Assert.Contains("te lang", (await grootboek.ZetMargegroep(wie, id, "8000", new string('x', 101))).Fouten[0], StringComparison.Ordinal);
        Assert.Equal(["Rekening 9999 bestaat niet."], (await grootboek.ZetMargegroep(wie, id, "9999", "X")).Fouten);
        await Assert.ThrowsAsync<GeenToegangException>(() => grootboek.ZetMargegroep(ondernemer, id, "8000", "X"));

        Assert.True((await grootboek.ZetMargegroep(wie, id, "8000", "  Advies ")).IsGelukt);
        Assert.Equal("Advies", (await grootboek.Schema(wie, id)).Zoek("8000")!.Margegroep);
        Assert.True((await grootboek.ZetMargegroep(wie, id, "8000", " ")).IsGelukt);
        Assert.Null((await grootboek.Schema(wie, id)).Zoek("8000")!.Margegroep);
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.RekeningGewijzigd && r.Details == "margegroep - → Advies");
    }

    [Fact]
    public async Task Te_verwerken_en_openstaand_gelijk_aan_de_andere_schermen()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Werk B.V.");
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        await app.Dienst<InkoopDienst>().Lees(wie, id, new MemoryStream(InkoopTests.Belnet()), "belnet.xml", null);

        var dashboard = await app.Dienst<DashboardDienst>().Laad(wie, id, Alles);

        var mutaties = await app.Dienst<BankDienst>().Lijst(wie, id);
        Assert.Equal(mutaties.Count(m => m.Status == Banktransactiestatus.TeVerwerken), dashboard.Bankmutaties!.Value.Aantal);
        var documenten = await app.Dienst<InkoopDienst>().Lijst(wie, id, Documentstatus.TeVerwerken);
        Assert.Equal((documenten.Count, documenten.Sum(d => d.Totaal ?? 0m)), dashboard.Inkoop!.Value);
        var stand = Assert.Single(await app.Dienst<BankDienst>().Stand(wie, id));
        var bank = Assert.Single(dashboard.Liquide!, r => r.Dagboek == stand.Dagboek);
        Assert.Equal((stand.Tot, stand.Saldo), (bank.Afschrift?.Datum, bank.Afschrift?.Saldo));
        var posten = await app.Dienst<BankDienst>().OpenPosten(wie, id);
        Assert.Equal(posten.Count(p => p.Soort == Boekhouding.Facturen.Factuursoort.Inkoop), dashboard.Crediteuren!.Aantal);
        Assert.Equal(Math.Abs(posten.Where(p => p.Soort == Boekhouding.Facturen.Factuursoort.Inkoop).Sum(p => p.Openstaand)), dashboard.Crediteuren.Totaal);
        var aansluitingen = await app.Dienst<AansluitingDienst>().Aansluitingen(wie, id);
        Assert.Equal((aansluitingen.Count, aansluitingen.Count(a => !a.Sluit)), (dashboard.Aansluitingen!.Value.Totaal, dashboard.Aansluitingen.Value.SluitenNiet.Count));
    }

    [Fact]
    public async Task Indeling_per_gebruiker_met_standaard_en_alleen_bekende_widgets()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Indeling B.V.");
        var inzien = await app.Gebruiker("Ida Inzien");
        await app.Dienst<AdministratieDienst>().ZetLid(id, inzien.Id!.Value, Lidmaatschapsrol.Inzien, wie);
        var buiten = await app.Gebruiker("Bart Buiten");
        var dienst = app.Dienst<DashboardDienst>();

        Assert.Equal([.. DashboardDienst.Standaard, new(Widget.Vennootschapsbelasting)], await dienst.Indeling(wie, id));

        IReadOnlyList<Widgetkeuze> eigen = [new(Widget.Maandgrafiek, Widgetkeuze.Kosten), new(Widget.Btw, Widgetkeuze.Maand), new(Widget.Liquide)];
        Assert.True((await dienst.BewaarIndeling(inzien, id, eigen)).IsGelukt);

        Assert.Equal(eigen, await dienst.Indeling(inzien, id));
        Assert.Equal([.. DashboardDienst.Standaard, new(Widget.Vennootschapsbelasting)], await dienst.Indeling(wie, id));
        Assert.False((await dienst.BewaarIndeling(wie, id, [new(Widget.Btw), new(Widget.Btw, Widgetkeuze.Maand)])).IsGelukt);
        Assert.False((await dienst.BewaarIndeling(Uitvoerder.Systeem, id, eigen)).IsGelukt);
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Indeling(buiten, id));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.BewaarIndeling(buiten, id, eigen));
        await Assert.ThrowsAsync<GeenToegangException>(() => dienst.Laad(buiten, id, eigen));
        Assert.Contains(await app.Dienst<AuditDienst>().Recent(), r => r.Actie == AuditActies.DashboardGewijzigd && r.Details == "maandgrafiek:kosten,btw:maand,liquide");

        // Leeg mag (dan staat er niets).
        Assert.True((await dienst.BewaarIndeling(inzien, id, [])).IsGelukt);
        Assert.Empty(await dienst.Indeling(inzien, id));
    }

    [Fact]
    public void Een_opgeslagen_indeling_lezen_slaat_onbekende_en_dubbele_widgets_over()
    {
        var gelezen = Widgetkeuze.Lees("liquide, verdwenen ,btw:jaar,BTW:maand,LIQUIDE,maandgrafiek:kosten,42");

        Assert.Equal([new Widgetkeuze(Widget.Liquide), new Widgetkeuze(Widget.Btw, Widgetkeuze.Kwartaal), new Widgetkeuze(Widget.Maandgrafiek, Widgetkeuze.Kosten)], gelezen);
        Assert.Equal("liquide,btw:kwartaal,maandgrafiek:kosten", Widgetkeuze.Schrijf(gelezen));
        Assert.Empty(Widgetkeuze.Lees(null));
    }

    [Fact]
    public async Task Aan_de_slag_vinkt_de_eerste_stappen_af_en_verdwijnt_als_alles_gedaan_is()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Aan De Slag B.V.");
        var dashboard = app.Dienst<DashboardDienst>();

        var stappen = await dashboard.Startstappen(wie, id);
        Assert.Equal(["Gegevens van de onderneming", "Eigen bankrekening", "Beginbalans", "Eerste bankafschrift", "Eerste factuur"], stappen.Select(s => s.Naam));
        Assert.All(stappen, s => Assert.False(s.Klaar && s.Naam == "Beginbalans"));

        // Een afschrift inlezen vinkt die stap af (het IBAN van het afschrift komt bij het bankdagboek).
        Assert.True((await app.Dienst<BankDienst>().Lees(wie, id, BankTests.Afschrift(), "ing.mt940")).IsGelukt);
        stappen = await dashboard.Startstappen(wie, id);
        Assert.True(stappen.Single(s => s.Naam == "Eerste bankafschrift").Klaar);
        Assert.True(stappen.Single(s => s.Naam == "Eigen bankrekening").Klaar);
        Assert.False(stappen.Single(s => s.Naam == "Beginbalans").Klaar);

        // Wie geen lid is, krijgt niets.
        var buiten = await app.Gebruiker("Bert Buiten Dashboard");
        await Assert.ThrowsAsync<GeenToegangException>(() => dashboard.Startstappen(buiten, id));
    }

    [Fact]
    public async Task Widgets_van_grootte_veranderen_in_het_raster()
    {
        // Lezen en schrijven: zonder grootte de standaard (oude indelingen), met grootte achter een tilde.
        Assert.Equal((1, 1), Grootte(Assert.Single(Widgetkeuze.Lees("btw:maand"))));
        Assert.Equal((2, 1), Grootte(Assert.Single(Widgetkeuze.Lees("maandgrafiek:inkoop"))));
        var groot = Assert.Single(Widgetkeuze.Lees("maandgrafiek:kosten~4x2"));
        Assert.Equal((4, 2, Widgetkeuze.Kosten), (groot.GeldigeBreedte, groot.GeldigeHoogte, groot.Geldig));
        Assert.Equal("maandgrafiek:kosten~4x2", groot.ToString());
        Assert.Equal("btw:maand", new Widgetkeuze(Widget.Btw, Widgetkeuze.Maand, 1, 1).ToString());
        Assert.Equal((1, 1), Grootte(Assert.Single(Widgetkeuze.Lees("btw~9x7"))));

        var id = await app.Administratie();
        var wie = await app.Beheerder();
        var dienst = app.Dienst<DashboardDienst>();
        Assert.True((await dienst.BewaarIndeling(wie, id, [new(Widget.Btw, null, 3, 2), new(Widget.Liquide)])).IsGelukt);
        var terug = await dienst.Indeling(wie, id);
        Assert.Equal([(3, 2), (1, 1)], terug.Select(Grootte));
        Assert.False((await dienst.BewaarIndeling(wie, id, [new(Widget.Btw, null, 5, 1)])).IsGelukt);
        Assert.False((await dienst.BewaarIndeling(wie, id, [new(Widget.Btw, null, 1, 3)])).IsGelukt);
    }

    private static (int, int) Grootte(Widgetkeuze k) => (k.GeldigeBreedte, k.GeldigeHoogte);
}
