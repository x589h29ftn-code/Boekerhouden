using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Domain;

namespace Boekerhouden.Import;

/// <summary>
/// Interpreteert de rijen van een importsjabloon en voert de controles I2–I6 uit (zie docs/onderzoek §3.3).
/// Alle fouten worden in één keer verzameld, zodat de gebruiker ze in één ronde kan herstellen.
/// </summary>
internal static partial class SjabloonVerwerker
{
    private const int MinimaalBoekjaar = 1900;
    private const int MaximaalBoekjaar = 2100;

    public static ImportResultaat Verwerk(IReadOnlyList<Rij> rijen, ImportOpties opties)
    {
        ArgumentNullException.ThrowIfNull(opties);
        var meldingen = new List<ImportMelding>();

        var kop = ZoekKop(rijen);
        if (kop is null)
        {
            return Mislukt(meldingen, Fout(ImportCodes.Bestand,
                $"De kolomkoppen ontbreken. Verwacht een rij met '{ImportSjabloon.KolomGrootboeknummer}' (of 'Rekening'), " +
                $"'{ImportSjabloon.KolomOmschrijving}' (of 'Omschr') en '{ImportSjabloon.KolomBedrag}' (of 'Saldo')."));
        }

        if (kop.Ontbrekend.Count > 0)
        {
            return Mislukt(meldingen, Fout(ImportCodes.Bestand,
                $"Kolom {string.Join(", ", kop.Ontbrekend.Select(k => $"'{k}'"))} ontbreekt in de kopregel.", kop.Rij.Rijnummer));
        }

        var rijenBoven = rijen.TakeWhile(r => r != kop.Rij).ToList();
        var boekjaar = BepaalBoekjaar(rijenBoven, opties.Boekjaar, meldingen);
        var klant = ZoekWaardeNaastLabel(rijenBoven, ImportSjabloon.LabelKlant)?.Tekst is { Length: > 0 } k ? k : null;

        var regels = LeesRegels(rijen.SkipWhile(r => r != kop.Rij).Skip(1), kop, meldingen);

        if (regels.Count == 0 && !meldingen.Any(m => m.Code == ImportCodes.VerplichteVelden))
        {
            meldingen.Add(Fout(ImportCodes.VerplichteVelden, "Er staan geen grootboekregels in het bestand."));
        }

        ControleerUniekeNummers(regels, meldingen);

        var bedragenGeldig = !meldingen.Any(m => m.Code == ImportCodes.GeldigeBedragen);
        if (bedragenGeldig && regels.Count > 0)
        {
            var saldo = regels.Sum(r => r.Bedrag);
            if (saldo != 0m)
            {
                meldingen.Add(Fout(ImportCodes.SaldoNihil,
                    $"Het saldo is niet nihil: het totaal van alle bedragen is {Bedragen.DebetCredit(saldo)}. " +
                    "Controleer of er regels ontbreken of een minteken is vergeten (debet = positief, credit = negatief)."));
            }
        }

        if (meldingen.Any(m => m.Ernst == MeldingErnst.Fout) || boekjaar is null)
        {
            return new ImportResultaat(null, meldingen);
        }

        var saldibalans = new Saldibalans(
            boekjaar.Value,
            klant,
            regels.Select(r => new SaldibalansRegel(r.Grootboeknummer, r.Omschrijving, r.Bedrag, r.RgsCode)).ToList());
        return new ImportResultaat(saldibalans, meldingen);
    }

    /// <summary>
    /// De kopregel is de eerste rij met alle drie de verplichte kolommen. Is die er niet, dan de eerste rij
    /// met een grootboeknummerkolom, zodat de melding kan noemen welke kolom ontbreekt.
    /// </summary>
    private static Kop? ZoekKop(IReadOnlyList<Rij> rijen)
    {
        Kop? onvolledig = null;
        foreach (var rij in rijen)
        {
            var nummer = IndexVan(rij, t => IsKolom(t, ImportSjabloon.SynoniemenGrootboeknummer));
            if (nummer < 0)
            {
                continue;
            }

            var omschrijving = IndexVan(rij, t => IsKolom(t, ImportSjabloon.SynoniemenOmschrijving));
            var bedrag = IndexVan(rij, t => IsKolom(t, ImportSjabloon.SynoniemenBedrag));
            var rgs = IndexVan(rij, t => t.StartsWith("RGS", StringComparison.OrdinalIgnoreCase));

            var ontbrekend = new List<string>();
            if (omschrijving < 0)
            {
                ontbrekend.Add(ImportSjabloon.KolomOmschrijving);
            }

            if (bedrag < 0)
            {
                ontbrekend.Add(ImportSjabloon.KolomBedrag);
            }

            var kop = new Kop(rij, nummer, omschrijving, bedrag, rgs, ontbrekend);
            if (ontbrekend.Count == 0)
            {
                return kop;
            }

            onvolledig ??= kop;
        }

        return onvolledig;
    }

    /// <summary>
    /// Het boekjaar komt uit het bestand (naast 'Boekjaar') of uit de keuze van de gebruiker bij het importeren.
    /// Staan ze er allebei, dan moeten ze gelijk zijn.
    /// </summary>
    private static int? BepaalBoekjaar(List<Rij> rijenBoven, int? opgegeven, List<ImportMelding> meldingen)
    {
        if (opgegeven is { } keuze && !IsGeldigJaar(keuze))
        {
            meldingen.Add(Fout(ImportCodes.Boekjaar, $"Het opgegeven boekjaar {keuze} is ongeldig."));
            return null;
        }

        var cel = ZoekWaardeNaastLabel(rijenBoven, ImportSjabloon.LabelBoekjaar);
        if (cel is null || cel.IsLeeg)
        {
            if (opgegeven is null)
            {
                meldingen.Add(Fout(ImportCodes.Boekjaar,
                    "Het boekjaar ontbreekt. Vul het in naast 'Boekjaar' (cel B1) of geef het op bij het importeren."));
            }

            return opgegeven;
        }

        int? uitBestand = cel.Getal is { } getal
            ? decimal.Truncate(getal) == getal && getal is >= MinimaalBoekjaar and <= MaximaalBoekjaar ? (int)getal : null
            : int.TryParse(cel.Tekst, NumberStyles.None, CultureInfo.InvariantCulture, out var jaar) && IsGeldigJaar(jaar) ? jaar : null;
        if (uitBestand is null)
        {
            meldingen.Add(Fout(ImportCodes.Boekjaar, $"Het boekjaar '{cel.Tekst}' is ongeldig. Vul een jaartal in, bijvoorbeeld 2025."));
            return null;
        }

        if (opgegeven is { } gekozen && gekozen != uitBestand)
        {
            meldingen.Add(Fout(ImportCodes.Boekjaar,
                $"Het boekjaar in het bestand ({uitBestand}) wijkt af van het opgegeven boekjaar ({gekozen})."));
            return null;
        }

        return uitBestand;
    }

    private static bool IsGeldigJaar(int jaar) => jaar is >= MinimaalBoekjaar and <= MaximaalBoekjaar;

    private static List<GelezenRegel> LeesRegels(IEnumerable<Rij> dataRijen, Kop kop, List<ImportMelding> meldingen)
    {
        var regels = new List<GelezenRegel>();
        foreach (var rij in dataRijen)
        {
            var nummer = rij[kop.Grootboeknummer].Tekst;
            var omschrijving = rij[kop.Omschrijving].Tekst;
            var bedragCel = rij[kop.Bedrag];
            var rgsCel = kop.RgsCode >= 0 ? rij[kop.RgsCode] : Cel.Leeg;

            if (nummer.Length == 0 && omschrijving.Length == 0 && bedragCel.IsLeeg && rgsCel.IsLeeg)
            {
                continue;
            }

            if (nummer.Length == 0 && omschrijving.StartsWith("totaal", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Een totaalregel onder de tabel hoort niet bij de saldibalans.
            }

            var compleet = true;
            if (nummer.Length == 0)
            {
                meldingen.Add(Fout(ImportCodes.VerplichteVelden, $"Rij {rij.Rijnummer}: het grootboeknummer ontbreekt.", rij.Rijnummer));
                compleet = false;
            }

            if (omschrijving.Length == 0)
            {
                meldingen.Add(Fout(ImportCodes.VerplichteVelden, $"Rij {rij.Rijnummer}: de omschrijving ontbreekt.", rij.Rijnummer));
                compleet = false;
            }

            var bedrag = LeesBedrag(rij, bedragCel, meldingen);
            var rgs = LeesRgsCode(rij, rgsCel, meldingen);

            if (compleet && bedrag is not null)
            {
                regels.Add(new GelezenRegel(rij.Rijnummer, nummer, omschrijving, bedrag.Value, rgs));
            }
        }

        return regels;
    }

    private static decimal? LeesBedrag(Rij rij, Cel cel, List<ImportMelding> meldingen)
    {
        decimal bedrag;
        if (cel.Getal is { } getal)
        {
            bedrag = getal;
        }
        else if (cel.Tekst.Length == 0)
        {
            return 0m; // Een leeg bedrag telt als nihil.
        }
        else if (!BedragParser.TryParse(cel.Tekst, out bedrag))
        {
            meldingen.Add(Fout(ImportCodes.GeldigeBedragen, $"Rij {rij.Rijnummer}: '{cel.Tekst}' is geen geldig bedrag.", rij.Rijnummer));
            return null;
        }

        if (decimal.Round(bedrag, 2) != bedrag)
        {
            meldingen.Add(Fout(ImportCodes.GeldigeBedragen,
                $"Rij {rij.Rijnummer}: het bedrag '{cel.Tekst}' heeft meer dan 2 decimalen.", rij.Rijnummer));
            return null;
        }

        return bedrag;
    }

    private static string? LeesRgsCode(Rij rij, Cel cel, List<ImportMelding> meldingen)
    {
        if (cel.Tekst.Length == 0)
        {
            return null;
        }

        if (RgsCodePatroon().IsMatch(cel.Tekst))
        {
            return cel.Tekst;
        }

        meldingen.Add(new ImportMelding(ImportCodes.RgsFormaat, MeldingErnst.Waarschuwing,
            $"Rij {rij.Rijnummer}: de RGS-code '{cel.Tekst}' heeft geen geldig formaat en wordt genegeerd.", rij.Rijnummer));
        return null;
    }

    private static void ControleerUniekeNummers(List<GelezenRegel> regels, List<ImportMelding> meldingen)
    {
        foreach (var groep in regels.GroupBy(r => r.Grootboeknummer, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var rijnummers = groep.Select(r => r.Rijnummer).ToList();
            meldingen.Add(Fout(ImportCodes.UniekeNummers,
                $"Grootboeknummer {groep.Key} komt meerdere keren voor (rij {string.Join(", ", rijnummers)}).", rijnummers[0]));
        }
    }

    private static Cel? ZoekWaardeNaastLabel(IEnumerable<Rij> rijen, string label)
    {
        foreach (var rij in rijen)
        {
            var positie = IndexVan(rij, t => t.TrimEnd(':').StartsWith(label, StringComparison.OrdinalIgnoreCase));
            if (positie >= 0)
            {
                return rij[positie + 1];
            }
        }

        return null;
    }

    private static int IndexVan(Rij rij, Func<string, bool> voorwaarde)
    {
        for (var i = 0; i < rij.Cellen.Count; i++)
        {
            if (rij.Cellen[i].Getal is null && voorwaarde(rij.Cellen[i].Tekst))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsKolom(string tekst, IReadOnlyList<string> synoniemen)
    {
        var genormaliseerd = new string(tekst.Where(c => !char.IsWhiteSpace(c) && c is not ('.' or ':')).ToArray());
        return synoniemen.Any(s => string.Equals(s, genormaliseerd, StringComparison.OrdinalIgnoreCase));
    }

    private static ImportMelding Fout(string code, string tekst, int? rij = null) => new(code, MeldingErnst.Fout, tekst, rij);

    private static ImportResultaat Mislukt(List<ImportMelding> meldingen, ImportMelding fout)
    {
        meldingen.Add(fout);
        return new ImportResultaat(null, meldingen);
    }

    /// <summary>RGS-referentiecodes bestaan uit letters en beginnen met B (balans) of W (winst-en-verlies).</summary>
    [GeneratedRegex("^[BW][A-Za-z]+$")]
    private static partial Regex RgsCodePatroon();

    private sealed record Kop(Rij Rij, int Grootboeknummer, int Omschrijving, int Bedrag, int RgsCode, List<string> Ontbrekend);

    private sealed record GelezenRegel(int Rijnummer, string Grootboeknummer, string Omschrijving, decimal Bedrag, string? RgsCode);
}
