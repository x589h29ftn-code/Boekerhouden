using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class RapportOpbouwTests
{
    private static int volgorde;

    private static RgsCode C(string code, string omschrijving, Saldokant? kant = Saldokant.Debet, string? omslag = null) =>
        new(code, code.Length == 1 ? 1 : 1 + ((code.Length - 1) / 3), code.Length == 1 ? null : code[..Math.Max(1, code.Length - 3)],
            omschrijving, omschrijving, kant, omslag, ++volgorde, null, false, new HashSet<string> { "Basis" }, new HashSet<string>());

    private static readonly RgsSchema Schema = new("test",
    [
        C("B", "Balans", null),
        C("BMva", "Materiële vaste activa"),
        C("BMvaInv", "Inventaris"),
        C("BMvaInvAan", "Aanschafwaarde inventaris"),
        C("BVor", "Vorderingen"),
        C("BVorDeb", "Handelsdebiteuren"),
        C("BVorDebHad", "Handelsdebiteuren nominaal"),
        C("BLim", "Liquide middelen"),
        C("BLimBan", "Tegoeden bij banken"),
        C("BLimBanRba", "Rekening-courant bank", omslag: "BSchSakRba"),
        C("BEiv", "Eigen vermogen", Saldokant.Credit),
        C("BEivGok", "Gestort en opgevraagd kapitaal", Saldokant.Credit),
        C("BEivGokGea", "Geplaatst kapitaal", Saldokant.Credit),
        C("BEivOvr", "Overige reserves", Saldokant.Credit),
        C("BEivOvrOvr", "Overige reserves", Saldokant.Credit),
        C("BSch", "Kortlopende schulden", Saldokant.Credit),
        C("BSchSak", "Schulden aan banken", Saldokant.Credit),
        C("BSchSakRba", "Rekening-courant bij kredietinstellingen", Saldokant.Credit, omslag: "BLimBanRba"),
        C("BSchCre", "Schulden aan leveranciers", Saldokant.Credit),
        C("BSchCreHac", "Handelscrediteuren", Saldokant.Credit),
        C("W", "W&V", null),
        C("WOmz", "Netto-omzet", Saldokant.Credit),
        C("WOmzNop", "Netto-omzet producten", Saldokant.Credit),
        C("WOmzNopNod", "Omzet hoog tarief", Saldokant.Credit),
        C("WKpr", "Kostprijs van de omzet"),
        C("WKprInk", "Inkoopwaarde", Saldokant.Debet),
        C("WKprInkInk", "Inkopen", Saldokant.Debet),
        C("WPer", "Lonen en salarissen"),
        C("WPerLes", "Lonen en salarissen"),
        C("WPerLesSal", "Salarissen"),
        C("WBed", "Overige bedrijfskosten"),
        C("WBedKan", "Kantoorkosten"),
        C("WBedKanKan", "Kantoorbenodigdheden"),
        C("WBel", "Belastingen"),
        C("WBelBel", "Vennootschapsbelasting"),
        C("WBelBelVpb", "Vennootschapsbelasting"),
    ]);

    private static readonly Guid Inventaris = Guid.NewGuid(), Debiteuren = Guid.NewGuid(), Bank = Guid.NewGuid(), Kapitaal = Guid.NewGuid(),
        Reserves = Guid.NewGuid(), Crediteuren = Guid.NewGuid(), Omzet = Guid.NewGuid(), Inkoop = Guid.NewGuid(), Salarissen = Guid.NewGuid(),
        Kantoor = Guid.NewGuid(), Porti = Guid.NewGuid(), Vpb = Guid.NewGuid();

    private static readonly Dictionary<Guid, KoppelDoel> Koppelingen = new()
    {
        [Inventaris] = new("BMvaInvAan"),
        [Debiteuren] = new("BVorDebHad"),
        [Bank] = new("BLimBanRba"),
        [Kapitaal] = new("BEivGokGea"),
        [Reserves] = new("BEivOvrOvr"),
        [Crediteuren] = new("BSchCreHac"),
        [Omzet] = new("WOmzNopNod"),
        [Inkoop] = new("WKprInkInk"),
        [Salarissen] = new("WPerLesSal"),
        [Kantoor] = new("WBedKanKan"),
        [Porti] = new("WBedKanKan"),
        [Vpb] = new("WBelBelVpb"),
    };

    /// <summary>Een sluitende saldibalans: resultaat 14.000 (vorig jaar 12.000); de bank staat vorig jaar rood.</summary>
    private static readonly RekeningSaldo[] Saldi =
    [
        new(Inventaris, "0100", "Inventaris", 10_000m, 12_000m),
        new(Debiteuren, "1300", "Debiteuren", 5_000m, 14_000m),
        new(Bank, "1100", "Bank", 8_000m, -3_000m),
        new(Kapitaal, "0500", "Aandelenkapitaal", -1_000m, -1_000m),
        new(Reserves, "0600", "Algemene reserve", -4_000m, -8_000m),
        new(Crediteuren, "1600", "Crediteuren", -4_000m, -2_000m),
        new(Omzet, "8000", "Omzet", -50_000m, -40_000m),
        new(Inkoop, "7000", "Inkopen", 20_000m, 15_000m),
        new(Salarissen, "4000", "Salarissen", 10_000m, 10_000m),
        new(Kantoor, "4300", "Kantoorartikelen", 3_000m, 2_000m),
        new(Porti, "4310", "Porti", 500m, 0m),
        new(Vpb, "9000", "Vennootschapsbelasting", 2_500m, 1_000m),
    ];

    private static RapportGegevens Gegevens(
        RapportSoort soort = RapportSoort.Jaarrekening,
        IReadOnlyList<RekeningSaldo>? saldi = null,
        IReadOnlyList<EigenPost>? eigenPosten = null,
        Dictionary<Guid, KoppelDoel>? koppelingen = null,
        Dictionary<Guid, RekeningWeergave>? weergave = null,
        HashSet<string>? zonderSpecificatie = null)
    {
        saldi ??= Saldi;
        eigenPosten ??= [];
        var balans = PostBalans.Bereken(Schema, [.. eigenPosten], koppelingen ?? Koppelingen, [.. saldi]);
        return new RapportGegevens(soort, "Voorbeeld BV", "Ergens", 2025, true, Schema, balans, saldi, eigenPosten,
            code => Schema.Zoek(code)?.Omschrijving ?? code, weergave ?? [], zonderSpecificatie ?? []);
    }

    private static Regel Regel(Tabel tabel, string omschrijving) => tabel.Regels.Single(r => r.Omschrijving == omschrijving);

    private static Tabel Specificatie(Rapport.Rapport rapport, string titel) =>
        rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>().Select(b => b.Tabel).Single(t => t.Titel == titel);

    [Fact]
    public void Balans_sluit_met_het_resultaat_als_regel_in_het_eigen_vermogen()
    {
        var tabellen = RapportOpbouw.Balans(Gegevens());
        var activa = tabellen[0];
        var passiva = tabellen[1];

        Assert.Equal(["Vaste activa", "Materiële vaste activa", "Vlottende activa", "Vorderingen", "Liquide middelen", "Totaal activa"],
            activa.Regels.Select(r => r.Omschrijving));
        Assert.Equal(23_000m, Regel(activa, "Totaal activa").Totaal);
        Assert.Equal(26_000m, Regel(activa, "Totaal activa").TotaalVorig);
        Assert.Equal(0m, Regel(activa, "Liquide middelen").TotaalVorig);

        var eigenVermogen = Regel(passiva, "Eigen vermogen");
        Assert.Equal(19_000m, eigenVermogen.Totaal);
        Assert.Equal(21_000m, eigenVermogen.TotaalVorig);
        Assert.Equal(14_000m, Regel(passiva, "Resultaat boekjaar").Detail);
        Assert.Equal(1_000m, Regel(passiva, "Gestort en opgevraagd kapitaal").Detail);

        // De rode bank van vorig jaar staat bij de kortlopende schulden.
        Assert.Equal(5_000m, Regel(passiva, "Kortlopende schulden").TotaalVorig);
        Assert.Equal(3_000m, Regel(passiva, "Schulden aan banken").DetailVorig);
        Assert.Equal(Regel(activa, "Totaal activa").Totaal, Regel(passiva, "Totaal passiva").Totaal);
        Assert.Equal(Regel(activa, "Totaal activa").TotaalVorig, Regel(passiva, "Totaal passiva").TotaalVorig);
    }

    [Fact]
    public void Winst_en_verlies_toont_brutomarge_en_eindigt_op_het_resultaat()
    {
        var wv = RapportOpbouw.WinstEnVerlies(Gegevens());

        Assert.Equal(50_000m, Regel(wv, "Netto-omzet").Detail);
        Assert.Equal(20_000m, Regel(wv, "Kostprijs van de omzet").Detail);
        Assert.Equal(30_000m, Regel(wv, "Brutomarge").Totaal);
        Assert.Equal(13_500m, Regel(wv, "Som der bedrijfslasten").Totaal);
        Assert.Equal(16_500m, Regel(wv, "Bedrijfsresultaat").Totaal);
        Assert.Equal(13_000m, Regel(wv, "Bedrijfsresultaat").TotaalVorig);
        Assert.Equal(-2_500m, Regel(wv, "Belastingen").Totaal);

        var resultaat = wv.Regels[^1];
        Assert.Equal(RegelSoort.Eindtotaal, resultaat.Soort);
        Assert.Equal("Resultaat na belastingen", resultaat.Omschrijving);
        Assert.Equal(14_000m, resultaat.Totaal);
        Assert.Equal(12_000m, resultaat.TotaalVorig);
    }

    [Fact]
    public void Specificaties_volgen_de_omslag_per_jaar_en_sluiten_op_de_post()
    {
        var rapport = RapportOpbouw.Maak(Gegevens());

        var liquide = Specificatie(rapport, "Liquide middelen");
        Assert.Equal(8_000m, Regel(liquide, "Bank").Detail);
        Assert.Equal(0m, Regel(liquide, "Bank").DetailVorig);

        var schulden = Specificatie(rapport, "Kortlopende schulden");
        Assert.Equal(["Schulden aan banken", "Bank", "Schulden aan leveranciers", "Crediteuren", "Totaal"], schulden.Regels.Select(r => r.Omschrijving));
        Assert.Equal(3_000m, Regel(schulden, "Bank").DetailVorig);
        Assert.Equal(4_000m, Regel(schulden, "Totaal").Detail);
        Assert.Equal(5_000m, Regel(schulden, "Totaal").DetailVorig);

        var kosten = Specificatie(rapport, "Overige bedrijfskosten");
        Assert.Equal(["Kantoorartikelen", "Porti", "Totaal"], kosten.Regels.Select(r => r.Omschrijving));
        Assert.Equal(3_500m, Regel(kosten, "Totaal").Detail);

        var eigenVermogen = Specificatie(rapport, "Eigen vermogen");
        Assert.Equal(RegelSoort.Post, Regel(eigenVermogen, "Resultaat boekjaar").Soort);
        Assert.Equal(14_000m, Regel(eigenVermogen, "Resultaat boekjaar").Totaal);
        Assert.Equal(19_000m, Regel(eigenVermogen, "Totaal").Detail);
        Assert.Empty(rapport.Waarschuwingen);
    }

    [Fact]
    public void Verborgen_rekeningen_tellen_samen_als_overige_en_een_omschrijving_kan_anders()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(weergave: new()
        {
            [Kantoor] = new RekeningWeergave("Kantoorkosten", false),
            [Porti] = new RekeningWeergave(null, true),
        }));

        var kosten = Specificatie(rapport, "Overige bedrijfskosten");
        Assert.Equal(["Kantoorkosten", "Overige", "Totaal"], kosten.Regels.Select(r => r.Omschrijving));
        Assert.Equal(500m, Regel(kosten, "Overige").Detail);
        Assert.Equal(3_500m, Regel(kosten, "Totaal").Detail);
    }

    [Fact]
    public void Een_eigen_post_is_een_regel_in_de_specificatie()
    {
        var eigen = new EigenPost(Guid.NewGuid(), "BVorDeb", "Debiteuren binnenland");
        var koppelingen = new Dictionary<Guid, KoppelDoel>(Koppelingen) { [Debiteuren] = new("BVorDeb", eigen.Id) };

        var rapport = RapportOpbouw.Maak(Gegevens(eigenPosten: [eigen], koppelingen: koppelingen));

        var vorderingen = Specificatie(rapport, "Vorderingen");
        Assert.Equal(["Debiteuren binnenland", "Totaal"], vorderingen.Regels.Select(r => r.Omschrijving));
        Assert.Equal(5_000m, Regel(vorderingen, "Debiteuren binnenland").Detail);
    }

    [Fact]
    public void Ongekoppelde_rekeningen_staan_apart_en_geven_een_waarschuwing()
    {
        var koppelingen = new Dictionary<Guid, KoppelDoel>(Koppelingen);
        koppelingen.Remove(Porti);

        var rapport = RapportOpbouw.Maak(Gegevens(koppelingen: koppelingen));

        var activa = ((TabelBlok)rapport.Hoofdstukken.First().Blokken[0]).Tabel;
        var passiva = ((TabelBlok)rapport.Hoofdstukken.First().Blokken[1]).Tabel;
        Assert.Equal(500m, Regel(activa, "Nog niet gerubriceerd").Totaal);
        Assert.Equal(Regel(activa, "Totaal activa").Totaal, Regel(passiva, "Totaal passiva").Totaal);
        Assert.Single(rapport.Waarschuwingen, w => w.Contains("niet gerubriceerd", StringComparison.Ordinal));
    }

    [Fact]
    public void Een_niet_sluitende_saldibalans_geeft_een_waarschuwing()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(saldi: [.. Saldi.Where(s => s.RekeningId != Crediteuren)]));

        Assert.Contains(rapport.Waarschuwingen, w => w.StartsWith("De balans sluit niet", StringComparison.Ordinal));
    }

    [Fact]
    public void Specificatie_kan_per_post_uit()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(zonderSpecificatie: ["BMva"]));

        Assert.DoesNotContain(rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>(), b => b.Tabel.Titel == "Materiële vaste activa");
        Assert.Contains(rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>(), b => b.Tabel.Titel == "Vorderingen");
    }

    [Fact]
    public void Aangiftebijlage_heeft_eigen_titels()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(RapportSoort.AangifteBijlage));

        Assert.Equal("Bijlage bij de aangifte inkomstenbelasting 2025", rapport.Titel);
        Assert.Equal("Geen accountantscontrole toegepast", rapport.Statustekst);
        Assert.Equal(["Balans per 31 december 2025", "Winst-en-verliesrekening over 2025", "Specificaties van de balans", "Specificaties van de winst-en-verliesrekening"],
            rapport.Hoofdstukken.Select(h => h.Titel));
        var wv = ((TabelBlok)rapport.Hoofdstukken.ElementAt(1).Blokken[0]).Tabel;
        Assert.Equal("Resultaat", wv.Regels[^1].Omschrijving);
    }
}
