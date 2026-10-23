using Boekerhouden.Domain.Correcties;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Tests;

public sealed class BoekingscontroleTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    [Fact]
    public void Boeking_in_evenwicht_is_goed()
    {
        Assert.Empty(Boekingscontrole.Controleer("Afschrijving inventaris", [new(A, 2340m), new(B, -2340m)]));
        Assert.Empty(Boekingscontrole.Controleer("Verdeling", [new(A, 100.10m), new(B, 50.05m), new(C, -150.15m)]));
    }

    [Fact]
    public void Boeking_niet_in_evenwicht_wordt_geweigerd_met_het_verschil()
    {
        var fouten = Boekingscontrole.Controleer("Te weinig credit", [new(A, 1000m), new(B, -900m)]);

        var fout = Assert.Single(fouten);
        Assert.Contains("niet in evenwicht", fout, StringComparison.Ordinal);
        Assert.Contains("verschil € 100,00", fout, StringComparison.Ordinal);
    }

    [Fact]
    public void Omschrijving_twee_regels_bedragen_en_decimalen_worden_gecontroleerd()
    {
        Assert.Contains(Boekingscontrole.Controleer(" ", [new(A, 1m), new(B, -1m)]), f => f.Contains("omschrijving", StringComparison.Ordinal));
        Assert.Contains(Boekingscontrole.Controleer("Eén regel", [new(A, 0m)]), f => f.Contains("minstens twee", StringComparison.Ordinal));
        Assert.Contains(Boekingscontrole.Controleer("Nul", [new(A, 5m), new(B, 0m), new(C, -5m)]), f => f == "Regel 2 heeft geen bedrag.");
        Assert.Contains(Boekingscontrole.Controleer("Centen", [new(A, 0.005m), new(B, -0.005m)]), f => f.Contains("twee decimalen", StringComparison.Ordinal));
    }
}

public sealed class CorrectieEffectTests
{
    private static readonly Guid Inventaris = Guid.NewGuid();
    private static readonly Guid Afschrijving = Guid.NewGuid();
    private static readonly Guid Reserve = Guid.NewGuid();
    private static readonly Guid Nieuw = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Codes = new()
    {
        [Inventaris] = "BMvaBedCae",
        [Afschrijving] = "WAfsAfsMvaBed",
        [Reserve] = "BEivOvr",
    };

    [Fact]
    public void Extra_afschrijving_verlaagt_resultaat_en_eigen_vermogen()
    {
        var effect = CorrectieEffect.Bereken([new(Afschrijving, 2340m), new(Inventaris, -2340m)], Codes.GetValueOrDefault);

        Assert.Equal(new CorrectieEffect(-2340m, -2340m, 0), effect);
    }

    [Fact]
    public void Boeking_op_de_reserves_raakt_alleen_het_eigen_vermogen()
    {
        var effect = CorrectieEffect.Bereken([new(Inventaris, 500m), new(Reserve, -500m)], Codes.GetValueOrDefault);

        Assert.Equal(0m, effect.Resultaat);
        Assert.Equal(500m, effect.EigenVermogen);
    }

    [Fact]
    public void Ongekoppelde_rekening_wordt_geteld_en_effecten_tellen_op()
    {
        var a = CorrectieEffect.Bereken([new(Afschrijving, 100m), new(Nieuw, -100m)], Codes.GetValueOrDefault);
        var b = CorrectieEffect.Bereken([new(Afschrijving, -40m), new(Inventaris, 40m)], Codes.GetValueOrDefault);

        Assert.Equal(1, a.Ongekoppeld);
        Assert.Equal(new CorrectieEffect(-60m, -60m, 1), a.Plus(b));
    }
}

public sealed class GecorrigeerdSaldoTests
{
    [Fact]
    public void Administratie_plus_correcties_per_jaar_en_afgerond()
    {
        var saldi = GecorrigeerdSaldo.Bereken(
            new Dictionary<string, decimal> { ["kas"] = 100.40m, ["bank"] = 200.40m, ["ev"] = -300.80m },
            new Dictionary<string, decimal> { ["bank"] = -50m, ["kosten"] = 50m },
            new Dictionary<string, decimal> { ["kas"] = 80m, ["ev"] = -80m },
            new Dictionary<string, decimal>());

        Assert.Equal(new GecorrigeerdSaldo(201m, -50m, null, 0m), saldi["bank"]);
        Assert.Equal(151m, saldi["bank"].Huidig);
        Assert.Equal(50m, saldi["kosten"].Huidig);
        Assert.Null(saldi["kosten"].Administratie);
        Assert.Null(saldi["kosten"].Vorig);
        Assert.Equal(80m, saldi["kas"].Vorig);
        Assert.Equal(0m, saldi.Values.Sum(s => s.Huidig ?? 0));
    }

    [Fact]
    public void Correcties_van_vorig_jaar_tellen_mee_in_de_vergelijkende_cijfers()
    {
        var saldi = GecorrigeerdSaldo.Bereken(
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal> { ["inventaris"] = 10_000m },
            new Dictionary<string, decimal> { ["inventaris"] = -1_000m });

        Assert.Equal(9_000m, saldi["inventaris"].Vorig);
        Assert.Null(saldi["inventaris"].Huidig);
    }
}

public sealed class PostBalansMetCorrectiesTests
{
    private static int volgorde;

    private static RgsCode C(string code, Saldokant? kant = Saldokant.Debet) =>
        new(code, code.Length == 1 ? 1 : 1 + ((code.Length - 1) / 3), code.Length == 1 ? null : code[..Math.Max(1, code.Length - 3)],
            code, code, kant, null, ++volgorde, null, false, new HashSet<string> { "Basis" }, new HashSet<string>());

    private static readonly RgsSchema Schema = new("test",
    [
        C("B", null), C("BMva"), C("BMvaBed"), C("BLim"), C("BLimKas"),
        C("W", null), C("WAfs"), C("WAfsAfs"),
    ]);

    [Fact]
    public void Posttotalen_tonen_administratie_correcties_en_verschil()
    {
        var inventaris = Guid.NewGuid();
        var afschrijving = Guid.NewGuid();
        var kas = Guid.NewGuid();
        var balans = PostBalans.Bereken(
            Schema,
            [],
            new Dictionary<Guid, KoppelDoel> { [inventaris] = new("BMvaBed"), [afschrijving] = new("WAfsAfs"), [kas] = new("BLimKas") },
            [
                new RekeningSaldo(inventaris, "0200", "Inventaris", 7_660m, 12_000m, Correcties: -2_340m),
                new RekeningSaldo(afschrijving, "4800", "Afschrijving", 2_340m, null, Correcties: 2_340m),
                new RekeningSaldo(kas, "1000", "Kas", 500m, 400m),
            ]);

        var mva = balans.Totaal("BMva");
        Assert.Equal(10_000m, mva.Administratie);
        Assert.Equal(-2_340m, mva.Correcties);
        Assert.Equal(-4_340m, mva.Verschil);
        Assert.Equal(-2_340m, balans.Balans.Correcties);
        Assert.Equal(2_340m, balans.WinstEnVerlies.Correcties);
        Assert.Equal(-2_340m, balans.ResultaatHuidig);
    }
}
