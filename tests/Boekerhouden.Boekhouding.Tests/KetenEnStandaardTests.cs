using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Tests.Gedeeld;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class JournaalketenTests
{
    private static readonly Journaalpost Eerste = JournaalTests.Memoriaal("2026-0001", new(2026, 1, 1), ("1100", 5000m), ("0500", -5000m));
    private static readonly Journaalpost Tweede = JournaalTests.Memoriaal("2026-0002", new(2026, 2, 1), ("4300", 12.50m), ("1100", -12.50m));

    [Fact]
    public void Hash_is_vast_en_hangt_af_van_inhoud_en_vorige()
    {
        var hash = Journaalketen.Hash(Eerste, 1, Journaalketen.Begin);

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, Journaalketen.Hash(Eerste with { }, 1, Journaalketen.Begin));
        Assert.NotEqual(hash, Journaalketen.Hash(Eerste, 2, Journaalketen.Begin));
        Assert.NotEqual(hash, Journaalketen.Hash(Eerste, 1, new string('1', 64)));
        Assert.NotEqual(hash, Journaalketen.Hash(Eerste with { Regels = [new("1100", 5000.01m), new("0500", -5000.01m)] }, 1, Journaalketen.Begin));
    }

    [Fact]
    public void Een_heel_keten_is_in_orde()
    {
        Assert.Empty(Journaalketen.Controleer(Keten()));
    }

    [Fact]
    public void Gewijzigd_bedrag_verwijderde_post_en_omgehangen_schakel_worden_gevonden()
    {
        var keten = Keten();
        var gewijzigd = new[] { keten[0], keten[1] with { Post = Tweede with { Omschrijving = "anders" } } };
        var zonderEerste = new[] { keten[1] };
        var omgehangen = new[] { keten[0], keten[1] with { VorigeHash = Journaalketen.Begin } };

        Assert.Equal([2L], Journaalketen.Controleer(gewijzigd));
        Assert.Equal([2L], Journaalketen.Controleer(zonderEerste));
        Assert.Equal([2L], Journaalketen.Controleer(omgehangen));
    }

    private static List<Ketenschakel> Keten()
    {
        var h1 = Journaalketen.Hash(Eerste, 1, Journaalketen.Begin);
        var h2 = Journaalketen.Hash(Tweede, 2, h1);
        return [new(1, Eerste, h1, Journaalketen.Begin), new(2, Tweede, h2, h1)];
    }
}

public sealed class StandaardRekeningschemaTests
{
    [Theory]
    [InlineData(Ondernemingsvorm.BV)]
    [InlineData(Ondernemingsvorm.Eenmanszaak)]
    public void Elke_rgs_code_bestaat_in_rgs_38_met_de_juiste_soort(Ondernemingsvorm vorm)
    {
        var rgs = RgsTestWerkboek.Regels.ToDictionary(r => (string)r[0]!, r => Convert.ToInt32(r[7], System.Globalization.CultureInfo.InvariantCulture));

        foreach (var rekening in StandaardRekeningschema.Rekeningen(vorm).Where(r => r.RgsCode is not null))
        {
            Assert.True(rgs.TryGetValue(rekening.RgsCode!, out var niveau), $"{rekening.Nummer}: {rekening.RgsCode} staat niet in RGS 3.8");
            Assert.InRange(niveau, 3, 4);
        }
    }

    [Theory]
    [InlineData(Ondernemingsvorm.BV)]
    [InlineData(Ondernemingsvorm.Eenmanszaak)]
    public void Schema_is_bruikbaar_voor_automatisch_boeken(Ondernemingsvorm vorm)
    {
        var schema = new Rekeningschema(StandaardRekeningschema.Rekeningen(vorm));

        foreach (var functie in Enum.GetValues<Rekeningfunctie>().Where(f => f != Rekeningfunctie.Geen))
        {
            Assert.NotNull(schema.Enige(functie));
        }

        Assert.All(StandaardRekeningschema.Dagboeken.Where(d => d.Rekeningnummer is not null), d => Assert.NotNull(schema.Zoek(d.Rekeningnummer)));
    }

    [Fact]
    public void Herstel_laadt_geboekte_posten_zonder_controles()
    {
        var journaal = Journaal.Herstel(Testadministratie.Schema(), [Testadministratie.Memoriaal], [JournaalTests.Memoriaal("2026-0001", new(2026, 1, 1), ("1100", 10m), ("0500", -10m))]);

        Assert.Single(journaal.Posten);
        Assert.Equal(10m, journaal.Proefbalans(2026).Regels.Single(r => r.Rekening.Nummer == "1100").Saldo);
    }
}

public sealed class RgsHoofdpostenTests
{
    [Fact]
    public void Balans_en_wv_met_alleen_de_hoofdposten()
    {
        var journaal = new Journaal(new Rekeningschema(StandaardRekeningschema.Rekeningen(Ondernemingsvorm.Eenmanszaak)), StandaardRekeningschema.Dagboeken);
        journaal.Boek(new("MEM", "1", new(2026, 1, 1), "Begin", [new("1100", 1000m), new("0500", -1000m)]));
        journaal.Boek(new("MEM", "2", new(2026, 2, 1), "Telefoon", [new("4310", 30m), new("1100", -30m)]));
        journaal.Boek(new("MEM", "3", new(2026, 2, 2), "Omzet", [new("1100", 500m), new("8000", -500m)]));

        var overzicht = Jaaroverzicht.Maak(journaal.Proefbalans(2026), RgsHoofdposten.Schema);

        Assert.Equal(["BLim"], overzicht.Activa.Select(p => p.Code));
        Assert.Equal("Liquide middelen", overzicht.Activa[0].Naam);
        Assert.Equal(["BEiv"], overzicht.Passiva.Select(p => p.Code));
        Assert.Equal(["WOmz", "WBed"], overzicht.Resultaatposten.Select(p => p.Code));
        Assert.Equal(470m, overzicht.Resultaat);
        Assert.Equal(overzicht.TotaalActiva, overzicht.TotaalPassiva);
    }
}
