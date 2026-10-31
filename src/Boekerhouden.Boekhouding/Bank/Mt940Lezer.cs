using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Relaties;

namespace Boekerhouden.Boekhouding.Bank;

/// <summary>
/// Leest MT940-bestanden (SWIFT-afschrift) zoals Nederlandse banken die leveren. Een bestand kan meerdere afschriften
/// bevatten; elk begint met <c>:20:</c>. De omschrijving (<c>:86:</c>) verschilt per bank:
/// <list type="bullet">
/// <item>gestructureerd met codes als <c>/CNTP/</c>, <c>/NAME/</c>, <c>/IBAN/</c>, <c>/REMI/</c> en <c>/EREF/</c>;</item>
/// <item>vrije tekst met <c>IBAN:</c>, <c>NAAM:</c>, <c>OMSCHRIJVING:</c> en <c>KENMERK:</c>;</item>
/// <item>vrije tekst zonder labels: IBAN en naam op de eerste regel, de omschrijving daaronder.</item>
/// </list>
/// </summary>
public static partial class Mt940Lezer
{
    private const int Regellengte = 65;

    private static readonly string[] Codes =
    [
        "EREF", "MARF", "CSID", "CNTP", "REMI", "PURP", "ULTC", "ULTD", "ULTB", "BENM", "ORDP", "NAME", "ID", "ADDR", "ISDT",
        "IBAN", "BIC", "TRTP", "CREF", "IREF", "RTRN", "SVCL", "BUSP", "ACCW", "BENB", "ORDB", "CDTRREF", "CDTRREFTP", "CDTRREFTPCD",
    ];

    public static IReadOnlyList<Bankafschrift> Lees(TextReader lezer)
    {
        ArgumentNullException.ThrowIfNull(lezer);
        var afschriften = new List<Bankafschrift>();
        Opbouw? huidig = null;
        foreach (var (tag, waarde, regel) in Velden(lezer))
        {
            try
            {
                switch (tag)
                {
                    case "20":
                        Sluit(huidig, afschriften);
                        huidig = new Opbouw();
                        break;
                    case "25":
                        Vereist(huidig, tag).Iban = Rekening(waarde);
                        break;
                    case "28C" or "28":
                        Vereist(huidig, tag).Nummer = waarde.Trim();
                        break;
                    case "60F" or "60M":
                        var begin = Saldo(waarde);
                        Vereist(huidig, tag);
                        if (huidig!.Beginsaldo is null)
                        {
                            (huidig.Begindatum, huidig.Beginsaldo, huidig.Valuta) = begin;
                        }

                        break;
                    case "61":
                        Vereist(huidig, tag).Mutaties.Add(Mutatieregel(waarde));
                        break;
                    case "86":
                        var opbouw = Vereist(huidig, tag);
                        if (opbouw.Mutaties.Count > 0 && opbouw.Mutaties[^1].Informatie is null)
                        {
                            opbouw.Mutaties[^1].Informatie = waarde;
                        }

                        break;
                    case "62F" or "62M":
                        (Vereist(huidig, tag).Einddatum, huidig!.Eindsaldo, _) = Saldo(waarde);
                        break;
                }
            }
            catch (Exception fout) when (fout is FormatException or OverflowException or ArgumentException)
            {
                throw new BankbestandException($"Regel {regel} (:{tag}:) is niet te lezen: {fout.Message}", fout);
            }
        }

        Sluit(huidig, afschriften);
        if (afschriften.Count == 0)
        {
            throw new BankbestandException("Het bestand bevat geen MT940-afschrift (geen :20:).");
        }

        return afschriften;
    }

    /// <summary>De velden van het bestand, met vervolgregels samengevoegd.</summary>
    private static IEnumerable<(string Tag, string Waarde, int Regel)> Velden(TextReader lezer)
    {
        string? tag = null;
        var waarde = new StringBuilder();
        var vorigeLengte = 0;
        var begonnenOp = 0;
        var nummer = 0;
        while (lezer.ReadLine() is { } regel)
        {
            nummer++;
            var match = Veldbegin().Match(regel);
            if (match.Success)
            {
                if (tag is not null)
                {
                    yield return (tag, waarde.ToString(), begonnenOp);
                }

                tag = match.Groups[1].Value;
                waarde.Clear().Append(regel[match.Length..]);
                vorigeLengte = regel.Length;
                begonnenOp = nummer;
            }
            else if (tag is not null && regel.Length > 0 && !regel.StartsWith('-'))
            {
                // Volle regels (65 tekens) zijn afgebroken midden in de tekst: zonder spatie samenvoegen.
                waarde.Append(vorigeLengte >= Regellengte ? string.Empty : "\n").Append(regel);
                vorigeLengte = regel.Length;
            }
        }

        if (tag is not null)
        {
            yield return (tag, waarde.ToString(), begonnenOp);
        }
    }

    private static Opbouw Vereist(Opbouw? opbouw, string tag) =>
        opbouw ?? throw new FormatException($"veld :{tag}: staat vóór het begin van een afschrift (:20:).");

    private static void Sluit(Opbouw? opbouw, List<Bankafschrift> afschriften)
    {
        if (opbouw is null)
        {
            return;
        }

        if (opbouw.Iban is null || opbouw.Beginsaldo is null || opbouw.Eindsaldo is null)
        {
            throw new BankbestandException("Een afschrift mist de rekening (:25:), het beginsaldo (:60F:) of het eindsaldo (:62F:).");
        }

        afschriften.Add(new Bankafschrift(opbouw.Iban, opbouw.Nummer, opbouw.Valuta ?? "EUR", opbouw.Begindatum, opbouw.Beginsaldo.Value,
            opbouw.Einddatum, opbouw.Eindsaldo.Value, opbouw.Mutaties.Select(Maak).ToList()));
    }

    private static string Rekening(string waarde)
    {
        var tekst = string.Concat(waarde.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
        var iban = IbanPatroon().Match(tekst);
        if (iban.Success)
        {
            var kandidaat = iban.Value;
            for (var lengte = kandidaat.Length; lengte >= 15; lengte--)
            {
                if (Iban.IsGeldig(kandidaat[..lengte]))
                {
                    return kandidaat[..lengte];
                }
            }
        }

        return tekst;
    }

    private static (DateOnly Datum, decimal Saldo, string Valuta) Saldo(string waarde)
    {
        var m = SaldoPatroon().Match(waarde.Trim());
        if (!m.Success)
        {
            throw new FormatException($"saldo '{waarde.Trim()}' heeft niet de vorm C/D + JJMMDD + valuta + bedrag.");
        }

        var bedrag = Bedrag(m.Groups[4].Value);
        return (Datum(m.Groups[2].Value), m.Groups[1].Value == "D" ? -bedrag : bedrag, m.Groups[3].Value);
    }

    private static Ruwemutatie Mutatieregel(string waarde)
    {
        var regels = waarde.Split('\n');
        var m = MutatiePatroon().Match(regels[0]);
        if (!m.Success)
        {
            throw new FormatException($"mutatie '{regels[0]}' heeft niet de verwachte vorm.");
        }

        var bedrag = Bedrag(m.Groups[4].Value);
        var negatief = m.Groups[3].Value is "D" or "RC";
        var aanvulling = regels.Length > 1 ? string.Join(' ', regels[1..]).Trim() : null;
        return new Ruwemutatie(Datum(m.Groups[1].Value), negatief ? -bedrag : bedrag, aanvulling);
    }

    private static Bankmutatie Maak(Ruwemutatie ruw)
    {
        var info = (ruw.Informatie ?? string.Empty).Replace("\n", " ", StringComparison.Ordinal).Trim();
        var velden = info.StartsWith('/') ? Gestructureerd(info) : VrijeTekst(info);
        if (velden.Count == 0 && ruw.Informatie is { } regels && TegenpartijOpEersteRegel().Match(regels.Split('\n')[0].Trim()) is { Success: true } eerste)
        {
            // Zonder codes of labels: "NL..IBAN Naam" op de eerste regel, de omschrijving op de volgende.
            Zet(velden, "IBAN", eerste.Groups[1].Value);
            Zet(velden, "NAME", eerste.Groups[2].Value);
            Zet(velden, "REMI", string.Join(' ', regels.Split('\n').Skip(1)));
        }

        velden.TryGetValue("IBAN", out var iban);
        if (iban is null && ruw.Aanvulling is { } aanvulling && IbanPatroon().Match(aanvulling.Replace(" ", string.Empty, StringComparison.Ordinal)) is { Success: true } a)
        {
            iban = a.Value;
        }

        velden.TryGetValue("NAME", out var naam);
        velden.TryGetValue("REMI", out var omschrijving);
        velden.TryGetValue("KENMERK", out var kenmerk);
        velden.TryGetValue("EREF", out var eref);
        if (eref is "NOTPROVIDED" or "")
        {
            eref = null;
        }

        return new Bankmutatie(ruw.Datum, ruw.Bedrag, Leeg(omschrijving) ?? Leeg(info) ?? string.Empty, Leeg(iban), Leeg(naam), Leeg(kenmerk), eref);
    }

    /// <summary>
    /// <c>/EREF/123//CNTP/NL..IBAN/BIC/NAAM/PLAATS////REMI/USTD//Factuur 1/</c> en varianten met
    /// <c>/ORDP//NAME/…</c>, <c>/IBAN/…</c> en <c>/REMI/STRD/CUR/kenmerk/</c>.
    /// </summary>
    private static Dictionary<string, string> Gestructureerd(string info)
    {
        var plekken = new List<(int Positie, string Code)>();
        foreach (Match m in CodePatroon().Matches(info))
        {
            if (Codes.Contains(m.Groups[1].Value))
            {
                plekken.Add((m.Index, m.Groups[1].Value));
            }
        }

        var ruw = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < plekken.Count; i++)
        {
            var start = plekken[i].Positie + plekken[i].Code.Length + 2;
            var einde = i + 1 < plekken.Count ? plekken[i + 1].Positie : info.Length;
            ruw.TryAdd(plekken[i].Code, info[start..Math.Max(start, einde)]);
        }

        var velden = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ruw.TryGetValue("CNTP", out var cntp))
        {
            // CNTP = IBAN/BIC/naam/plaats
            var delen = cntp.Split('/');
            Zet(velden, "IBAN", delen.ElementAtOrDefault(0));
            Zet(velden, "NAME", delen.ElementAtOrDefault(2));
        }

        Zet(velden, "IBAN", Waarde(ruw, "IBAN"));
        Zet(velden, "NAME", Waarde(ruw, "NAME"));
        Zet(velden, "EREF", Waarde(ruw, "EREF"));
        if (ruw.TryGetValue("REMI", out var remi))
        {
            if (remi.StartsWith("USTD//", StringComparison.Ordinal))
            {
                Zet(velden, "REMI", remi[6..].TrimEnd('/'));
            }
            else if (remi.StartsWith("STRD/", StringComparison.Ordinal))
            {
                // STRD/CUR/kenmerk: een gestructureerd betalingskenmerk.
                var kenmerk = remi.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                Zet(velden, "KENMERK", kenmerk);
                Zet(velden, "REMI", kenmerk);
            }
            else
            {
                Zet(velden, "REMI", remi.TrimEnd('/'));
            }
        }

        return velden;
    }

    /// <summary><c>SEPA OVERBOEKING IBAN: NL.. BIC: .. NAAM: .. OMSCHRIJVING: .. KENMERK: ..</c></summary>
    private static Dictionary<string, string> VrijeTekst(string info)
    {
        var velden = new Dictionary<string, string>(StringComparer.Ordinal);
        var labels = LabelPatroon().Matches(info).ToList();
        for (var i = 0; i < labels.Count; i++)
        {
            var start = labels[i].Index + labels[i].Length;
            var einde = i + 1 < labels.Count ? labels[i + 1].Index : info.Length;
            var waarde = info[start..einde];
            switch (labels[i].Groups[1].Value.ToUpperInvariant())
            {
                case "IBAN":
                    Zet(velden, "IBAN", waarde.Replace(" ", string.Empty, StringComparison.Ordinal));
                    break;
                case "NAAM" or "NAME":
                    Zet(velden, "NAME", waarde);
                    break;
                case "OMSCHRIJVING":
                    Zet(velden, "REMI", waarde);
                    break;
                case "BETALINGSKENM." or "BETALINGSKENMERK":
                    Zet(velden, "KENMERK", waarde.Replace(" ", string.Empty, StringComparison.Ordinal));
                    break;
                case "KENMERK":
                    Zet(velden, "EREF", waarde);
                    break;
            }
        }

        return velden;
    }

    private static string? Waarde(Dictionary<string, string> ruw, string code) =>
        ruw.TryGetValue(code, out var waarde) ? waarde.Trim('/', ' ') : null;

    private static void Zet(Dictionary<string, string> velden, string code, string? waarde)
    {
        if (Leeg(waarde) is { } w && !velden.ContainsKey(code))
        {
            velden[code] = w;
        }
    }

    private static string? Leeg(string? tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return null;
        }

        return MeerdereSpaties().Replace(tekst.Trim(), " ");
    }

    private static decimal Bedrag(string tekst) =>
        decimal.Parse(tekst.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static DateOnly Datum(string jjmmdd) =>
        DateOnly.ParseExact(jjmmdd, "yyMMdd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^:([0-9]{2}[A-Z]?):")]
    private static partial Regex Veldbegin();

    [GeneratedRegex(@"^([CD])([0-9]{6})([A-Z]{3})([0-9]+,[0-9]*)")]
    private static partial Regex SaldoPatroon();

    [GeneratedRegex(@"^([0-9]{6})([0-9]{4})?(RC|RD|C|D)[A-Z]?([0-9]+,[0-9]*)")]
    private static partial Regex MutatiePatroon();

    [GeneratedRegex(@"[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}")]
    private static partial Regex IbanPatroon();

    [GeneratedRegex(@"^([A-Z]{2}[0-9]{2}[A-Z0-9]{11,30})\s+(\S.*)$")]
    private static partial Regex TegenpartijOpEersteRegel();

    [GeneratedRegex(@"/([A-Z]{2,11})(?=/)")]
    private static partial Regex CodePatroon();

    [GeneratedRegex(@"\b(IBAN|BIC|NAAM|NAME|OMSCHRIJVING|BETALINGSKENM\.|BETALINGSKENMERK|KENMERK|MACHTIGING|INCASSANT):\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPatroon();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MeerdereSpaties();

    private sealed class Opbouw
    {
        public string? Iban { get; set; }

        public string? Nummer { get; set; }

        public string? Valuta { get; set; }

        public DateOnly? Begindatum { get; set; }

        public decimal? Beginsaldo { get; set; }

        public DateOnly? Einddatum { get; set; }

        public decimal? Eindsaldo { get; set; }

        public List<Ruwemutatie> Mutaties { get; } = [];
    }

    private sealed class Ruwemutatie(DateOnly datum, decimal bedrag, string? aanvulling)
    {
        public DateOnly Datum { get; } = datum;

        public decimal Bedrag { get; } = bedrag;

        public string? Aanvulling { get; } = aanvulling;

        public string? Informatie { get; set; }
    }
}
