using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Boekerhouden.Administratie.Data;
using Boekerhouden.Documenten;
using Microsoft.EntityFrameworkCore;

namespace Boekerhouden.Administratie.Diensten;

/// <summary>Wat een losgelaten bestand is, en dus waar het heen gaat.</summary>
public enum Inleessoort
{
    Inkoop,
    Verkoop,
    Bank,
    Loonjournaal,
}

/// <summary>Wat er met één bestand is gebeurd: de soort, of het gelukt is, en de melding van het inlezen.</summary>
/// <param name="Nakijken">Wel ingelezen, maar niet als factuur herkend: staat bij inkoop om met de hand na te kijken.</param>
public sealed record Overalingelezen(string Bestand, Inleessoort Soort, bool Gelukt, string Melding, bool Nakijken = false);

/// <summary>De eigen gegevens van de administratie waarmee een verkoopfactuur te herkennen is.</summary>
public sealed record Eigengegevens(string Naam, string? KvkNummer, string? BtwNummer, IReadOnlyList<string> Ibans);

/// <summary>
/// Overal slepen: een bestand dat op een willekeurige pagina wordt losgelaten, herkent Sifer zelf en zet het in de
/// juiste werklijst. Een afschrift (MT940, CAMT.053, een PDF met begin- en eindsaldo) gaat naar de bank, een
/// loonjournaal naar het memoriaal (daar worden de rekeningen gekoppeld), een factuur waar het eigen IBAN of KvK-nummer
/// op staat (of een e-factuur met de eigen onderneming als leverancier) naar verkoop, en de rest naar inkoop. Een PDF
/// wordt pas geopend na de virusscan (besluit B3); zonder schone scan gaat hij naar inkoop, die hem in quarantaine zet.
/// </summary>
public sealed class InleesDienst(
    Toegang toegang,
    InkoopDienst inkoop,
    VerkoopimportDienst verkoop,
    BankDienst bank,
    IEnumerable<IVirusscanner> virusscanners)
{
    /// <summary>Hoe groot een bestand mag zijn: het grootste van wat de werklijsten aannemen.</summary>
    public const int MaximumBytes = 20 * 1024 * 1024;

    private static readonly string[] Bankextensies = [".mt940", ".940", ".sta", ".swi", ".camt"];
    private static readonly string[] Afbeeldingen = [".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff"];

    /// <summary>
    /// Leest één bestand in waar het hoort. Een loonjournaal wordt niet hier geboekt (de rekeningen moeten eerst
    /// gekoppeld worden): dan is <see cref="Overalingelezen.Gelukt"/> waar en gaat de pagina naar het memoriaal.
    /// </summary>
    public async Task<Overalingelezen> Lees(Uitvoerder wie, Guid administratie, byte[] inhoud, string bestandsnaam, string? inhoudstype, CancellationToken annulering = default)
    {
        ArgumentNullException.ThrowIfNull(inhoud);
        var naam = Path.GetFileName(bestandsnaam);
        Eigengegevens eigen;
        await using (var db = await toegang.Open(wie, administratie, Recht.Invoeren))
        {
            var a = await db.Administraties.AsNoTracking().SingleAsync(x => x.Id == administratie, annulering);
            var bankIbans = await db.Dagboeken.Where(d => d.Iban != null).Select(d => d.Iban!).ToListAsync(annulering);
            eigen = new Eigengegevens(a.Naam, a.KvkNummer, a.BtwNummer, [.. a.Ibans, .. bankIbans]);
        }

        var soort = Herken(naam, inhoud, eigen, await MagOpenen(inhoud, naam, annulering) ? PdfTekst.Woorden : null);
        switch (soort)
        {
            case Inleessoort.Bank:
                var b = await bank.Lees(wie, administratie, new MemoryStream(inhoud), naam, annulering);
                return new(naam, soort, b.IsGelukt, b.IsGelukt ? "afschrift ingelezen" : string.Join(" ", b.Fouten));
            case Inleessoort.Verkoop:
                var v = await verkoop.Lees(wie, administratie, new MemoryStream(inhoud), naam, inhoudstype, annulering);
                return new(naam, soort, v.Id is not null && !v.Dubbel, v.Melding);
            case Inleessoort.Loonjournaal:
                return new(naam, soort, true, "loonjournaal: de rekeningen koppelen in het memoriaal");
            default:
                var i = await inkoop.Lees(wie, administratie, new MemoryStream(inhoud), naam, inhoudstype, annulering: annulering);
                return new(naam, Inleessoort.Inkoop, i.DocumentId is not null && i.Status != Documentstatus.Quarantaine && !i.Dubbel, i.Melding, i.NietHerkend);
        }
    }

    /// <summary>Of een PDF geopend mag worden om te herkennen: alleen na een schone virusscan (zonder scanner: lokaal, mag).</summary>
    private async Task<bool> MagOpenen(byte[] inhoud, string naam, CancellationToken annulering)
    {
        // Op naam én op inhoud: een PDF met een andere extensie wordt ook als PDF geopend (Herken), dus ook gescand.
        if (!naam.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !IsPdf(inhoud))
        {
            return true;
        }

        if (virusscanners.FirstOrDefault() is not { } scanner)
        {
            return true;
        }

        var scan = await scanner.ScanAsync(new MemoryStream(inhoud), annulering);
        return scan.Uitslag == Scanuitslag.Schoon;
    }

    internal static bool IsPdf(byte[] inhoud) =>
        Encoding.Latin1.GetString(inhoud, 0, Math.Min(inhoud.Length, 4096)).StartsWith("%PDF-", StringComparison.Ordinal);

    /// <summary>
    /// De soort van een bestand, op naam en inhoud. <paramref name="pdfWoorden"/> leest de tekst van een PDF; leeg als
    /// de PDF niet geopend mag worden (dan inkoop).
    /// </summary>
    internal static Inleessoort Herken(string naam, byte[] inhoud, Eigengegevens eigen, Func<byte[], IReadOnlyList<Tekstwoord>>? pdfWoorden)
    {
        var extensie = Path.GetExtension(naam).ToLowerInvariant();
        if (Bankextensies.Contains(extensie))
        {
            return Inleessoort.Bank;
        }

        if (Afbeeldingen.Contains(extensie))
        {
            return Inleessoort.Inkoop;
        }

        var begin = Encoding.UTF8.GetString(inhoud, 0, Math.Min(inhoud.Length, 4096));
        if (extensie == ".pdf" || begin.StartsWith("%PDF-", StringComparison.Ordinal))
        {
            if (pdfWoorden is null)
            {
                return Inleessoort.Inkoop;
            }

            IReadOnlyList<Tekstwoord> woorden;
            try
            {
                woorden = pdfWoorden(inhoud);
            }
            catch (DocumentException)
            {
                return Inleessoort.Inkoop;
            }

            return HerkenTekst(string.Join(' ', woorden.Select(w => w.Tekst)), eigen);
        }

        if (begin.Contains("camt.053", StringComparison.OrdinalIgnoreCase))
        {
            return Inleessoort.Bank;
        }

        if (begin.TrimStart().StartsWith(":20:", StringComparison.Ordinal) || begin.Contains("\n:61:", StringComparison.Ordinal))
        {
            return Inleessoort.Bank;
        }

        if (extensie == ".xml" || begin.TrimStart().StartsWith('<'))
        {
            return Leverancier(inhoud) is { } leverancier && IsEigen(leverancier, eigen) ? Inleessoort.Verkoop : Inleessoort.Inkoop;
        }

        return Inleessoort.Inkoop;
    }

    /// <summary>
    /// De soort van een PDF op zijn tekst: een afschrift (saldo's), een loonjournaal, of een factuur; een factuur met
    /// het eigen IBAN of KvK-nummer is een verkoopfactuur (op een inkoopfactuur staat het IBAN van de leverancier).
    /// </summary>
    internal static Inleessoort HerkenTekst(string tekst, Eigengegevens eigen)
    {
        var klein = tekst.ToLowerInvariant();
        bool Bevat(params string[] woorden) => woorden.Any(w => klein.Contains(w, StringComparison.Ordinal));

        if (Bevat("rekeningafschrift", "vorig saldo", "beginsaldo", "saldo vorig afschrift") && Bevat("nieuw saldo", "eindsaldo", "geboekt op", "saldo nieuw afschrift"))
        {
            return Inleessoort.Bank;
        }

        if (Bevat("loonjournaal") || (Bevat("journaalpost") && Bevat("loonheffing")))
        {
            return Inleessoort.Loonjournaal;
        }

        var compact = Compact(tekst);
        if (eigen.Ibans.Any(i => Compact(i) is { Length: >= 15 } iban && OpFactuurAlsBetaalrekening(tekst, iban)))
        {
            return Inleessoort.Verkoop;
        }

        return eigen.KvkNummer is { } kvk && Compact(kvk) is { Length: 8 } k && compact.Contains(k, StringComparison.Ordinal)
            ? Inleessoort.Verkoop
            : Inleessoort.Inkoop;
    }

    /// <summary>
    /// Of het eigen IBAN op de factuur staat als de rekening om op te betalen (dan is het een verkoopfactuur), en niet
    /// alleen als de rekening waarvan de leverancier het bedrag afschrijft ("wordt afgeschreven van rekeningnummer …",
    /// automatische incasso): dat staat op een inkoopfactuur.
    /// </summary>
    private static bool OpFactuurAlsBetaalrekening(string tekst, string iban)
    {
        var patroon = string.Join(@"\s*", iban.Select(c => Regex.Escape(c.ToString())));
        foreach (Match m in Regex.Matches(tekst, patroon, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var ervoor = tekst[Math.Max(0, m.Index - 100)..m.Index].ToLowerInvariant();
            if (!Incassowoorden.Any(w => ervoor.Contains(w, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] Incassowoorden =
        ["afgeschreven", "afschrijven", "incasso", "geïncasseerd", "geincasseerd", "incasseren", "machtiging", "van uw rekening", "van uw bankrekening"];

    /// <summary>De leverancier in een e-factuur (UBL): de namen en de nummers (btw, KvK, endpoint).</summary>
    private static (List<string> Namen, List<string> Nummers)? Leverancier(byte[] inhoud)
    {
        try
        {
            var instellingen = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var lezer = XmlReader.Create(new MemoryStream(inhoud), instellingen);
            var xml = XDocument.Load(lezer);
            if (xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "AccountingSupplierParty") is not { } partij)
            {
                return null;
            }

            List<string> Waarden(params string[] namen) => [.. partij.Descendants()
                .Where(e => namen.Contains(e.Name.LocalName) && !e.HasElements).Select(e => e.Value.Trim()).Where(v => v.Length > 0)];
            return (Waarden("Name", "RegistrationName"), Waarden("CompanyID", "EndpointID"));
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Of de leverancier de eigen onderneming is: dezelfde naam, of (zonder naam) hetzelfde btw- of KvK-nummer. Op de
    /// naam, want een nummer alleen kan een vergissing zijn (een leverancier met het btw-nummer van de klant erin).
    /// </summary>
    private static bool IsEigen((List<string> Namen, List<string> Nummers) leverancier, Eigengegevens eigen)
    {
        if (leverancier.Namen.Count > 0)
        {
            return leverancier.Namen.Any(n => Compact(n) == Compact(eigen.Naam));
        }

        return leverancier.Nummers.Any(w =>
            (eigen.BtwNummer is { } btw && Compact(btw).Length > 0 && Compact(w) == Compact(btw))
            || (eigen.KvkNummer is { } kvk && Compact(kvk).Length > 0 && Compact(w) == Compact(kvk)));
    }

    private static string Compact(string tekst) => new([.. tekst.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
