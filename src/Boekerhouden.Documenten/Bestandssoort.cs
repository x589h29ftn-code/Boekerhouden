namespace Boekerhouden.Documenten;

/// <summary>
/// Wat voor bestand het is, op de inhoud bepaald en niet op de naam. Alleen een PDF of XML mag in een ander programma
/// worden geopend, zodat een geüpload programma of script nooit wordt gestart.
/// </summary>
public static class Bestandssoort
{
    /// <summary><c>.pdf</c>, <c>.xml</c> of leeg (iets anders).</summary>
    public static string? Extensie(ReadOnlySpan<byte> inhoud)
    {
        var begin = inhoud[..Math.Min(inhoud.Length, 1024)];
        if (begin.IndexOf("%PDF-"u8) >= 0)
        {
            return ".pdf";
        }

        var tekst = System.Text.Encoding.UTF8.GetString(begin).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return tekst.StartsWith("<?xml", StringComparison.Ordinal) || (tekst.StartsWith('<') && !tekst.StartsWith("<!", StringComparison.Ordinal) && !tekst.Contains("<script", StringComparison.OrdinalIgnoreCase) && !tekst.Contains("<html", StringComparison.OrdinalIgnoreCase))
            ? ".xml"
            : null;
    }
}
