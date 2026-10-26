using System.Text;

namespace Boekerhouden.Boekhouding.Bank;

/// <summary>Leest een bankbestand en herkent zelf of het MT940 of CAMT.053 is.</summary>
public static class Bankbestand
{
    public static IReadOnlyList<Bankafschrift> Lees(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var kopie = new MemoryStream();
        stream.CopyTo(kopie);
        kopie.Position = 0;
        var begin = Encoding.UTF8.GetString(kopie.GetBuffer(), 0, (int)Math.Min(kopie.Length, 512)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (begin.StartsWith('<'))
        {
            return Camt053Lezer.Lees(kopie);
        }

        // MT940 is meestal Latin-1 of UTF-8; bij ongeldige UTF-8 terugvallen op Latin-1.
        var bytes = kopie.ToArray();
        string tekst;
        try
        {
            tekst = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            tekst = Encoding.Latin1.GetString(bytes);
        }

        using var lezer = new StringReader(tekst.TrimStart('﻿'));
        return Mt940Lezer.Lees(lezer);
    }
}
