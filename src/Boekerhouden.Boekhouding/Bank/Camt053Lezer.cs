using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Boekerhouden.Boekhouding.Bank;

/// <summary>
/// Leest CAMT.053-bestanden (ISO 20022, <c>BkToCstmrStmt</c>), ongeacht de versie van het schema: de oude
/// <c>camt.053.001.02</c> en de versie van 2019 (<c>camt.053.001.08</c>, waar de banken naar overgaan). In die laatste
/// staat de tegenpartij onder <c>Pty</c>, is de status een code (<c>Sts/Cd</c>) en hebben datums vaak een tijdzone;
/// een datum is altijd de dag zoals de bank hem schrijft, zonder omrekenen. Per afschrift
/// (<c>Stmt</c>) de rekening, het begin- en eindsaldo (<c>OPBD</c>/<c>PRCD</c> en <c>CLBD</c>) en per boeking
/// (<c>Ntry</c>) het bedrag, de datum, de tegenpartij, de omschrijving en de referenties. Een verzamelboeking met
/// meerdere transacties met elk een eigen bedrag wordt in die transacties gesplitst.
/// </summary>
public static class Camt053Lezer
{
    public static IReadOnlyList<Bankafschrift> Lees(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XDocument document;
        try
        {
            using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(xml);
        }
        catch (XmlException fout)
        {
            throw new BankbestandException("Het bestand is geen geldige XML.", fout);
        }

        var afschriften = document.Descendants().Where(e => e.Name.LocalName == "Stmt").Select(Afschrift).ToList();
        if (afschriften.Count == 0)
        {
            throw new BankbestandException("Het bestand bevat geen CAMT.053-afschrift (geen Stmt).");
        }

        return afschriften;
    }

    private static Bankafschrift Afschrift(XElement stmt)
    {
        var rekening = Kind(stmt, "Acct");
        var iban = Tekst(Kind(Kind(rekening, "Id"), "IBAN"))
                   ?? Tekst(Kind(Kind(Kind(rekening, "Id"), "Othr"), "Id"))
                   ?? throw new BankbestandException("Een afschrift heeft geen rekening (Acct/Id).");
        var saldi = Kinderen(stmt, "Bal").Select(b => (Code: Tekst(Kind(Kind(Kind(b, "Tp"), "CdOrPrtry"), "Cd")), Bedrag: Bedrag(b), Datum: Datum(Kind(b, "Dt")))).ToList();
        var begin = saldi.FirstOrDefault(s => s.Code is "OPBD" or "PRCD");
        var eind = saldi.FirstOrDefault(s => s.Code is "CLBD");
        if (begin.Code is null || eind.Code is null)
        {
            throw new BankbestandException($"Het afschrift van {iban} mist het begin- of eindsaldo (OPBD/PRCD en CLBD).");
        }

        // Alleen geboekte mutaties: een reservering (PDNG) of informatieve regel (INFO) telt niet mee in het saldo.
        var mutaties = Kinderen(stmt, "Ntry").Where(Geboekt).SelectMany(Mutaties).ToList();
        return new Bankafschrift(iban, Tekst(Kind(stmt, "ElctrncSeqNb")) ?? Tekst(Kind(stmt, "LglSeqNb")) ?? Tekst(Kind(stmt, "Id")), Tekst(Kind(rekening, "Ccy")) ?? "EUR",
            begin.Datum, begin.Bedrag, eind.Datum, eind.Bedrag, mutaties);
    }

    /// <summary>De status staat als tekst (<c>Sts</c>, oude versie) of als code (<c>Sts/Cd</c>, 2019); zonder status geldt geboekt.</summary>
    private static bool Geboekt(XElement ntry)
    {
        var status = Kind(ntry, "Sts");
        return (Tekst(Kind(status, "Cd")) ?? Tekst(Kind(status, "Prtry")) ?? Tekst(status)) is null or "BOOK";
    }

    private static IEnumerable<Bankmutatie> Mutaties(XElement ntry)
    {
        var bedrag = Bedrag(ntry);
        if (Tekst(Kind(ntry, "RvslInd")) is "true")
        {
            bedrag = -bedrag;
        }

        var datum = Datum(Kind(ntry, "BookgDt")) ?? Datum(Kind(ntry, "ValDt"))
                    ?? throw new BankbestandException("Een boeking heeft geen datum (BookgDt).");
        var transacties = Kind(ntry, "NtryDtls")?.Elements().Where(e => e.Name.LocalName == "TxDtls").ToList() ?? [];
        var algemeen = Tekst(Kind(ntry, "AddtlNtryInf"));

        // Verzamelboeking: alleen splitsen als elke transactie een bedrag heeft en de som klopt.
        var bedragen = transacties.Select(TransactieBedrag).ToList();
        if (transacties.Count > 1 && bedragen.All(b => b is not null) && bedragen.Sum(b => Math.Abs(b!.Value)) == Math.Abs(bedrag))
        {
            var teken = Math.Sign(bedrag);
            return transacties.Select((t, i) => Mutatie(datum, teken * Math.Abs(bedragen[i]!.Value), t, algemeen));
        }

        // Een verzamelboeking met bij- en afschrijvingen door elkaar (2019: elke transactie heeft een eigen richting).
        var metRichting = transacties.Select(t => (Bedrag: TransactieBedrag(t), Richting: Tekst(Kind(t, "CdtDbtInd")))).ToList();
        if (transacties.Count > 1 && metRichting.All(t => t.Bedrag is not null && t.Richting is not null)
            && metRichting.Sum(t => t.Richting == "DBIT" ? -Math.Abs(t.Bedrag!.Value) : Math.Abs(t.Bedrag!.Value)) == bedrag)
        {
            return transacties.Select((t, i) => Mutatie(datum, metRichting[i].Richting == "DBIT" ? -Math.Abs(metRichting[i].Bedrag!.Value) : Math.Abs(metRichting[i].Bedrag!.Value), t, algemeen));
        }

        return [Mutatie(datum, bedrag, transacties.FirstOrDefault(), algemeen)];
    }

    private static Bankmutatie Mutatie(DateOnly datum, decimal bedrag, XElement? tx, string? algemeen)
    {
        // Bij een ontvangst is de tegenpartij de betaler (Dbtr), bij een betaling de ontvanger (Cdtr).
        var partij = bedrag >= 0 ? "Dbtr" : "Cdtr";
        var partijen = Kind(tx, "RltdPties");
        var naam = Naam(Kind(partijen, partij)) ?? Naam(Kind(partijen, "Ultmt" + partij));
        var iban = Tekst(Kind(Kind(Kind(partijen, partij + "Acct"), "Id"), "IBAN"))
                   ?? Tekst(Kind(Kind(Kind(Kind(partijen, partij + "Acct"), "Id"), "Othr"), "Id"));
        var rmt = Kind(tx, "RmtInf");
        var vrij = string.Join(' ', Kinderen(rmt, "Ustrd").Select(Tekst).Where(t => t is not null));
        var kenmerk = Tekst(Kind(Kind(Kind(rmt, "Strd"), "CdtrRefInf"), "Ref"));
        var eref = Tekst(Kind(Kind(tx, "Refs"), "EndToEndId"));
        if (eref is "NOTPROVIDED")
        {
            eref = null;
        }

        var omschrijving = vrij.Length > 0 ? vrij : kenmerk ?? Tekst(Kind(tx, "AddtlTxInf")) ?? algemeen ?? string.Empty;
        return new Bankmutatie(datum, bedrag, omschrijving, iban, naam, kenmerk, eref);
    }

    /// <summary>De naam van een partij: direct (<c>Nm</c>) of, sinds 2019, onder <c>Pty</c>.</summary>
    private static string? Naam(XElement? partij) => Tekst(Kind(partij, "Nm")) ?? Tekst(Kind(Kind(partij, "Pty"), "Nm"));

    private static decimal? TransactieBedrag(XElement tx) =>
        Kind(tx, "Amt") is { } amt ? Getal(amt.Value)
        : Kind(Kind(Kind(tx, "AmtDtls"), "TxAmt"), "Amt") is { } tx2 ? Getal(tx2.Value)
        : null;

    private static decimal Bedrag(XElement element)
    {
        var amt = Kind(element, "Amt") ?? throw new BankbestandException($"Geen bedrag (Amt) in {element.Name.LocalName}.");
        var bedrag = Getal(amt.Value);
        return Tekst(Kind(element, "CdtDbtInd")) == "DBIT" ? -bedrag : bedrag;
    }

    private static decimal Getal(string tekst) =>
        decimal.TryParse(tekst.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var getal)
            ? getal
            : throw new BankbestandException($"Bedrag '{tekst}' is niet te lezen.");

    private static DateOnly? Datum(XElement? element)
    {
        var tekst = Tekst(Kind(element, "Dt")) ?? Tekst(Kind(element, "DtTm"));
        if (tekst is null)
        {
            return null;
        }

        // De dag zoals de bank hem schrijft: "2026-05-04T23:30:00+02:00" en "2026-05-04+02:00" zijn 4 mei, ook als dat
        // in een andere tijdzone al de volgende dag is.
        return tekst.Length >= 10 && DateOnly.TryParseExact(tekst[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum)
            ? datum
            : throw new BankbestandException($"Datum '{tekst}' is niet te lezen.");
    }

    private static XElement? Kind(XElement? element, string naam) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName == naam);

    private static IEnumerable<XElement> Kinderen(XElement? element, string naam) =>
        element?.Elements().Where(e => e.Name.LocalName == naam) ?? [];

    private static string? Tekst(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();
}
