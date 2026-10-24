using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class VerloopTests
{
    private static int volgorde;

    private static RgsCode C(string code, string omschrijving, Saldokant kant = Saldokant.Debet) =>
        new(code, code.Length == 1 ? 1 : 1 + ((code.Length - 1) / 3), code.Length == 1 ? null : code[..Math.Max(1, code.Length - 3)],
            omschrijving, omschrijving, kant, null, ++volgorde, null, false, new HashSet<string> { "Basis" }, new HashSet<string>());

    private static readonly RgsSchema Schema = new("test",
    [
        C("B", "Balans"),
        C("BMva", "Materiële vaste activa"),
        C("BMvaBei", "Inventaris"),
        C("BMvaBeiVvp", "Verkrijgingsprijs inventaris"),
        C("BMvaBeiCae", "Cumulatieve afschrijvingen inventaris", Saldokant.Credit),
        C("BMvaTev", "Automobielen"),
        C("BMvaTevVvp", "Verkrijgingsprijs automobielen"),
        C("BMvaTevCae", "Cumulatieve afschrijvingen automobielen", Saldokant.Credit),
        C("BFva", "Financiële vaste activa"),
        C("BFvaLen", "Leningen u/g"),
        C("BVor", "Vorderingen"),
        C("BVorOvr", "Overige vorderingen"),
        C("BVorOvrRcb", "Rekening-courant bestuurders"),
        C("BLim", "Liquide middelen"),
        C("BLimBan", "Banken"),
        C("BEiv", "Eigen vermogen", Saldokant.Credit),
        C("BEivGok", "Aandelenkapitaal", Saldokant.Credit),
        C("BEivOvr", "Overige reserves", Saldokant.Credit),
        C("W", "Winst-en-verliesrekening"),
        C("WOmz", "Netto-omzet", Saldokant.Credit),
        C("WOmzNod", "Diensten", Saldokant.Credit),
        C("WAfs", "Afschrijvingen"),
        C("WAfsAmv", "Afschrijvingen materiële vaste activa"),
        C("WAfsAmvBei", "Afschrijvingen inventaris"),
        C("WAfsAmvTev", "Afschrijvingen automobielen"),
    ]);

    private static readonly Guid InvAan = Guid.NewGuid(), InvCum = Guid.NewGuid(), AutoAan = Guid.NewGuid(), AutoCum = Guid.NewGuid(),
        Lening = Guid.NewGuid(), Rc = Guid.NewGuid(), Bank = Guid.NewGuid(), Kapitaal = Guid.NewGuid(), Reserves = Guid.NewGuid(), Omzet = Guid.NewGuid(), AfsInv = Guid.NewGuid(), AfsAuto = Guid.NewGuid();

    private static RapportGegevens Gegevens(decimal autoAanschaf = 9_373m, decimal autoCum = -5_268m, Dictionary<string, bool>? verlopen = null, Dictionary<string, IReadOnlyList<VerloopMutatie>>? mutaties = null,
        Dictionary<Guid, decimal>? tweeJaarTerug = null, HashSet<string>? zonderVorig = null, Dictionary<string, string>? percentages = null)
    {
        RekeningSaldo[] saldi =
        [
            new(InvAan, "0270", "Inventaris aanschaf", 4_141m, 1_407m),
            new(InvCum, "0271", "Inventaris cum. afschr.", -1_538m, -963m),
            new(AutoAan, "0400", "Auto aanschaf", autoAanschaf, 9_373m),
            new(AutoCum, "0401", "Auto cum. afschr.", autoCum, -3_593m),
            new(Lening, "0600", "Lening u/g", 8_000m, 10_000m),
            new(Rc, "1600", "Rekening-courant DGA", 57_076m, 15_231m),
            new(Bank, "1100", "Bank", 9_373m - autoAanschaf - 5_268m - autoCum, 0m),
            new(Kapitaal, "0410", "Aandelenkapitaal", -5_301m, -5_301m),
            new(Reserves, "0461", "Overige reserves", -78_118m, 0m),
            new(Omzet, "8000", "Omzet", -100_000m, -80_000m),
            new(AfsInv, "4740", "Afs. inventaris", 575m, 207m),
            new(AfsAuto, "4730", "Afs. auto", 1_675m, 1_675m),
        ];
        var koppelingen = new Dictionary<Guid, KoppelDoel>
        {
            [InvAan] = new("BMvaBeiVvp"),
            [InvCum] = new("BMvaBeiCae"),
            [AutoAan] = new("BMvaTevVvp"),
            [AutoCum] = new("BMvaTevCae"),
            [Lening] = new("BFvaLen"),
            [Rc] = new("BVorOvrRcb"),
            [Bank] = new("BLimBan"),
            [Kapitaal] = new("BEivGok"),
            [Reserves] = new("BEivOvr"),
            [Omzet] = new("WOmzNod"),
            [AfsInv] = new("WAfsAmvBei"),
            [AfsAuto] = new("WAfsAmvTev"),
        };
        var balans = PostBalans.Bereken(Schema, [], koppelingen, saldi);
        return new RapportGegevens(RapportSoort.Jaarrekening, "Voorbeeld BV", "Ergens", 2025, true, Schema, balans, saldi, [],
            code => Schema.Zoek(code)?.Omschrijving ?? code, new Dictionary<Guid, RekeningWeergave>(), new HashSet<string>(),
            Verlopen: verlopen, VerloopMutaties: mutaties, SaldiTweeJaarTerug: tweeJaarTerug, VerloopZonderVorigJaar: zonderVorig, Afschrijvingspercentages: percentages);
    }

    private static List<Verloop> Verlopen(Rapport.Rapport rapport) =>
        [.. rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<VerloopBlok>().Select(b => b.Verloop)];

    private static VerloopRegel Regel(Verloop v, string omschrijving) => v.Regels.Single(r => r.Omschrijving == omschrijving);

    [Fact]
    public void Materiele_vaste_activa_hebben_standaard_een_verloop_met_investeringen_en_afschrijvingen()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(verlopen: new() { ["BVor"] = false }));

        var mva = Verlopen(rapport).Single(v => v.Sleutel == "BMva");
        Assert.True(mva.Standaard);
        Assert.False(mva.Aangepast);
        Assert.Equal(["Inventaris", "Automobielen", "Totaal"], mva.Kolommen);
        Assert.Equal(
            ["Boekwaarde 1 januari 2025", "Verkrijgings- of vervaardigingsprijs", "Cumulatieve afschrijvingen en waardeverminderingen", "", "Mutaties 2025",
             "Investeringen", "Afschrijvingen", "", "Boekwaarde 31 december 2025", "Verkrijgings- of vervaardigingsprijs", "Cumulatieve afschrijvingen en waardeverminderingen", "", "Afschrijvingspercentage"],
            mva.Regels.Select(r => r.Omschrijving));
        Assert.Equal([1_407m, 9_373m, 10_780m], mva.Regels[1].Bedragen);
        Assert.Equal([-963m, -3_593m, -4_556m], mva.Regels[2].Bedragen);
        Assert.Equal([2_734m, 0m, 2_734m], Regel(mva, "Investeringen").Bedragen);
        Assert.Equal([-575m, -1_675m, -2_250m], Regel(mva, "Afschrijvingen").Bedragen);
        Assert.Equal([2_603m, 4_105m, 6_708m], mva.Regels[^2].Bedragen);
        Assert.Equal(VerloopRegelSoort.Eindstand, mva.Regels[^2].Soort);
        Assert.Equal(["15,0", "20,0", ""], mva.Regels[^1].Teksten);
        Assert.DoesNotContain(rapport.Waarschuwingen, w => w.Contains("Verloop", StringComparison.Ordinal));

        // Financiële vaste activa: standen, zonder afschrijvingen.
        var fva = Verlopen(rapport).Single(v => v.Sleutel == "BFva");
        Assert.Equal(["Stand 1 januari 2025", "Afname", "Stand 31 december 2025"], fva.Regels.Select(r => r.Omschrijving));
        Assert.Equal([-2_000m], Regel(fva, "Afname").Bedragen);

        // Vlottende activa hebben geen verloop.
        Assert.DoesNotContain(Verlopen(rapport), v => v.Sleutel == "BVor");
    }

    [Fact]
    public void Een_verkochte_auto_is_een_desinvestering()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(autoAanschaf: 0m, autoCum: 0m));

        var mva = Verlopen(rapport).Single(v => v.Sleutel == "BMva");
        Assert.Equal([0m, -4_105m, -4_105m], Regel(mva, "Desinvesteringen").Bedragen);
        Assert.Equal([-575m, -1_675m, -2_250m], Regel(mva, "Afschrijvingen").Bedragen);
        Assert.Equal([2_603m, 0m, 2_603m], mva.Regels[^2].Bedragen);
    }

    [Fact]
    public void Aangepaste_mutaties_die_niet_aansluiten_geven_een_regel_en_een_waarschuwing()
    {
        var mutaties = new Dictionary<string, IReadOnlyList<VerloopMutatie>>
        {
            ["BMva"] = [new("Investeringen", new Dictionary<string, decimal> { ["BMvaBei"] = 2_000m }), new("Afschrijvingen", new Dictionary<string, decimal> { ["BMvaBei"] = -575m, ["BMvaTev"] = -1_675m })],
        };

        var rapport = RapportOpbouw.Maak(Gegevens(mutaties: mutaties));

        var mva = Verlopen(rapport).Single(v => v.Sleutel == "BMva");
        Assert.True(mva.Aangepast);
        Assert.Equal([734m, 0m, 734m], Regel(mva, RapportOpbouw.NietVerklaard).Bedragen);
        Assert.Contains("Verloop materiële vaste activa sluit niet aan: nog niet verklaard 734.", rapport.Waarschuwingen);
    }

    [Fact]
    public void Een_verloop_bij_een_rekening_staat_met_vorig_jaar_ernaast()
    {
        var rc = RapportOpbouw.RekeningSleutel(Rc);
        var rapport = RapportOpbouw.Maak(Gegevens(verlopen: new() { [rc] = true, ["BMva"] = false }, tweeJaarTerug: new() { [Rc] = 10_000m }));

        Assert.DoesNotContain(Verlopen(rapport), v => v.Sleutel == "BMva");
        var verloop = Verlopen(rapport).Single(v => v.Sleutel == rc);
        Assert.Equal("Verloop Rekening-courant DGA", verloop.Titel);
        Assert.True(verloop.Vergelijkend);
        Assert.Equal(["2025", "2024"], verloop.Kolommen);
        Assert.Equal(["Stand 1 januari", "Toename", "Stand 31 december"], verloop.Regels.Select(r => r.Omschrijving));
        Assert.Equal([15_231m, 10_000m], verloop.Regels[0].Bedragen);
        Assert.Equal([41_845m, 5_231m], verloop.Regels[1].Bedragen);
        Assert.Equal([57_076m, 15_231m], verloop.Regels[2].Bedragen);

        // Het verloop staat direct na de specificatie van de post.
        var blokken = rapport.Hoofdstukken.SelectMany(h => h.Blokken).ToList();
        var specificatie = blokken.FindIndex(b => b is TabelBlok { Tabel.Code: "BVor" });
        Assert.IsType<VerloopBlok>(blokken[specificatie + 1]);
    }

    [Fact]
    public void Vorig_jaar_komt_uit_het_verloop_van_vorig_jaar_of_staat_uit()
    {
        var rc = RapportOpbouw.RekeningSleutel(Rc);
        var vorige = new Dictionary<string, IReadOnlyList<VerloopMutatie>>
        {
            [rc] = [new("Opgenomen gelden", new Dictionary<string, decimal> { [""] = 13_000m }), new("Bijgeschreven rente", new Dictionary<string, decimal> { [""] = 231m })],
        };
        var g = Gegevens(verlopen: new() { [rc] = true }) with { VorigeVerloopMutaties = vorige };

        var verloop = Verlopen(RapportOpbouw.Maak(g)).Single(v => v.Sleutel == rc);
        Assert.Equal(["Stand 1 januari", "Toename", "Opgenomen gelden", "Bijgeschreven rente", "Stand 31 december"], verloop.Regels.Select(r => r.Omschrijving));
        Assert.Equal([15_231m, 2_000m], verloop.Regels[0].Bedragen);
        Assert.Equal([0m, 13_000m], verloop.Regels[2].Bedragen);
        Assert.False(verloop.Doel.ContainsKey(RapportOpbouw.KolomVorig));

        var enkel = Verlopen(RapportOpbouw.Maak(Gegevens(verlopen: new() { [rc] = true }, zonderVorig: [rc]))).Single(v => v.Sleutel == rc);
        Assert.False(enkel.Vergelijkend);
        Assert.Equal(["2025"], enkel.Kolommen);
        Assert.Equal(["Stand 1 januari 2025", "Toename", "Stand 31 december 2025"], enkel.Regels.Select(r => r.Omschrijving));
    }

    [Fact]
    public void Het_eigen_vermogen_heeft_standaard_een_verloop_met_resultaat_en_bestemming()
    {
        var rapport = RapportOpbouw.Maak(Gegevens());

        var ev = Verlopen(rapport).Single(v => v.Sleutel == "BEiv");
        Assert.True(ev.Standaard);
        Assert.Equal(["Aandelenkapitaal", "Overige reserves", "Resultaat boekjaar", "Totaal"], ev.Kolommen);
        Assert.Equal(["Stand 1 januari 2025", "Resultaat 2025", "Bestemming resultaat 2024", "Stand 31 december 2025"], ev.Regels.Select(r => r.Omschrijving));
        Assert.Equal([5_301m, 0m, 78_118m, 83_419m], ev.Regels[0].Bedragen);
        Assert.Equal([0m, 0m, 97_750m, 97_750m], ev.Regels[1].Bedragen);
        Assert.Equal([0m, 78_118m, -78_118m, 0m], ev.Regels[2].Bedragen);
        Assert.Equal([5_301m, 78_118m, 97_750m, 181_169m], ev.Regels[^1].Bedragen);
        Assert.DoesNotContain(rapport.Waarschuwingen, w => w.Contains("Verloop", StringComparison.Ordinal));
    }

    [Fact]
    public void Een_eigen_afschrijvingspercentage_gaat_voor_het_voorstel()
    {
        var rapport = RapportOpbouw.Maak(Gegevens(percentages: new() { ["BMvaTev"] = "20 - 25" }));

        var mva = Verlopen(rapport).Single(v => v.Sleutel == "BMva");
        Assert.Equal(["15,0", "20 - 25", ""], mva.Regels[^1].Teksten);
        Assert.Equal("20 - 25", mva.Percentages!["BMvaTev"]);
    }
}
