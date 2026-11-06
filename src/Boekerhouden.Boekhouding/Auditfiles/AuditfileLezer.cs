using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Boekhouding.Auditfiles;

/// <summary>
/// Leest een XML Auditfile Financieel (XAF 3.1 en 3.2; van 4.0 de velden die hetzelfde heten). Veilig: zonder DTD en
/// zonder externe verwijzingen, en begrensd in grootte. Elementen worden op hun naam gezocht, los van de namespace.
/// Na het lezen worden de opgegeven aantallen en totalen nagerekend; een verschil is een fout.
/// </summary>
public static partial class AuditfileLezer
{
    /// <summary>Groter dan dit leest het pakket niet in (tekens in het document).</summary>
    public const long MaximumTekens = 400_000_000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Leest de auditfile; de lijst met fouten bevat wat niet klopt (aantallen, totalen, evenwicht, onbekende rekeningen).</summary>
    public static (Auditfile Auditfile, IReadOnlyList<string> Fouten) Lees(Stream inhoud)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        XDocument doc;
        try
        {
            using var lezer = XmlReader.Create(inhoud, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumTekens,
                IgnoreComments = true,
            });
            doc = XDocument.Load(lezer);
        }
        catch (XmlException fout)
        {
            throw new AuditfileException($"Geen geldige XML: {fout.Message}", fout);
        }

        var wortel = doc.Root;
        if (wortel is null || wortel.Name.LocalName != "auditfile")
        {
            throw new AuditfileException("Dit is geen auditfile (het hoofdelement heet geen 'auditfile').");
        }

        var ns = wortel.Name.NamespaceName;
        var versie = ns.Contains("3.1", StringComparison.Ordinal) ? "3.1" : ns.Contains("3.2", StringComparison.Ordinal) ? "3.2"
            : ns.Contains("4.0", StringComparison.Ordinal) ? "4.0" : "onbekend";
        var fouten = new List<string>();
        var header = Kind(wortel, "header") ?? throw new AuditfileException("De auditfile heeft geen header.");
        var company = Kind(wortel, "company") ?? throw new AuditfileException("De auditfile heeft geen company.");

        var begin = Datum(Tekst(header, "startDate")) ?? throw new AuditfileException("De auditfile heeft geen startDate.");
        var eind = Datum(Tekst(header, "endDate")) ?? begin.AddYears(1).AddDays(-1);
        var kop = new AuditfileKop(int.TryParse(Tekst(header, "fiscalYear"), NumberStyles.Integer, Inv, out var jaar) ? jaar : begin.Year, begin, eind,
            Tekst(header, "curCode") ?? "EUR", Datum(Tekst(header, "dateCreated")) ?? begin, Tekst(header, "softwareDesc") ?? "", Tekst(header, "softwareVersion") ?? "");

        var adres = Kind(company, "streetAddress");
        var bedrijf = new AuditfileBedrijf(Tekst(company, "companyName") ?? "Onbekend", Kvk(Tekst(company, "companyIdent")) ?? Kvk(Tekst(company, "commerceNr")),
            Leeg(Tekst(company, "taxRegIdent")), adres is null ? null : Straat(adres), adres is null ? null : Tekst(adres, "postalCode"),
            adres is null ? null : Tekst(adres, "city"), adres is null ? null : Tekst(adres, "country"));

        var relaties = Kinderen(Kind(company, "customersSuppliers"), "customerSupplier").Select(c =>
        {
            var tp = Tekst(c, "custSupTp") ?? "O";
            return new AuditfileRelatie(Tekst(c, "custSupID") ?? "", Tekst(c, "custSupName") ?? Tekst(c, "custSupID") ?? "", Kvk(Tekst(c, "commerceNr")),
                Leeg(Tekst(c, "taxRegIdent")), tp is "C" or "B", tp is "S" or "B",
                [.. Kinderen(c, "bankAccount").Select(b => Tekst(b, "bankAccNr")).OfType<string>().Where(t => t.Length > 0)]);
        }).Where(r => r.Code.Length > 0).ToList();

        var rekeningen = Kinderen(Kind(company, "generalLedger"), "ledgerAccount").Select(l =>
        {
            var rgs = RgsCode(l);
            var tp = Tekst(l, "accTp");
            var soort = tp == "P" ? Rekeningsoort.Resultaat : tp == "B" ? Rekeningsoort.Balans
                : rgs is { } r && r.StartsWith('W') ? Rekeningsoort.Resultaat : Rekeningsoort.Balans;
            return new AuditfileRekening(Tekst(l, "accID") ?? "", Tekst(l, "accDesc") ?? Tekst(l, "accID") ?? "", soort, rgs);
        }).Where(r => r.Nummer.Length > 0).ToList();

        var btwcodes = Kinderen(Kind(company, "vatCodes"), "vatCode").Select(v =>
            new AuditfileBtwcode(Tekst(v, "vatID") ?? "", Tekst(v, "vatDesc") ?? "", Leeg(Tekst(v, "vatToPayAccID")), Leeg(Tekst(v, "vatToClaimAccID"))))
            .Where(v => v.Code.Length > 0).ToList();

        var opening = Kind(company, "openingBalance");
        var beginbalans = Kinderen(opening, "obLine").Select(o => new AuditfileRegel(Tekst(o, "accID") ?? "", Bedrag(o, fouten))).ToList();
        if (opening is not null)
        {
            Natellen(opening, "beginbalans", beginbalans, fouten);
        }

        var transacties = Kind(company, "transactions");
        var dagboeken = Kinderen(transacties, "journal").Select(j =>
        {
            var tp = Tekst(j, "jrnTp");
            var soort = tp switch
            {
                "B" => Dagboeksoort.Bank,
                "C" => Dagboeksoort.Kas,
                "P" => Dagboeksoort.Inkoop,
                "S" => Dagboeksoort.Verkoop,
                _ => Dagboeksoort.Memoriaal,
            };
            var boekingen = Kinderen(j, "transaction").Select(t =>
            {
                var datum = Datum(Tekst(t, "trDt")) ?? begin;
                return new AuditfileBoeking(Tekst(t, "nr") ?? "", Leeg(Tekst(t, "desc")), datum,
                    int.TryParse(Tekst(t, "periodNumber"), NumberStyles.Integer, Inv, out var p) ? p : datum.Month,
                    [.. Kinderen(t, "trLine").Select(r => new AuditfileRegel(Tekst(r, "accID") ?? "", Bedrag(r, fouten), Leeg(Tekst(r, "desc")),
                        Leeg(Tekst(r, "custSupID")), Leeg(Tekst(r, "invRef")), Leeg(Tekst(Kind(r, "vat"), "vatID"))))]);
            }).ToList();
            var code = Tekst(j, "jrnID") ?? "";
            return new AuditfileDagboek(code, Tekst(j, "desc") ?? code, soort, Leeg(Tekst(j, "offsetAccID")), boekingen);
        }).Where(d => d.Code.Length > 0).ToList();
        if (transacties is not null)
        {
            Natellen(transacties, "boekingen", [.. dagboeken.SelectMany(d => d.Boekingen).SelectMany(b => b.Regels)], fouten);
        }

        var auditfile = new Auditfile(versie, kop, bedrijf, relaties, rekeningen, btwcodes, opening is null ? null : Datum(Tekst(opening, "opBalDate")), beginbalans, dagboeken);
        fouten.AddRange(auditfile.Controleer());
        return (auditfile, fouten);
    }

    /// <summary>
    /// De RGS-code van een rekening, waar die ook staat: RGScode (4.0), taxonomy (taxoRef of conceptRef), leadReference,
    /// of leadCode als die op een RGS-code lijkt.
    /// </summary>
    private static string? RgsCode(XElement rekening)
    {
        var kandidaten = new[]
        {
            Tekst(rekening, "RGScode"),
            Kind(rekening, "taxonomy") is { } tax ? Tekst(tax, "taxoRef") ?? Tekst(tax, "conceptRef") : null,
            Tekst(rekening, "leadReference"),
            Tekst(rekening, "leadCode"),
        };
        return kandidaten.FirstOrDefault(k => k is not null && RgsVorm().IsMatch(k));
    }

    [GeneratedRegex("^[BW][A-Z][A-Za-z0-9]{2,}$")]
    private static partial Regex RgsVorm();

    /// <summary>Rekent het opgegeven aantal regels en de totalen debet en credit na.</summary>
    private static void Natellen(XElement e, string wat, List<AuditfileRegel> regels, List<string> fouten)
    {
        if (int.TryParse(Tekst(e, "linesCount"), NumberStyles.Integer, Inv, out var aantal) && aantal != regels.Count)
        {
            fouten.Add($"Het aantal regels van de {wat} klopt niet: opgegeven {aantal}, gevonden {regels.Count}.");
        }

        if (Getal(Tekst(e, "totalDebit")) is { } debet && debet != regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag))
        {
            fouten.Add($"Het totaal debet van de {wat} klopt niet: opgegeven {debet:0.00}, gevonden {regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag):0.00}.");
        }

        if (Getal(Tekst(e, "totalCredit")) is { } credit && credit != -regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag))
        {
            fouten.Add($"Het totaal credit van de {wat} klopt niet: opgegeven {credit:0.00}, gevonden {-regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag):0.00}.");
        }
    }

    /// <summary>Het bedrag van een regel: amnt met amntTp (D of C); zonder amntTp telt het teken van amnt (4.0).</summary>
    private static decimal Bedrag(XElement regel, List<string> fouten)
    {
        var tekst = Tekst(regel, "amnt");
        if (Getal(tekst) is not { } bedrag)
        {
            fouten.Add($"Bedrag '{tekst}' is niet te lezen.");
            return 0m;
        }

        return Tekst(regel, "amntTp") switch
        {
            "C" => -Math.Abs(bedrag),
            "D" => Math.Abs(bedrag),
            _ => bedrag,
        };
    }

    private static decimal? Getal(string? tekst) =>
        decimal.TryParse(tekst, NumberStyles.Number, Inv, out var g) ? g : null;

    private static DateOnly? Datum(string? tekst) =>
        DateOnly.TryParseExact(tekst?.Length > 10 ? tekst[..10] : tekst, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;

    private static string? Kvk(string? tekst) => tekst is { Length: 8 } && tekst.All(char.IsAsciiDigit) ? tekst : null;

    private static string? Straat(XElement adres) =>
        Leeg(string.Join(' ', new[] { Tekst(adres, "streetname"), Tekst(adres, "number"), Tekst(adres, "numberExtension") }.Where(t => !string.IsNullOrWhiteSpace(t))));

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();

    private static XElement? Kind(XElement? e, string naam) => e?.Elements().FirstOrDefault(k => k.Name.LocalName == naam);

    private static IEnumerable<XElement> Kinderen(XElement? e, string naam) => e?.Elements().Where(k => k.Name.LocalName == naam) ?? [];

    private static string? Tekst(XElement? e, string naam) => Kind(e, naam)?.Value.Trim();
}
