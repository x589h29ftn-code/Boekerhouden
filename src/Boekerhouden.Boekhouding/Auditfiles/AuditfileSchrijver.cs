using System.Globalization;
using System.Text;
using System.Xml;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Auditfiles;

/// <summary>
/// Schrijft een <see cref="Auditfile"/> als XAF 3.2 (namespace http://www.auditfiles.nl/XAF/3.2), in de volgorde van
/// de elementen uit de XSD: header, company met customersSuppliers, generalLedger (met de RGS-code in leadReference),
/// vatCodes, periods, openingBalance en transactions per journal. Bedragen positief met D of C.
/// </summary>
public static class AuditfileSchrijver
{
    public const string Namespace = "http://www.auditfiles.nl/XAF/3.2";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Schrijf(Auditfile a)
    {
        ArgumentNullException.ThrowIfNull(a);
        using var uit = new MemoryStream();
        using (var x = XmlWriter.Create(uit, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            x.WriteStartDocument();
            x.WriteStartElement("auditfile", Namespace);

            x.WriteStartElement("header", Namespace);
            Veld(x, "fiscalYear", a.Kop.Boekjaar.ToString(Inv));
            Veld(x, "startDate", Datum(a.Kop.Begin));
            Veld(x, "endDate", Datum(a.Kop.Eind));
            Veld(x, "curCode", a.Kop.Valuta);
            Veld(x, "dateCreated", Datum(a.Kop.Gemaakt));
            Veld(x, "softwareDesc", a.Kop.Software);
            Veld(x, "softwareVersion", a.Kop.Versie);
            x.WriteEndElement();

            x.WriteStartElement("company", Namespace);
            Veld(x, "companyIdent", a.Bedrijf.KvkNummer ?? a.Bedrijf.Naam);
            Veld(x, "companyName", a.Bedrijf.Naam);
            Veld(x, "taxRegistrationCountry", a.Bedrijf.Land ?? "NL");
            Veld(x, "taxRegIdent", a.Bedrijf.BtwNummer ?? "");
            if (a.Bedrijf.Plaats is { Length: > 0 })
            {
                x.WriteStartElement("streetAddress", Namespace);
                Leeg(x, "streetname", a.Bedrijf.Straat);
                Veld(x, "city", a.Bedrijf.Plaats);
                Leeg(x, "postalCode", a.Bedrijf.Postcode);
                Veld(x, "country", a.Bedrijf.Land ?? "NL");
                x.WriteEndElement();
            }

            if (a.Relaties.Count > 0)
            {
                x.WriteStartElement("customersSuppliers", Namespace);
                foreach (var r in a.Relaties)
                {
                    x.WriteStartElement("customerSupplier", Namespace);
                    Veld(x, "custSupID", r.Code);
                    Veld(x, "custSupName", r.Naam);
                    Leeg(x, "commerceNr", r.KvkNummer);
                    if (r.BtwNummer is { Length: > 2 } btw)
                    {
                        Veld(x, "taxRegistrationCountry", btw[..2]);
                        Veld(x, "taxRegIdent", btw);
                    }

                    Veld(x, "custSupTp", r.Klant && r.Leverancier ? "B" : r.Klant ? "C" : r.Leverancier ? "S" : "O");
                    foreach (var iban in r.Ibans)
                    {
                        x.WriteStartElement("bankAccount", Namespace);
                        Veld(x, "bankAccNr", iban);
                        x.WriteEndElement();
                    }

                    x.WriteEndElement();
                }

                x.WriteEndElement();
            }

            x.WriteStartElement("generalLedger", Namespace);
            foreach (var r in a.Rekeningen)
            {
                x.WriteStartElement("ledgerAccount", Namespace);
                Veld(x, "accID", r.Nummer);
                Veld(x, "accDesc", r.Naam);
                Veld(x, "accTp", r.Soort == Rekeningsoort.Balans ? "B" : "P");
                Leeg(x, "leadReference", r.RgsCode);
                x.WriteEndElement();
            }

            x.WriteEndElement();

            if (a.Btwcodes.Count > 0)
            {
                x.WriteStartElement("vatCodes", Namespace);
                foreach (var b in a.Btwcodes)
                {
                    x.WriteStartElement("vatCode", Namespace);
                    Veld(x, "vatID", b.Code);
                    Veld(x, "vatDesc", b.Omschrijving);
                    Leeg(x, "vatToPayAccID", b.AfdragenRekening);
                    Leeg(x, "vatToClaimAccID", b.VorderenRekening);
                    x.WriteEndElement();
                }

                x.WriteEndElement();
            }

            x.WriteStartElement("periods", Namespace);
            for (var maand = 1; maand <= 12; maand++)
            {
                var begin = new DateOnly(a.Kop.Boekjaar, maand, 1);
                x.WriteStartElement("period", Namespace);
                Veld(x, "periodNumber", maand.ToString(Inv));
                Veld(x, "periodDesc", begin.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("nl-NL")));
                Veld(x, "startDatePeriod", Datum(begin));
                Veld(x, "endDatePeriod", Datum(begin.AddMonths(1).AddDays(-1)));
                x.WriteEndElement();
            }

            x.WriteEndElement();

            if (a.Beginbalans.Count > 0)
            {
                x.WriteStartElement("openingBalance", Namespace);
                Veld(x, "opBalDate", Datum(a.BeginbalansDatum ?? a.Kop.Begin));
                Veld(x, "opBalDesc", "Beginbalans");
                Veld(x, "linesCount", a.Beginbalans.Count.ToString(Inv));
                Veld(x, "totalDebit", Bedrag(a.Beginbalans.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag)));
                Veld(x, "totalCredit", Bedrag(-a.Beginbalans.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag)));
                var nr = 0;
                foreach (var r in a.Beginbalans)
                {
                    x.WriteStartElement("obLine", Namespace);
                    Veld(x, "nr", (++nr).ToString(Inv));
                    Veld(x, "accID", r.Rekening);
                    Veld(x, "amnt", Bedrag(Math.Abs(r.Bedrag)));
                    Veld(x, "amntTp", r.Bedrag < 0 ? "C" : "D");
                    x.WriteEndElement();
                }

                x.WriteEndElement();
            }

            var regels = a.Dagboeken.SelectMany(d => d.Boekingen).SelectMany(b => b.Regels).ToList();
            x.WriteStartElement("transactions", Namespace);
            Veld(x, "linesCount", regels.Count.ToString(Inv));
            Veld(x, "totalDebit", Bedrag(regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag)));
            Veld(x, "totalCredit", Bedrag(-regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag)));
            foreach (var d in a.Dagboeken)
            {
                x.WriteStartElement("journal", Namespace);
                Veld(x, "jrnID", d.Code);
                Veld(x, "desc", d.Naam);
                Veld(x, "jrnTp", Soortcode(d.Soort));
                Leeg(x, "offsetAccID", d.Tegenrekening);
                foreach (var b in d.Boekingen)
                {
                    x.WriteStartElement("transaction", Namespace);
                    Veld(x, "nr", b.Boekstuk);
                    Veld(x, "desc", b.Omschrijving ?? "");
                    Veld(x, "periodNumber", b.Periode.ToString(Inv));
                    Veld(x, "trDt", Datum(b.Datum));
                    Veld(x, "amnt", Bedrag(b.Regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag)));
                    Veld(x, "amntTp", "D");
                    var regelnr = 0;
                    foreach (var r in b.Regels)
                    {
                        x.WriteStartElement("trLine", Namespace);
                        Veld(x, "nr", (++regelnr).ToString(Inv));
                        Veld(x, "accID", r.Rekening);
                        Veld(x, "docRef", r.Factuurnummer ?? b.Boekstuk);
                        Veld(x, "effDate", Datum(b.Datum));
                        Veld(x, "desc", r.Omschrijving ?? b.Omschrijving ?? "");
                        Veld(x, "amnt", Bedrag(Math.Abs(r.Bedrag)));
                        Veld(x, "amntTp", r.Bedrag < 0 ? "C" : "D");
                        Leeg(x, "custSupID", r.Relatie);
                        Leeg(x, "invRef", r.Factuurnummer);
                        x.WriteEndElement();
                    }

                    x.WriteEndElement();
                }

                x.WriteEndElement();
            }

            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndDocument();
        }

        return uit.ToArray();
    }

    /// <summary>Het dagboektype van XAF (jrnTp): B bank, C kas, P inkoop, S verkoop, Z memoriaal.</summary>
    public static string Soortcode(Dagboeksoort soort) => soort switch
    {
        Dagboeksoort.Bank => "B",
        Dagboeksoort.Kas => "C",
        Dagboeksoort.Inkoop => "P",
        Dagboeksoort.Verkoop => "S",
        _ => "Z",
    };

    private static void Veld(XmlWriter x, string naam, string waarde) => x.WriteElementString(naam, Namespace, waarde);

    private static void Leeg(XmlWriter x, string naam, string? waarde)
    {
        if (!string.IsNullOrWhiteSpace(waarde))
        {
            Veld(x, naam, waarde);
        }
    }

    private static string Datum(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    private static string Bedrag(decimal b) => b.ToString("0.00", Inv);
}
