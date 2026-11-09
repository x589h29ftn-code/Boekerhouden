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
        Assert.Equal((609m, -21m, 210m, 420m, 0m), (jaar.Jaaraangifte.TeBetalen, jaar.Aangegeven, jaar.Suppletie, jaar.NietAangegeven, jaar.Rest));
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
    public void Rondrekening_per_rubriek_met_perioden_naast_elkaar_administratie_en_afwijking()
    {
        // Het 1e kwartaal is aangegeven voordat V-1 geboekt was: alleen de voorbelasting van de inkoop.
        var vastgelegd = new VastgelegdeAangifte(new(2026, 1, 1), new(2026, 3, 31), [], 21m, DateTimeOffset.UnixEpoch, "Bea");
        var jaar = Btwjaar.Maak(2026, Aangiftefrequentie.Kwartaal, Posten, Schema, Codes, [vastgelegd]);

        var rond = Btwrondrekening.Maak(jaar, Posten, Schema, Codes);
        Rondrekeningregel Regel(string omschrijving, int welke = 0) => rond.Regels.Where(r => r.Omschrijving == omschrijving || r.Code == omschrijving).ElementAt(welke);

        // Alleen rubrieken met cijfers: 1a (omzet en btw), en altijd 5a–5c.
        Assert.Equal(["1a", "1a", "5a", "5b", "5c"], rond.Regels.Where(r => r.Code is not null).Select(r => r.Code));
        var omzet = Regel("1a");
        Assert.Equal([0m, 2000m, 0m, 0m], omzet.Perioden);
        Assert.Equal((2000m, 3000m, 1000m), (omzet.Totaal, omzet.Administratie, omzet.Afwijking));
        var btw = Regel("1a", 1);
        Assert.Equal((420m, 630m, 210m), (btw.Totaal, btw.Administratie, btw.Afwijking));
        var teBetalen = Regel("5c");
        Assert.Equal([-21m, 420m, 0m, 0m], teBetalen.Perioden);
        Assert.Equal((399m, 609m, 210m), (teBetalen.Totaal, teBetalen.Administratie, teBetalen.Afwijking));

        // Aansluiting met het grootboek per periode: de omzet die niet in de aangifte van het 1e kwartaal zat.
        Assert.Equal([1000m, 2000m, 0m, 0m], Regel("Omzet volgens het grootboek").Perioden);
        Assert.Equal([1000m, 0m, 0m, 0m], Regel("Verschil omzet").Perioden);
        Assert.Equal([189m, 420m, 0m, 0m], Regel("Btw geboekt op de btw-rekeningen").Perioden);
        Assert.Equal([210m, 0m, 0m, 0m], Regel("Verschil btw").Perioden);

        // Doorklikken: de boekingsregels achter een getal tellen op tot dat getal.
        var bron = rond.Bronnen(omzet.Sleutel!, new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(["V-1", "V-2"], bron.Select(b => b.Post.Boekstuk));
        Assert.Equal(3000m, bron.Sum(b => b.Bedrag));
        Assert.Equal(609m, rond.Bronnen(teBetalen.Sleutel!, new(2026, 1, 1), new(2026, 12, 31)).Sum(b => b.Bedrag));
        Assert.Equal(1000m, rond.Bronnen(Regel("Omzet volgens het grootboek").Sleutel!, new(2026, 1, 1), new(2026, 3, 31)).Sum(b => b.Bedrag));
        Assert.Equal("1e kw", jaar.Perioden[0].Periode.Kort);
    }

    [Fact]
    public void Jaaropstelling_omzet_en_btw_onder_elkaar_met_afdrachten_per_periode_en_suppletie()
    {
        var vastgelegd = new VastgelegdeAangifte(new(2026, 1, 1), new(2026, 3, 31), [], 21m, DateTimeOffset.UnixEpoch, "Bea");
        var jaar = Btwjaar.Maak(2026, Aangiftefrequentie.Kwartaal, Posten, Schema, Codes, [vastgelegd]);

        var regels = Btwrondrekening.Maak(jaar, Posten, Schema, Codes).Jaaropstelling();
        Jaaropstellingregel Regel(string tekst) => regels.First(r => r.Soort != Rondrekeningsoort.Kop && r.Omschrijving.StartsWith(tekst, StringComparison.Ordinal));

        Assert.Equal((3000m, 630m), (Regel("Omzet hoog tarief").Omzet, Regel("Omzet hoog tarief").Btw));
        Assert.Equal((3000m, 0m), (Regel("Netto-omzet").Omzet, Regel("Overige verschillen").Omzet));
        Assert.Equal(21m, Regel("Voorbelasting").Omzet);
        Assert.Equal(609m, Regel("Te betalen omzetbelasting").Btw);

        // De afdrachten in de frequentie van de administratie: het 1e kwartaal aangegeven, het 2e nog niet.
        Assert.Equal(-21m, Regel("1e kwartaal 2026").Omzet);
        Assert.Null(Regel("2e kwartaal 2026 (nog niet aangegeven)").Omzet);
        Assert.Equal((-21m, 420m), (Regel("Totaal aangegeven").Btw, Regel("Nog niet aangegeven").Btw));
        Assert.Equal(("Suppletie omzetbelasting 2026: te betalen", 210m), (Regel("Suppletie").Omschrijving, Regel("Suppletie").Btw));
        Assert.Equal(609m, Regel("Saldo btw-rekeningen").Btw);

        var maanden = Btwrondrekening.Maak(Btwjaar.Maak(2026, Aangiftefrequentie.Maand, Posten, Schema, Codes, []), Posten, Schema, Codes).Jaaropstelling();
        Assert.Contains(maanden, r => r.Omschrijving == "februari 2026 (nog niet aangegeven)");
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
