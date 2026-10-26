using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Boekhouding.Tests;

/// <summary>Een kleine, verzonnen administratie voor de tests.</summary>
internal static class Testadministratie
{
    public static readonly Dagboek Inkoop = new("INK", "Inkoopboek", Dagboeksoort.Inkoop);
    public static readonly Dagboek Verkoop = new("VRK", "Verkoopboek", Dagboeksoort.Verkoop);
    public static readonly Dagboek Bank = new("BNK", "Bank", Dagboeksoort.Bank, "1100");
    public static readonly Dagboek Memoriaal = new("MEM", "Memoriaal", Dagboeksoort.Memoriaal);

    public static IReadOnlyList<BtwCode> Btw => BtwCodes.Standaard;

    public static Rekeningschema Schema() => new(
    [
        new("0400", "Inventaris", Rekeningsoort.Balans, "BMvaBeiVvp"),
        new("0500", "Kapitaal", Rekeningsoort.Balans, "BEivKapOnd"),
        new("0590", "Onverdeeld resultaat", Rekeningsoort.Balans, "BEivOvrOrs", Rekeningfunctie.OnverdeeldResultaat),
        new("1100", "Bank", Rekeningsoort.Balans, "BLimBanRba", Rekeningfunctie.Bank),
        new("1300", "Debiteuren", Rekeningsoort.Balans, "BVorDebHad", Rekeningfunctie.Debiteuren),
        new("1510", "Te vorderen btw", Rekeningsoort.Balans, "BVorVbkTvo", Rekeningfunctie.Voorbelasting),
        new("1520", "Af te dragen btw", Rekeningsoort.Balans, "BSchBepBtw", Rekeningfunctie.AfTeDragenBtw),
        new("1600", "Crediteuren", Rekeningsoort.Balans, "BSchCreHac", Rekeningfunctie.Crediteuren),
        new("2000", "Vraagposten", Rekeningsoort.Balans, null, Rekeningfunctie.Vraagposten),
        new("4100", "Telefoonkosten", Rekeningsoort.Resultaat, "WBedKanTef"),
        new("4200", "Kantoorbenodigdheden", Rekeningsoort.Resultaat, "WBedKanKan"),
        new("4300", "Bankkosten", Rekeningsoort.Resultaat, "WFbeOrlRkb"),
        new("8000", "Omzet hoog", Rekeningsoort.Resultaat, "WOmzNodOdh"),
        new("8100", "Omzet laag", Rekeningsoort.Resultaat),
    ]);

    public static Journaal Journaal() => new(Schema(), [Inkoop, Verkoop, Bank, Memoriaal]);

    /// <summary>De RGS-codes (3.8) die het rekeningschema gebruikt, met hun bovenliggende codes, in de officiële volgorde.</summary>
    public static RgsSchema Rgs()
    {
        (string Code, int Niveau, string? Ouder, string Naam, Saldokant? Kant)[] codes =
        [
            ("B", 1, null, "BALANS", null),
            ("BMva", 2, "B", "Materiële vaste activa", Saldokant.Debet),
            ("BMvaBei", 3, "BMva", "Inventaris", Saldokant.Debet),
            ("BMvaBeiVvp", 4, "BMvaBei", "Verkrijgings- of vervaardigingsprijs", Saldokant.Debet),
            ("BVor", 2, "B", "Vorderingen", Saldokant.Debet),
            ("BVorDeb", 3, "BVor", "Vorderingen op handelsdebiteuren", Saldokant.Debet),
            ("BVorDebHad", 4, "BVorDeb", "Handelsdebiteuren nominaal", Saldokant.Debet),
            ("BVorVbk", 3, "BVor", "Vorderingen uit hoofde van belastingen", Saldokant.Debet),
            ("BVorVbkTvo", 4, "BVorVbk", "Terug te vorderen Omzetbelasting", Saldokant.Debet),
            ("BLim", 2, "B", "Liquide middelen", Saldokant.Debet),
            ("BLimBan", 3, "BLim", "Tegoeden bij banken", Saldokant.Debet),
            ("BLimBanRba", 4, "BLimBan", "Rekening-courant bank", Saldokant.Debet),
            ("BEiv", 2, "B", "Groepsvermogen - Eigen vermogen - Kapitaal", Saldokant.Credit),
            ("BEivOvr", 3, "BEiv", "Overige reserves", Saldokant.Credit),
            ("BEivOvrOrs", 4, "BEivOvr", "Overige reserves", Saldokant.Credit),
            ("BEivKap", 3, "BEiv", "Eigen vermogen onderneming natuurlijke personen", Saldokant.Credit),
            ("BEivKapOnd", 4, "BEivKap", "Ondernemingsvermogen exclusief fiscale reserves fiscaal", Saldokant.Credit),
            ("BSch", 2, "B", "Kortlopende schulden", Saldokant.Credit),
            ("BSchCre", 3, "BSch", "Schulden aan leveranciers en handelskredieten", Saldokant.Credit),
            ("BSchCreHac", 4, "BSchCre", "Handelscrediteuren nominaal", Saldokant.Credit),
            ("BSchBep", 3, "BSch", "Belastingen en premies sociale verzekeringen", Saldokant.Credit),
            ("BSchBepBtw", 4, "BSchBep", "Te betalen Omzetbelasting", Saldokant.Credit),
            ("W", 1, null, "WINST- EN VERLIESREKENING", null),
            ("WOmz", 2, "W", "Netto-omzet", Saldokant.Credit),
            ("WOmzNod", 3, "WOmz", "Opbrengsten uit het verlenen van diensten", Saldokant.Credit),
            ("WOmzNodOdh", 4, "WOmzNod", "1a. Netto-omzet uit verleende diensten belast met hoog tarief", Saldokant.Credit),
            ("WBed", 2, "W", "Overige bedrijfskosten", Saldokant.Debet),
            ("WBedKan", 3, "WBed", "Kantoorkosten", Saldokant.Debet),
            ("WBedKanKan", 4, "WBedKan", "Kantoorbenodigdheden", Saldokant.Debet),
            ("WBedKanTef", 4, "WBedKan", "Telefoonkosten", Saldokant.Debet),
            ("WFbe", 2, "W", "Financiële baten en lasten", Saldokant.Credit),
            ("WFbeOrl", 3, "WFbe", "Overige rentelasten", Saldokant.Debet),
            ("WFbeOrlRkb", 4, "WFbeOrl", "Rente en kosten bank", Saldokant.Debet),
        ];
        return new RgsSchema("3.8", codes.Select((c, i) => new RgsCode(c.Code, c.Niveau, c.Ouder, c.Naam, c.Naam, c.Kant, null, i, null, false, new HashSet<string>(), new HashSet<string>())));
    }
}
