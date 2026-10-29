using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Herkenning;

/// <summary>
/// Leest een e-factuur in UBL 2.x (<c>Invoice</c> of <c>CreditNote</c>, ook volgens NLCIUS/Peppol BIS): leverancier
/// met btw-, KvK-nummer en IBAN, factuurnummer, datums, totalen, btw per tarief en regels. Een creditnota krijgt
/// negatieve bedragen. Exact, dus zonder herkenning.
/// </summary>
public static class UblFactuurLezer
{
    /// <summary>Is dit een UBL-factuur of -creditnota? Kijkt alleen naar het begin van de XML.</summary>
    public static bool IsUbl(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var positie = stream.Position;
        try
        {
            using var xml = XmlReader.Create(stream, Instellingen(sluiten: false));
            return xml.MoveToContent() == XmlNodeType.Element && xml.LocalName is "Invoice" or "CreditNote" && xml.NamespaceURI.Contains("ubl", StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return false;
        }
        finally
        {
            stream.Position = positie;
        }
    }

    public static HerkendeFactuur Lees(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XElement wortel;
        try
        {
            using var xml = XmlReader.Create(stream, Instellingen(sluiten: false));
            wortel = XDocument.Load(xml).Root ?? throw new FormatException("De e-factuur is leeg.");
        }
        catch (XmlException fout)
        {
            throw new FormatException("De e-factuur is geen geldige XML.", fout);
        }

        if (wortel.Name.LocalName is not ("Invoice" or "CreditNote"))
        {
            throw new FormatException($"Geen UBL-factuur: het document begint met {wortel.Name.LocalName}.");
        }

        var credit = wortel.Name.LocalName == "CreditNote";
        var teken = credit ? -1 : 1;
        var partij = Pad(wortel, "AccountingSupplierParty", "Party");
        var naam = Tekst(Pad(partij, "PartyName", "Name")) ?? Tekst(Pad(partij, "PartyLegalEntity", "RegistrationName"));
        var btwNummer = Kinderen(partij, "PartyTaxScheme").Select(p => Tekst(Kind(p, "CompanyID"))).FirstOrDefault(n => n is not null && BtwNummer.IsGeldig(n));
        var kvk = Kind(Kind(partij, "PartyLegalEntity"), "CompanyID") is { } id && (string?)id.Attribute("schemeID") is null or "0106" ? Tekst(id) : null;
        var betaling = Kinderen(wortel, "PaymentMeans").FirstOrDefault();
        var iban = Tekst(Pad(betaling, "PayeeFinancialAccount", "ID"));
        var totalen = Kind(wortel, "LegalMonetaryTotal");

        var btw = Kinderen(wortel, "TaxTotal")
            .SelectMany(t => Kinderen(t, "TaxSubtotal"))
            .Select(s => new Btwregel(
                Getal(Pad(s, "TaxCategory", "Percent")) ?? 0m,
                teken * Getal(Kind(s, "TaxableAmount")),
                teken * (Getal(Kind(s, "TaxAmount")) ?? 0m)))
            .ToList();

        var regels = Kinderen(wortel, credit ? "CreditNoteLine" : "InvoiceLine")
            .Select(r => new HerkendeRegel(
                Tekst(Pad(r, "Item", "Name")) ?? Tekst(Pad(r, "Item", "Description")) ?? string.Empty,
                teken * (Getal(Kind(r, "LineExtensionAmount")) ?? 0m),
                Getal(Pad(r, "Item", "ClassifiedTaxCategory", "Percent"))))
            .ToList();

        var factuurdatum = Datum(Kind(wortel, "IssueDate"));
        return new HerkendeFactuur(
            Herkenningsbron.EFactuur,
            Naam: naam,
            BtwNummer: btwNummer is null ? null : BtwNummer.Normaliseer(btwNummer),
            KvkNummer: kvk,
            Iban: iban is null ? null : Iban.Normaliseer(iban),
            Factuurnummer: Tekst(Kind(wortel, "ID")),
            Factuurdatum: factuurdatum,
            Vervaldatum: Datum(Kind(wortel, "DueDate")) ?? Datum(Kind(betaling, "PaymentDueDate")) ?? Datum(Pad(wortel, "PaymentTerms", "PaymentDueDate")),
            TotaalExclusief: teken * Getal(Kind(totalen, "TaxExclusiveAmount")),
            TotaalInclusief: teken * (Getal(Kind(totalen, "PayableAmount")) ?? Getal(Kind(totalen, "TaxInclusiveAmount"))),
            Btw: btw,
            Regels: regels,
            Betalingskenmerk: Tekst(Kind(betaling, "PaymentID")),
            Omschrijving: regels.Count > 0 ? regels[0].Omschrijving : Tekst(Kind(wortel, "Note")),
            Postcodes: [.. new[] { Pad(partij, "PostalAddress"), Pad(wortel, "AccountingCustomerParty", "Party", "PostalAddress"), Pad(wortel, "Delivery", "DeliveryLocation", "Address") }
                .Select(a => Tekst(Kind(a, "PostalZone")))
                .SelectMany(p => p is null ? [] : Postcode.Zoek(p.ToUpperInvariant()))
                .Distinct(StringComparer.Ordinal)],

            // Btw-categorie AE (verlegd) of K (intracommunautair): de afnemer berekent de btw.
            Verlegd: Kinderen(wortel, "TaxTotal").SelectMany(t => Kinderen(t, "TaxSubtotal"))
                .Any(t => Tekst(Pad(t, "TaxCategory", "ID")) is "AE" or "K"));
    }

    private static XmlReaderSettings Instellingen(bool sluiten) =>
        new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = sluiten };

    private static XElement? Kind(XElement? element, string naam) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName == naam);

    private static IEnumerable<XElement> Kinderen(XElement? element, string naam) =>
        element?.Elements().Where(e => e.Name.LocalName == naam) ?? [];

    private static XElement? Pad(XElement? element, params string[] namen) =>
        namen.Aggregate(element, Kind);

    private static string? Tekst(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static decimal? Getal(XElement? element) =>
        Tekst(element) is { } tekst && decimal.TryParse(tekst, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var getal) ? getal : null;

    private static DateOnly? Datum(XElement? element) =>
        Tekst(element) is { } tekst && DateOnly.TryParseExact(tekst, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum) ? datum : null;
}
