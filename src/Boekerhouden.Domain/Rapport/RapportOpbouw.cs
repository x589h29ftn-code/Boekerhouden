using System.Globalization;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Rapport;

/// <summary>Hoe een grootboekrekening in de specificaties staat: een eigen omschrijving, of verborgen (telt mee in "Overige").</summary>
public sealed record RekeningWeergave(string? Omschrijving, bool Verbergen);

/// <param name="Balans">De proefbalans per post, na correcties, in hele euro's (zoals <c>DossierSaldi</c> die levert).</param>
/// <param name="Naam">De naam van een post in het rapport (klantnaam, kantoornaam of RGS-omschrijving).</param>
/// <param name="ZonderSpecificatie">Hoofdposten (codes) waarvan de specificatie niet in het rapport komt.</param>
public sealed record RapportGegevens(
    RapportSoort Soort,
    string Klantnaam,
    string? Plaats,
    int Boekjaar,
    bool Concept,
    RgsSchema Schema,
    PostBalans Balans,
    IReadOnlyList<RekeningSaldo> Rekeningen,
    IReadOnlyList<EigenPost> EigenPosten,
    Func<string, string> Naam,
    IReadOnlyDictionary<Guid, RekeningWeergave> Weergave,
    IReadOnlySet<string> ZonderSpecificatie);

/// <summary>
/// Bouwt het rapport (plakje 6, docs §9.1–9.3) uit de proefbalans per post: de balans (tweekoloms, met het resultaat
/// als aparte regel in het eigen vermogen), de winst-en-verliesrekening met brutomarge, en per post een specificatie
/// uit de grootboekrekeningen. Bedragen staan positief aan de normale kant van de post.
/// </summary>
public static class RapportOpbouw
{
    private const string EigenVermogen = "BEiv";
    private const string Kostprijs = "WKpr";
    private static readonly string[] VasteActiva = ["BIva", "BMva", "BVas", "BFva"];
    private static readonly string[] Opbrengsten = ["WOmz", "WWiv", "WOvb"];
    private static readonly string[] Bedrijfslasten = ["WPer", "WAfs", "WWvi", "WBwv", "WBed"];
    private static readonly string[] Financieel = ["WFbe", "WOvt", "WWfa"];

    public static Rapport Maak(RapportGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var ib = g.Soort == RapportSoort.AangifteBijlage;
        var balans = Balans(g);
        var hoofdstukken = new List<Hoofdstuk>
        {
            new(1, $"Balans per 31 december {g.Boekjaar}", [.. balans.Select(t => new TabelBlok(t))]),
            new(2, $"Winst-en-verliesrekening over {g.Boekjaar}", [new TabelBlok(WinstEnVerlies(g))]),
            new(3, ib ? "Specificaties van de balans" : "Toelichting op de balans", Specificaties(g, balans: true), Paragrafen: true),
            new(4, ib ? "Specificaties van de winst-en-verliesrekening" : "Toelichting op de winst-en-verliesrekening", Specificaties(g, balans: false), Paragrafen: true),
        };

        return new Rapport(
            g.Soort,
            ib ? $"Bijlage bij de aangifte inkomstenbelasting {g.Boekjaar}" : $"Jaarrekening {g.Boekjaar}",
            g.Klantnaam,
            g.Plaats,
            g.Boekjaar,
            ib ? "Geen accountantscontrole toegepast" : string.Empty,
            g.Concept,
            [new Deel($"JAARREKENING {g.Boekjaar}", hoofdstukken)],
            Waarschuwingen(g, balans));
    }

    /// <summary>Activa en passiva, elk in een tweekolomstabel: per post het totaal, met de subposten als detail.</summary>
    public static IReadOnlyList<Tabel> Balans(RapportGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var kopHuidig = $"31 december {g.Boekjaar}";
        var kopVorig = $"31 december {g.Boekjaar - 1}";
        var posten = MetSaldo(g, g.Schema.Hoofdposten.Where(p => p.IsBalans)).ToList();
        var activaPosten = posten.Where(p => p.Kant != Saldokant.Credit).ToList();

        var activa = new List<Regel>();
        foreach (var (kop, groep) in new[]
        {
            ("Vaste activa", activaPosten.Where(p => VasteActiva.Contains(p.Code)).ToList()),
            ("Vlottende activa", activaPosten.Where(p => !VasteActiva.Contains(p.Code)).ToList()),
        })
        {
            if (groep.Count > 0)
            {
                activa.Add(new Regel(RegelSoort.Kop, kop));
                activa.AddRange(groep.SelectMany(p => PostMetDetails(g, p)));
            }
        }

        if (g.Balans.Ongekoppeld.Aantal > 0)
        {
            activa.Add(new Regel(RegelSoort.Post, "Nog niet gerubriceerd", Totaal: g.Balans.Ongekoppeld.Huidig, TotaalVorig: g.Balans.Ongekoppeld.Vorig));
        }

        activa.Add(Eindtotaal("Totaal activa", activa));

        var passiva = posten.Where(p => p.Kant == Saldokant.Credit).SelectMany(p => PostMetDetails(g, p)).ToList();
        if (!posten.Any(p => p.Code == EigenVermogen) && HeeftResultaat(g))
        {
            passiva.Insert(0, new Regel(RegelSoort.Post, g.Naam(EigenVermogen), Totaal: g.Balans.ResultaatHuidig, TotaalVorig: g.Balans.ResultaatVorig));
        }

        passiva.Add(Eindtotaal("Totaal passiva", passiva));

        return
        [
            new Tabel("Activa", true, kopHuidig, kopVorig, activa),
            new Tabel("Passiva", true, kopHuidig, kopVorig, passiva),
        ];
    }

    /// <summary>
    /// De W&amp;V met brutomarge (art. 2:397 lid 3 BW): opbrengsten min kostprijs, de bedrijfslasten, het
    /// bedrijfsresultaat, de financiële baten en lasten en daarna de overige posten, zoals de belastingen. Het
    /// eindtotaal is het resultaat van de proefbalans.
    /// </summary>
    public static Tabel WinstEnVerlies(RapportGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var posten = MetSaldo(g, g.Schema.Hoofdposten.Where(p => !p.IsBalans)).Select(p => p.Code).ToList();
        var regels = new List<Regel>();

        // Bijdrage aan het resultaat (credit positief) en last (debet positief).
        (decimal Huidig, decimal Vorig) Bijdrage(string code) => (-g.Balans.Totaal(code).Huidig, -g.Balans.Totaal(code).Vorig);
        (decimal Huidig, decimal Vorig) Last(string code) => (g.Balans.Totaal(code).Huidig, g.Balans.Totaal(code).Vorig);

        var opbrengsten = Opbrengsten.Append(Kostprijs).Where(posten.Contains).ToList();
        var bruto = Som(opbrengsten.Select(Bijdrage));
        if (opbrengsten.Count == 1)
        {
            regels.Add(new Regel(RegelSoort.Post, g.Naam(opbrengsten[0]), Totaal: bruto.Huidig, TotaalVorig: bruto.Vorig));
        }
        else if (opbrengsten.Count > 1)
        {
            // De kostprijs staat als last (positief) onder de opbrengsten.
            regels.AddRange(opbrengsten.Select(c => c == Kostprijs
                ? new Regel(RegelSoort.Detail, g.Naam(c), Last(c).Huidig, null, Last(c).Vorig)
                : new Regel(RegelSoort.Detail, g.Naam(c), Bijdrage(c).Huidig, null, Bijdrage(c).Vorig)));
            regels.Add(new Regel(RegelSoort.Subtotaal, opbrengsten.Contains(Kostprijs) ? "Brutomarge" : "Som der bedrijfsopbrengsten", Totaal: bruto.Huidig, TotaalVorig: bruto.Vorig));
        }

        var lasten = Bedrijfslasten.Where(posten.Contains).ToList();
        var somLasten = Som(lasten.Select(Last));
        if (lasten.Count > 0)
        {
            regels.Add(new Regel(RegelSoort.Kop, "Bedrijfslasten"));
            regels.AddRange(lasten.Select(c => new Regel(RegelSoort.Detail, g.Naam(c), Last(c).Huidig, null, Last(c).Vorig)));
            regels.Add(new Regel(RegelSoort.Subtotaal, "Som der bedrijfslasten", Totaal: somLasten.Huidig, TotaalVorig: somLasten.Vorig));
        }

        var bedrijfsresultaat = (Huidig: bruto.Huidig - somLasten.Huidig, Vorig: bruto.Vorig - somLasten.Vorig);
        regels.Add(new Regel(RegelSoort.Subtotaal, "Bedrijfsresultaat", Totaal: bedrijfsresultaat.Huidig, TotaalVorig: bedrijfsresultaat.Vorig));

        var financieel = Financieel.Where(posten.Contains).ToList();
        regels.AddRange(financieel.Select(c => new Regel(RegelSoort.Post, g.Naam(c), Totaal: Bijdrage(c).Huidig, TotaalVorig: Bijdrage(c).Vorig)));
        var overig = posten.Except(opbrengsten).Except(lasten).Except(financieel).ToList();
        if (financieel.Count > 0 && overig.Count > 0)
        {
            var fin = Som(financieel.Select(Bijdrage));
            regels.Add(new Regel(RegelSoort.Subtotaal, "Resultaat voor belastingen", Totaal: bedrijfsresultaat.Huidig + fin.Huidig, TotaalVorig: bedrijfsresultaat.Vorig + fin.Vorig));
        }

        regels.AddRange(overig.Select(c => new Regel(RegelSoort.Post, g.Naam(c), Totaal: Bijdrage(c).Huidig, TotaalVorig: Bijdrage(c).Vorig)));
        var naam = g.Soort == RapportSoort.Jaarrekening && overig.Count > 0 ? "Resultaat na belastingen" : "Resultaat";
        regels.Add(new Regel(RegelSoort.Eindtotaal, naam, Totaal: g.Balans.ResultaatHuidig, TotaalVorig: g.Balans.ResultaatVorig));
        return new Tabel(null, true, Jaar(g.Boekjaar), Jaar(g.Boekjaar - 1), regels);
    }

    /// <summary>
    /// Per hoofdpost met saldo een specificatie: per subpost de grootboekrekeningen, eigen posten als één regel en
    /// verborgen rekeningen samen als "Overige"; subtotalen per subpost en het totaal van de post.
    /// </summary>
    public static IReadOnlyList<Blok> Specificaties(RapportGegevens g, bool balans)
    {
        ArgumentNullException.ThrowIfNull(g);
        var kopHuidig = balans ? $"31-12-{g.Boekjaar}" : Jaar(g.Boekjaar);
        var kopVorig = balans ? $"31-12-{g.Boekjaar - 1}" : Jaar(g.Boekjaar - 1);
        var perSubpost = BedragenPerSubpost(g);
        var blokken = new List<Blok>();
        foreach (var post in MetSaldo(g, g.Schema.Hoofdposten.Where(p => p.IsBalans == balans)))
        {
            if (g.ZonderSpecificatie.Contains(post.Code))
            {
                continue;
            }

            var teken = Teken(post);
            var regels = new List<Regel>();
            var subposten = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3)).ToList();
            foreach (var sub in subposten)
            {
                var rijen = Rijen(g, sub, perSubpost.GetValueOrDefault(sub.Code) ?? [], teken);
                if (subposten.Count > 1)
                {
                    regels.Add(new Regel(RegelSoort.Kop, g.Naam(sub.Code)));
                }

                regels.AddRange(rijen);
                if (subposten.Count > 1 && rijen.Count > 1)
                {
                    regels.Add(new Regel(RegelSoort.Subtotaal, string.Empty, rijen.Sum(r => r.Detail ?? 0), null, rijen.Sum(r => r.DetailVorig ?? 0)));
                }
            }

            var huidig = teken * g.Balans.Totaal(post.Code).Huidig;
            var vorig = teken * g.Balans.Totaal(post.Code).Vorig;
            if (balans && post.Code == EigenVermogen && HeeftResultaat(g))
            {
                // Bij meerdere groepen staat het resultaat op het niveau van de groepen, niet ingesprongen onder de laatste.
                regels.Add(subposten.Count > 1
                    ? new Regel(RegelSoort.Post, "Resultaat boekjaar", Totaal: g.Balans.ResultaatHuidig, TotaalVorig: g.Balans.ResultaatVorig)
                    : new Regel(RegelSoort.Detail, "Resultaat boekjaar", g.Balans.ResultaatHuidig, null, g.Balans.ResultaatVorig));
                huidig += g.Balans.ResultaatHuidig;
                vorig += g.Balans.ResultaatVorig;
            }

            regels.Add(new Regel(RegelSoort.Eindtotaal, "Totaal", huidig, null, vorig));
            blokken.Add(new TabelBlok(new Tabel(g.Naam(post.Code), false, kopHuidig, kopVorig, regels, post.Code)));
        }

        if (blokken.Count == 0)
        {
            blokken.Add(new TekstBlok("Er zijn geen posten met een saldo."));
        }

        return blokken;
    }

    /// <summary>De specificatieregels van één subpost: rekeningen, eigen posten (als één regel) en "Overige".</summary>
    private static List<Regel> Rijen(RapportGegevens g, RgsCode subpost, List<Bedrag> bedragen, int teken)
    {
        bool Verborgen(Bedrag b) => g.Weergave.GetValueOrDefault(b.Rekening.RekeningId) is { Verbergen: true };

        var rijen = new List<Regel>();
        foreach (var rekening in bedragen.Where(b => b.EigenPostId is null && !Verborgen(b)).GroupBy(b => b.Rekening).OrderBy(r => r.Key.Nummer, StringComparer.Ordinal))
        {
            var omschrijving = g.Weergave.GetValueOrDefault(rekening.Key.RekeningId)?.Omschrijving ?? rekening.Key.Omschrijving;
            rijen.Add(new Regel(RegelSoort.Detail, omschrijving, teken * rekening.Sum(b => b.Huidig), null, teken * rekening.Sum(b => b.Vorig)));
        }

        foreach (var eigen in bedragen.Where(b => b.EigenPostId is not null).GroupBy(b => b.EigenPostId!.Value))
        {
            var naam = g.EigenPosten.FirstOrDefault(e => e.Id == eigen.Key)?.Naam ?? subpost.Omschrijving;
            rijen.Add(new Regel(RegelSoort.Detail, naam, teken * eigen.Sum(b => b.Huidig), null, teken * eigen.Sum(b => b.Vorig)));
        }

        var verborgen = bedragen.Where(b => b.EigenPostId is null && Verborgen(b)).ToList();
        if (verborgen.Count > 0)
        {
            rijen.Add(new Regel(RegelSoort.Detail, "Overige", teken * verborgen.Sum(b => b.Huidig), null, teken * verborgen.Sum(b => b.Vorig)));
        }

        return [.. rijen.Where(r => r.Detail != 0 || r.DetailVorig != 0)];
    }

    private sealed record Bedrag(RekeningSaldo Rekening, Guid? EigenPostId, decimal Huidig, decimal Vorig);

    /// <summary>
    /// Per subpost (niveau 3) de bedragen van de rekeningen die er staan. Het boekjaar en het vorige jaar apart,
    /// want door omslag kan een rekening per jaar bij een andere post staan.
    /// </summary>
    private static Dictionary<string, List<Bedrag>> BedragenPerSubpost(RapportGegevens g)
    {
        var eigen = g.EigenPosten.ToDictionary(e => PostBalans.EigenSleutel(e.Id));
        var resultaat = new Dictionary<string, List<Bedrag>>(StringComparer.Ordinal);

        void Voeg(RekeningSaldo rekening, string? sleutel, decimal huidig, decimal vorig)
        {
            if (sleutel is null || (huidig == 0 && vorig == 0))
            {
                return;
            }

            Guid? eigenId = null;
            var code = sleutel;
            if (eigen.TryGetValue(sleutel, out var post))
            {
                eigenId = post.Id;
                code = post.OuderCode;
            }

            var subpost = g.Schema.Zoek(code) is { Niveau: 3 } ? code : g.Schema.Voorouders(code).FirstOrDefault(c => c.Niveau == 3)?.Code;
            if (subpost is null)
            {
                return;
            }

            if (!resultaat.TryGetValue(subpost, out var lijst))
            {
                lijst = [];
                resultaat[subpost] = lijst;
            }

            lijst.Add(new Bedrag(rekening, eigenId, huidig, vorig));
        }

        foreach (var rekening in g.Rekeningen)
        {
            var huidig = g.Balans.Plaatsing.GetValueOrDefault(rekening.RekeningId);
            var vorig = g.Balans.PlaatsingVorig.GetValueOrDefault(rekening.RekeningId);
            if (huidig == vorig)
            {
                Voeg(rekening, huidig, rekening.Huidig ?? 0, rekening.Vorig ?? 0);
            }
            else
            {
                Voeg(rekening, huidig, rekening.Huidig ?? 0, 0);
                Voeg(rekening, vorig, 0, rekening.Vorig ?? 0);
            }
        }

        return resultaat;
    }

    /// <summary>Een balanspost met zijn totaal, gevolgd door de subposten als detail (bij meer dan één subpost).</summary>
    private static List<Regel> PostMetDetails(RapportGegevens g, RgsCode post)
    {
        var teken = Teken(post);
        var details = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3))
            .Select(c => new Regel(RegelSoort.Detail, g.Naam(c.Code), teken * g.Balans.Totaal(c.Code).Huidig, null, teken * g.Balans.Totaal(c.Code).Vorig))
            .ToList();
        var huidig = teken * g.Balans.Totaal(post.Code).Huidig;
        var vorig = teken * g.Balans.Totaal(post.Code).Vorig;
        if (post.Code == EigenVermogen && HeeftResultaat(g))
        {
            details.Add(new Regel(RegelSoort.Detail, "Resultaat boekjaar", g.Balans.ResultaatHuidig, null, g.Balans.ResultaatVorig));
            huidig += g.Balans.ResultaatHuidig;
            vorig += g.Balans.ResultaatVorig;
        }

        return [new Regel(RegelSoort.Post, g.Naam(post.Code), Totaal: huidig, TotaalVorig: vorig), .. details.Count > 1 ? details : []];
    }

    private static List<string> Waarschuwingen(RapportGegevens g, IReadOnlyList<Tabel> balans)
    {
        var waarschuwingen = new List<string>();
        if (g.Balans.Ongekoppeld.Aantal > 0)
        {
            waarschuwingen.Add($"{g.Balans.Ongekoppeld.Aantal} rekening(en) met saldo zijn nog niet gerubriceerd; ze staan samen als \"Nog niet gerubriceerd\" op de balans.");
        }

        var onbekend = g.Balans.Signalen.Count(s => s.Soort == SignaalSoort.OnbekendeCode);
        if (onbekend > 0)
        {
            waarschuwingen.Add($"{onbekend} rekening(en) zijn gekoppeld aan een RGS-code die niet (meer) in het schema staat; de posten zijn onvolledig.");
        }

        var activa = balans[0].Regels[^1];
        var passiva = balans[1].Regels[^1];
        if (activa.Totaal != passiva.Totaal || activa.TotaalVorig != passiva.TotaalVorig)
        {
            waarschuwingen.Add($"De balans sluit niet: activa {Bedragen.Heel(activa.Totaal ?? 0)} en passiva {Bedragen.Heel(passiva.Totaal ?? 0)} " +
                $"(vorig jaar {Bedragen.Heel(activa.TotaalVorig ?? 0)} en {Bedragen.Heel(passiva.TotaalVorig ?? 0)}).");
        }

        return waarschuwingen;
    }

    private static Regel Eindtotaal(string omschrijving, List<Regel> regels) =>
        new(RegelSoort.Eindtotaal, omschrijving,
            Totaal: regels.Where(r => r.Soort == RegelSoort.Post).Sum(r => r.Totaal ?? 0),
            TotaalVorig: regels.Where(r => r.Soort == RegelSoort.Post).Sum(r => r.TotaalVorig ?? 0));

    private static (decimal Huidig, decimal Vorig) Som(IEnumerable<(decimal Huidig, decimal Vorig)> bedragen)
    {
        var lijst = bedragen.ToList();
        return (lijst.Sum(b => b.Huidig), lijst.Sum(b => b.Vorig));
    }

    private static bool HeeftResultaat(RapportGegevens g) => g.Balans.ResultaatHuidig != 0 || g.Balans.ResultaatVorig != 0;

    /// <summary>Gepresenteerd teken: debetposten (activa, kosten) zoals ze zijn, creditposten (passiva, opbrengsten) omgekeerd.</summary>
    private static int Teken(RgsCode post) => post.Kant == Saldokant.Credit ? -1 : 1;

    private static IEnumerable<RgsCode> MetSaldo(RapportGegevens g, IEnumerable<RgsCode> codes) =>
        codes.Where(c => g.Balans.Totaal(c.Code) is { Aantal: > 0 } t && (t.Huidig != 0 || t.Vorig != 0));

    private static string Jaar(int jaar) => jaar.ToString(CultureInfo.InvariantCulture);
}
