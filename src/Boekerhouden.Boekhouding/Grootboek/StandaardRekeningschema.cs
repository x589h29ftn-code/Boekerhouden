namespace Boekerhouden.Boekhouding.Grootboek;

public enum Ondernemingsvorm
{
    BV,
    Eenmanszaak,
}

/// <summary>
/// Het rekeningschema en de dagboeken waarmee een nieuwe administratie begint: een gangbaar decimaal schema voor een
/// kleine onderneming, elke rekening gekoppeld aan een code uit RGS 3.8. Het kantoor vult het per klant aan.
/// </summary>
public static class StandaardRekeningschema
{
    public static IReadOnlyList<Rekening> Rekeningen(Ondernemingsvorm vorm)
    {
        var balans = new List<Rekening>
        {
            B("0200", "Inventaris", "BMvaBeiVvp"),
            B("0210", "Afschrijving inventaris", "BMvaBeiCae"),
            B("0300", "Vervoermiddelen", "BMvaTevVvp"),
            B("0310", "Afschrijving vervoermiddelen", "BMvaTevCae"),
        };
        balans.AddRange(vorm == Ondernemingsvorm.BV
            ?
            [
                B("0500", "Aandelenkapitaal", "BEivGokGea"),
                B("0510", "Overige reserves", "BEivOvrOrs", Rekeningfunctie.OnverdeeldResultaat),
            ]
            :
            [
                B("0500", "Ondernemingsvermogen", "BEivKapOnd", Rekeningfunctie.OnverdeeldResultaat),
                B("0510", "Privé-stortingen", "BEivKapPrs"),
                B("0520", "Privé-opnamen", "BEivKapPro"),
            ]);
        balans.AddRange(
        [
            B("0700", "Lening bank", "BLasSakLvl"),
            B("1000", "Kas", "BLimKasKas", Rekeningfunctie.Kas),
            B("1100", "Bank", "BLimBanRba", Rekeningfunctie.Bank),
            B("1110", "Spaarrekening", "BLimBanSpa"),
            B("1300", "Debiteuren", "BVorDebHad", Rekeningfunctie.Debiteuren),
            B("1400", "Overige vorderingen", "BVorOvrOvk"),
            B("1410", "Vooruitbetaalde kosten", "BVorOvaVan"),
            B("1500", "Te vorderen omzetbelasting", "BVorVbkTvo", Rekeningfunctie.Voorbelasting),
            B("1510", "Te betalen omzetbelasting", "BSchBepBtw", Rekeningfunctie.AfTeDragenBtw),
            B("1520", "Te betalen loonheffing", "BSchBepLhe"),
        ]);
        if (vorm == Ondernemingsvorm.BV)
        {
            balans.Add(B("1550", "Vennootschapsbelasting", "BSchShbShb"));
            balans.Add(B("1560", "Rekening-courant directeur", "BVorOvrRcb"));
        }

        balans.AddRange(
        [
            B("1600", "Crediteuren", "BSchCreHac", Rekeningfunctie.Crediteuren),
            B("1700", "Nog te betalen kosten", "BSchOpaNtb"),
            B("1800", "Overige schulden", "BSchOvsOvs"),
            B("2000", "Vraagposten", null, Rekeningfunctie.Vraagposten),
        ]);

        return
        [
            .. balans,
            W("4000", "Lonen", "WPerLesLon"),
            W("4010", "Sociale lasten", "WPerSolPsv"),
            W("4020", "Pensioenpremies", "WPerPenPen"),
            W("4100", "Huur", "WBedHuiBeh"),
            W("4110", "Gas, water en elektra", "WBedHuiGwe"),
            W("4200", "Brandstof", "WBedAutBra"),
            W("4210", "Onderhoud vervoermiddelen", "WBedAutRoa"),
            W("4300", "Kantoorbenodigdheden", "WBedKanKan"),
            W("4310", "Telefoon en internet", "WBedKanTef"),
            W("4320", "Kleine aanschaffingen", "WBedKanKak"),
            W("4330", "Contributies en abonnementen", "WBedKanCea"),
            W("4400", "Reclame", "WBedVkkRea"),
            W("4410", "Reis- en verblijfkosten", "WBedVkkRev"),
            W("4500", "Verzekeringen", "WBedAssOva"),
            W("4600", "Accountants- en advieskosten", "WBedAeaAea"),
            W("4700", "Gereedschap", "WBedEemGsk"),
            W("4800", "Afschrijving inventaris", "WAfsAmvBei"),
            W("4810", "Afschrijving vervoermiddelen", "WAfsAmvTev"),
            W("4900", "Algemene kosten", "WBedAlkOal"),
            W("7000", "Inkoopwaarde", "WKprInhInh"),
            W("7100", "Uitbesteed werk", "WKprKuwKuw"),
            W("8000", "Omzet diensten", "WOmzNodOdh"),
            W("8010", "Omzet leveringen", "WOmzNopOlh"),
            W("9000", "Rente en kosten bank", "WFbeOrlRkb"),
            W("9010", "Rente spaarrekening", "WFbeRlmRlm"),
        ];
    }

    public static IReadOnlyList<Dagboek> Dagboeken { get; } =
    [
        new("INK", "Inkoopboek", Dagboeksoort.Inkoop),
        new("VRK", "Verkoopboek", Dagboeksoort.Verkoop),
        new("BNK", "Bank", Dagboeksoort.Bank, "1100"),
        new("KAS", "Kas", Dagboeksoort.Kas, "1000"),
        new("MEM", "Memoriaal", Dagboeksoort.Memoriaal),
    ];

    private static Rekening B(string nummer, string naam, string? rgs, Rekeningfunctie functie = Rekeningfunctie.Geen) =>
        new(nummer, naam, Rekeningsoort.Balans, rgs, functie);

    private static Rekening W(string nummer, string naam, string rgs) => new(nummer, naam, Rekeningsoort.Resultaat, rgs);
}
