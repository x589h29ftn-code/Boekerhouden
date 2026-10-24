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
        C("BVorVbk", "Vooruitbetaalde en te vorderen belastingen"),
        C("BVorVbkTvo", "Terug te vorderen omzetbelasting", omslag: "BSchBepBtw"),
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
        C("BSchBep", "Belastingen en premies sociale verzekeringen", Saldokant.Credit),
        C("BSchBepBla", "Omzetbelasting algemeen tarief", Saldokant.Credit),
        C("BSchBepBoo", "Voorbelasting", Saldokant.Credit),
        C("BSchBepBtw", "Te betalen omzetbelasting", Saldokant.Credit, omslag: "BVorVbkTvo"),
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
        C("WBedAut", "Autokosten"),
        C("WBedAutBra", "Brandstof"),
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
        HashSet<string>? zonderSpecificatie = null,
        Dictionary<string, bool>? uitklappen = null,
        Dictionary<string, string>? toelichtingen = null,
        Dictionary<Guid, IReadOnlyList<SplitsRegel>>? uitsplitsingen = null)
    {
        saldi ??= Saldi;
        eigenPosten ??= [];
        var balans = PostBalans.Bereken(Schema, [.. eigenPosten], koppelingen ?? Koppelingen, [.. saldi]);
        return new RapportGegevens(soort, "Voorbeeld BV", "Ergens", 2025, true, Schema, balans, saldi, eigenPosten,
            code => Schema.Zoek(code)?.Omschrijving ?? code, weergave ?? [], zonderSpecificatie ?? [], uitklappen, toelichtingen, uitsplitsingen);
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

        Assert.Equal(50_000m, Regel(wv, "Netto-omzet").Totaal);
        Assert.Equal(20_000m, Regel(wv, "Kostprijs van de omzet").Totaal);
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

    private static readonly Guid Auto = Guid.NewGuid();

    /// <summary>De saldi met een extra autokostenrekening, zodat de overige bedrijfskosten twee subposten hebben.</summary>
    private static RapportGegevens MetAutokosten(Dictionary<string, bool>? uitklappen = null) =>
        Gegevens(
            saldi: [.. Saldi.Select(s => s.RekeningId == Omzet ? s with { Huidig = -51_000m } : s), new(Auto, "4200", "Brandstof bestelauto", 1_000m, 0m)],
            koppelingen: new Dictionary<Guid, KoppelDoel>(Koppelingen) { [Auto] = new("WBedAutBra") },
            uitklappen: uitklappen);

    [Fact]
    public void Een_post_in_de_winst_en_verliesrekening_klapt_uit_naar_zijn_subposten()
    {
        var ingeklapt = RapportOpbouw.WinstEnVerlies(MetAutokosten());
        var kosten = Regel(ingeklapt, "Overige bedrijfskosten");
        Assert.False(kosten.Uitgeklapt);
        Assert.Equal(new Bron(Onderwerp.Post, "WBed", null, "WBed · RGS: Overige bedrijfskosten"), kosten.Bron);
        Assert.DoesNotContain(ingeklapt.Regels, r => r.Omschrijving == "Autokosten");
        Assert.Null(Regel(ingeklapt, "Lonen en salarissen").Uitgeklapt);

        var uitgeklapt = RapportOpbouw.WinstEnVerlies(MetAutokosten(new() { ["WBed"] = true }));
        var regels = uitgeklapt.Regels.Select(r => r.Omschrijving).ToList();
        var index = regels.IndexOf("Overige bedrijfskosten");
        Assert.Equal(["Kantoorkosten", "Autokosten"], regels.Skip(index + 1).Take(2));
        Assert.Equal(3_500m, Regel(uitgeklapt, "Kantoorkosten").Detail);
        Assert.Equal(1_000m, Regel(uitgeklapt, "Autokosten").Detail);
        Assert.Equal(4_500m, Regel(uitgeklapt, "Overige bedrijfskosten").Totaal);
        Assert.Equal(14_500m, Regel(uitgeklapt, "Som der bedrijfslasten").Totaal);
    }

    [Fact]
    public void Een_balanspost_is_standaard_uitgeklapt_en_kan_dicht()
    {
        var open = RapportOpbouw.Balans(Gegevens())[1];
        Assert.True(Regel(open, "Kortlopende schulden").Uitgeklapt);
        Assert.Contains(open.Regels, r => r.Omschrijving == "Schulden aan leveranciers");

        var dicht = RapportOpbouw.Balans(Gegevens(uitklappen: new() { ["BSch"] = false }))[1];
        Assert.False(Regel(dicht, "Kortlopende schulden").Uitgeklapt);
        Assert.DoesNotContain(dicht.Regels, r => r.Omschrijving == "Schulden aan leveranciers");
        Assert.Equal(Regel(open, "Totaal passiva").Totaal, Regel(dicht, "Totaal passiva").Totaal);
    }

    [Fact]
    public void Een_groep_in_de_specificatie_klapt_in_tot_een_regel()
    {
        var open = Specificatie(RapportOpbouw.Maak(MetAutokosten()), "Overige bedrijfskosten");
        Assert.Equal(["Kantoorkosten", "Kantoorartikelen", "Porti", string.Empty, "Autokosten", "Brandstof bestelauto", "Totaal"], open.Regels.Select(r => r.Omschrijving));
        Assert.True(open.Regels[0].Uitgeklapt);
        Assert.Null(Regel(open, "Autokosten").Uitgeklapt);
        Assert.Equal(Onderwerp.Rekening, Regel(open, "Porti").Bron!.Soort);
        Assert.Equal(Porti, Regel(open, "Porti").Bron!.Id);

        var dicht = Specificatie(RapportOpbouw.Maak(MetAutokosten(new() { ["WBedKan"] = false })), "Overige bedrijfskosten");
        Assert.Equal(["Kantoorkosten", "Autokosten", "Brandstof bestelauto", "Totaal"], dicht.Regels.Select(r => r.Omschrijving));
        Assert.Equal(RegelSoort.Detail, dicht.Regels[0].Soort);
        Assert.False(dicht.Regels[0].Uitgeklapt);
        Assert.Equal(3_500m, dicht.Regels[0].Detail);
        Assert.Equal(4_500m, Regel(dicht, "Totaal").Detail);
    }

    [Fact]
    public void Een_toelichting_staat_onder_de_specificatie_of_met_eigen_titel()
    {
        var blokken = RapportOpbouw.Maak(Gegevens(toelichtingen: new() { ["BMva"] = "In 2025 is nieuwe inventaris gekocht." })).Hoofdstukken.ElementAt(2).Blokken;
        var index = blokken.ToList().FindIndex(b => b is TabelBlok { Tabel.Code: "BMva" });
        var tekst = Assert.IsType<TekstBlok>(blokken[index + 1]);
        Assert.Equal("BMva", tekst.Toelichting);
        Assert.Null(tekst.Titel);

        var zonderSpecificatie = RapportOpbouw.Maak(Gegevens(zonderSpecificatie: ["BMva"], toelichtingen: new() { ["BMva"] = "Alleen tekst." })).Hoofdstukken.ElementAt(2).Blokken;
        var los = zonderSpecificatie.OfType<TekstBlok>().Single(b => b.Toelichting == "BMva");
        Assert.Equal("Materiële vaste activa", los.Titel);
        Assert.DoesNotContain(zonderSpecificatie, b => b is TabelBlok { Tabel.Code: "BMva" });
    }

    [Fact]
    public void Een_uitgesplitste_rekening_toont_de_regels_als_ze_aansluiten()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(uitsplitsingen: new()
        {
            [Crediteuren] = [new("Leveranciers grondstoffen", 2_500m, 1_500m), new("Overige leveranciers", 1_500m, 500m)],
        }));

        var schulden = Specificatie(rapport, "Kortlopende schulden");
        Assert.Equal(["Schulden aan banken", "Bank", "Schulden aan leveranciers", "Leveranciers grondstoffen", "Overige leveranciers", string.Empty, "Totaal"],
            schulden.Regels.Select(r => r.Omschrijving));
        Assert.Equal(Onderwerp.Uitsplitsing, Regel(schulden, "Overige leveranciers").Bron!.Soort);
        Assert.Equal(4_000m, Regel(schulden, "Totaal").Detail);
        Assert.Empty(rapport.Waarschuwingen);
    }

    [Fact]
    public void Een_uitsplitsing_die_niet_aansluit_houdt_het_verschil_apart_en_waarschuwt()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(uitsplitsingen: new() { [Crediteuren] = [new("Leveranciers grondstoffen", 2_500m, null)] }));

        var schulden = Specificatie(rapport, "Kortlopende schulden");
        var rest = Regel(schulden, RapportOpbouw.NietGespecificeerd);
        Assert.Equal(1_500m, rest.Detail);
        Assert.Equal(2_000m, rest.DetailVorig);
        Assert.Equal(4_000m, Regel(schulden, "Totaal").Detail);
        Assert.Contains(rapport.Waarschuwingen, w => w.StartsWith("Uitsplitsing 1600 Crediteuren sluit niet aan", StringComparison.Ordinal));
    }

    [Fact]
    public void Afrondingsverschillen_van_een_uitsplitsing_gaan_naar_de_grootste_regel()
    {
        var (regels, verschil) = RapportOpbouw.Verdeel([1_000.50m, 2_000.50m], 3_001m);
        Assert.Equal(3_001m, regels.Sum());
        Assert.Equal(0m, verschil);

        var (_, teWeinig) = RapportOpbouw.Verdeel([1_000m], 3_001m);
        Assert.Equal(2_001m, teWeinig);
    }

    private static readonly Grondslag[] Bibliotheek =
    [
        new("algemeen", "Algemeen", "Algemeen", "De jaarrekening is opgesteld volgens Titel 9 Boek 2 BW.", GrondslagStandaard.Altijd, []),
        new("foutenherstel", "Algemeen", "Foutenherstel", "Er is een fout hersteld.", GrondslagStandaard.Optioneel, []),
        new("bijzonder", "Algemeen", "Bijzondere waardeverminderingen", "Bij een lagere realiseerbare waarde.", GrondslagStandaard.Optioneel, ["BVrd"]),
        new("leasing", "Algemeen", "Leasing", "Leasebetalingen zijn een last.", GrondslagStandaard.Optioneel, ["BMva"]),
        new("mva", "Waardering", "Materiële vaste activa", "Tegen verkrijgingsprijs min afschrijvingen.", GrondslagStandaard.BijPost, ["BMva"]),
        new("voorraden", "Waardering", "Voorraden", "Tegen verkrijgingsprijs.", GrondslagStandaard.BijPost, ["BVrd"]),
        new("latentie", "Waardering", "Latente belastingen", "Voor tijdelijke verschillen.", GrondslagStandaard.BijPost, ["BVrzVvbVlb"]),
    ];

    private static RapportGegevens MetGrondslagen(RapportSoort soort = RapportSoort.Jaarrekening, Dictionary<string, GrondslagKeuze>? keuzes = null) =>
        Gegevens(soort) with { Grondslagen = Bibliotheek, GrondslagKeuzes = keuzes };

    [Fact]
    public void Grondslagen_staan_open_bij_posten_met_saldo_en_optionele_dicht()
    {
        var rapport = RapportOpbouw.Maak(MetGrondslagen());

        Assert.Equal(["Balans per 31 december 2025", "Winst-en-verliesrekening over 2025", "Algemene toelichting en grondslagen", "Toelichting op de balans", "Toelichting op de winst-en-verliesrekening"],
            rapport.Hoofdstukken.Select(h => h.Titel));
        var blokken = rapport.Hoofdstukken.ElementAt(2).Blokken;
        Assert.Equal(["Algemeen", "algemeen", "foutenherstel", "bijzonder", "leasing", "Waardering", "mva"],
            blokken.Select(b => b switch { GroepBlok groep => groep.Titel, GrondslagBlok grondslag => grondslag.Sleutel, _ => "?" }));
        Assert.True(blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "mva").Opgenomen);
        Assert.False(blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "foutenherstel").Opgenomen);

        // Een optionele grondslag met een post: open als die post saldo heeft, anders dicht (maar wel te openen).
        Assert.True(blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "leasing").Opgenomen);
        Assert.False(blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "bijzonder").Opgenomen);
        Assert.All(blokken.OfType<GroepBlok>(), groep => Assert.True(groep.HeeftOpgenomen));
    }

    [Fact]
    public void De_keuze_in_het_dossier_gaat_voor()
    {
        var rapport = RapportOpbouw.Maak(MetGrondslagen(keuzes: new()
        {
            ["mva"] = new(false, null),
            ["foutenherstel"] = new(true, "In 2025 is een fout in de voorraadwaardering hersteld."),
            ["latentie"] = new(true, null),
        }));

        var blokken = rapport.Hoofdstukken.ElementAt(2).Blokken;
        var fout = blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "foutenherstel");
        Assert.True(fout.Opgenomen);
        Assert.True(fout.EigenTekst);
        Assert.Equal("In 2025 is een fout in de voorraadwaardering hersteld.", fout.Tekst);
        Assert.False(blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "mva").Opgenomen);

        // Zonder saldo, maar bewust aangezet: dan staat hij er toch.
        Assert.True(blokken.OfType<GrondslagBlok>().Single(b => b.Sleutel == "latentie").Opgenomen);
        Assert.True(blokken.OfType<GroepBlok>().Single(b => b.Titel == "Waardering").HeeftOpgenomen);
    }

    [Fact]
    public void Een_groep_zonder_open_grondslagen_heeft_niets_in_het_rapport()
    {
        var rapport = RapportOpbouw.Maak(MetGrondslagen(keuzes: new() { ["mva"] = new(false, null) }));

        Assert.False(rapport.Hoofdstukken.ElementAt(2).Blokken.OfType<GroepBlok>().Single(b => b.Titel == "Waardering").HeeftOpgenomen);
    }

    [Fact]
    public void De_aangiftebijlage_heeft_geen_grondslagen()
    {
        var rapport = RapportOpbouw.Maak(MetGrondslagen(RapportSoort.AangifteBijlage));

        Assert.DoesNotContain(rapport.Hoofdstukken, h => h.Titel.Contains("grondslagen", StringComparison.Ordinal));
        Assert.Equal(4, rapport.Hoofdstukken.Count());
    }

    private static readonly Guid BtwHoog = Guid.NewGuid(), Voorbelasting = Guid.NewGuid();

    /// <summary>Omzetbelasting op twee rekeningen in een gesaldeerde eigen post: dit jaar per saldo te vorderen, vorig jaar te betalen.</summary>
    private static RapportGegevens MetOmzetbelasting(string? omslag = "BVorVbkTvo")
    {
        var btw = new EigenPost(Guid.NewGuid(), "BSchBep", "Omzetbelasting", omslag);
        return Gegevens(
            saldi: [.. Saldi, new(BtwHoog, "1500", "Omzetbelasting 21%", -5_000m, -5_000m), new(Voorbelasting, "1510", "Voorbelasting", 7_000m, 3_000m)],
            eigenPosten: [btw],
            koppelingen: new Dictionary<Guid, KoppelDoel>(Koppelingen) { [BtwHoog] = new("BSchBep", btw.Id), [Voorbelasting] = new("BSchBep", btw.Id) });
    }

    [Fact]
    public void Een_gesaldeerde_post_staat_per_jaar_aan_de_kant_van_zijn_saldo()
    {
        var g = MetOmzetbelasting();
        var btw = g.EigenPosten.Single();

        Assert.Equal("BVorVbkTvo", g.Balans.EigenOuder(btw, vorig: false));
        Assert.Equal("BSchBep", g.Balans.EigenOuder(btw, vorig: true));
        Assert.Equal(2_000m, g.Balans.Totaal("BVorVbk").Huidig);
        Assert.Equal(0m, g.Balans.Totaal("BVorVbk").Vorig);
        Assert.Equal(0m, g.Balans.Totaal("BSchBep").Huidig);
        Assert.Equal(-2_000m, g.Balans.Totaal("BSchBep").Vorig);
        Assert.DoesNotContain(g.Balans.Signalen, s => s.Soort == SignaalSoort.VerkeerdeKant);

        var rapport = RapportOpbouw.Maak(g);
        var vorderingen = Specificatie(rapport, "Vorderingen");
        Assert.Equal(2_000m, Regel(vorderingen, "Omzetbelasting").Detail);
        Assert.Equal(0m, Regel(vorderingen, "Omzetbelasting").DetailVorig);
        var schulden = Specificatie(rapport, "Kortlopende schulden");
        Assert.Equal(0m, Regel(schulden, "Omzetbelasting").Detail);
        Assert.Equal(2_000m, Regel(schulden, "Omzetbelasting").DetailVorig);
    }

    [Fact]
    public void Zonder_saldering_telt_elke_rekening_apart_in_de_schulden()
    {
        var g = MetOmzetbelasting(omslag: null);

        Assert.Equal(2_000m, g.Balans.Totaal("BSchBep").Huidig);
        Assert.Equal(0m, g.Balans.Totaal("BVorVbk").Huidig);
    }
}
