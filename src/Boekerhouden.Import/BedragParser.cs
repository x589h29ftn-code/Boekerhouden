using System.Globalization;

namespace Boekerhouden.Import;

/// <summary>
/// Leest bedragen uit tekst, in Nederlandse (<c>1.234,56</c>) en Engelse (<c>1,234.56</c>) notatie.
/// </summary>
/// <remarks>
/// Regels:
/// <list type="bullet">
/// <item>Komen punt en komma allebei voor, dan is het laatste teken het decimaalteken.</item>
/// <item>Alleen een komma: één komma is het decimaalteken; meerdere komma's zijn duizendtallen.</item>
/// <item>Alleen een punt: meerdere punten of precies drie cijfers na één punt (zoals <c>1.234</c>) zijn
/// duizendtallen; anders is de punt het decimaalteken (<c>1234.5</c>).</item>
/// <item>Negatief met een minteken vooraan of achteraan, of tussen haakjes. Euroteken en spaties worden genegeerd.</item>
/// <item>Duizendtalgroepen moeten uit precies drie cijfers bestaan; <c>12.34.567</c> is ongeldig.</item>
/// </list>
/// Het aantal decimalen wordt hier niet beperkt; dat controleert de aanroeper.
/// </remarks>
internal static class BedragParser
{
    public static bool TryParse(string invoer, out decimal bedrag)
    {
        bedrag = 0m;
        var tekst = new string(invoer.Where(c => !char.IsWhiteSpace(c) && c != '€').ToArray()).Replace('−', '-');
        if (tekst.Length == 0)
        {
            return false;
        }

        var negatief = false;
        if (tekst.StartsWith('(') && tekst.EndsWith(')'))
        {
            negatief = true;
            tekst = tekst[1..^1];
        }
        else if (tekst.StartsWith('-') || tekst.EndsWith('-'))
        {
            negatief = true;
            tekst = tekst.StartsWith('-') ? tekst[1..] : tekst[..^1];
        }
        else if (tekst.StartsWith('+'))
        {
            tekst = tekst[1..];
        }

        if (tekst.Length == 0 || !tekst.All(c => char.IsAsciiDigit(c) || c == '.' || c == ','))
        {
            return false;
        }

        if (!Normaliseer(tekst, out var genormaliseerd)
            || !decimal.TryParse(genormaliseerd, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var waarde))
        {
            return false;
        }

        bedrag = negatief ? -waarde : waarde;
        return true;
    }

    /// <summary>Zet de tekst om naar cijfers met hoogstens één punt als decimaalteken.</summary>
    private static bool Normaliseer(string tekst, out string resultaat)
    {
        var punten = tekst.Count(c => c == '.');
        var kommas = tekst.Count(c => c == ',');

        char? decimaalteken = null;
        char? duizendtalteken = null;
        if (punten > 0 && kommas > 0)
        {
            decimaalteken = tekst.LastIndexOf('.') > tekst.LastIndexOf(',') ? '.' : ',';
            duizendtalteken = decimaalteken == '.' ? ',' : '.';
        }
        else if (kommas == 1)
        {
            decimaalteken = ',';
        }
        else if (kommas > 1)
        {
            duizendtalteken = ',';
        }
        else if (punten > 0)
        {
            var naPunt = tekst.Length - tekst.IndexOf('.') - 1;
            var duizendtallen = punten > 1 || (naPunt == 3 && !tekst.StartsWith('0') && !tekst.StartsWith('.'));
            if (duizendtallen)
            {
                duizendtalteken = '.';
            }
            else
            {
                decimaalteken = '.';
            }
        }

        resultaat = string.Empty;
        string geheel = tekst, fractie = string.Empty;
        if (decimaalteken is { } d)
        {
            var positie = tekst.LastIndexOf(d);
            if (tekst.Count(c => c == d) > 1)
            {
                return false;
            }

            geheel = tekst[..positie];
            fractie = tekst[(positie + 1)..];
            if (fractie.Length == 0 || !fractie.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        if (duizendtalteken is { } t)
        {
            var groepen = geheel.Split(t);
            if (groepen[0].Length is < 1 or > 3 || groepen.Skip(1).Any(g => g.Length != 3))
            {
                return false;
            }

            geheel = string.Concat(groepen);
        }

        if (geheel.Length == 0)
        {
            geheel = "0";
        }

        if (!geheel.All(char.IsAsciiDigit))
        {
            return false;
        }

        resultaat = fractie.Length > 0 ? $"{geheel}.{fractie}" : geheel;
        return true;
    }
}
