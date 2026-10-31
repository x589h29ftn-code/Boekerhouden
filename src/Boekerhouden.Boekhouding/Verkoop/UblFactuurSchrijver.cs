using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Verkoop;

/// <summary>
/// Een verkoopfactuur als e-factuur: UBL 2.1 volgens Peppol BIS Billing 3.0 (dat voldoet ook aan NLCIUS, de Nederlandse
/// norm). Een creditfactuur wordt een <c>CreditNote</c> met positieve bedragen en een verwijzing naar de factuur. Btw per
/// categorie: S (hoog/laag), Z (0%), E (vrijgesteld), AE (verlegd), K (levering binnen de EU) en G (uitvoer).
/// </summary>
public static class UblFactuurSchrijver
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Crn = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    public const string Customization = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0";
    public const string Profile = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    /// <param name="referentie">Het kenmerk van de klant (inkoopordernummer); anders staat het factuurnummer als referentie.</param>
    /// <param name="tekst">Een eigen tekst (komt in <c>Note</c>).</param>
    public static string Schrijf(Verkoopfactuur factuur, Partij afzender, IReadOnlyList<BtwCode> codes, string? referentie = null, string? tekst = null)
    {
        ArgumentNullException.ThrowIfNull(factuur);
        ArgumentNullException.ThrowIfNull(afzender);
        ArgumentNullException.ThrowIfNull(codes);
        if (factuur.Nummer is null)
        {
            throw new InvalidOperationException("Een e-factuur kan pas als de factuur een nummer heeft.");
        }

        var credit = factuur.IsCredit;
        var ns = credit ? Crn : Inv;
        var teken = credit ? -1m : 1m;
        var btw = factuur.Btw(codes);
        var exclusief = teken * factuur.TotaalExclusief;
        var btwTotaal = teken * btw.Sum(b => b.Btw);
        var vermeldingen = Verkoopcontrole.Vermeldingen(factuur, codes);

        var wortel = new XElement(ns + (credit ? "CreditNote" : "Invoice"),
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XElement(Cbc + "CustomizationID", Customization),
            new XElement(Cbc + "ProfileID", Profile),
            new XElement(Cbc + "ID", factuur.Nummer),
            new XElement(Cbc + "IssueDate", Datum(factuur.Datum)),
            credit ? null : new XElement(Cbc + "DueDate", Datum(factuur.Vervaldatum)),
            new XElement(Cbc + (credit ? "CreditNoteTypeCode" : "InvoiceTypeCode"), credit ? "381" : "380"),
            Note(string.Join(" ", new[] { tekst?.Trim() }.Concat(vermeldingen).Where(t => !string.IsNullOrWhiteSpace(t)))),
            new XElement(Cbc + "DocumentCurrencyCode", "EUR"),
            new XElement(Cbc + "BuyerReference", string.IsNullOrWhiteSpace(referentie) ? factuur.Nummer : referentie.Trim()),
            factuur.CreditVoor is { } bij ? new XElement(Cac + "BillingReference", new XElement(Cac + "InvoiceDocumentReference", new XElement(Cbc + "ID", bij))) : null,
            new XElement(Cac + "AccountingSupplierParty", Party(afzender)),
            new XElement(Cac + "AccountingCustomerParty", Party(factuur.Klant)),
            Betaling(factuur, afzender, credit),
            new XElement(Cac + "TaxTotal",
                Bedrag("TaxAmount", btwTotaal),
                btw.Select(b => new XElement(Cac + "TaxSubtotal",
                    Bedrag("TaxableAmount", teken * b.Grondslag),
                    Bedrag("TaxAmount", teken * b.Btw),
                    Categorie("TaxCategory", b.Code)))),
            new XElement(Cac + "LegalMonetaryTotal",
                Bedrag("LineExtensionAmount", exclusief),
                Bedrag("TaxExclusiveAmount", exclusief),
                Bedrag("TaxInclusiveAmount", exclusief + btwTotaal),
                Bedrag("PayableAmount", exclusief + btwTotaal)),
            factuur.Regels.Select((r, i) => Regel(r, i + 1, codes, teken, credit)));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), wortel);
        var uit = new Utf8Schrijver();
        using (var xml = XmlWriter.Create(uit, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            document.Save(xml);
        }

        return uit.ToString();
    }

    /// <summary>De btw-categorie van Peppol bij een btw-code.</summary>
    public static string Categoriecode(BtwCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Soort switch
        {
            Btwsoort.VerkoopNul => "Z",
            Btwsoort.VerkoopVrijgesteld => "E",
            Btwsoort.VerkoopVerlegd => "AE",
            Btwsoort.VerkoopBinnenEu => "K",
            Btwsoort.VerkoopBuitenEu => "G",
            _ => code.Percentage == 0 ? "Z" : "S",
        };
    }

    private static XElement? Note(string tekst) => string.IsNullOrWhiteSpace(tekst) ? null : new XElement(Cbc + "Note", tekst);

    private static XElement Party(Partij partij)
    {
        var land = partij.Adres.Landcode;
        var btw = partij.BtwNummer is { } b ? BtwNummer.Normaliseer(b) : null;
        var kvk = partij.KvkNummer?.Trim();
        // Het elektronische adres: KvK (0106) of het Nederlandse btw-nummer (9944); anders het e-mailadres niet (geen Peppol-schema).
        XElement? endpoint = kvk is { Length: 8 } && land == "NL" ? new XElement(Cbc + "EndpointID", new XAttribute("schemeID", "0106"), kvk)
            : btw is { } n && n.StartsWith("NL", StringComparison.Ordinal) ? new XElement(Cbc + "EndpointID", new XAttribute("schemeID", "9944"), n)
            : null;
        return new XElement(Cac + "Party",
            endpoint,
            new XElement(Cac + "PartyName", new XElement(Cbc + "Name", partij.Naam)),
            new XElement(Cac + "PostalAddress",
                Optioneel("StreetName", partij.Adres.Straat),
                Optioneel("CityName", partij.Adres.Plaats),
                Optioneel("PostalZone", partij.Adres.Postcode),
                new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", land))),
            btw is null ? null : new XElement(Cac + "PartyTaxScheme", new XElement(Cbc + "CompanyID", btw), new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))),
            new XElement(Cac + "PartyLegalEntity",
                new XElement(Cbc + "RegistrationName", partij.Naam),
                kvk is { Length: > 0 } ? new XElement(Cbc + "CompanyID", new XAttribute("schemeID", "0106"), kvk) : null),
            partij.Email is { Length: > 0 } email || partij.Telefoon is { Length: > 0 }
                ? new XElement(Cac + "Contact", Optioneel("Name", partij.Contactpersoon), Optioneel("Telephone", partij.Telefoon), Optioneel("ElectronicMail", partij.Email))
                : null);
    }

    private static XElement? Betaling(Verkoopfactuur factuur, Partij afzender, bool credit)
    {
        var iban = afzender.Ibans?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));
        if (iban is null)
        {
            return null;
        }

        return new XElement(Cac + "PaymentMeans",
            new XElement(Cbc + "PaymentMeansCode", "58"),
            credit || factuur.Betalingskenmerk is null ? null : new XElement(Cbc + "PaymentID", factuur.Betalingskenmerk),
            new XElement(Cac + "PayeeFinancialAccount", new XElement(Cbc + "ID", Iban.Normaliseer(iban)), new XElement(Cbc + "Name", afzender.Naam)));
    }

    private static XElement Regel(Verkoopregel regel, int nummer, IReadOnlyList<BtwCode> codes, decimal teken, bool credit)
    {
        var code = BtwCodes.Zoek(codes, regel.BtwCode) ?? throw new ArgumentException($"Btw-code {regel.BtwCode} bestaat niet.", nameof(codes));
        var aantal = teken * regel.Aantal;
        var bruto = Math.Round(aantal * regel.Prijs, 2, MidpointRounding.AwayFromZero);
        var netto = teken * regel.Bedrag;
        var korting = bruto - netto;
        return new XElement(Cac + (credit ? "CreditNoteLine" : "InvoiceLine"),
            new XElement(Cbc + "ID", nummer.ToString(CultureInfo.InvariantCulture)),
            new XElement(Cbc + (credit ? "CreditedQuantity" : "InvoicedQuantity"), new XAttribute("unitCode", Eenheidcode(regel.Eenheid)), Getal(aantal)),
            Bedrag("LineExtensionAmount", netto),
            korting == 0 ? null : new XElement(Cac + "AllowanceCharge",
                new XElement(Cbc + "ChargeIndicator", "false"),
                new XElement(Cbc + "AllowanceChargeReason", $"Korting {regel.Korting.ToString("0.##", CultureInfo.InvariantCulture)}%"),
                Bedrag("Amount", korting)),
            new XElement(Cac + "Item",
                new XElement(Cbc + "Name", Kort(regel.Omschrijving.Trim(), 200)),
                Categorie("ClassifiedTaxCategory", code)),
            new XElement(Cac + "Price", new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", "EUR"), Getal(regel.Prijs))));
    }

    private static XElement Categorie(string naam, BtwCode code)
    {
        var categorie = Categoriecode(code);
        var reden = categorie switch
        {
            "AE" => "Btw verlegd",
            "K" => "Intracommunautaire levering",
            "G" => "Uitvoer buiten de EU",
            "E" => "Vrijgesteld van btw",
            _ => null,
        };
        return new XElement(Cac + naam,
            new XElement(Cbc + "ID", categorie),
            new XElement(Cbc + "Percent", Getal(code.Percentage)),
            reden is null || naam != "TaxCategory" ? null : new XElement(Cbc + "TaxExemptionReason", reden),
            new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")));
    }

    /// <summary>De eenheid als UN/ECE-code: uur, dag, stuk, kilo, meter …; onbekend = stuk (C62).</summary>
    public static string Eenheidcode(string? eenheid) => eenheid?.Trim().ToLowerInvariant() switch
    {
        "uur" or "uren" or "u" or "hour" or "hours" => "HUR",
        "dag" or "dagen" or "day" or "days" => "DAY",
        "maand" or "maanden" or "mnd" or "month" => "MON",
        "jaar" or "year" => "ANN",
        "week" or "weken" => "WEE",
        "kg" or "kilo" => "KGM",
        "m" or "meter" => "MTR",
        "m2" or "m²" => "MTK",
        "m3" or "m³" => "MTQ",
        "l" or "liter" => "LTR",
        "km" => "KMT",
        _ => "C62",
    };

    private static XElement? Optioneel(string naam, string? waarde) =>
        string.IsNullOrWhiteSpace(waarde) ? null : new XElement(Cbc + naam, waarde.Trim());

    private static XElement Bedrag(string naam, decimal bedrag) =>
        new(Cbc + naam, new XAttribute("currencyID", "EUR"), bedrag.ToString("0.00", CultureInfo.InvariantCulture));

    private static string Getal(decimal getal) => getal.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Datum(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] : tekst;

    /// <summary>Een StringWriter die UTF-8 meldt, zodat de XML-declaratie klopt.</summary>
    private sealed class Utf8Schrijver() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
