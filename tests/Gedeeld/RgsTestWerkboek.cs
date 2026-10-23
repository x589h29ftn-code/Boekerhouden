using ClosedXML.Excel;

namespace Boekerhouden.Tests.Gedeeld;

/// <summary>
/// Een klein uittreksel (135 regels) uit het officiële werkboek RGS 3.8 (Taakgroep RGS, 10-12-2025), in dezelfde
/// opmaak: titelrij, kopregel, codes in de officiële volgorde, met alle filterkolommen. Bedoeld voor tests; het
/// volledige werkboek staat niet in de repository. Gekozen zijn de codes die de fictieve testklanten nodig hebben,
/// hun bovenliggende codes, enkele omslagparen en één inactieve code.
/// </summary>
internal static class RgsTestWerkboek
{
    public const string Bestandsnaam = "RGS 3.8-def (uittreksel).xlsx";

    public static readonly object?[] Titel = ["RGS3.8", null, null, null, null, null, null, null, "Filter  - te kiezen bij aard van entiteit", null, null, null, null, null, "Filters - te vervallen c.q. te elimineren bij geen gebruik van … of indien geen …", null, null, null, null, null, null, null, null, null, null, null, null, null, null];
    public static readonly string[] Koppen = ["Referentiecode", "ReferentieOmslagcode", "Sortering", "Referentienummer", "Omschrijving (verkort)", "Omschrijving", "D/C", "Nivo", "Basis", "Uitgebr", "EZ/VOF", "ZZP", "WoCo", "Inactief", "BB", "Agro", "WKR", "EZ/VOF", "BV", "WoCo", "Bank", "OZW-Coop-Sticht-FWO", "Afrek syst", "Nivo5", "Uitbr5", "Stichting", "OZW", "FWO", "Coop"];
    public static readonly object?[][] Regels =
    [
        ["B", null, null, null, "BALANS", "BALANS", null, 1, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BIva", null, "A", "01", "Immateriële vaste activa", "Immateriële vaste activa", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BIvaKou", null, "A.A", "0101000", "Kosten van oprichting en van uitgifte van aandelen", "Kosten van oprichting en van uitgifte van aandelen", "D", 3, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BIvaKouCuh", null, "A.A.C", "0101030", "Cumulatieve herwaarderingen", "Cumulatieve herwaarderingen kosten van oprichting en van uitgifte van aandelen", "D", 4, 1, 1, null, null, null, 1, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BMva", null, "B", "02", "Materiële vaste activa", "Materiële vaste activa", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaBeg", null, "B.B", "0202000", "Bedrijfsgebouwen", "Bedrijfsgebouwen", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaBegVvp", null, "B.B.A", "0202010", "Verkrijgings- of vervaardigingsprijs", "Verkrijgings- of vervaardigingsprijs bedrijfsgebouwen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaBegVvpBeg", null, "B.B.A010", "0202010.01", "Beginbalans (overname eindsaldo vorig jaar)", "Beginbalans (overname eindsaldo vorig jaar) bedrijfsgebouwen", "D", 5, 1, 1, 1, null, 1, null, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null],
        ["BMvaObe", null, "B.E", "0214000", "Andere vaste bedrijfsmiddelen", "Andere vaste bedrijfsmiddelen", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaObeVvp", null, "B.E.A", "0214010", "Verkrijgings- of vervaardigingsprijs", "Verkrijgings- of vervaardigingsprijs andere vaste bedrijfsmiddelen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaObeCae", null, "B.E.B", "0214020", "Cumulatieve afschrijvingen en waardeverminderingen", "Cumulatieve afschrijvingen en waardeverminderingen andere vaste bedrijfsmiddelen", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaBei", null, "B.H", "0215000", "Inventaris", "Inventaris", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaBeiVvp", null, "B.H.A", "0215010", "Verkrijgings- of vervaardigingsprijs", "Verkrijgings- of vervaardigingsprijs inventaris", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaBeiCae", null, "B.H.B", "0215020", "Cumulatieve afschrijvingen en waardeverminderingen", "Cumulatieve afschrijvingen en waardeverminderingen inventaris", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaTev", null, "B.I", "0213000", "Automobielen en overige transportmiddelen", "Automobielen en overige transportmiddelen", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaTevVvp", null, "B.I.A", "0213010", "Verkrijgings- of vervaardigingsprijs", "Verkrijgings- of vervaardigingsprijs automobielen en overige transportmiddelen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BMvaTevCae", null, "B.I.B", "0213020", "Cumulatieve afschrijvingen en waardeverminderingen", "Cumulatieve afschrijvingen en waardeverminderingen automobielen en overige transportmiddelen", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BVrd", null, "E", "30", "Voorraden", "Voorraden", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BVrdGeh", null, "E.A", "3002000", "Voorraad grond- en hulpstoffen", "Voorraad grond- en hulpstoffen", "D", 3, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BVrdGehVoo", null, "E.A.010", "3002010", "Voorraad grond- en hulpstoffen, bruto", "Voorraad grond- en hulpstoffen, bruto voorraad grond- en hulpstoffen", "D", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BVor", null, "G", 11, "Vorderingen", "Vorderingen", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, 1, null, null, null, null],
        ["BVorDeb", null, "G.A", "1101000", "Vorderingen op handelsdebiteuren", "Vorderingen op handelsdebiteuren", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, 1, null, null, null, null],
        ["BVorDebHad", null, "G.A.010", "1101010", "Handelsdebiteuren nominaal", "Handelsdebiteuren nominaal", "D", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, 1, null, null, null, null],
        ["BVorDebVdd", null, "G.A.030", "1101030", "Voorziening voor oninbaarheid op vorderingen op handelsdebiteuren", "Voorziening voor oninbaarheid op vorderingen op handelsdebiteuren ", "C", 4, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null],
        ["BVorVbk", null, "G.G", 1103159, "Vorderingen uit hoofde van belastingen", "Vorderingen uit hoofde van belastingen", "D", 3, 1, 1, 1, " ", 1, null, null, null, null, " ", null, null, null, null, null, null, " ", null, null, null, null],
        ["BVorVbkTvo", "BSchBepBtw", "G.G.B", "1102010", "Terug te vorderen Omzetbelasting", "Terug te vorderen Omzetbelasting vorderingen uit hoofde van belastingen", "D", 4, 1, 1, 1, " ", 1, null, null, null, null, " ", null, null, null, null, null, null, " ", null, null, null, null],
        ["BVorVbkTvl", "BSchBepLhe", "G.G.E", 1102020, "Terug te vorderen Loonheffing", "Terug te vorderen Loonheffing vorderingen uit hoofde van belastingen", "D", 4, 1, 1, 1, null, 1, null, null, null, null, " ", null, null, null, null, null, null, null, null, null, null, null],
        ["BVorOvr", null, "G.K", 1103000, "Overige vorderingen", "Overige vorderingen", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, null, null, " ", null, null, null, null],
        ["BVorOvrRcb", "BSchOvsRcb", "G.K.D", 1103140, "Rekening-courant bestuurders", "Rekening-courant bestuurders overige vorderingen (kortlopend)", "D", 4, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BVorOvrRca", "BSchOvsSaa", "G.K.G", 1103143, "Rekening-courant aandeelhouders", "Rekening-courant aandeelhouders (kortlopend)", "D", 4, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BVorOvrOvk", "BSchOvsOvs", "G.K.T", "1103190", "Overige vorderingen", "Overige vorderingen overige vorderingen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BVorOva", null, "G.L", "1104000", "Overlopende activa", "Overlopende activa", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BVorOvaVof", null, "G.L.A", "1104010", "Vooruitbetaalde facturen", "Vooruitbetaalde facturen overlopende activa", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BVorOvaVan", "BSchOpaNtb", "G.L.U", 1104240, "Vooruitbetaalde andere kosten", "Vooruitbetaalde andere kosten overlopende activa", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BLim", null, "I", "10", "Liquide middelen", "Liquide middelen", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, 1, null, null, null, null, null, null],
        ["BLimKas", null, "I.A", "1001000", "Kasmiddelen", "Kasmiddelen", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, 1, null, null, null, null, null, null],
        ["BLimKasKas", null, "I.A010", "1001010", "Kas", "Kas kasmiddelen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, 1, null, null, null, null, null, null],
        ["BLimBan", null, "I.B", "1002000", "Tegoeden bij banken", "Tegoeden bij banken", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, 1, null, null, null, null, null, null],
        ["BLimBanRba", "BSchSakRba", "I.B.A", "1002010", "Rekening-courant bank", "Rekening-courant bank tegoeden bij banken", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, 1, null, null, null, null, null, null],
        ["BLimBanSpa", null, "I.B.G", 1002070, "(Bedrijfs)spaarrekening", "(Bedrijfs)spaarrekening", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BEiv", null, "J", "05", "Groepsvermogen - Eigen vermogen - Kapitaal", "Groepsvermogen - Eigen vermogen - Kapitaal", "C", 2, 1, 1, 1, 1, 1, null, null, null, null, " ", null, null, null, null, null, null, null, null, null, null, null],
        ["BEivGok", null, "J.A", "0501000", "Aandelenkapitaal", "Aandelenkapitaal", "C", 3, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BEivGokGea", null, "J.A.A", "0501010", "Normale aandelen", "Normale aandelen aandelenkapitaal", "C", 4, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BEivOvr", null, "J.K", 506000, "Overige reserves", "Overige reserves", "C", 3, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, 1, 1, null],
        ["BEivOvrOrs", null, "J.K.C", 506005, "Overige reserves", "Overige reserves ", "C", 4, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, 1, 1, null],
        ["BEivOre", null, "J.L", 506009, "Onverdeelde winst", "Onverdeelde winst", "C", 3, 1, 1, 1, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, 1, null, null],
        ["BEivOreRvh", null, "J.L.B", "0506020", "Resultaat van het boekjaar", "Resultaat van het boekjaar ", "C", 4, 1, 1, 1, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, 1, null, null],
        ["BEivKap", null, "J.M", 509000, "Eigen vermogen onderneming natuurlijke personen", "Eigen vermogen onderneming natuurlijke personen", "C", 3, 1, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["BEivKapOnd", null, "J.M.A", "0509010", "Ondernemingsvermogen exclusief fiscale reserves fiscaal", "Ondernemingsvermogen exclusief fiscale reserves fiscaal eigen vermogen onderneming natuurlijke personen", "C", 4, 1, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["BEivKapPrs", null, "J.M.B", "0509030", "Privé-stortingen", "Privé-stortingen", "C", 4, 1, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["BEivKapPro", null, "J.M.C", "0509040", "Privé-opnamen", "Privé-opnamen", "D", 4, 1, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["BEivFir", null, "J.Y", "0510000", "Fiscale reserves", "Fiscale reserves", "C", 3, 1, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, 1, null, null],
        ["BEivFirFor", null, "J.Y.B", "0510010", "Fiscale oudedagsreserve", "Fiscale oudedagsreserve eigen vermogen onderneming natuurlijke personen", "C", 4, 1, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["BLas", null, "L", "08", "Langlopende schulden", "Langlopende schulden", "C", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BLasSak", null, "L.E", "0805000", "Schulden aan banken", "Schulden aan banken (langlopend)", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BLasSakLvl", null, "L.E.C0", 805030, "Hoofdsom leningen", "Hoofdsom leningen van kredietinstellingen (langlopend)", "C", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSch", null, "M", 12, "Kortlopende schulden", "Kortlopende schulden", "C", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BSchSak", null, "M.B", 1209029, "Schulden aan banken", "Schulden aan banken", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSchSakRba", "BLimBanRba", "M.B.A", "1209030", "Rekening-courant bij kredietinstellingen", "Rekening-courant bij kredietinstellingen schulden aan kredietinstellingen", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSchCre", null, "M.D", "1203000", "Schulden aan leveranciers en handelskredieten", "Schulden aan leveranciers en handelskredieten", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BSchCreHac", null, "M.D.A", "1203010", "Handelscrediteuren nominaal", "Handelscrediteuren nominaal schulden aan leveranciers en handelskredieten", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BSchShb", null, "M.I", 1208400, "Schulden uit hoofde van belasting naar de winst", "Schulden uit hoofde van belasting naar de winst", "C", 3, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BSchShbShb", null, "M.I.A", 1208410, "Schulden uit hoofde van belasting naar de winst", "Schulden uit hoofde van belasting naar de winst", "C", 4, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BSchBep", null, "M.K", 1205009, "Belastingen en premies sociale verzekeringen", "Belastingen en premies sociale verzekeringen", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSchBepBtw", "BVorVbkTvo", "M.K.A", 1205010, "Te betalen Omzetbelasting", "Te betalen Omzetbelasting", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSchBepLhe", "BVorVbkTvl", "M.K.B", "1206010", "Te betalen Loonheffing", "Te betalen Loonheffing belastingen en premies sociale verzekeringen", "C", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSchOvs", null, "M.P", "1209000", "Overige schulden", "Overige schulden", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BSchOvsRcb", "BVorOvrRcb", "M.P.A", 1209020, "Rekening-courant bestuurders (Kortlopend)", "Rekening-courant bestuurders (Kortlopend)", "C", 4, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, 1, null, null, null, null],
        ["BSchOvsSaa", "BVorOvrRca", "M.P.D", "1209010", "Schulden aan aandeelhouders", "Schulden aan aandeelhouders", "C", 4, 1, 1, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["BSchOvsOvs", "BVorOvrOvk", "M.P.M", 1209150, "Overige schulden", "Overige schulden overige schulden", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["BSchOpa", null, "M.Q", "1210000", "Overlopende passiva", "Overlopende passiva", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["BSchOpaNtb", "BVorOvaVan", "M.Q.B", "1210020", "Nog te betalen andere kosten", "Nog te betalen andere kosten overlopende passiva", "C", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["W", null, null, null, "WINST- EN VERLIESREKENING", "WINST- EN VERLIESREKENING", null, 1, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WOmz", null, "O", "80", "Netto-omzet", "Netto-omzet", "C", 2, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WOmzNop", null, "O.A", "8001000", "Netto-omzet uit leveringen geproduceerde goederen", "Netto-omzet uit leveringen geproduceerde goederen opbrengsten uit de verkoop van goederen", "C", 3, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WOmzNopOlh", null, "O.A010", "8001010", "1a. Netto-omzet uit leveringen geproduceerde goederen belast met hoog tarief", "1a. Netto-omzet uit leveringen geproduceerde goederen belast met hoog tarief", "C", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WOmzNod", null, "O.C", "8003000", "Opbrengsten uit het verlenen van diensten", "Opbrengsten uit het verlenen van diensten", "C", 3, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WOmzNodOdh", null, "O.C010", "8003010", "1a. Netto-omzet uit verleende diensten belast met hoog tarief", "1a. Netto-omzet uit verleende diensten belast met hoog tarief", "C", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WOmzNoo", null, "O.F", "8004000", "Overige netto-omzet", "Overige netto-omzet", "C", 3, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WOmzNooNdl", null, "O.F.A", "8004010", "Overige netto-omzet waarvan licenties", "Overige netto-omzet waarvan licenties overige netto-omzet", "C", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WKpr", null, "Q", "70", "Kostprijs van de omzet", "Kostprijs van de omzet", "D", 2, 1, 1, 1, 1, null, null, null, " ", null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WKprKuw", null, "Q.C", "7003000", "Kosten uitbesteed werk en andere externe kosten", "Kosten uitbesteed werk en andere externe kosten", "D", 3, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WKprKuwKuw", null, "Q.C.A", 7003010, "Kosten van uitbesteed werk", "Kosten van uitbesteed werk", "D", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WKprInh", null, "Q.H", "7005000", "Inkoopwaarde handelsgoederen", "Inkoopwaarde handelsgoederen", "D", 3, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WKprInhInh", null, "Q.H.A", "7005010", "Inkoopwaarde handelsgoederen", "Inkoopwaarde handelsgoederen", "D", 4, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, 1, null, null, null, null, null, null],
        ["WPer", null, "S", "40", "Lasten uit hoofde van personeelsbeloningen", "Lasten uit hoofde van personeelsbeloningen", "D", 2, 1, 1, 1, null, 1, null, null, null, null, " ", null, null, null, null, null, " ", null, null, null, null, null],
        ["WPerLes", null, "S.A", "4001000", "Lonen en salarissen", "Lonen en salarissen", "D", 3, 1, 1, 1, null, 1, null, null, null, null, " ", null, null, null, null, null, " ", null, null, null, null, null],
        ["WPerLesLon", null, "S.A.D", "4001040", "Lonen", "Lonen en salarissen", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WPerSol", null, "S.B", "4002000", "Sociale lasten", "Sociale lasten", "D", 3, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WPerSolPsv", null, "S.B.A", "4002010", "Premies sociale verzekeringen", "Premies sociale verzekeringen sociale lasten", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WPerPen", null, "S.C", "4003000", "Pensioenlasten", "Pensioenlasten", "D", 3, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WPerPenPen", null, "S.C.A", "4003010", "Pensioenpremies", "Pensioenpremies pensioenlasten", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WAfs", null, "T", "41", "Afschrijvingen op immateriële en materiële vaste activa", "Afschrijvingen op immateriële en materiële vaste activa", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WAfsAmv", null, "T.B", "4102000", "Afschrijvingen op materiële vaste activa", "Afschrijvingen op materiële vaste activa", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WAfsAmvObe", null, "T.B.F", "4102080", "Afschrijvingen Andere vaste bedrijfsmiddelen", "Afschrijvingen Andere vaste bedrijfsmiddelen ", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WAfsAmvTev", null, "T.B.I", "4102070", "Afschrijvingen Automobielen en overige transportmiddelen", "Afschrijvingen Automobielen en overige transportmiddelen ", "D", 4, 1, 1, 1, 1, 1, null, null, " ", null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WAfsAmvBei", null, "T.B.J", "4102090", "Afschrijvingen Inventaris", "Afschrijvingen Inventaris ", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBed", null, "W", "42", "Overige bedrijfskosten", "Overige bedrijfskosten", "D", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, " ", null, null, null, null, null],
        ["WBedHui", null, "W.C", "4201000", "Huisvestingskosten", "Huisvestingskosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedHuiBeh", null, "W.C.E", "4201040", "Betaalde huur", "Betaalde huur huisvestingskosten", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedHuiGwe", null, "W.C.L", 4201105, "Gas,water en elektra (algemeen)", "Gas,water en elektra (algemeen)", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedEem", null, "W.D", "4202000", "Exploitatie- en machinekosten", "Exploitatie- en machinekosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedEemGsk", null, "W.D.F", "4202050", "Gereedschapskosten", "Gereedschapskosten exploitatie- en machinekosten", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedVkk", null, "W.E", "4203000", "Verkoop gerelateerde kosten", "Verkoop gerelateerde kosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedVkkRea", null, "W.E.A", "4203010", "Reclame- en advertentiekosten", "Reclame- en advertentiekosten verkoop gerelateerde kosten", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedVkkRev", null, "W.E.H", "4203070", "Reis- en verblijfkosten", "Reis- en verblijfkosten verkoop gerelateerde kosten", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAut", null, "W.F", "4204000", "Autokosten en andere vervoermiddelen", "Autokosten en andere vervoermiddelen", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAutBra", null, "W.F.A", "4204010", "Brandstofkosten auto's en andere vervoermiddelen", "Brandstofkosten auto's autokosten en andere vervoermiddelen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAutRoa", null, "W.F.C", "4204020", "Reparatie en onderhoud auto's en andere vervoermiddelen", "Reparatie en onderhoud auto's autokosten en andere vervoermiddelen", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedKan", null, "W.H", "4206000", "Kantoorkosten", "Kantoorkosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedKanKan", null, "W.H.A", "4206010", "Kantoorbenodigdheden", "Kantoorbenodigdheden kantoorkosten", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedKanTef", null, "W.H.D", "4206030", "Telefoonkosten", "Telefoonkosten kantoorkosten", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedKanKak", null, "W.H.G", "4206060", "Kleine aanschaffingen kantoorinventaris", "Kleine aanschaffingen kantoorinventaris kantoorkosten", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedKanCea", null, "W.H.H", "4206070", "Contributies en abonnementen", "Contributies en abonnementen kantoorkosten", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAss", null, "W.J", "4208000", "Assurantiekosten", "Assurantiekosten", "D", 3, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAssOva", null, "W.J.C", "4208020", "Overige assurantiepremies", "Overige assurantiepremies assurantiekosten", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAea", null, "W.K", "4209000", "Accountants- en advieskosten", "Accountants- en advieskosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAeaAea", null, "W.K.A", 4209005, "Accountants- en advieskosten", "Accountants- en advieskosten", "D", 4, 1, 1, 1, " ", 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAlk", null, "W.R", "4215000", "Andere kosten", "Andere kosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBedAlkOal", null, "W.R.A", "4215010", "Algemene kosten", "Algemene kosten andere kosten", "D", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbe", null, "Z.02", "50", "Financiële baten en lasten", "Financiële baten en lasten", "C", 2, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeRlm", null, "Z.02.A", 8402000, "Rentebaten en soortgelijke opbrengsten", "Rentebaten en soortgelijke opbrengsten", "C", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeRlmRgi", "WFbeRlsRgi", "Z.02.A010", 8402030, "Rentebaten vorderingen groepsmaatschappijen binnenland", "Rentebaten vorderingen groepsmaatschappijen binnenland", "C", 4, 1, 1, null, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeRlmRlm", "WFbeOrlRkb", "Z.02.A070", 8402110, "Rente liquide middelen", "Rente liquide middelen rentebaten en soortgelijke opbrengsten", "C", 4, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeRls", null, "Z.02.B", "8404000", "Rentelasten en soortgelijke kosten", "Rentelasten en soortgelijke kosten", "D", 3, 1, 1, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeRlsRal", null, "Z.02.B010", "8404010", "Rentelasten achtergestelde leningen", "Rentelasten achtergestelde leningen", "D", 4, 1, 1, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeRlsRgi", "WFbeRlmRgi", "Z.02.B080", "8404080", "Rentelasten schulden groepsmaatschappijen binnenland", "Rentelasten schulden groepsmaatschappijen binnenland rentelasten en soortgelijke kosten", "D", 4, 1, 1, null, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeOrl", null, "Z.02.C", "8406000", "Overige rentelasten", "Overige rentelasten", "D", 3, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WFbeOrlRkb", "WFbeRlmRlm", "Z.02.C050", 8406050, "Rente en kosten bank", "Rente en kosten bank", "D", 4, 1, 1, 1, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null],
        ["WBel", null, "Z.03", "60", "Belastingen", "Belastingen", "D", 2, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["WBelBgr", null, "Z.03.A", "9101000", "Belastingen over de winst of het verlies", "Belastingen over de winst of het verlies", "D", 3, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["WBelBgrLab", null, "Z.03.A010", 9101005, "Latente belastingen", "Latente belastingen belastingen over de winst of het verlies", "D", 4, 1, 1, null, null, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null],
        ["WMfo", null, "Z.09", "95", "Mutatie fiscale oudedagsreserve", "Mutatie fiscale oudedagsreserve", "D", 2, null, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["WMfoBel", null, "Z.09.A", "9104000", "Mutatie fiscale oudedagsreserve", "Mutatie fiscale oudedagsreserve", "D", 3, null, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
        ["WMfoBelMfo", null, "Z.09.A010", "9104010", "Mutatie fiscale oudedagsreserve", "Mutatie fiscale oudedagsreserve belastingen over de winst of het verlies", "D", 4, null, 1, 1, 1, null, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null],
    ];

    /// <summary>Schrijft het uittreksel als Excel-werkboek met het hoofdblad "Totaal-RGS3.8-def".</summary>
    public static byte[] Maak(IEnumerable<object?[]>? extraRegels = null, string werkblad = "Totaal-RGS3.8-def")
    {
        using var werkboek = new XLWorkbook();
        werkboek.AddWorksheet("Recap").Cell(1, 1).Value = "Releasenotes (testuittreksel)";
        var blad = werkboek.AddWorksheet(werkblad);
        Zet(blad, 1, Titel);
        Zet(blad, 2, [.. Koppen]);
        var rij = 3;
        foreach (var regel in Regels.Concat(extraRegels ?? []))
        {
            Zet(blad, rij++, regel);
        }

        using var stream = new MemoryStream();
        werkboek.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Zet(IXLWorksheet blad, int rij, object?[] waarden)
    {
        for (var i = 0; i < waarden.Length; i++)
        {
            blad.Cell(rij, i + 1).Value = waarden[i] switch
            {
                null => Blank.Value,
                int getal => getal,
                var tekst => tekst.ToString(),
            };
        }
    }
}
