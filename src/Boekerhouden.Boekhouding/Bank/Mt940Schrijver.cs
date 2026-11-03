using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Boekerhouden.Boekhouding.Bank;

/// <summary>
/// Schrijft een bankafschrift als MT940 (SWIFT), met de omschrijving gestructureerd zoals de Nederlandse banken dat
/// doen: <c>/EREF/…//CNTP/IBAN/BIC/naam/plaats////REMI/USTD//omschrijving/</c>. Zo leest elk pakket (en
/// <see cref="Mt940Lezer"/>) hetzelfde terug. Gebruikt om een afschrift dat alleen als PDF bestaat om te zetten.
/// </summary>
public static partial class Mt940Schrijver
{
    private const int Regellengte = 65;

    public static string Schrijf(Bankafschrift afschrift, string? referentie = null)
    {
        ArgumentNullException.ThrowIfNull(afschrift);
        var mt = new StringBuilder();
        void Regel(string tekst) => mt.Append(tekst).Append("\r\n");

        var begin = afschrift.Begindatum ?? afschrift.Mutaties.Select(m => m.Datum).DefaultIfEmpty(afschrift.Einddatum ?? DateOnly.MinValue).Min();
        var einde = afschrift.Einddatum ?? afschrift.Mutaties.Select(m => m.Datum).DefaultIfEmpty(begin).Max();
        Regel(":20:" + Kort(referentie ?? $"PDF{einde:yyMMdd}", 16));
        Regel($":25:{afschrift.Iban} {afschrift.Valuta}");
        Regel(":28C:" + Kort(afschrift.Nummer is { Length: > 0 } nummer ? nummer : "00000", 5));
        Regel($":60F:{Saldo(afschrift.Beginsaldo, begin, afschrift.Valuta)}");
        foreach (var m in afschrift.Mutaties)
        {
            Regel($":61:{m.Datum:yyMMdd}{m.Datum:MMdd}{(m.Bedrag < 0 ? "D" : "C")}{Bedrag(Math.Abs(m.Bedrag))}NTRFNONREF");
            var info = new StringBuilder();
            if (m.EndToEndId is { Length: > 0 } eref)
            {
                info.Append("/EREF/").Append(Veilig(eref)).Append('/');
            }

            if (m.TegenIban is { Length: > 0 } || m.TegenNaam is { Length: > 0 })
            {
                info.Append("/CNTP/").Append(m.TegenIban).Append("//").Append(Veilig(m.TegenNaam ?? string.Empty)).Append("////");
            }

            if (m.Betalingskenmerk is { Length: > 0 } kenmerk && m.Omschrijving.Trim() == kenmerk)
            {
                info.Append("/REMI/STRD/CUR/").Append(kenmerk).Append('/');
            }
            else
            {
                info.Append("/REMI/USTD//").Append(Omschrijving(m.Omschrijving)).Append('/');
            }

            var tekst = info.ToString();
            Regel(":86:" + tekst[..Math.Min(tekst.Length, Regellengte - 4)]);
            for (var i = Regellengte - 4; i < tekst.Length; i += Regellengte)
            {
                Regel(tekst.Substring(i, Math.Min(Regellengte, tekst.Length - i)));
            }
        }

        Regel($":62F:{Saldo(afschrift.Eindsaldo, einde, afschrift.Valuta)}");
        Regel("-");
        return mt.ToString();
    }

    private static string Saldo(decimal saldo, DateOnly datum, string valuta) =>
        $"{(saldo < 0 ? "D" : "C")}{datum:yyMMdd}{valuta}{Bedrag(Math.Abs(saldo))}";

    private static string Bedrag(decimal bedrag) => bedrag.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>
    /// Tekst zonder schuine strepen (die scheiden de velden van <c>:86:</c>) en regeleinden, en zonder tekens die in
    /// een SWIFT-bericht niet horen.
    /// </summary>
    private static string Veilig(string tekst) =>
        string.Join(' ', new string([.. tekst.Select(c => c is '/' ? '-' : char.IsControl(c) ? ' ' : c)])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// De omschrijving mag schuine strepen houden ("1323/ 2026"), behalve waar die op een veldcode lijken
    /// (<c>/EREF/</c>) of de omschrijving afsluiten.
    /// </summary>
    private static string Omschrijving(string tekst) =>
        Veldcode().Replace(string.Join(' ', new string([.. tekst.Select(c => char.IsControl(c) ? ' ' : c)]).Split(' ', StringSplitOptions.RemoveEmptyEntries)), "-").TrimEnd('/');

    [GeneratedRegex("/(?=[A-Z]{2,11}/)")]
    private static partial Regex Veldcode();

    private static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] : tekst;
}
