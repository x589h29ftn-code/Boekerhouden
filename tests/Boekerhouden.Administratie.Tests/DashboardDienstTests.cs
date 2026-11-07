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
    public async Task Altijd_vanaf_vandaag_ook_als_er_dit_jaar_nog_niets_is_geboekt()
    {
        var wie = await app.Beheerder();
        var id = await app.Administratie("Dashboard Vorig Jaar B.V.");
        Assert.True((await app.Dienst<GrootboekDienst>().Boek(wie, id, Memoriaal("2025-0001", new(2025, 6, 1), ("1100", 800m), ("8000", -800m)))).IsGelukt);

        var dashboard = await app.Dienst<DashboardDienst>().Laad(wie, id, Alles);

        Assert.Equal(2026, dashboard.Maandgrafiek!.Value.Jaar);
        Assert.All(dashboard.Maandgrafiek.Value.Maanden, m => Assert.Equal(0m, m.Omzet));
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

        Assert.Equal(DashboardDienst.Standaard, await dienst.Indeling(wie, id));

        IReadOnlyList<Widgetkeuze> eigen = [new(Widget.Maandgrafiek, Widgetkeuze.Kosten), new(Widget.Btw, Widgetkeuze.Maand), new(Widget.Liquide)];
        Assert.True((await dienst.BewaarIndeling(inzien, id, eigen)).IsGelukt);

        Assert.Equal(eigen, await dienst.Indeling(inzien, id));
        Assert.Equal(DashboardDienst.Standaard, await dienst.Indeling(wie, id));
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
}
