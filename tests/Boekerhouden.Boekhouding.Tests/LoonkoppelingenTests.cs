using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Loon;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>Rekeningen van het salarispakket koppelen aan het rekeningschema, en de boeking.</summary>
public sealed class LoonkoppelingenTests
{
    private static readonly Rekeningschema Schema = new(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));

    private static readonly Loonjournaal Journaal = new("Voorbeeld B.V.", "2026-1", "2026-1",
        [new("4000", "Brutolonen", 7_000m), new("1500", "Af te dragen loonheffing", -2_500m), new("2100", "Netto loon", -4_000m), new("4011", "Pensioen", -500m)], 7_000m, 7_000m);

    [Fact]
    public void Eerst_op_nummer_en_naam_daarna_onthouden()
    {
        // 1500 is in het schema "Te vorderen omzetbelasting": niet hetzelfde nummer, wel de gelijkende naam (1520).
        Assert.False(Loonkoppelingen.Lijkt("Af te dragen loonheffing", "Te vorderen omzetbelasting"));
        Assert.True(Loonkoppelingen.Lijkt("Brutolonen", "Lonen"));
        // De eerste keer (niets onthouden): op nummer en naam; Netto loon kiest de gebruiker.
        var koppelingen = Loonkoppelingen.Stel(Journaal, Schema, new Dictionary<string, string>());

        Assert.Equal(
            [("4000", Loonkoppelbron.ZelfdeNummer), ("1520", Loonkoppelbron.ZelfdeNaam), (null, Loonkoppelbron.Geen), ("4020", Loonkoppelbron.ZelfdeNaam)],
            koppelingen.Select(k => (k.Rekening, k.Bron)));

        // Daarna wat onthouden is.
        var alles = new Dictionary<string, string> { ["4000"] = "4000", ["1500"] = "1520", ["2100"] = "1800", ["4011"] = "4020" };
        Assert.All(Loonkoppelingen.Stel(Journaal, Schema, alles), k => Assert.Equal(Loonkoppelbron.Onthouden, k.Bron));
    }

    [Fact]
    public void Boeking_met_de_namen_uit_het_salarispakket_en_controle()
    {
        var post = Loonkoppelingen.Boeking("MEM", new(2026, 1, 31), "Loonjournaal januari 2026", [("4000", Journaal.Regels[0]), ("1520", Journaal.Regels[1]), ("1600", Journaal.Regels[2]), ("1600", Journaal.Regels[3])]);

        Assert.Equal(0m, post.Regels.Sum(r => r.Bedrag));
        Assert.Equal("Netto loon", post.Regels[2].Omschrijving);
        Assert.Empty(Journaal.Controle());
        Assert.Contains("Niet in balans", (Journaal with { Regels = [.. Journaal.Regels.Skip(1)] }).Controle().First(), StringComparison.Ordinal);
    }

    [Fact]
    public void Een_nieuwe_rekening_in_het_salarispakket_kiest_de_gebruiker_met_een_voorstel()
    {
        var onthouden = new Dictionary<string, string> { ["4000"] = "4000", ["1500"] = "1520", ["2100"] = "1800" };

        var koppelingen = Loonkoppelingen.Stel(Journaal, Schema, onthouden);

        // 4011 Pensioen was er de vorige keer niet: geen keuze, wel een voorstel (4020 Pensioenpremies).
        var nieuw = koppelingen.Single(k => k.Regel.Nummer == "4011");
        Assert.Equal((null, Loonkoppelbron.Nieuw, "4020"), (nieuw.Rekening, nieuw.Bron, nieuw.Voorstel));
        Assert.All(koppelingen.Where(k => k.Regel.Nummer != "4011"), k => Assert.Equal(Loonkoppelbron.Onthouden, k.Bron));
    }
}
