using Boekerhouden.Boekhouding.Activa;

namespace Boekerhouden.Boekhouding.Tests;

public sealed class ActivastaatlezerTests
{
    private static Staatcel T(string tekst) => new(tekst);

    private static Staatcel G(decimal getal) => new(null, getal);

    private static readonly Staatcel L = Staatcel.Leeg;

    /// <summary>
    /// Een verzonnen staat zoals een samenstelsjabloon hem maakt: een groepskop boven de kolommen, de kop over meerdere
    /// regels, een regel met €, groepen met lege sjabloonregels, subtotalen en een eindtotaal.
    /// </summary>
    internal static Staattabel Sjabloon(decimal subtotaalInventaris = 4766m) => new(
    [
        [T("Bakkerij De Korst B.V. te Ergens")],
        [L, L, L, T("Aanschafwaarde"), L, L, L, T("Afschrijvingen")],
        [L, T("Omschrijving"), T("Datum"), T("Aanschaf-\nfingen tot\n01-01-2025"), T("Inves-\nteringen\n2025"), T("Desinves-\nteringen\n2025"), T("Aanschaf-\nfingen t/m\n31-12-2025"),
            T("Afschrij-\nvingen tot\n01-01-2025"), T("Afschrij-\nvingen \n2025"), T("Afschrij-\nving desin-\nvesteringen"), T("Afschrij-\nvingen t/m\n31-12-2025"), T("Boekwaarde per \n31-12-2025"), T("Residu-\nwaarde"), T("Afschrij-\nvingsper-\ncentage")],
        [L, L, L, T("€"), T("€"), T("€"), T("€"), T("€"), T("€"), T("€"), T("€"), T("€"), T("€"), T("%")],
        [L, T("Materiële vaste activa")],
        [L, T("Bedrijfsgebouwen en -terreinen")],
        [L, T("Gebouwen 1"), L, L, L, L, G(0)],
        [L, T("Gebouwen 2"), L, L, L, L, G(0)],
        [L, T("Inventaris")],
        [L, T("Oven"), new(null, Datum: new DateOnly(2022, 2, 11)), G(3000), L, L, G(3000), G(1450), G(600), L, G(2050), G(950), L, G(20)],
        [L, T("Kassa"), L, G(800), L, L, G(800), G(800), L, L, G(800), G(0), L, G(20)],
        [L, T("Schilderij"), L, G(966), L, L, G(966), L, L, L, L, G(966), G(966)],
        [L, L, L, G(4766), L, L, G(subtotaalInventaris), G(2250), G(600), L, G(2850), G(1916), G(966)],
        [L, T("Vervoermiddelen")],
        [L, T("Bestelbus"), new(null, Datum: new DateOnly(2024, 7, 1)), L, G(24000), L, G(24000), L, G(2200), L, G(2200), G(21800), G(2000), G(20)],
        [L, L, L, L, G(24000), L, G(24000), L, G(2200), L, G(2200), G(21800), G(2000)],
        [L, T("Totaal materiële vaste activa"), L, G(4766), G(24000), L, G(28766), G(2250), G(2800), L, G(5050), G(23716), G(2966)],
    ]);

    [Fact]
    public void Een_staat_uit_een_sjabloon_met_groepen_subtotalen_en_eindtotaal()
    {
        var staat = Activastaatlezer.Lees(Sjabloon());

        Assert.Equal(2025, staat.Jaar);
        Assert.True(staat.Sluit, string.Join(" | ", staat.Signalen));
        Assert.Equal(["Inventaris", "Vervoermiddelen"], staat.Groepen.Select(g => g.Naam));
        Assert.Equal(["Oven", "Kassa", "Schilderij", "Bestelbus"], staat.Regels.Select(r => r.Omschrijving));
        var oven = staat.Regels.First();
        Assert.Equal((new DateOnly(2022, 2, 11), 3000m, 2050m, 950m, 20m, 600m), (oven.Datum!.Value, oven.AanschafEind, oven.AfschrijvingEind, oven.Boekwaarde, oven.Percentage!.Value, oven.AfschrijvingJaar));
        Assert.Equal((966m, (decimal?)null), (staat.Regels.Single(r => r.Omschrijving == "Schilderij").Restwaarde, staat.Regels.Single(r => r.Omschrijving == "Schilderij").Percentage));
        Assert.Equal(new Staattotaal(28766m, 5050m, 23716m, 2800m), staat.Totaal);
    }

    [Fact]
    public void Een_subtotaal_dat_niet_sluit_geeft_een_signaal()
    {
        var staat = Activastaatlezer.Lees(Sjabloon(subtotaalInventaris: 4800m));

        Assert.False(staat.Sluit);
        Assert.Contains(staat.Signalen, s => s.Tekst.StartsWith("Subtotaal Inventaris: aanschafwaarde van de regels 4.766,00, op de staat 4.800,00", StringComparison.Ordinal));
    }

    [Fact]
    public void Een_eenvoudige_lijst_met_andere_koppen_en_bedragen_als_tekst()
    {
        // Zoals een staat uit een PDF of een eigen Excel-lijst: één kopregel, bedragen als tekst, het percentage als 0,2.
        var tabel = new Staattabel(
        [
            [T("Activum"), T("Aankoopdatum"), T("Aanschafwaarde"), T("Afschrijving t/m vorig jaar"), T("Afschrijving 2025"), T("Afschrijving t/m 2025"), T("Restwaarde"), T("Afschr. %")],
            [T("Laptop"), T("15-03-2024"), T("1.200,00"), T("180,00"), T("240,00"), T("420,00"), T("0"), G(0.2m)],
            [T("Bureau"), T("01-09-2023"), T("2.500,00"), T("208,33"), T("250,00"), T("458,33"), T("0"), T("10%")],
            [T("Totaal"), L, T("3.700,00"), T("388,33"), T("490,00"), T("878,33")],
        ]);

        var staat = Activastaatlezer.Lees(tabel);

        Assert.True(staat.Sluit, string.Join(" | ", staat.Signalen));
        Assert.Equal(2025, staat.Jaar);
        Assert.Equal([(new DateOnly(2024, 3, 15), 20m, 780m), (new DateOnly(2023, 9, 1), 10m, 2041.67m)], staat.Regels.Select(r => (r.Datum!.Value, r.Percentage!.Value, r.Boekwaarde)));
    }

    [Fact]
    public void Zonder_herkenbare_kop_een_duidelijke_melding()
    {
        var staat = Activastaatlezer.Lees(new Staattabel([[T("Naam"), T("Bedrag")], [T("Iets"), G(10)]]));

        Assert.Empty(staat.Regels);
        Assert.Contains("kopregel", staat.Signalen.Single().Tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void Overname_per_1_januari_met_resterende_looptijd_niet_afschrijven_en_geschatte_datum()
    {
        var staat = Activastaatlezer.Lees(Sjabloon());
        var begin = new DateOnly(2026, 1, 1);
        Overname O(string naam) => Staatovername.Van(staat.Regels.Single(r => r.Omschrijving == naam), begin)!;

        // Oven: 3000 tegen 20% is 50 per maand; nog 950 af: 19 maanden.
        Assert.Equal((Afschrijvingsmethode.Lineair, 19, 2050m, false, begin), (O("Oven").Methode, O("Oven").Looptijd, O("Oven").AfgeschrevenVoorBegin, O("Oven").DatumGeschat, O("Oven").BeginAfschrijving));
        // Kassa: helemaal afgeschreven; geen datum: geschat 60 maanden terug (800 / 13,33 per maand).
        Assert.Equal((1, 800m, true, new DateOnly(2021, 1, 1)), (O("Kassa").Looptijd, O("Kassa").AfgeschrevenVoorBegin, O("Kassa").DatumGeschat, O("Kassa").Aanschafdatum));
        // Schilderij: geen percentage, restwaarde gelijk aan de aanschaf: niet afschrijven.
        Assert.Equal((Afschrijvingsmethode.Geen, 966m), (O("Schilderij").Methode, O("Schilderij").Restwaarde));
        // Bestelbus: (24000 − 2000) × 20% = 366,67 per maand; nog 19800 af: 54 maanden.
        Assert.Equal((54, 2200m, 2000m), (O("Bestelbus").Looptijd, O("Bestelbus").AfgeschrevenVoorBegin, O("Bestelbus").Restwaarde));

        // Elke overname is een geldig actief.
        Assert.All(staat.Regels.Select(r => Staatovername.Van(r, begin)!), o => Assert.Empty(new Activum(o.Omschrijving, o.Aanschafdatum, o.Aanschafwaarde, o.Restwaarde, o.BeginAfschrijving,
            o.Methode == Afschrijvingsmethode.Geen ? 0 : o.Looptijd, o.Methode, "0200", "0210", "4800", o.AfgeschrevenVoorBegin).Controleer()));
    }
}
