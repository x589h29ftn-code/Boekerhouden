using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>
/// Overzichten › Dagboeken: per dagboek wat er geboekt is. Inkoop en verkoop als factuur (exclusief, btw, totaal; een
/// creditnota negatief), bank en kas met het bedrag op de bankrekening (bij positief, af negatief), memoriaal met het
/// totaal van de boeking; de tabbladen in de volgorde inkoop, verkoop, bank, kas, memoriaal.
/// </summary>
public sealed class DagboekoverzichtTests
{
    private static readonly Rekeningschema Schema = new(
    [
        new Rekening("1100", "Bank", Rekeningsoort.Balans, Functie: Rekeningfunctie.Bank),
        new Rekening("1000", "Kas", Rekeningsoort.Balans, Functie: Rekeningfunctie.Kas),
        new Rekening("1300", "Debiteuren", Rekeningsoort.Balans, Functie: Rekeningfunctie.Debiteuren),
        new Rekening("1600", "Crediteuren", Rekeningsoort.Balans, Functie: Rekeningfunctie.Crediteuren),
        new Rekening("1510", "Voorbelasting", Rekeningsoort.Balans, Functie: Rekeningfunctie.Voorbelasting),
        new Rekening("1520", "Af te dragen btw", Rekeningsoort.Balans, Functie: Rekeningfunctie.AfTeDragenBtw),
        new Rekening("4500", "Kantoorkosten", Rekeningsoort.Resultaat),
        new Rekening("8000", "Omzet", Rekeningsoort.Resultaat),
    ]);

    [Fact]
    public void Inkoop_en_verkoop_als_factuur_met_btw_een_creditnota_negatief()
    {
        var inkoop = new Dagboek("INK", "Inkoop", Dagboeksoort.Inkoop);
        var factuur = new Journaalpost("INK", "2026-0001", new(2026, 3, 1), "Papier",
            [new("4500", 100m, BtwCode: "I21"), new("1510", 21m), new("1600", -121m, Relatie: "Papier B.V.", Factuurnummer: "F-1")]);
        Assert.Equal(("F-1", "Papier B.V.", 100m, 21m, 121m), Kort(Dagboekoverzicht.Regel(factuur, inkoop, Schema)));
        var credit = factuur.Tegenboeking("2026-0002", new(2026, 3, 5), "Creditnota");
        Assert.Equal((-100m, -21m, -121m), Bedragen(Dagboekoverzicht.Regel(credit, inkoop, Schema)));

        var verkoop = new Dagboek("VK", "Verkoop", Dagboeksoort.Verkoop);
        var vf = new Journaalpost("VK", "2026-0001", new(2026, 3, 2), "Advies",
            [new("1300", 242m, Relatie: "Klant B.V.", Factuurnummer: "2026-001"), new("8000", -200m, BtwCode: "V21"), new("1520", -42m)]);
        Assert.Equal(("2026-001", "Klant B.V.", 200m, 42m, 242m), Kort(Dagboekoverzicht.Regel(vf, verkoop, Schema)));
    }

    [Fact]
    public void Bank_met_het_bedrag_op_de_bankrekening_memoriaal_met_het_totaal()
    {
        var bank = new Dagboek("BNK", "Bank", Dagboeksoort.Bank, "1100");
        var af = new Journaalpost("BNK", "2026-0003", new(2026, 3, 10), "Betaling", [new("1600", 121m), new("1100", -121m)]);
        var bij = new Journaalpost("BNK", "2026-0004", new(2026, 3, 11), "Ontvangst", [new("1100", 242m), new("1300", -242m)]);
        Assert.Equal(-121m, Dagboekoverzicht.Regel(af, bank, Schema).Bedrag);
        Assert.Equal(242m, Dagboekoverzicht.Regel(bij, bank, Schema).Bedrag);

        var memoriaal = new Dagboek("MEM", "Memoriaal", Dagboeksoort.Memoriaal);
        var mem = new Journaalpost("MEM", "2026-0001", new(2026, 3, 31), "Afschrijving", [new("4500", 50m), new("1100", -50m)]);
        Assert.Equal(50m, Dagboekoverzicht.Regel(mem, memoriaal, Schema).Bedrag);
    }

    [Fact]
    public void Tabbladen_in_de_volgorde_inkoop_verkoop_bank_kas_memoriaal()
    {
        var volgorde = Dagboekoverzicht.Volgorde(
        [
            new("MEM", "Memoriaal", Dagboeksoort.Memoriaal), new("BNK2", "Spaarrekening", Dagboeksoort.Bank), new("VK", "Verkoop", Dagboeksoort.Verkoop),
            new("KAS", "Kas", Dagboeksoort.Kas), new("BNK", "Bank", Dagboeksoort.Bank), new("INK", "Inkoop", Dagboeksoort.Inkoop),
        ]);
        Assert.Equal(["INK", "VK", "BNK", "BNK2", "KAS", "MEM"], volgorde.Select(d => d.Code));
    }

    private static (string?, string?, decimal, decimal, decimal) Kort(Dagboekregel r) => (r.Factuur, r.Relatie, r.Exclusief, r.Btw, r.Bedrag);

    private static (decimal, decimal, decimal) Bedragen(Dagboekregel r) => (r.Exclusief, r.Btw, r.Bedrag);
}
