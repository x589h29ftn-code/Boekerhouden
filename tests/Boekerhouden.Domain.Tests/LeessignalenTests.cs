using Boekerhouden.Domain.Doorlezen;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class LeessignalenTests
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
        C("BVor", "Vorderingen"),
        C("BVorOvr", "Overige vorderingen"),
        C("BVorOvrRcb", "Rekening-courant bestuurders"),
        C("BLim", "Liquide middelen"),
        C("BLimBan", "Banken"),
        C("BEiv", "Eigen vermogen", Saldokant.Credit),
        C("BEivGok", "Aandelenkapitaal", Saldokant.Credit),
        C("W", "Winst-en-verliesrekening"),
        C("WOmz", "Netto-omzet", Saldokant.Credit),
        C("WOmzNod", "Diensten", Saldokant.Credit),
        C("WKpr", "Kostprijs van de omzet"),
        C("WKprInh", "Inkoopwaarde"),
        C("WAfs", "Afschrijvingen"),
        C("WAfsAmv", "Afschrijvingen materiële vaste activa"),
        C("WFbe", "Financiële baten en lasten", Saldokant.Credit),
        C("WFbeRlm", "Rentebaten", Saldokant.Credit),
        C("WFbeRlmRrb", "Rentebaten rekening-courant bestuurders", Saldokant.Credit),
        C("WBel", "Belastingen"),
        C("WBelBgr", "Belastingen over de winst"),
    ]);

    /// <summary>Een gezonde BV: alles sluit en geen bijzonderheden.</summary>
    private static readonly Dictionary<string, (decimal Huidig, decimal Vorig)> Gezond = new()
    {
        ["BMvaBeiVvp"] = (10_000m, 8_000m),
        ["BVorOvrRcb"] = (5_000m, 5_000m),
        ["BLimBan"] = (20_000m, 18_000m),
        ["BEivGok"] = (-30_000m, -28_000m),
        ["WOmzNod"] = (-100_000m, -95_000m),
        ["WKprInh"] = (60_000m, 57_000m),
        ["WAfsAmv"] = (2_000m, 2_000m),
        ["WFbeRlmRrb"] = (-200m, -200m),
        ["WBelBgr"] = (5_000m, 5_000m),
    };

    private static IReadOnlyList<Leessignaal> Signalen(
        Action<Dictionary<string, (decimal, decimal)>>? wijzig = null,
        RapportSoort soort = RapportSoort.Jaarrekening,
        IReadOnlyCollection<GrondslagBlok>? grondslagen = null,
        IReadOnlyList<string>? waarschuwingen = null,
        IReadOnlySet<string>? toegelicht = null)
    {
        var saldi = new Dictionary<string, (decimal, decimal)>(Gezond);
        wijzig?.Invoke(saldi);
        var rekeningen = saldi.Select((s, i) => (Code: s.Key, Saldo: new RekeningSaldo(Guid.NewGuid(), $"{1000 + i}", s.Key, s.Value.Item1, s.Value.Item2))).ToList();
        var balans = PostBalans.Bereken(Schema, [], rekeningen.ToDictionary(r => r.Saldo.RekeningId, r => new KoppelDoel(r.Code)), [.. rekeningen.Select(r => r.Saldo)]);
        return Leessignalen.Bepaal(new LeesGegevens(soort, Schema, balans, c => Schema.Zoek(c)?.Omschrijving ?? c, grondslagen, waarschuwingen ?? [], toegelicht ?? new HashSet<string>()));
    }

    private static IEnumerable<string> Sleutels(IEnumerable<Leessignaal> signalen) => signalen.Select(s => s.Sleutel);

    [Fact]
    public void Een_gezonde_jaarrekening_geeft_geen_signalen() => Assert.Empty(Signalen());

    [Fact]
    public void Waarschuwingen_uit_het_rapport_worden_signalen()
    {
        var s = Assert.Single(Signalen(waarschuwingen: ["Grondslagen nog niet gekozen.", "Grondslagen nog niet gekozen."]));
        Assert.Equal(("rapport:Grondslagen nog niet gekozen.", "Grondslagen nog niet gekozen."), (s.Sleutel, s.Titel));
    }

    [Fact]
    public void Negatief_eigen_vermogen_zonder_alinea_over_de_continuiteit()
    {
        static void Negatief(Dictionary<string, (decimal, decimal)> s) => s["BEivGok"] = (12_000m, -28_000m);

        var s = Assert.Single(Signalen(Negatief), x => x.Sleutel == "negatief-vermogen");
        Assert.Equal("Negatief eigen vermogen", s.Titel);
        Assert.Contains("-12.000", s.Tekst, StringComparison.Ordinal);

        GrondslagBlok continuiteit = new("continuiteit", "Continuïteit", "…", Opgenomen: true, EigenTekst: false);
        Assert.DoesNotContain("negatief-vermogen", Sleutels(Signalen(Negatief, grondslagen: [continuiteit])));
        Assert.Equal("Negatief ondernemingsvermogen", Signalen(Negatief, RapportSoort.AangifteBijlage).Single(x => x.Sleutel == "negatief-vermogen").Titel);
    }

    [Fact]
    public void Winst_zonder_vennootschapsbelasting_alleen_bij_de_bv()
    {
        static void ZonderBelasting(Dictionary<string, (decimal, decimal)> s) => s["WBelBgr"] = (0m, 5_000m);

        Assert.Contains("winst-zonder-vpb", Sleutels(Signalen(ZonderBelasting)));
        Assert.DoesNotContain("winst-zonder-vpb", Sleutels(Signalen(ZonderBelasting, RapportSoort.AangifteBijlage)));
    }

    [Fact]
    public void Rekening_courant_met_de_bestuurder_zonder_rente()
    {
        var s = Assert.Single(Signalen(s => s["WFbeRlmRrb"] = (0m, -200m)));
        Assert.Equal(("rc-zonder-rente", "BVor"), (s.Sleutel, s.Post));
        Assert.Contains("5.000", s.Tekst, StringComparison.Ordinal);
        Assert.DoesNotContain("rc-zonder-rente", Sleutels(Signalen(s => (s["WFbeRlmRrb"], s["BVorOvrRcb"]) = ((0m, -200m), (0m, 5_000m)))));
    }

    [Fact]
    public void Afschrijvingen_en_vaste_activa_horen_bij_elkaar()
    {
        Assert.Equal(["afschrijving-zonder-activa"], Sleutels(Signalen(s => s["BMvaBeiVvp"] = (0m, 0m))).Where(k => !k.StartsWith("verschil", StringComparison.Ordinal)));
        Assert.Equal(["activa-zonder-afschrijving"], Sleutels(Signalen(s => s["WAfsAmv"] = (0m, 2_000m))).Where(k => !k.StartsWith("verschil", StringComparison.Ordinal)));
    }

    [Fact]
    public void Brutomarge_die_meer_dan_vijf_procentpunt_afwijkt()
    {
        // 40% naar 34%: wel; 40% naar 37%: niet.
        var s = Assert.Single(Signalen(s => s["WKprInh"] = (66_000m, 57_000m)));
        Assert.Equal("brutomarge", s.Sleutel);
        Assert.Contains("34,0%", s.Tekst, StringComparison.Ordinal);
        Assert.Contains("40,0%", s.Tekst, StringComparison.Ordinal);
        Assert.Empty(Signalen(s => s["WKprInh"] = (63_000m, 57_000m)));
    }

    [Fact]
    public void Groot_verschil_met_vorig_jaar_tenzij_er_een_cijferbeoordeling_is()
    {
        // Liquide middelen 20.000 naar 50.000: boven 5% van de omvang (100.000) en boven 25%.
        static void Bank(Dictionary<string, (decimal, decimal)> s) => s["BLimBan"] = (50_000m, 18_000m);

        var s = Assert.Single(Signalen(Bank));
        Assert.Equal(("verschil:BLim", "BLim", "Liquide middelen: groot verschil met vorig jaar"), (s.Sleutel, s.Post, s.Titel));
        Assert.Contains("+177,8%", s.Tekst, StringComparison.Ordinal);
        Assert.Empty(Signalen(Bank, toegelicht: new HashSet<string> { "BLim" }));

        // Onder de drempel van 5% van de omvang: geen signaal.
        Assert.Empty(Signalen(s => s["BLimBan"] = (22_000m, 18_000m)));
    }

    [Fact]
    public void Balanspost_aan_de_andere_kant()
    {
        var s = Assert.Single(Signalen(s => s["BLimBan"] = (-3_000m, -1_000m)));
        Assert.Equal("andere-kant:BLim", s.Sleutel);
        Assert.Contains("credit (3.000)", s.Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Post_met_saldo_waarvan_de_grondslag_uit_staat()
    {
        GrondslagBlok Mva(bool opgenomen) => new("mva", "Materiële vaste activa", "…", opgenomen, false) { Codes = ["BMva"] };

        var s = Assert.Single(Signalen(grondslagen: [Mva(false)]));
        Assert.Equal(("grondslag:BMva", "Materiële vaste activa zonder grondslag"), (s.Sleutel, s.Titel));
        Assert.Empty(Signalen(grondslagen: [Mva(true)]));
        Assert.Empty(Signalen(grondslagen: [Mva(false)], soort: RapportSoort.AangifteBijlage));
    }
}
