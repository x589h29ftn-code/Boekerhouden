using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Rapport;

/// <summary>
/// Verloopoverzichten onder de specificaties. Standaard bij de immateriële, materiële en financiële vaste activa
/// (per subpost een kolom); de gebruiker kan er een toevoegen bij een andere balanspost of bij een rekening, bijv. de
/// rekening-courant met de directeur. De mutaties zijn een voorstel uit de saldi (investeringen, desinvesteringen en
/// de afschrijvingen uit de winst-en-verliesrekening) en zijn aan te passen; ze moeten aansluiten op het verschil
/// tussen begin- en eindstand.
/// </summary>
public static partial class RapportOpbouw
{
    /// <summary>Het voorvoegsel van een verloopoverzicht bij een rekening: "rek:" en het id van de rekening.</summary>
    public const string RekeningVoorvoegsel = "rek:";

    /// <summary>De regel voor het deel van de mutaties dat (nog) niet aansluit.</summary>
    public const string NietVerklaard = "Nog niet verklaard";

    /// <summary>Posten met standaard een verloopoverzicht.</summary>
    public static readonly IReadOnlySet<string> StandaardVerloop = new HashSet<string>(StringComparer.Ordinal) { "BIva", "BMva", "BFva" };

    /// <summary>Posten waarop wordt afgeschreven: daar staan verkrijgingsprijs en cumulatieve afschrijvingen apart.</summary>
    private static readonly HashSet<string> Afschrijfbaar = new(StringComparer.Ordinal) { "BIva", "BMva" };

    /// <summary>Bij een subpost van de vaste activa de afschrijvingen in de W&amp;V (RGS), als die niet hetzelfde achtervoegsel hebben.</summary>
    private static readonly Dictionary<string, string[]> Afschrijvingscodes = new(StringComparer.Ordinal)
    {
        ["BMvaTer"] = ["WAfsAmvAft"],
        ["BMvaVer"] = ["WAfsAmvVeb"],
        ["BMvaVli"] = ["WAfsAmvSev"],
        ["BMvaSch"] = ["WAfsAmvAfs"],
        ["BMvaMep"] = ["WAfsAmvAmp"],
        ["BMvaGeb"] = ["WAfsAmvAfg"],
        ["BMvaVmv"] = ["WAfsAmvAvm"],
        ["BMvaNad"] = ["WAfsAmvBgm"],
        ["BIvaKou"] = ["WAfsAivOek"],
        ["BIvaKoo"] = ["WAfsAivKoe"],
        ["BIvaGoo"] = ["WAfsAivGoo", "WAfsAivGon"],
        ["BIvaVoi"] = ["WAfsAivViv"],
    };

    public static string RekeningSleutel(Guid rekeningId) => RekeningVoorvoegsel + rekeningId.ToString("N");

    /// <summary>De verloopoverzichten bij een balanspost: dat van de post zelf en die van zijn rekeningen.</summary>
    private static IEnumerable<Verloop> Verlopen(RapportGegevens g, RgsCode post, Dictionary<string, List<Bedrag>> perSubpost, ICollection<string>? waarschuwingen)
    {
        var standaard = StandaardVerloop.Contains(post.Code);
        if (post.Code != EigenVermogen && (g.Verlopen?.TryGetValue(post.Code, out var tonen) == true ? tonen : standaard))
        {
            yield return PostVerloop(g, post, perSubpost, standaard, waarschuwingen);
        }

        var teken = Teken(post);
        var rekeningen = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3))
            .SelectMany(s => perSubpost.GetValueOrDefault(s.Code) ?? [])
            .Where(b => b.EigenPostId is null && g.Verlopen?.GetValueOrDefault(RekeningSleutel(b.Rekening.RekeningId)) == true)
            .GroupBy(b => b.Rekening)
            .OrderBy(r => r.Key.Nummer, StringComparer.Ordinal);
        foreach (var rekening in rekeningen)
        {
            var r = rekening.Key;
            var naam = g.Weergave.GetValueOrDefault(r.RekeningId)?.Omschrijving ?? r.Omschrijving;
            var begin = teken * rekening.Sum(b => b.Vorig);
            var eind = teken * rekening.Sum(b => b.Huidig);
            yield return Standen(g, RekeningSleutel(r.RekeningId), $"Verloop {naam}", [(string.Empty, naam, begin, eind)], standaard: false, waarschuwingen);
        }
    }

    private static Verloop PostVerloop(RapportGegevens g, RgsCode post, Dictionary<string, List<Bedrag>> perSubpost, bool standaard, ICollection<string>? waarschuwingen)
    {
        var teken = Teken(post);
        var subposten = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3)).ToList();
        var titel = $"Verloop {g.Naam(post.Code).ToLowerInvariant()}";
        bool Cumulatief(Bedrag b) => g.Schema.Zoek(b.Code) is { } c && c.Kant != Saldokant.Debet && post.Kant == Saldokant.Debet;
        var kolommen = subposten.Select(s =>
        {
            var bedragen = perSubpost.GetValueOrDefault(s.Code) ?? [];
            return (Sleutel: s.Code, Naam: g.Naam(s.Code),
                Aanschaf0: teken * bedragen.Where(b => !Cumulatief(b)).Sum(b => b.Vorig), Aanschaf1: teken * bedragen.Where(b => !Cumulatief(b)).Sum(b => b.Huidig),
                Cum0: teken * bedragen.Where(Cumulatief).Sum(b => b.Vorig), Cum1: teken * bedragen.Where(Cumulatief).Sum(b => b.Huidig),
                MetCum: bedragen.Any(Cumulatief));
        }).ToList();

        if (!Afschrijfbaar.Contains(post.Code) || !kolommen.Any(k => k.MetCum))
        {
            var voorstel = Afschrijfbaar.Contains(post.Code) ? AfschrijvingsVoorstel(g, post, kolommen.Select(k => (k.Sleutel, false, k.Aanschaf0 + k.Cum0, k.Aanschaf1 + k.Cum1, 0m, 0m)).ToList()) : null;
            return Standen(g, post.Code, titel, [.. kolommen.Select(k => (k.Sleutel, k.Naam, k.Aanschaf0 + k.Cum0, k.Aanschaf1 + k.Cum1))], standaard, waarschuwingen, voorstel, afschrijfbaar: voorstel is not null);
        }

        var sleutels = kolommen.Select(k => k.Sleutel).ToList();
        var doel = kolommen.ToDictionary(k => k.Sleutel, k => k.Aanschaf1 + k.Cum1 - k.Aanschaf0 - k.Cum0, StringComparer.Ordinal);
        var bewaard = g.VerloopMutaties?.GetValueOrDefault(post.Code);
        var mutaties = bewaard ?? AfschrijvingsVoorstel(g, post, kolommen.Select(k => (k.Sleutel, k.MetCum, k.Aanschaf0 + k.Cum0, k.Aanschaf1 + k.Cum1, k.Aanschaf1 - k.Aanschaf0, k.Cum1 - k.Cum0)).ToList());
        var jaar = Jaar(g.Boekjaar);
        var regels = new List<VerloopRegel>
        {
            new(VerloopRegelSoort.Kop, $"Boekwaarde 1 januari {jaar}", []),
            Rij(VerloopRegelSoort.Stand, "Verkrijgings- of vervaardigingsprijs", kolommen.Select(k => k.Aanschaf0)),
            Rij(VerloopRegelSoort.Stand, "Cumulatieve afschrijvingen en waardeverminderingen", kolommen.Select(k => k.Cum0)),
            Rij(VerloopRegelSoort.Subtotaal, string.Empty, kolommen.Select(k => k.Aanschaf0 + k.Cum0)),
            new(VerloopRegelSoort.Kop, $"Mutaties {jaar}", []),
        };
        regels.AddRange(Mutatieregels(titel, sleutels, mutaties, doel, waarschuwingen));
        regels.Add(Rij(VerloopRegelSoort.Subtotaal, string.Empty, sleutels.Select(s => doel[s])));
        regels.Add(new(VerloopRegelSoort.Kop, $"Boekwaarde 31 december {jaar}", []));
        regels.Add(Rij(VerloopRegelSoort.Stand, "Verkrijgings- of vervaardigingsprijs", kolommen.Select(k => k.Aanschaf1)));
        regels.Add(Rij(VerloopRegelSoort.Stand, "Cumulatieve afschrijvingen en waardeverminderingen", kolommen.Select(k => k.Cum1)));
        regels.Add(Rij(VerloopRegelSoort.Eindstand, string.Empty, kolommen.Select(k => k.Aanschaf1 + k.Cum1)));
        return new Verloop(post.Code, titel, sleutels, Koppen(kolommen.Select(k => k.Naam)), [.. regels.Select(r => MetTotaal(r, sleutels.Count))],
            mutaties, doel, bewaard is not null, standaard);
    }

    /// <summary>Een verloop van standen: 1 januari, de mutaties en 31 december (financiële vaste activa, een rekening).</summary>
    private static Verloop Standen(RapportGegevens g, string sleutel, string titel, IReadOnlyList<(string Sleutel, string Naam, decimal Begin, decimal Eind)> kolommen,
        bool standaard, ICollection<string>? waarschuwingen, IReadOnlyList<VerloopMutatie>? voorstel = null, bool afschrijfbaar = false)
    {
        var sleutels = kolommen.Select(k => k.Sleutel).ToList();
        var doel = kolommen.ToDictionary(k => k.Sleutel, k => k.Eind - k.Begin, StringComparer.Ordinal);
        var bewaard = g.VerloopMutaties?.GetValueOrDefault(sleutel);
        var mutaties = bewaard ?? voorstel ?? MutatieVoorstel(sleutels, doel);
        var jaar = Jaar(g.Boekjaar);
        var regels = new List<VerloopRegel> { Rij(VerloopRegelSoort.Stand, $"{(afschrijfbaar ? "Boekwaarde" : "Stand")} 1 januari {jaar}", kolommen.Select(k => k.Begin)) };
        regels.AddRange(Mutatieregels(titel, sleutels, mutaties, doel, waarschuwingen));
        regels.Add(Rij(VerloopRegelSoort.Eindstand, $"{(afschrijfbaar ? "Boekwaarde" : "Stand")} 31 december {jaar}", kolommen.Select(k => k.Eind)));
        var koppen = sleutels.Count == 1 && sleutels[0].Length == 0 ? [jaar] : Koppen(kolommen.Select(k => k.Naam));
        return new Verloop(sleutel, titel, sleutels, koppen, [.. regels.Select(r => MetTotaal(r, sleutels.Count))], mutaties, doel, bewaard is not null, standaard);
    }

    /// <summary>De mutatieregels, met een regel voor wat niet aansluit (en een waarschuwing).</summary>
    private static IEnumerable<VerloopRegel> Mutatieregels(string titel, IReadOnlyList<string> sleutels, IReadOnlyList<VerloopMutatie> mutaties,
        Dictionary<string, decimal> doel, ICollection<string>? waarschuwingen)
    {
        foreach (var m in mutaties)
        {
            yield return Rij(VerloopRegelSoort.Mutatie, m.Omschrijving, sleutels.Select(s => m.Bedragen.GetValueOrDefault(s)));
        }

        var verschil = sleutels.Select(s => doel[s] - mutaties.Sum(m => m.Bedragen.GetValueOrDefault(s))).ToList();
        if (verschil.Any(v => v != 0))
        {
            waarschuwingen?.Add($"{titel} sluit niet aan: nog niet verklaard {Bedragen.Heel(verschil.Sum())}.");
            yield return Rij(VerloopRegelSoort.Verschil, NietVerklaard, verschil);
        }
    }

    /// <summary>
    /// Het voorstel bij vaste activa: de afschrijvingen uit de W&amp;V (of de toename van de cumulatieve afschrijvingen),
    /// een toename van de verkrijgingsprijs als investering, en de rest als desinvestering.
    /// </summary>
    private static List<VerloopMutatie> AfschrijvingsVoorstel(RapportGegevens g, RgsCode post,
        IReadOnlyList<(string Sleutel, bool MetCum, decimal Begin, decimal Eind, decimal DeltaAanschaf, decimal DeltaCum)> kolommen)
    {
        var investeringen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var desinvesteringen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var afschrijvingen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var totaalWv = post.Code == "BIva" ? "WAfsAiv" : "WAfsAmv";
        foreach (var k in kolommen)
        {
            var codes = Afschrijvingscodes.GetValueOrDefault(k.Sleutel)
                ?? [totaalWv + k.Sleutel[Math.Min(4, k.Sleutel.Length)..]];
            var wv = codes.Where(c => g.Schema.Zoek(c) is not null).Select(c => g.Balans.Totaal(c)).ToList();
            decimal afschrijving = wv.Any(t => t.Aantal > 0) ? wv.Sum(t => t.Huidig)
                : k.MetCum ? -k.DeltaCum
                : kolommen.Count == 1 ? g.Balans.Totaal(totaalWv).Huidig
                : 0;
            var investering = k.MetCum ? Math.Max(k.DeltaAanschaf, 0) : Math.Max(k.Eind - k.Begin + afschrijving, 0);
            investeringen[k.Sleutel] = investering;
            desinvesteringen[k.Sleutel] = k.Eind - k.Begin + afschrijving - investering;
            afschrijvingen[k.Sleutel] = -afschrijving;
        }

        return [.. new[] { ("Investeringen", investeringen), ("Desinvesteringen", desinvesteringen), ("Afschrijvingen", afschrijvingen) }
            .Where(m => m.Item2.Values.Any(b => b != 0))
            .Select(m => new VerloopMutatie(m.Item1, m.Item2))];
    }

    /// <summary>Het voorstel bij standen: een toename en een afname.</summary>
    private static List<VerloopMutatie> MutatieVoorstel(IReadOnlyList<string> sleutels, Dictionary<string, decimal> doel) =>
        [.. new[]
        {
            new VerloopMutatie("Toename", sleutels.ToDictionary(s => s, s => Math.Max(doel[s], 0), StringComparer.Ordinal)),
            new VerloopMutatie("Afname", sleutels.ToDictionary(s => s, s => Math.Min(doel[s], 0), StringComparer.Ordinal)),
        }.Where(m => m.Bedragen.Values.Any(b => b != 0))];

    private static VerloopRegel Rij(VerloopRegelSoort soort, string omschrijving, IEnumerable<decimal> bedragen) =>
        new(soort, omschrijving, [.. bedragen.Select(b => (decimal?)b)]);

    /// <summary>Bij meer dan één kolom komt er een totaalkolom bij.</summary>
    private static VerloopRegel MetTotaal(VerloopRegel regel, int kolommen) =>
        kolommen < 2 || regel.Bedragen.Count == 0 ? regel : regel with { Bedragen = [.. regel.Bedragen, regel.Bedragen.Sum(b => b ?? 0)] };

    private static List<string> Koppen(IEnumerable<string> namen)
    {
        var koppen = namen.ToList();
        return koppen.Count > 1 ? [.. koppen, "Totaal"] : koppen;
    }
}
