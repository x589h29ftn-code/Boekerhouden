using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Domain.Rgs;
using ClosedXML.Excel;

namespace Boekerhouden.Import;

/// <summary>Uitkomst van het inlezen van een RGS-werkboek. Zonder fouten bevat het de codes van het hoofdblad.</summary>
public sealed record RgsWerkboek(
    string? Versie,
    string Werkblad,
    IReadOnlyList<RgsCode> Codes,
    IReadOnlyList<string> Kiesfilters,
    IReadOnlyList<string> Wegfilters,
    IReadOnlyList<ImportMelding> Meldingen)
{
    public bool IsGeslaagd => Codes.Count > 0 && Meldingen.All(m => m.Ernst != MeldingErnst.Fout);
}

/// <summary>
/// Leest het officiële RGS-werkboek (bijv. <c>RGS 3.8-def.xlsx</c>, Kennisbank van referentiegrootboekschema.nl).
/// Gebruikt het hoofdblad (<c>Totaal-…</c>): de kolomkoppen staan in de rij die met "Referentiecode" begint.
/// De kiesfilters staan tussen <c>Nivo</c> en <c>Inactief</c>, de wegfilters erna; sommige namen (EZ/VOF, WoCo)
/// komen in beide groepen voor, dus de positie bepaalt de betekenis.
/// </summary>
public static partial class RgsWerkboekLezer
{
    public static RgsWerkboek Lees(Stream stream, string bestandsnaam)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(bestandsnaam);
        try
        {
            using var werkboek = new XLWorkbook(stream);
            return Lees(werkboek, bestandsnaam);
        }
        catch (Exception fout) when (fout is InvalidDataException or IOException or FormatException or InvalidOperationException or ArgumentException)
        {
            return Mislukt($"Het bestand kan niet worden gelezen als Excel-werkboek ({fout.Message}).");
        }
    }

    private static RgsWerkboek Lees(XLWorkbook werkboek, string bestandsnaam)
    {
        var blad = werkboek.Worksheets.FirstOrDefault(b => b.Name.StartsWith("Totaal", StringComparison.OrdinalIgnoreCase) && KopRij(b) is not null)
                   ?? werkboek.Worksheets.FirstOrDefault(b => KopRij(b) is not null);
        if (blad is null)
        {
            return Mislukt("Geen werkblad met de kolom 'Referentiecode' gevonden. Is dit het RGS-werkboek?");
        }

        var kopRij = KopRij(blad)!.Value;
        var koppen = new List<string>();
        for (var kolom = 1; kolom <= blad.LastColumnUsed()!.ColumnNumber(); kolom++)
        {
            koppen.Add(blad.Cell(kopRij, kolom).GetString().Trim());
        }

        int Kolom(string naam) => koppen.FindIndex(k => string.Equals(k, naam, StringComparison.OrdinalIgnoreCase)) + 1;
        var kCode = Kolom("Referentiecode");
        var kOmslag = Kolom("ReferentieOmslagcode");
        var kNummer = Kolom("Referentienummer");
        var kKort = Kolom("Omschrijving (verkort)");
        var kLang = Kolom("Omschrijving");
        var kDc = Kolom("D/C");
        var kNivo = Kolom("Nivo");
        var kInactief = Kolom("Inactief");

        var meldingen = new List<ImportMelding>();
        if (kKort == 0 && kLang == 0)
        {
            return Mislukt("De kolom 'Omschrijving' ontbreekt in het RGS-werkboek.");
        }

        var kiesKolommen = new List<(int Kolom, string Naam)>();
        var wegKolommen = new List<(int Kolom, string Naam)>();
        if (kNivo > 0 && kInactief > kNivo)
        {
            for (var k = kNivo + 1; k < kInactief; k++)
            {
                if (koppen[k - 1].Length > 0)
                {
                    kiesKolommen.Add((k, koppen[k - 1]));
                }
            }

            for (var k = kInactief + 1; k <= koppen.Count; k++)
            {
                if (koppen[k - 1].Length > 0)
                {
                    wegKolommen.Add((k, koppen[k - 1]));
                }
            }
        }
        else
        {
            meldingen.Add(new ImportMelding("RGS", MeldingErnst.Waarschuwing,
                "Geen filterkolommen gevonden (verwacht tussen 'Nivo' en 'Inactief'); alle codes doen mee."));
        }

        var codes = new List<RgsCode>();
        var gezien = new Dictionary<string, RgsCode>(StringComparer.Ordinal);
        var gezienZonderHoofdletters = new Dictionary<string, RgsCode>(StringComparer.OrdinalIgnoreCase);
        var laatstePerNiveau = new Dictionary<int, string>();
        var volgorde = 0;
        foreach (var rij in blad.RowsUsed().Where(r => r.RowNumber() > kopRij))
        {
            var code = rij.Cell(kCode).GetString().Trim();
            if (code.Length == 0)
            {
                continue;
            }

            if (!CodePatroon().IsMatch(code))
            {
                meldingen.Add(new ImportMelding("RGS", MeldingErnst.Waarschuwing, $"'{code}' is geen RGS-code; overgeslagen.", rij.RowNumber()));
                continue;
            }

            if (gezien.ContainsKey(code))
            {
                meldingen.Add(new ImportMelding("RGS", MeldingErnst.Waarschuwing,
                    $"Code {code} komt meer dan eens voor; de eerste wordt gebruikt.", rij.RowNumber()));
                continue;
            }

            var niveau = kNivo > 0 && int.TryParse(rij.Cell(kNivo).GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : 1 + ((code.Length - 1) / 3);
            var ouder = niveau <= 1 ? null : BepaalOuder(code, niveau, gezienZonderHoofdletters, laatstePerNiveau);
            if (niveau > 1 && ouder is null)
            {
                meldingen.Add(new ImportMelding("RGS", MeldingErnst.Waarschuwing, $"Geen bovenliggende code gevonden voor {code}.", rij.RowNumber()));
            }

            var kort = Tekst(rij, kKort);
            var lang = Tekst(rij, kLang);
            var rgs = new RgsCode(
                code,
                niveau,
                ouder,
                kort ?? lang ?? code,
                lang ?? kort ?? code,
                Tekst(rij, kDc)?.ToUpperInvariant() switch { "D" => Saldokant.Debet, "C" => Saldokant.Credit, _ => (Saldokant?)null },
                Tekst(rij, kOmslag),
                ++volgorde,
                Tekst(rij, kNummer),
                kInactief > 0 && IsGezet(rij.Cell(kInactief)),
                kiesKolommen.Where(k => IsGezet(rij.Cell(k.Kolom))).Select(k => k.Naam).ToHashSet(StringComparer.Ordinal),
                wegKolommen.Where(k => IsGezet(rij.Cell(k.Kolom))).Select(k => k.Naam).ToHashSet(StringComparer.Ordinal));
            codes.Add(rgs);
            gezien[code] = rgs;
            gezienZonderHoofdletters.TryAdd(code, rgs);
            laatstePerNiveau[niveau] = code;
        }

        foreach (var c in codes.Where(c => c.Omslagcode is not null && !gezien.ContainsKey(c.Omslagcode)))
        {
            meldingen.Add(new ImportMelding("RGS", MeldingErnst.Waarschuwing, $"Omslagcode {c.Omslagcode} bij {c.Code} bestaat niet in het werkboek."));
        }

        if (codes.Count == 0)
        {
            meldingen.Add(new ImportMelding("RGS", MeldingErnst.Fout, "Het werkboek bevat geen RGS-codes."));
        }

        return new RgsWerkboek(
            BepaalVersie(blad, kopRij, bestandsnaam),
            blad.Name,
            codes,
            [.. kiesKolommen.Select(k => k.Naam)],
            [.. wegKolommen.Select(k => k.Naam)],
            meldingen);
    }

    /// <summary>
    /// De ouder is de code één niveau hoger waarvan de code het voorvoegsel is (hoofdletterongevoelig, want het
    /// werkboek kent enkele onregelmatige codes). Anders: de laatst gelezen code één niveau hoger.
    /// </summary>
    private static string? BepaalOuder(string code, int niveau, Dictionary<string, RgsCode> gezien, Dictionary<int, string> laatstePerNiveau)
    {
        for (var lengte = code.Length - 1; lengte >= 1; lengte--)
        {
            if (gezien.TryGetValue(code[..lengte], out var kandidaat) && kandidaat.Niveau == niveau - 1)
            {
                return kandidaat.Code;
            }
        }

        return laatstePerNiveau.GetValueOrDefault(niveau - 1);
    }

    private static int? KopRij(IXLWorksheet blad)
    {
        for (var rij = 1; rij <= 10; rij++)
        {
            if (string.Equals(blad.Cell(rij, 1).GetString().Trim(), "Referentiecode", StringComparison.OrdinalIgnoreCase))
            {
                return rij;
            }
        }

        return null;
    }

    private static string? BepaalVersie(IXLWorksheet blad, int kopRij, string bestandsnaam)
    {
        var bronnen = new List<string> { blad.Name, bestandsnaam };
        for (var rij = 1; rij < kopRij; rij++)
        {
            bronnen.Add(blad.Cell(rij, 1).GetString());
        }

        return bronnen.Select(b => VersiePatroon().Match(b)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
    }

    private static string? Tekst(IXLRow rij, int kolom)
    {
        if (kolom <= 0)
        {
            return null;
        }

        var tekst = rij.Cell(kolom).GetString().Replace(' ', ' ').Trim();
        return tekst.Length == 0 ? null : tekst;
    }

    private static bool IsGezet(IXLCell cel)
    {
        var tekst = cel.GetString().Trim();
        return tekst.Length > 0 && tekst != "0";
    }

    private static RgsWerkboek Mislukt(string tekst) =>
        new(null, string.Empty, [], [], [], [new ImportMelding("RGS", MeldingErnst.Fout, tekst)]);

    [GeneratedRegex("^[BW][A-Za-z0-9]*$")]
    private static partial Regex CodePatroon();

    [GeneratedRegex(@"RGS\s*(\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersiePatroon();
}
