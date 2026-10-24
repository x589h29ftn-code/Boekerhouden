using System.Globalization;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Rapport;

/// <summary>
/// Verloopoverzichten onder de specificaties. Standaard bij de immateriële, materiële en financiële vaste activa en
/// het eigen vermogen (per subpost een kolom); de gebruiker kan er een toevoegen bij een andere balanspost of bij een
/// rekening, bijv. de rekening-courant met de directeur (standaard met vorig jaar ernaast). De mutaties zijn een
/// voorstel uit de saldi (investeringen, desinvesteringen, de afschrijvingen uit de winst-en-verliesrekening, het
/// resultaat) en zijn aan te passen; ze moeten aansluiten op het verschil tussen begin- en eindstand.
/// </summary>
public static partial class RapportOpbouw
{
    /// <summary>Het voorvoegsel van een verloopoverzicht bij een rekening: "rek:" en het id van de rekening.</summary>
    public const string RekeningVoorvoegsel = "rek:";

    /// <summary>De regel voor het deel van de mutaties dat (nog) niet aansluit.</summary>
    public const string NietVerklaard = "Nog niet verklaard";

    /// <summary>De kolom van het vorige jaar in een vergelijkend verloop (bij een rekening).</summary>
    public const string KolomVorig = "vorig";

    /// <summary>De kolom van het onverdeelde resultaat in het verloop van het eigen vermogen.</summary>
    public const string KolomResultaat = "resultaat";

    /// <summary>Posten met standaard een verloopoverzicht.</summary>
    public static readonly IReadOnlySet<string> StandaardVerloop = new HashSet<string>(StringComparer.Ordinal) { "BIva", "BMva", "BFva", EigenVermogen };

    /// <summary>Posten waarop wordt afgeschreven: daar staan verkrijgingsprijs en cumulatieve afschrijvingen apart.</summary>
    private static readonly HashSet<string> Afschrijfbaar = new(StringComparer.Ordinal) { "BIva", "BMva" };

    /// <summary>Waar het resultaat van vorig jaar standaard naartoe gaat bij de bestemming (de eerste die er is).</summary>
    private static readonly string[] Reserves = ["BEivOvr", "BEivKap"];

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

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static string RekeningSleutel(Guid rekeningId) => RekeningVoorvoegsel + rekeningId.ToString("N");

    /// <summary>Een kolom van een verloop: de sleutel (subpost, "" of "vorig"), de kop en de standen.</summary>
    private sealed record Kolom(string Sleutel, string Naam, decimal Begin, decimal Eind);

    /// <summary>De verloopoverzichten bij een balanspost: dat van de post zelf en die van zijn rekeningen.</summary>
    private static IEnumerable<Verloop> Verlopen(RapportGegevens g, RgsCode post, Dictionary<string, List<Bedrag>> perSubpost, ICollection<string>? waarschuwingen)
    {
        var standaard = StandaardVerloop.Contains(post.Code);
        if (g.Verlopen?.TryGetValue(post.Code, out var tonen) == true ? tonen : standaard)
        {
            yield return post.Code == EigenVermogen
                ? EigenVermogenVerloop(g, post, standaard, waarschuwingen)
                : PostVerloop(g, post, perSubpost, standaard, waarschuwingen);
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
            var sleutel = RekeningSleutel(r.RekeningId);
            var naam = g.Weergave.GetValueOrDefault(r.RekeningId)?.Omschrijving ?? r.Omschrijving;
            var beginVorig = g.SaldiTweeJaarTerug?.TryGetValue(r.RekeningId, out var saldo) == true ? teken * saldo : (decimal?)null;
            yield return RekeningVerloop(g, sleutel, $"Verloop {naam}", teken * rekening.Sum(b => b.Vorig), teken * rekening.Sum(b => b.Huidig), beginVorig,
                g.VerloopZonderVorigJaar?.Contains(sleutel) != true, waarschuwingen);
        }
    }

    private static Verloop PostVerloop(RapportGegevens g, RgsCode post, Dictionary<string, List<Bedrag>> perSubpost, bool standaard, ICollection<string>? waarschuwingen)
    {
        var teken = Teken(post);
        var subposten = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3)).ToList();
        var titel = Titel(g, post);
        bool Cumulatief(Bedrag b) => g.Schema.Zoek(b.Code) is { } c && c.Kant != Saldokant.Debet && post.Kant == Saldokant.Debet;
        var kolommen = subposten.Select(s =>
        {
            var bedragen = perSubpost.GetValueOrDefault(s.Code) ?? [];
            return (Sleutel: s.Code, Naam: g.Naam(s.Code),
                Aanschaf0: teken * bedragen.Where(b => !Cumulatief(b)).Sum(b => b.Vorig), Aanschaf1: teken * bedragen.Where(b => !Cumulatief(b)).Sum(b => b.Huidig),
                Cum0: teken * bedragen.Where(Cumulatief).Sum(b => b.Vorig), Cum1: teken * bedragen.Where(Cumulatief).Sum(b => b.Huidig),
                MetCum: bedragen.Any(Cumulatief));
        }).ToList();
        var afschrijfbaar = Afschrijfbaar.Contains(post.Code);
        var afschrijvingen = kolommen.ToDictionary(k => k.Sleutel,
            k => Afschrijving(g, post, k.Sleutel, k.MetCum ? k.Cum1 - k.Cum0 : null, kolommen.Count), StringComparer.Ordinal);
        var percentages = afschrijfbaar
            ? kolommen.ToDictionary(k => k.Sleutel, k => g.Afschrijvingspercentages?.GetValueOrDefault(k.Sleutel) ?? Percentage(afschrijvingen[k.Sleutel], k.Aanschaf1, k.MetCum), StringComparer.Ordinal)
            : null;
        var percentageRegel = percentages is null ? null
            : new VerloopRegel(VerloopRegelSoort.Tekst, "Afschrijvingspercentage", []) { Teksten = [.. kolommen.Select(k => percentages[k.Sleutel])] };

        if (!afschrijfbaar || !kolommen.Any(k => k.MetCum))
        {
            var voorstel = afschrijfbaar
                ? AfschrijvingsVoorstel(kolommen.Select(k => (k.Sleutel, false, k.Aanschaf0 + k.Cum0, k.Aanschaf1 + k.Cum1, 0m, afschrijvingen[k.Sleutel])).ToList())
                : null;
            var standen = Standen(g, post.Code, titel, [.. kolommen.Select(k => new Kolom(k.Sleutel, k.Naam, k.Aanschaf0 + k.Cum0, k.Aanschaf1 + k.Cum1))], standaard, waarschuwingen,
                voorstel, afschrijfbaar ? "Boekwaarde" : "Stand");
            return percentageRegel is null ? standen
                : standen with { Regels = [.. standen.Regels, MetTotaal(percentageRegel, kolommen.Count)] } with { Percentages = percentages };
        }

        var sleutels = kolommen.Select(k => k.Sleutel).ToList();
        var doel = kolommen.ToDictionary(k => k.Sleutel, k => k.Aanschaf1 + k.Cum1 - k.Aanschaf0 - k.Cum0, StringComparer.Ordinal);
        var bewaard = g.VerloopMutaties?.GetValueOrDefault(post.Code);
        var mutaties = bewaard ?? AfschrijvingsVoorstel(
            [.. kolommen.Select(k => (k.Sleutel, k.MetCum, k.Aanschaf0 + k.Cum0, k.Aanschaf1 + k.Cum1, k.Aanschaf1 - k.Aanschaf0, afschrijvingen[k.Sleutel]))]);
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
        regels.Add(percentageRegel!);
        return new Verloop(post.Code, titel, sleutels, Koppen(kolommen.Select(k => k.Naam)), [.. regels.Select(r => MetTotaal(r, sleutels.Count))],
            mutaties, doel, bewaard is not null, standaard)
        { Percentages = percentages };
    }

    /// <summary>
    /// Het verloop van het eigen vermogen: per subpost een kolom en een kolom voor het onverdeelde resultaat. Voorstel:
    /// het resultaat van dit jaar, de bestemming van het resultaat van vorig jaar (naar de overige reserves) en de rest
    /// als overige mutaties (bijv. dividend of privé-opnamen).
    /// </summary>
    private static Verloop EigenVermogenVerloop(RapportGegevens g, RgsCode post, bool standaard, ICollection<string>? waarschuwingen)
    {
        var kolommen = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3))
            .Select(s => new Kolom(s.Code, g.Naam(s.Code), -g.Balans.Totaal(s.Code).Vorig, -g.Balans.Totaal(s.Code).Huidig))
            .ToList();
        if (HeeftResultaat(g))
        {
            kolommen.Add(new Kolom(KolomResultaat, "Resultaat boekjaar", g.Balans.ResultaatVorig, g.Balans.ResultaatHuidig));
        }

        var reserve = Reserves.FirstOrDefault(r => kolommen.Any(k => k.Sleutel == r)) ?? kolommen.LastOrDefault(k => k.Sleutel != KolomResultaat)?.Sleutel;
        var vorigJaar = Jaar(g.Boekjaar - 1);
        var voorstel = new List<VerloopMutatie>();
        if (HeeftResultaat(g))
        {
            voorstel.Add(new VerloopMutatie($"Resultaat {Jaar(g.Boekjaar)}", new Dictionary<string, decimal>(StringComparer.Ordinal) { [KolomResultaat] = g.Balans.ResultaatHuidig }));
            if (g.Balans.ResultaatVorig != 0 && reserve is not null)
            {
                voorstel.Add(new VerloopMutatie($"Bestemming resultaat {vorigJaar}", new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [KolomResultaat] = -g.Balans.ResultaatVorig,
                    [reserve] = g.Balans.ResultaatVorig,
                }));
            }
        }

        var overig = kolommen.Where(k => k.Sleutel != KolomResultaat)
            .ToDictionary(k => k.Sleutel, k => k.Eind - k.Begin - voorstel.Sum(m => m.Bedragen.GetValueOrDefault(k.Sleutel)), StringComparer.Ordinal);
        if (overig.Values.Any(b => b != 0))
        {
            voorstel.Add(new VerloopMutatie("Overige mutaties", overig));
        }

        return Standen(g, post.Code, Titel(g, post), kolommen, standaard, waarschuwingen, voorstel);
    }

    /// <summary>Het verloop van een rekening; standaard met vorig jaar ernaast (de mutaties van vorig jaar komen uit het dossier van vorig jaar).</summary>
    private static Verloop RekeningVerloop(RapportGegevens g, string sleutel, string titel, decimal begin, decimal eind, decimal? beginVorig, bool vergelijkend,
        ICollection<string>? waarschuwingen)
    {
        if (!vergelijkend)
        {
            return Standen(g, sleutel, titel, [new Kolom(string.Empty, Jaar(g.Boekjaar), begin, eind)], standaard: false, waarschuwingen);
        }

        var eindVorig = begin;
        var bewaard = g.VerloopMutaties?.GetValueOrDefault(sleutel);
        var mutaties = bewaard ?? Combineer(
            MutatieVoorstel([string.Empty], new(StringComparer.Ordinal) { [string.Empty] = eind - begin }),
            g.VorigeVerloopMutaties?.GetValueOrDefault(sleutel) is { } vorige
                ? [.. vorige.Select(m => new VerloopMutatie(m.Omschrijving, new Dictionary<string, decimal>(StringComparer.Ordinal) { [KolomVorig] = m.Bedragen.GetValueOrDefault(string.Empty) }))]
                : beginVorig is { } bv ? MutatieVoorstel([KolomVorig], new(StringComparer.Ordinal) { [KolomVorig] = eindVorig - bv }) : []);

        // Zonder saldi van twee jaar terug volgt de beginstand van vorig jaar uit de mutaties.
        var startVorig = beginVorig ?? eindVorig - mutaties.Sum(m => m.Bedragen.GetValueOrDefault(KolomVorig));
        var verloop = Standen(g, sleutel, titel,
            [new Kolom(string.Empty, Jaar(g.Boekjaar), begin, eind), new Kolom(KolomVorig, Jaar(g.Boekjaar - 1), startVorig, eindVorig)],
            standaard: false, waarschuwingen, mutaties, metJaar: false, totaal: false);
        // Zonder bekende beginstand sluit vorig jaar altijd aan: daar valt niets te controleren.
        var doel = beginVorig is null ? verloop.Doel.Where(d => d.Key != KolomVorig).ToDictionary(StringComparer.Ordinal) : verloop.Doel;
        return verloop with { Aangepast = bewaard is not null, Vergelijkend = true, Doel = doel };
    }

    /// <summary>Een verloop van standen: 1 januari, de mutaties en 31 december (financiële vaste activa, eigen vermogen, een rekening).</summary>
    private static Verloop Standen(RapportGegevens g, string sleutel, string titel, IReadOnlyList<Kolom> kolommen, bool standaard, ICollection<string>? waarschuwingen,
        IReadOnlyList<VerloopMutatie>? voorstel = null, string stand = "Stand", bool metJaar = true, bool totaal = true)
    {
        var sleutels = kolommen.Select(k => k.Sleutel).ToList();
        var doel = kolommen.ToDictionary(k => k.Sleutel, k => k.Eind - k.Begin, StringComparer.Ordinal);
        var bewaard = g.VerloopMutaties?.GetValueOrDefault(sleutel);
        var mutaties = bewaard ?? voorstel ?? MutatieVoorstel(sleutels, doel);
        var jaar = metJaar ? $" {Jaar(g.Boekjaar)}" : string.Empty;
        var regels = new List<VerloopRegel> { Rij(VerloopRegelSoort.Stand, $"{stand} 1 januari{jaar}", kolommen.Select(k => k.Begin)) };
        regels.AddRange(Mutatieregels(titel, sleutels, mutaties, doel, waarschuwingen));
        regels.Add(Rij(VerloopRegelSoort.Eindstand, $"{stand} 31 december{jaar}", kolommen.Select(k => k.Eind)));
        var koppen = totaal ? Koppen(kolommen.Select(k => k.Naam)) : [.. kolommen.Select(k => k.Naam)];
        return new Verloop(sleutel, titel, sleutels, koppen, [.. regels.Select(r => totaal ? MetTotaal(r, sleutels.Count) : r)], mutaties, doel, bewaard is not null, standaard);
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
    /// De afschrijving van een subpost in het boekjaar: uit de W&amp;V (via de RGS-code), anders de toename van de
    /// cumulatieve afschrijvingen, anders bij één kolom alle afschrijvingen op deze soort vaste activa.
    /// </summary>
    private static decimal Afschrijving(RapportGegevens g, RgsCode post, string subpost, decimal? deltaCum, int kolommen)
    {
        if (!Afschrijfbaar.Contains(post.Code))
        {
            return 0;
        }

        var totaalWv = post.Code == "BIva" ? "WAfsAiv" : "WAfsAmv";
        var codes = Afschrijvingscodes.GetValueOrDefault(subpost) ?? [totaalWv + subpost[Math.Min(4, subpost.Length)..]];
        var wv = codes.Where(c => g.Schema.Zoek(c) is not null).Select(g.Balans.Totaal).ToList();
        return wv.Any(t => t.Aantal > 0) ? wv.Sum(t => t.Huidig)
            : deltaCum is { } d ? -d
            : kolommen == 1 ? g.Balans.Totaal(totaalWv).Huidig
            : 0;
    }

    /// <summary>
    /// Het voorstel bij vaste activa: de afschrijvingen, een toename van de verkrijgingsprijs als investering, en de
    /// rest als desinvestering.
    /// </summary>
    private static List<VerloopMutatie> AfschrijvingsVoorstel(
        IReadOnlyList<(string Sleutel, bool MetCum, decimal Begin, decimal Eind, decimal DeltaAanschaf, decimal Afschrijving)> kolommen)
    {
        var investeringen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var desinvesteringen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var afschrijvingen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var k in kolommen)
        {
            var investering = k.MetCum ? Math.Max(k.DeltaAanschaf, 0) : Math.Max(k.Eind - k.Begin + k.Afschrijving, 0);
            investeringen[k.Sleutel] = investering;
            desinvesteringen[k.Sleutel] = k.Eind - k.Begin + k.Afschrijving - investering;
            afschrijvingen[k.Sleutel] = -k.Afschrijving;
        }

        return [.. new[] { ("Investeringen", investeringen), ("Desinvesteringen", desinvesteringen), ("Afschrijvingen", afschrijvingen) }
            .Where(m => m.Item2.Values.Any(b => b != 0))
            .Select(m => new VerloopMutatie(m.Item1, m.Item2))];
    }

    /// <summary>
    /// Een voorstel voor het afschrijvingspercentage: de afschrijving van dit jaar ten opzichte van de verkrijgingsprijs,
    /// afgerond op 5%. Zonder verkrijgingsprijs (alleen een boekwaarde) geen voorstel.
    /// </summary>
    private static string Percentage(decimal afschrijving, decimal aanschaf, bool metCum)
    {
        if (!metCum || aanschaf <= 0 || afschrijving <= 0)
        {
            return "-";
        }

        var procent = Math.Max(5, Math.Round(afschrijving / aanschaf * 100 / 5, MidpointRounding.AwayFromZero) * 5);
        return procent.ToString("0.0", Nl);
    }

    /// <summary>Het voorstel bij standen: een toename en een afname.</summary>
    private static List<VerloopMutatie> MutatieVoorstel(IReadOnlyList<string> sleutels, Dictionary<string, decimal> doel) =>
        [.. new[]
        {
            new VerloopMutatie("Toename", sleutels.ToDictionary(s => s, s => Math.Max(doel[s], 0), StringComparer.Ordinal)),
            new VerloopMutatie("Afname", sleutels.ToDictionary(s => s, s => Math.Min(doel[s], 0), StringComparer.Ordinal)),
        }.Where(m => m.Bedragen.Values.Any(b => b != 0))];

    /// <summary>Voegt mutaties met dezelfde omschrijving samen (de bedragen van de ene kolom bij die van de andere).</summary>
    private static List<VerloopMutatie> Combineer(IEnumerable<VerloopMutatie> eerste, IEnumerable<VerloopMutatie> tweede)
    {
        var lijst = new List<VerloopMutatie>();
        foreach (var m in eerste.Concat(tweede))
        {
            var i = lijst.FindIndex(l => string.Equals(l.Omschrijving, m.Omschrijving, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                lijst.Add(m);
                continue;
            }

            var bedragen = new Dictionary<string, decimal>(lijst[i].Bedragen, StringComparer.Ordinal);
            foreach (var (kolom, bedrag) in m.Bedragen)
            {
                bedragen[kolom] = bedragen.GetValueOrDefault(kolom) + bedrag;
            }

            lijst[i] = lijst[i] with { Bedragen = bedragen };
        }

        return lijst;
    }

    private static string Titel(RapportGegevens g, RgsCode post) => $"Verloop {g.Naam(post.Code).ToLowerInvariant()}";

    private static VerloopRegel Rij(VerloopRegelSoort soort, string omschrijving, IEnumerable<decimal> bedragen) =>
        new(soort, omschrijving, [.. bedragen.Select(b => (decimal?)b)]);

    /// <summary>Bij meer dan één kolom komt er een totaalkolom bij (een tekstregel krijgt daar niets).</summary>
    private static VerloopRegel MetTotaal(VerloopRegel regel, int kolommen) =>
        kolommen < 2 ? regel
        : regel.Teksten is { } teksten ? regel with { Teksten = [.. teksten, string.Empty] }
        : regel.Bedragen.Count == 0 ? regel
        : regel with { Bedragen = [.. regel.Bedragen, regel.Bedragen.Sum(b => b ?? 0)] };

    private static List<string> Koppen(IEnumerable<string> namen)
    {
        var koppen = namen.ToList();
        return koppen.Count > 1 ? [.. koppen, "Totaal"] : koppen;
    }
}
