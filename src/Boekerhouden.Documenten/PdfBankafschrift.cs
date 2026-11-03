using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Boekhouding.Bank;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Boekhouding.Verkoop;

namespace Boekerhouden.Documenten;

/// <summary>
/// Leest een rekeningafschrift dat alleen als PDF bestaat (zoals de Rabobank het bij een zakelijke rekening levert),
/// zodat het als MT940 ingelezen kan worden. Het werkt op de plek van de woorden: de kop met IBAN, vorig en nieuw saldo
/// en de datums, en per mutatie de datum en code links, de tegenrekening, naam en omschrijving in het midden en het
/// bedrag in de kolom "Mutaties af (debet)" of "Mutaties bij (credit)". Een mutatie loopt door tot de volgende datum.
/// Het resultaat wordt gecontroleerd: vorig saldo plus de mutaties moet het nieuwe saldo zijn.
/// </summary>
public static partial class PdfBankafschrift
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Het afschrift, of een <see cref="BankbestandException"/> met de reden als het niet te lezen is.</summary>
    public static Bankafschrift Lees(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var woorden = PdfTekst.Woorden(pdf);
        if (woorden.Count == 0)
        {
            throw new BankbestandException("De PDF heeft geen tekst (een scan?); alleen een afschrift met tekst is om te zetten.");
        }

        var regels = Regels(woorden);
        var alleTekst = string.Join('\n', regels.Select(r => r.Tekst));
        var iban = Kopiban(alleTekst) ?? throw new BankbestandException("Geen IBAN van de rekening gevonden op het afschrift.");
        var (vorigeDatum, vorigSaldo) = Kopsaldo(regels, "Vorig afschrift", "Vorig saldo")
            ?? throw new BankbestandException("Geen vorig saldo gevonden op het afschrift.");
        var (nieuweDatum, nieuwSaldo) = Kopsaldo(regels, "Nieuw afschrift", "Nieuw saldo")
            ?? throw new BankbestandException("Geen nieuw saldo gevonden op het afschrift.");
        var volgnummer = Kopwaarde(regels, "Volgnummer");

        var mutaties = new List<Ruw>();
        foreach (var pagina in regels.GroupBy(r => r.Pagina))
        {
            Lees(pagina.ToList(), mutaties);
        }

        if (mutaties.Count == 0)
        {
            throw new BankbestandException("Geen mutaties gevonden; is dit een rekeningafschrift met kolommen voor debet en credit?");
        }

        var afschrift = new Bankafschrift(iban, volgnummer, "EUR", vorigeDatum, vorigSaldo, nieuweDatum, nieuwSaldo,
            [.. mutaties.Select(m => Maak(m, nieuweDatum))]);
        if (!afschrift.SluitAan)
        {
            throw new BankbestandException(
                $"Het afschrift sluit niet: vorig saldo {vorigSaldo:N2} plus de {mutaties.Count} mutaties ({afschrift.Mutaties.Sum(m => m.Bedrag):N2}) is niet het nieuwe saldo {nieuwSaldo:N2}; mogelijk is een mutatie niet goed gelezen.");
        }

        return afschrift;
    }

    /// <summary>Een regel tekst met zijn woorden (gesorteerd van links naar rechts).</summary>
    private sealed record Regel(int Pagina, double Y, IReadOnlyList<Tekstwoord> Woorden)
    {
        public string Tekst => string.Join(' ', Woorden.Select(w => w.Tekst));
    }

    /// <summary>Een mutatie zoals gelezen: datum (dag en maand), bedrag met teken, en de tekstregels.</summary>
    private sealed class Ruw(int dag, int maand, decimal bedrag)
    {
        public int Dag { get; } = dag;

        public int Maand { get; } = maand;

        public decimal Bedrag { get; } = bedrag;

        public List<string> Tekst { get; } = [];
    }

    private static List<Regel> Regels(IReadOnlyList<Tekstwoord> woorden)
    {
        var regels = new List<Regel>();
        foreach (var pagina in woorden.GroupBy(w => w.Pagina).OrderBy(g => g.Key))
        {
            foreach (var woord in pagina.OrderByDescending(w => w.Y1).ThenBy(w => w.X0))
            {
                var midden = (woord.Y0 + woord.Y1) / 2;
                var hoogte = Math.Max(4, woord.Y1 - woord.Y0);
                var regel = regels.LastOrDefault(r => r.Pagina == woord.Pagina && Math.Abs(r.Y - midden) < hoogte * 0.5);
                if (regel is null)
                {
                    regels.Add(new Regel(woord.Pagina, midden, [woord]));
                }
                else
                {
                    regels[regels.IndexOf(regel)] = regel with { Woorden = [.. regel.Woorden.Append(woord).OrderBy(w => w.X0)] };
                }
            }
        }

        return [.. regels.OrderBy(r => r.Pagina).ThenByDescending(r => r.Y)];
    }

    /// <summary>De mutaties van één pagina: tussen de kolomkoppen en het eerste stuk tekst dat links begint zonder datum.</summary>
    private static void Lees(List<Regel> regels, List<Ruw> mutaties)
    {
        var kop = regels.FindIndex(r => r.Tekst.Contains("Mutaties af", StringComparison.OrdinalIgnoreCase) && r.Tekst.Contains("Mutaties bij", StringComparison.OrdinalIgnoreCase));
        if (kop < 0)
        {
            return;
        }

        var kopwoorden = regels[kop].Woorden;
        var debetX = kopwoorden.First(w => w.Tekst.Equals("Mutaties", StringComparison.OrdinalIgnoreCase)).X0;
        var creditX = kopwoorden.Last(w => w.Tekst.Equals("Mutaties", StringComparison.OrdinalIgnoreCase)).X0;
        var omschrijvingX = kopwoorden.FirstOrDefault(w => w.Tekst.StartsWith("Tegenrekening", StringComparison.OrdinalIgnoreCase))?.X0 ?? kopwoorden[0].X1;
        var linksX = kopwoorden[0].X0;
        Ruw? huidig = null;
        foreach (var regel in regels.Skip(kop + 1))
        {
            var eerste = regel.Woorden[0];
            var datum = Datumpatroon().Match(eerste.Tekst);
            if (datum.Success && eerste.X0 < omschrijvingX - 2)
            {
                // Een nieuwe mutatie: datum, code, tekst, bedrag in een van de twee bedragkolommen.
                var bedragwoord = regel.Woorden.LastOrDefault(w => w.X0 >= debetX - 5 && Bedragpatroon().IsMatch(w.Tekst));
                if (bedragwoord is null)
                {
                    continue;
                }

                var bedrag = decimal.Parse(bedragwoord.Tekst, NumberStyles.Number, Nl);
                huidig = new Ruw(int.Parse(datum.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(datum.Groups[2].Value, CultureInfo.InvariantCulture),
                    bedragwoord.X0 >= creditX - 5 ? bedrag : -bedrag);
                huidig.Tekst.Add(Tekst(regel.Woorden.Where(w => w.X0 >= omschrijvingX - 2 && w.X1 <= debetX)));
                mutaties.Add(huidig);
            }
            else if (eerste.X0 >= omschrijvingX - 2 && huidig is not null && regel.Woorden.All(w => w.X1 <= debetX))
            {
                huidig.Tekst.Add(Tekst(regel.Woorden));
            }
            else if (huidig is not null && (eerste.X0 < omschrijvingX - 2 || eerste.X0 <= linksX + 1))
            {
                // Tekst die links begint en geen datum is (uitleg, voetnoot): einde van de mutaties op deze pagina.
                break;
            }
        }
    }

    /// <summary>
    /// Van de tekst van een mutatie: de tegenrekening (IBAN in groepen van vier), de naam (achter het IBAN, of op de
    /// regel eronder), de omschrijving, het betalingskenmerk en de transactiereferentie.
    /// </summary>
    private static Bankmutatie Maak(Ruw ruw, DateOnly einde)
    {
        var jaar = ruw.Maand > einde.Month ? einde.Year - 1 : einde.Year;
        var datum = new DateOnly(jaar, ruw.Maand, Math.Min(ruw.Dag, DateTime.DaysInMonth(jaar, ruw.Maand)));
        var regels = ruw.Tekst.Where(t => t.Length > 0).ToList();
        string? iban = null;
        string? naam = null;
        if (regels.Count > 0 && Tegenrekening(regels[0]) is { } gevonden)
        {
            iban = gevonden.Iban;
            var rest = regels[0][gevonden.Lengte..].Trim();
            regels.RemoveAt(0);
            if (rest.Length > 0)
            {
                naam = rest;
            }
            else if (regels.Count > 0)
            {
                naam = regels[0];
                regels.RemoveAt(0);
            }
        }

        string? eref = null;
        var referentie = regels.FindIndex(r => r.StartsWith("Transactiereferentie:", StringComparison.OrdinalIgnoreCase));
        if (referentie >= 0)
        {
            var waarde = regels[referentie]["Transactiereferentie:".Length..].Trim();
            if (waarde.Length == 0 && referentie + 1 < regels.Count)
            {
                waarde = regels[referentie + 1];
                regels.RemoveAt(referentie + 1);
            }

            eref = waarde.Length > 0 ? waarde : null;
            regels.RemoveAt(referentie);
        }

        var omschrijving = string.Join(' ', regels);
        var kenmerk = Kenmerkpatroon().Matches(omschrijving).Select(m => m.Value.Replace(" ", string.Empty, StringComparison.Ordinal)).FirstOrDefault(Betalingskenmerk.IsGeldig);
        return new Bankmutatie(datum, ruw.Bedrag, omschrijving.Length > 0 ? omschrijving : naam ?? string.Empty, iban, naam, kenmerk, eref);
    }

    /// <summary>Een IBAN aan het begin van de regel, ook in groepen van vier geschreven ("NL04 ABNA 0484 1745 33").</summary>
    private static (string Iban, int Lengte)? Tegenrekening(string regel)
    {
        if (!IbanBegin().IsMatch(regel))
        {
            return null;
        }

        // Neem groepen zolang het samen een geldig IBAN kan worden; het langste geldige wint.
        var delen = regel.Split(' ');
        (string, int)? beste = null;
        var samen = string.Empty;
        var lengte = 0;
        foreach (var deel in delen)
        {
            if (samen.Length > 0 && !deel.All(char.IsAsciiLetterOrDigit))
            {
                break;
            }

            samen += deel;
            lengte += deel.Length + (lengte > 0 ? 1 : 0);
            if (samen.Length > 34)
            {
                break;
            }

            if (Iban.IsGeldig(samen))
            {
                beste = (samen.ToUpperInvariant(), lengte);
            }
        }

        return beste;
    }

    private static string? Kopiban(string tekst) =>
        Ibanpatroon().Matches(tekst).Select(m => m.Value.Replace(" ", string.Empty, StringComparison.Ordinal)).FirstOrDefault(Iban.IsGeldig);

    /// <summary>De datum en het saldo onder een kop als "Vorig afschrift … Vorig saldo": de regel eronder, met CR of D.</summary>
    private static (DateOnly Datum, decimal Saldo)? Kopsaldo(List<Regel> regels, string datumlabel, string saldolabel)
    {
        var i = regels.FindIndex(r => r.Tekst.Contains(datumlabel, StringComparison.OrdinalIgnoreCase) && r.Tekst.Contains(saldolabel, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= regels.Count)
        {
            return null;
        }

        var m = Saldopatroon().Match(regels[i + 1].Tekst);
        if (!m.Success || !DateOnly.TryParseExact(m.Groups[1].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum))
        {
            return null;
        }

        var saldo = decimal.Parse(m.Groups[2].Value, NumberStyles.Number, Nl);
        return (datum, m.Groups[3].Value.StartsWith('D') ? -saldo : saldo);
    }

    /// <summary>Een waarde onder een kopwoord (bijv. het volgnummer), op dezelfde hoogte als de datum van het afschrift.</summary>
    private static string? Kopwaarde(List<Regel> regels, string label)
    {
        var i = regels.FindIndex(r => r.Woorden.Any(w => w.Tekst.Equals(label, StringComparison.OrdinalIgnoreCase)));
        if (i < 0 || i + 1 >= regels.Count)
        {
            return null;
        }

        var kop = regels[i].Woorden.First(w => w.Tekst.Equals(label, StringComparison.OrdinalIgnoreCase));
        return regels[i + 1].Woorden.FirstOrDefault(w => Math.Abs(w.X0 - kop.X0) < 15)?.Tekst;
    }

    private static string Tekst(IEnumerable<Tekstwoord> woorden) => string.Join(' ', woorden.Select(w => w.Tekst)).Trim();

    [GeneratedRegex(@"^([0-3][0-9])-([01][0-9])$")]
    private static partial Regex Datumpatroon();

    [GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{3})*,[0-9]{2}$")]
    private static partial Regex Bedragpatroon();

    [GeneratedRegex(@"([0-3][0-9]-[01][0-9]-[0-9]{4})\s+([0-9.]+,[0-9]{2})\s*(CR|D|DR)?")]
    private static partial Regex Saldopatroon();

    [GeneratedRegex(@"^[A-Z]{2}[0-9]{2}\s?[A-Z0-9]{4}")]
    private static partial Regex IbanBegin();

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}(?:\s?[A-Z0-9]{4}){2,7}(?:\s?[A-Z0-9]{1,3})?\b")]
    private static partial Regex Ibanpatroon();

    [GeneratedRegex(@"\b[0-9]{4}\s?[0-9]{4}\s?[0-9]{4}\s?[0-9]{4}\b")]
    private static partial Regex Kenmerkpatroon();
}
