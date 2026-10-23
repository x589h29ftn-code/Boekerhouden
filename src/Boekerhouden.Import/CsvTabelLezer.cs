using System.Text;

namespace Boekerhouden.Import;

/// <summary>
/// Leest een CSV-bestand in de opbouw van het importsjabloon. Herkent zelf de tekencodering
/// (UTF-8, UTF-16 of Windows-1252) en het scheidingsteken (puntkomma, tab of komma).
/// </summary>
internal static class CsvTabelLezer
{
    private static readonly char[] Scheidingstekens = [';', '\t', ','];
    private static readonly Encoding StrikteUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1252 = MaakWindows1252();

    public static IReadOnlyList<Rij> Lees(Stream stream)
    {
        var tekst = LeesTekst(stream);
        return SplitsInRijen(tekst, BepaalScheidingsteken(tekst));
    }

    internal static string LeesTekst(Stream stream)
    {
        using var geheugen = new MemoryStream();
        stream.CopyTo(geheugen);
        var bytes = geheugen.ToArray();

        return bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3),
            [0xFF, 0xFE, ..] => Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
            [0xFE, 0xFF, ..] => Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2),
            _ => LeesZonderBom(bytes),
        };
    }

    private static string LeesZonderBom(byte[] bytes)
    {
        try
        {
            return StrikteUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Geen geldige UTF-8: oudere Excel-exports zijn meestal Windows-1252.
            return Windows1252.GetString(bytes);
        }
    }

    /// <summary>Kiest het scheidingsteken dat het vaakst voorkomt in de kopregel (met "Grootboeknummer").</summary>
    internal static char BepaalScheidingsteken(string tekst)
    {
        var kopregel = tekst
            .Split('\n')
            .FirstOrDefault(r => r.Contains(ImportSjabloon.KolomGrootboeknummer, StringComparison.OrdinalIgnoreCase));
        if (kopregel is null)
        {
            return ';';
        }

        var beste = Scheidingstekens
            .Select(teken => (teken, aantal: kopregel.Count(c => c == teken)))
            .OrderByDescending(x => x.aantal)
            .First();
        return beste.aantal >= 2 ? beste.teken : ';';
    }

    /// <summary>Splitst de tekst in rijen en cellen. Velden tussen aanhalingstekens mogen het scheidingsteken bevatten.</summary>
    internal static IReadOnlyList<Rij> SplitsInRijen(string tekst, char scheidingsteken)
    {
        var rijen = new List<Rij>();
        var cellen = new List<Cel>();
        var veld = new StringBuilder();
        var tussenAanhalingstekens = false;
        var rijnummer = 1;

        void SluitVeld()
        {
            cellen.Add(new Cel(veld.ToString().Trim()));
            veld.Clear();
        }

        void SluitRij()
        {
            SluitVeld();
            rijen.Add(new Rij(rijnummer++, cellen));
            cellen = [];
        }

        for (var i = 0; i < tekst.Length; i++)
        {
            var teken = tekst[i];
            if (tussenAanhalingstekens)
            {
                if (teken != '"')
                {
                    veld.Append(teken);
                }
                else if (i + 1 < tekst.Length && tekst[i + 1] == '"')
                {
                    veld.Append('"');
                    i++;
                }
                else
                {
                    tussenAanhalingstekens = false;
                }
            }
            else if (teken == '"' && string.IsNullOrWhiteSpace(veld.ToString()))
            {
                veld.Clear();
                tussenAanhalingstekens = true;
            }
            else if (teken == scheidingsteken)
            {
                SluitVeld();
            }
            else if (teken == '\n' || (teken == '\r' && (i + 1 == tekst.Length || tekst[i + 1] != '\n')))
            {
                SluitRij();
            }
            else if (teken != '\r')
            {
                veld.Append(teken);
            }
        }

        if (veld.Length > 0 || cellen.Count > 0)
        {
            SluitRij();
        }

        return rijen;
    }

    private static Encoding MaakWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
