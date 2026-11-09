using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>De btw van een jaar: perioden, rondrekening en de aansluiting met de btw-rekeningen.</summary>
public sealed class BtwjaarTests
{
    private static readonly Rekeningschema Schema = new(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.BV));
    private static readonly IReadOnlyList<BtwCode> Codes = BtwCodes.Standaard;

    private static Journaalpost Verkoop(string nummer, DateOnly datum, decimal omzet) =>
        Factuurboeker.Boek(new Factuur(Factuursoort.Verkoop, "KLANT", nummer, datum, datum.AddDays(14), nummer, [new Factuurregel("Advies", "8000", "V21", omzet)]),
            StandaardRekeningschema.Dagboeken.Single(d => d.Code == "VRK"), nummer, Schema, Codes).Post;

    private static Journaalpost Inkoop(string nummer, DateOnly datum, decimal kosten) =>
        Factuurboeker.Boek(new Factuur(Factuursoort.Inkoop, "LEV", nummer, datum, datum.AddDays(14), nummer, [new Factuurregel("Papier", "4300", "I21", kosten)]),
            StandaardRekeningschema.Dagboeken.Single(d => d.Code == "INK"), nummer, Schema, Codes).Post;

    // De btw van het 4e kwartaal van vorig jaar (500 te betalen) staat in de beginstand en wordt in januari betaald.
    private static readonly Journaalpost[] Posten =
    [
        new("", "begin", new(2025, 12, 31), "Beginstand", [new("1510", -500m), new("0500", 500m)]),
        Verkoop("V-1", new(2026, 2, 10), 1000m),
        Inkoop("I-1", new(2026, 3, 5), 100m),
        new("BNK", "2026-0001", new(2026, 1, 25), "Btw 4e kwartaal 2025", [new("1510", 500m), new("1100", -500m)]),
        Verkoop("V-2", new(2026, 5, 2), 2000m),
    ];

    [Fact]
    public void Perioden_per_kwartaal_met_rondrekening_tegen_wat_is_aangegeven()
    {
        // Het 1e kwartaal is aangegeven voordat V-1 geboekt was: alleen de inkoop (21 terug).
        var vastgelegd = new VastgelegdeAangifte(new(2026, 1, 1), new(2026, 3, 31), [], 21m, DateTimeOffset.UnixEpoch, "Bea");

        var jaar = Btwjaar.Maak(2026, Aangiftefrequentie.Kwartaal, Posten, Schema, Codes, [vastgelegd]);

        Assert.Equal(4, jaar.Perioden.Count);
        Assert.Equal(["1e kwartaal 2026", "2e kwartaal 2026", "3e kwartaal 2026", "4e kwartaal 2026"], jaar.Perioden.Select(p => p.Periode.Naam));
        Assert.Equal([210m - 21m, 420m, 0m, 0m], jaar.Perioden.Select(p => p.Berekend.TeBetalen));
        Assert.Equal(210m, jaar.Perioden[0].Verschil);
        Assert.Equal((609m, -21m, 630m), (jaar.Jaaraangifte.TeBetalen, jaar.Aangegeven, jaar.Suppletie));
        Assert.Equal(["2e kwartaal 2026"], jaar.Open.Select(p => p.Periode.Naam));
        Assert.Equal(12, Btwjaar.Maak(2026, Aangiftefrequentie.Maand, Posten, Schema, Codes, []).Perioden.Count);
    }

    [Fact]
    public void Aansluiting_begin_plus_aangiften_plus_betalingen_is_het_saldo_van_de_btw_rekeningen()
    {
        var q1 = Btwaansluiting.Bereken(Posten, Schema, Codes, new(2026, 1, 1), new(2026, 3, 31));
        var q2 = Btwaansluiting.Bereken(Posten, Schema, Codes, new(2026, 1, 1), new(2026, 6, 30));

        Assert.Equal((500m, 189m, -500m, 189m, 0m), (q1.Begin, q1.Aangiften, q1.Overig, q1.Saldo, q1.Verschil));
        Assert.Equal((609m, 609m, 0m), (q2.Aangiften, q2.Saldo, q2.Verschil));

        // Btw met de hand op de btw-rekening met een code zonder rubriek: valt in geen aangifte, dus een verschil.
        Journaalpost[] scheef = [.. Posten, new("MEM", "2026-0001", new(2026, 3, 31), "Correctie", [new("1510", -10m, BtwCode: "GB"), new("4900", 10m)])];
        Assert.Equal(10m, Btwaansluiting.Bereken(scheef, Schema, Codes, new(2026, 1, 1), new(2026, 3, 31)).Verschil);
    }

    [Fact]
    public void Btw_rekeningen_op_functie_of_rgs_groep()
    {
        var ovpa = new Rekeningschema(Rekeningschemas.Rekeningen(Schemakeuze.Ovpa, Ondernemingsvorm.BV));

        Assert.True(Btwaangifte.IsBtwrekening(Schema.Zoek("1500")!));
        Assert.True(Btwaangifte.IsBtwrekening(ovpa.Zoek("1820")!));
        Assert.True(Btwaangifte.IsBtwrekening(ovpa.Zoek("1807")!));
        Assert.False(Btwaangifte.IsBtwrekening(ovpa.Zoek("1780")!));
    }
}
