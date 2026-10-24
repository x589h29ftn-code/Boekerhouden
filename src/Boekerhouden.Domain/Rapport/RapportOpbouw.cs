using System.Globalization;
using System.Text.RegularExpressions;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Rapport;

/// <summary>Hoe een grootboekrekening in de specificaties staat: een eigen omschrijving, of verborgen (telt mee in "Overige").</summary>
public sealed record RekeningWeergave(string? Omschrijving, bool Verbergen);

/// <summary>Eén regel van de uitsplitsing van een rekening; bedragen zoals in het rapport (positief aan de normale kant).</summary>
public sealed record SplitsRegel(string Omschrijving, decimal Bedrag, decimal? BedragVorig);

/// <param name="Balans">De proefbalans per post, na correcties, in hele euro's (zoals <c>DossierSaldi</c> die levert).</param>
/// <param name="Naam">De naam van een post in het rapport (klantnaam, kantoornaam of RGS-omschrijving).</param>
/// <param name="ZonderSpecificatie">Hoofdposten (codes) waarvan de specificatie niet in het rapport komt.</param>
/// <param name="Uitklappen">Per post de stand uitgeklapt/ingeklapt; ontbreekt hij, dan de standaard.</param>
/// <param name="Toelichtingen">Per hoofdpost een toelichtende tekst onder de specificatie.</param>
/// <param name="Uitsplitsingen">Per rekening de regels waarin hij in de specificatie is uitgesplitst.</param>
/// <param name="Grondslagen">De grondslagen voor de jaarrekening (bibliotheek, al met de tekst voor commercieel of fiscaal); null = geen hoofdstuk.</param>
/// <param name="GrondslagKeuzes">Per grondslag de keuze in het dossier: opgenomen of niet, en een eigen tekst.</param>
/// <param name="Kvk">Het KvK-nummer van de klant, voor de variabele {kvk}.</param>
/// <param name="Verlopen">Per post (RGS-code) of rekening ("rek:&lt;id&gt;") of er een verloopoverzicht komt; ontbreekt hij, dan de standaard.</param>
/// <param name="VerloopMutaties">Per verloopoverzicht de aangepaste mutaties; ontbreekt hij, dan het voorstel uit de saldi.</param>
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
    IReadOnlySet<string> ZonderSpecificatie,
    IReadOnlyDictionary<string, bool>? Uitklappen = null,
    IReadOnlyDictionary<string, string>? Toelichtingen = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<SplitsRegel>>? Uitsplitsingen = null,
    IReadOnlyList<Grondslag>? Grondslagen = null,
    IReadOnlyDictionary<string, GrondslagKeuze>? GrondslagKeuzes = null,
    string? Kvk = null,
    IReadOnlyDictionary<string, bool>? Verlopen = null,
    IReadOnlyDictionary<string, IReadOnlyList<VerloopMutatie>>? VerloopMutaties = null);

/// <summary>
/// Bouwt het rapport (plakje 6, docs §9.1–9.3) uit de proefbalans per post: de balans (tweekoloms, met het resultaat
/// als aparte regel in het eigen vermogen), de winst-en-verliesrekening met brutomarge, en per post een specificatie
/// uit de grootboekrekeningen. Bedragen staan positief aan de normale kant van de post. Posten zijn in en uit te
/// klappen (balans standaard uit-, W&amp;V standaard ingeklapt), met per post een toelichting en per rekening een
/// uitsplitsing die moet aansluiten op het saldo.
/// </summary>
public static partial class RapportOpbouw
{
    private const string EigenVermogen = "BEiv";
    private const string Kostprijs = "WKpr";
    private static readonly string[] VasteActiva = ["BIva", "BMva", "BVas", "BFva"];
    private static readonly string[] Opbrengsten = ["WOmz", "WWiv", "WOvb"];
    private static readonly string[] Bedrijfslasten = ["WPer", "WAfs", "WWvi", "WBwv", "WBed"];
    private static readonly string[] Financieel = ["WFbe", "WOvt", "WWfa"];

    /// <summary>De regel voor het deel van een uitsplitsing dat niet aansluit op het saldo.</summary>
    public const string NietGespecificeerd = "Nog niet gespecificeerd";

    public static Rapport Maak(RapportGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var ib = g.Soort == RapportSoort.AangifteBijlage;
        var balans = Balans(g);
        var waarschuwingen = Waarschuwingen(g, balans);
        var hoofdstukken = new List<Hoofdstuk>
        {
            new(1, $"Balans per 31 december {g.Boekjaar}", [.. balans.Select(t => new TabelBlok(t))]),
            new(2, $"Winst-en-verliesrekening over {g.Boekjaar}", [new TabelBlok(WinstEnVerlies(g))]),
        };

        // De grondslagen staan in de jaarrekening van de BV tussen de W&V en de toelichting (docs §9.1).
        if (!ib && g.Grondslagen is not null)
        {
            hoofdstukken.Add(new Hoofdstuk(hoofdstukken.Count + 1, "Algemene toelichting en grondslagen", Grondslagen(g), Paragrafen: true));
        }

        hoofdstukken.Add(new Hoofdstuk(hoofdstukken.Count + 1, ib ? "Specificaties van de balans" : "Toelichting op de balans", Specificaties(g, balans: true, waarschuwingen), Paragrafen: true));
        hoofdstukken.Add(new Hoofdstuk(hoofdstukken.Count + 1, ib ? "Specificaties van de winst-en-verliesrekening" : "Toelichting op de winst-en-verliesrekening", Specificaties(g, balans: false, waarschuwingen), Paragrafen: true));

        // Variabelen in de teksten invullen; daarna pas controleren wat nog moet worden ingevuld.
        var variabelen = RapportVariabelen.Bepaal(g, balans);
        var onbekend = new SortedSet<string>(StringComparer.Ordinal);
        string Vul(string tekst) => RapportVariabelen.Vul(tekst, g, variabelen, onbekend);
        Blok VulIn(Blok blok) => blok switch
        {
            GrondslagBlok b when Vul(b.Tekst) is var t && t != b.Tekst => b with { Tekst = t, Sjabloon = b.Tekst },
            TekstBlok b when Vul(b.Tekst) is var t && t != b.Tekst => b with { Tekst = t, Sjabloon = b.Tekst },
            _ => blok,
        };
        hoofdstukken = [.. hoofdstukken.Select(h => h with { Blokken = [.. h.Blokken.Select(VulIn)] })];
        var teksten = hoofdstukken.SelectMany(h => h.Blokken).ToList();
        waarschuwingen.AddRange(teksten.OfType<GrondslagBlok>().Where(b => b.Opgenomen && InTeVullen().IsMatch(b.Tekst))
            .Select(b => $"{b.Titel}: tekst tussen [ ] nog invullen."));
        waarschuwingen.AddRange(teksten.OfType<TekstBlok>().Where(b => b.Toelichting is not null && InTeVullen().IsMatch(b.Tekst))
            .Select(b => $"Toelichting {g.Naam(b.Toelichting!)}: tekst tussen [ ] nog invullen."));
        waarschuwingen.AddRange(onbekend.Select(v => $"Onbekende variabele {v} in een tekst."));

        return new Rapport(
            g.Soort,
            ib ? $"Bijlage bij de aangifte inkomstenbelasting {g.Boekjaar}" : $"Jaarrekening {g.Boekjaar}",
            g.Klantnaam,
            g.Plaats,
            g.Boekjaar,
            ib ? "Geen accountantscontrole toegepast" : string.Empty,
            g.Concept,
            [new Deel($"JAARREKENING {g.Boekjaar}", hoofdstukken)],
            waarschuwingen)
        {
            Variabelen = variabelen,
        };
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
                activa.AddRange(groep.SelectMany(p => PostMetDetails(g, p, standaardUit: true)));
            }
        }

        if (g.Balans.Ongekoppeld.Aantal > 0)
        {
            activa.Add(new Regel(RegelSoort.Post, "Nog niet gerubriceerd", Totaal: g.Balans.Ongekoppeld.Huidig, TotaalVorig: g.Balans.Ongekoppeld.Vorig));
        }

        activa.Add(Eindtotaal("Totaal activa", activa));

        var passiva = posten.Where(p => p.Kant == Saldokant.Credit).SelectMany(p => PostMetDetails(g, p, standaardUit: true)).ToList();
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
    /// De W&amp;V met brutomarge (art. 2:397 lid 3 BW): opbrengsten en kostprijs, de bedrijfslasten, het
    /// bedrijfsresultaat, de financiële baten en lasten en daarna de overige posten, zoals de belastingen. Elke post
    /// staat met zijn totaal in de rechterkolom en is uit te klappen naar zijn subposten. Het eindtotaal is het
    /// resultaat van de proefbalans.
    /// </summary>
    public static Tabel WinstEnVerlies(RapportGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var posten = MetSaldo(g, g.Schema.Hoofdposten.Where(p => !p.IsBalans)).ToDictionary(p => p.Code);
        var regels = new List<Regel>();

        // Bijdrage aan het resultaat (credit positief) en last (debet positief).
        (decimal Huidig, decimal Vorig) Bijdrage(string code) => (-g.Balans.Totaal(code).Huidig, -g.Balans.Totaal(code).Vorig);
        (decimal Huidig, decimal Vorig) Last(string code) => (g.Balans.Totaal(code).Huidig, g.Balans.Totaal(code).Vorig);
        IEnumerable<Regel> Post(string code, int teken) => PostMetDetails(g, posten[code], standaardUit: false, teken);

        var opbrengsten = Opbrengsten.Append(Kostprijs).Where(posten.ContainsKey).ToList();
        var bruto = Som(opbrengsten.Select(Bijdrage));
        foreach (var code in opbrengsten)
        {
            // De kostprijs staat als last (positief) onder de opbrengsten.
            regels.AddRange(Post(code, code == Kostprijs ? 1 : -1));
        }

        if (opbrengsten.Count > 1)
        {
            regels.Add(new Regel(RegelSoort.Subtotaal, opbrengsten.Contains(Kostprijs) ? "Brutomarge" : "Som der bedrijfsopbrengsten", Totaal: bruto.Huidig, TotaalVorig: bruto.Vorig));
        }

        var lasten = Bedrijfslasten.Where(posten.ContainsKey).ToList();
        var somLasten = Som(lasten.Select(Last));
        if (lasten.Count > 0)
        {
            regels.Add(new Regel(RegelSoort.Kop, "Bedrijfslasten"));
            regels.AddRange(lasten.SelectMany(c => Post(c, 1)));
            regels.Add(new Regel(RegelSoort.Subtotaal, "Som der bedrijfslasten", Totaal: somLasten.Huidig, TotaalVorig: somLasten.Vorig));
        }

        var bedrijfsresultaat = (Huidig: bruto.Huidig - somLasten.Huidig, Vorig: bruto.Vorig - somLasten.Vorig);
        regels.Add(new Regel(RegelSoort.Subtotaal, "Bedrijfsresultaat", Totaal: bedrijfsresultaat.Huidig, TotaalVorig: bedrijfsresultaat.Vorig));

        var financieel = Financieel.Where(posten.ContainsKey).ToList();
        regels.AddRange(financieel.SelectMany(c => Post(c, -1)));
        var overig = posten.Keys.Except(opbrengsten).Except(lasten).Except(financieel).ToList();
        if (financieel.Count > 0 && overig.Count > 0)
        {
            var fin = Som(financieel.Select(Bijdrage));
            regels.Add(new Regel(RegelSoort.Subtotaal, "Resultaat voor belastingen", Totaal: bedrijfsresultaat.Huidig + fin.Huidig, TotaalVorig: bedrijfsresultaat.Vorig + fin.Vorig));
        }

        regels.AddRange(overig.SelectMany(c => Post(c, -1)));
        var naam = g.Soort == RapportSoort.Jaarrekening && overig.Count > 0 ? "Resultaat na belastingen" : "Resultaat";
        regels.Add(new Regel(RegelSoort.Eindtotaal, naam, Totaal: g.Balans.ResultaatHuidig, TotaalVorig: g.Balans.ResultaatVorig));
        return new Tabel(null, true, Jaar(g.Boekjaar), Jaar(g.Boekjaar - 1), regels);
    }

    /// <summary>
    /// De algemene toelichting en de grondslagen per groep (algemene toelichting, algemene grondslagen, waardering,
    /// resultaatbepaling). Een grondslag bij een post staat er als die post een saldo heeft; algemene grondslagen staan
    /// er altijd; optionele (bijv. foutenherstel) staan dicht, tenzij een van hun posten een saldo heeft. De keuze in
    /// het dossier gaat voor: open of dicht, en een eigen tekst. Dichte grondslagen ziet alleen de viewer.
    /// </summary>
    public static IReadOnlyList<Blok> Grondslagen(RapportGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var blokken = new List<Blok>();
        foreach (var groep in (g.Grondslagen ?? []).GroupBy(x => x.Groep))
        {
            var items = new List<GrondslagBlok>();
            foreach (var grondslag in groep)
            {
                var keuze = g.GrondslagKeuzes?.GetValueOrDefault(grondslag.Sleutel);
                var metSaldo = grondslag.Codes.Any(c => HeeftSaldo(g, c));
                if (grondslag.Standaard == GrondslagStandaard.BijPost && !metSaldo && keuze?.Opgenomen != true)
                {
                    continue;
                }

                var standaard = grondslag.Standaard != GrondslagStandaard.Optioneel || metSaldo;
                var eigen = !string.IsNullOrWhiteSpace(keuze?.Tekst);
                items.Add(new GrondslagBlok(grondslag.Sleutel, grondslag.Titel, eigen ? keuze!.Tekst! : grondslag.Tekst, keuze?.Opgenomen ?? standaard, eigen));
            }

            if (items.Count > 0)
            {
                blokken.Add(new GroepBlok(groep.Key, items.Any(i => i.Opgenomen)));
                blokken.AddRange(items);
            }
        }

        return blokken;
    }

    /// <summary>
    /// Per hoofdpost met saldo een specificatie: per subpost de grootboekrekeningen (of hun uitsplitsing), eigen
    /// posten als één regel en verborgen rekeningen samen als "Overige"; subtotalen per subpost en het totaal van de
    /// post. Een subpost is in te klappen tot één regel. Daaronder de toelichting van de post, als die er is.
    /// </summary>
    /// <param name="waarschuwingen">Hier komen uitsplitsingen bij die niet aansluiten op het saldo.</param>
    public static IReadOnlyList<Blok> Specificaties(RapportGegevens g, bool balans, ICollection<string>? waarschuwingen = null)
    {
        ArgumentNullException.ThrowIfNull(g);
        var kopHuidig = balans ? $"31-12-{g.Boekjaar}" : Jaar(g.Boekjaar);
        var kopVorig = balans ? $"31-12-{g.Boekjaar - 1}" : Jaar(g.Boekjaar - 1);
        var perSubpost = BedragenPerSubpost(g);
        var blokken = new List<Blok>();
        foreach (var post in MetSaldo(g, g.Schema.Hoofdposten.Where(p => p.IsBalans == balans)))
        {
            var toelichting = g.Toelichtingen?.GetValueOrDefault(post.Code);
            if (g.ZonderSpecificatie.Contains(post.Code))
            {
                if (!string.IsNullOrWhiteSpace(toelichting))
                {
                    blokken.Add(new TekstBlok(toelichting) { Toelichting = post.Code, Titel = g.Naam(post.Code) });
                }

                continue;
            }

            var teken = Teken(post);
            var regels = new List<Regel>();
            var subposten = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3)).ToList();
            foreach (var sub in subposten)
            {
                var rijen = Rijen(g, sub, perSubpost.GetValueOrDefault(sub.Code) ?? [], teken, waarschuwingen);
                if (subposten.Count == 1)
                {
                    regels.AddRange(rijen);
                    continue;
                }

                var klapbaar = rijen.Count > 1;
                if (klapbaar && !Uitgeklapt(g, sub.Code, standaard: true))
                {
                    regels.Add(new Regel(RegelSoort.Detail, g.Naam(sub.Code), rijen.Sum(r => r.Detail ?? 0), null, rijen.Sum(r => r.DetailVorig ?? 0))
                    {
                        Bron = PostBron(g, sub.Code),
                        Uitgeklapt = false,
                    });
                    continue;
                }

                regels.Add(new Regel(RegelSoort.Kop, g.Naam(sub.Code)) { Bron = PostBron(g, sub.Code), Uitgeklapt = klapbaar ? true : null });
                regels.AddRange(rijen);
                if (klapbaar)
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
            blokken.Add(new TabelBlok(new Tabel(g.Naam(post.Code), false, kopHuidig, kopVorig, regels, post.Code) { Bron = PostBron(g, post.Code) }));
            if (!string.IsNullOrWhiteSpace(toelichting))
            {
                blokken.Add(new TekstBlok(toelichting) { Toelichting = post.Code });
            }

            if (balans)
            {
                blokken.AddRange(Verlopen(g, post, perSubpost, waarschuwingen).Select(v => new VerloopBlok(v)));
            }
        }

        if (blokken.Count == 0)
        {
            blokken.Add(new TekstBlok("Er zijn geen posten met een saldo."));
        }

        return blokken;
    }

    /// <summary>
    /// De specificatieregels van één subpost: rekeningen (of hun uitsplitsing), eigen posten (als één regel) en
    /// "Overige" voor verborgen rekeningen.
    /// </summary>
    private static List<Regel> Rijen(RapportGegevens g, RgsCode subpost, List<Bedrag> bedragen, int teken, ICollection<string>? waarschuwingen)
    {
        bool Verborgen(Bedrag b) => g.Weergave.GetValueOrDefault(b.Rekening.RekeningId) is { Verbergen: true };

        var rijen = new List<Regel>();
        foreach (var rekening in bedragen.Where(b => b.EigenPostId is null && !Verborgen(b)).GroupBy(b => b.Rekening).OrderBy(r => r.Key.Nummer, StringComparer.Ordinal))
        {
            var r = rekening.Key;
            var omschrijving = g.Weergave.GetValueOrDefault(r.RekeningId)?.Omschrijving ?? r.Omschrijving;
            var huidig = teken * rekening.Sum(b => b.Huidig);
            var vorig = teken * rekening.Sum(b => b.Vorig);

            // Een uitsplitsing hoort bij de plek waar de rekening dit jaar staat (bij omslag kan vorig jaar elders staan).
            if (g.Uitsplitsingen?.GetValueOrDefault(r.RekeningId) is { Count: > 0 } splitsing && (huidig != 0 || (r.Huidig ?? 0) == 0))
            {
                rijen.AddRange(Uitsplitsing(r, omschrijving, splitsing, huidig, vorig, waarschuwingen));
                continue;
            }

            rijen.Add(new Regel(RegelSoort.Detail, omschrijving, huidig, null, vorig)
            {
                Bron = new Bron(Onderwerp.Rekening, r.Nummer, r.RekeningId, $"Rekening {r.Nummer} {r.Omschrijving}"),
            });
        }

        foreach (var eigen in bedragen.Where(b => b.EigenPostId is not null).GroupBy(b => b.EigenPostId!.Value))
        {
            var post = g.EigenPosten.FirstOrDefault(e => e.Id == eigen.Key);
            rijen.Add(new Regel(RegelSoort.Detail, post?.Naam ?? subpost.Omschrijving, teken * eigen.Sum(b => b.Huidig), null, teken * eigen.Sum(b => b.Vorig))
            {
                Bron = new Bron(Onderwerp.EigenPost, PostBalans.EigenSleutel(eigen.Key), eigen.Key,
                    $"Eigen post onder {post?.OuderCode} · rekening(en) {string.Join(", ", eigen.Select(b => b.Rekening.Nummer).Distinct())}"),
            });
        }

        var verborgen = bedragen.Where(b => b.EigenPostId is null && Verborgen(b)).ToList();
        if (verborgen.Count > 0)
        {
            rijen.Add(new Regel(RegelSoort.Detail, "Overige", teken * verborgen.Sum(b => b.Huidig), null, teken * verborgen.Sum(b => b.Vorig)));
        }

        return [.. rijen.Where(r => r.Detail != 0 || r.DetailVorig != 0 || r.Bron?.Soort == Onderwerp.Uitsplitsing)];
    }

    /// <summary>
    /// De regels waarin een rekening is uitgesplitst, afgerond op hele euro's zodat ze optellen tot het saldo. Sluit
    /// de uitsplitsing niet aan, dan komt het verschil op een regel "Nog niet gespecificeerd", zodat de specificatie
    /// blijft kloppen, en volgt een waarschuwing.
    /// </summary>
    private static IEnumerable<Regel> Uitsplitsing(RekeningSaldo r, string omschrijving, IReadOnlyList<SplitsRegel> splitsing, decimal huidig, decimal vorig, ICollection<string>? waarschuwingen)
    {
        var (regelsHuidig, restHuidig) = Verdeel([.. splitsing.Select(s => s.Bedrag)], huidig);
        var (regelsVorig, restVorig) = Verdeel([.. splitsing.Select(s => s.BedragVorig ?? 0)], vorig);
        var herkomst = $"Uitgesplitst uit rekening {r.Nummer} {r.Omschrijving}";
        for (var i = 0; i < splitsing.Count; i++)
        {
            yield return new Regel(RegelSoort.Detail, splitsing[i].Omschrijving, regelsHuidig[i], null, regelsVorig[i])
            {
                Bron = new Bron(Onderwerp.Uitsplitsing, r.Nummer, r.RekeningId, herkomst),
            };
        }

        if (restHuidig != 0 || restVorig != 0)
        {
            waarschuwingen?.Add($"Uitsplitsing {r.Nummer} {omschrijving} sluit niet aan: verschil {Bedragen.Heel(restHuidig)} (vorig jaar {Bedragen.Heel(restVorig)}).");
            yield return new Regel(RegelSoort.Detail, NietGespecificeerd, restHuidig, null, restVorig)
            {
                Bron = new Bron(Onderwerp.Uitsplitsing, r.Nummer, r.RekeningId, $"Verschil tussen de uitsplitsing en het saldo van rekening {r.Nummer}"),
            };
        }
    }

    /// <summary>
    /// Rondt de regels van een uitsplitsing af op hele euro's. Scheelt de som minder dan een euro met het saldo
    /// (afronding), dan gaat het verschil naar de grootste regel; anders blijft het als rest over.
    /// </summary>
    public static (decimal[] Regels, decimal Verschil) Verdeel(IReadOnlyList<decimal> bedragen, decimal saldo)
    {
        ArgumentNullException.ThrowIfNull(bedragen);
        var afgerond = Afronding.OpHeleEuros(bedragen.Select((b, i) => KeyValuePair.Create(i, b)));
        var regels = Enumerable.Range(0, bedragen.Count).Select(i => afgerond[i]).ToArray();
        var rest = saldo - regels.Sum();
        if (rest != 0 && regels.Length > 0 && Math.Abs(saldo - bedragen.Sum()) < 1)
        {
            var grootste = Array.IndexOf(regels, regels.MaxBy(Math.Abs));
            regels[grootste] += rest;
            rest = 0;
        }

        return (regels, rest);
    }

    /// <param name="Code">De RGS-code waar het bedrag staat (bij een eigen post die van de post erboven).</param>
    private sealed record Bedrag(RekeningSaldo Rekening, Guid? EigenPostId, string Code, decimal Huidig, decimal Vorig);

    /// <summary>Per subpost (niveau 3) de bedragen van de rekeningen die er staan.</summary>
    private static Dictionary<string, List<Bedrag>> BedragenPerSubpost(RapportGegevens g)
    {
        var eigen = g.EigenPosten.ToDictionary(e => PostBalans.EigenSleutel(e.Id));
        var resultaat = new Dictionary<string, List<Bedrag>>(StringComparer.Ordinal);

        void Voeg(RekeningSaldo rekening, string? sleutel, decimal bedrag, bool vorig)
        {
            if (sleutel is null || bedrag == 0)
            {
                return;
            }

            Guid? eigenId = null;
            var code = sleutel;
            if (eigen.TryGetValue(sleutel, out var post))
            {
                // Een gesaldeerde eigen post kan per jaar onder een andere RGS-post staan.
                eigenId = post.Id;
                code = g.Balans.EigenOuder(post, vorig);
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

            lijst.Add(new Bedrag(rekening, eigenId, code, vorig ? 0 : bedrag, vorig ? bedrag : 0));
        }

        // Het boekjaar en het vorige jaar apart: door omslag of saldering kan een rekening per jaar elders staan.
        foreach (var rekening in g.Rekeningen)
        {
            Voeg(rekening, g.Balans.Plaatsing.GetValueOrDefault(rekening.RekeningId), rekening.Huidig ?? 0, vorig: false);
            Voeg(rekening, g.Balans.PlaatsingVorig.GetValueOrDefault(rekening.RekeningId), rekening.Vorig ?? 0, vorig: true);
        }

        return resultaat;
    }

    /// <summary>
    /// Een post met zijn totaal, en als hij is uitgeklapt de subposten als detail. Uitklappen kan bij twee of meer
    /// subposten (bij het eigen vermogen telt het resultaat mee).
    /// </summary>
    /// <param name="teken">Het gepresenteerde teken; standaard dat van de post (debet zoals het is, credit omgekeerd).</param>
    private static List<Regel> PostMetDetails(RapportGegevens g, RgsCode post, bool standaardUit, int? teken = null)
    {
        var t = teken ?? Teken(post);
        var details = MetSaldo(g, g.Schema.Kinderen(post.Code).Where(c => c.Niveau == 3))
            .Select(c => new Regel(RegelSoort.Detail, g.Naam(c.Code), t * g.Balans.Totaal(c.Code).Huidig, null, t * g.Balans.Totaal(c.Code).Vorig) { Bron = PostBron(g, c.Code) })
            .ToList();
        var huidig = t * g.Balans.Totaal(post.Code).Huidig;
        var vorig = t * g.Balans.Totaal(post.Code).Vorig;
        if (post.Code == EigenVermogen && HeeftResultaat(g))
        {
            details.Add(new Regel(RegelSoort.Detail, "Resultaat boekjaar", g.Balans.ResultaatHuidig, null, g.Balans.ResultaatVorig));
            huidig += g.Balans.ResultaatHuidig;
            vorig += g.Balans.ResultaatVorig;
        }

        bool? uitgeklapt = details.Count > 1 ? Uitgeklapt(g, post.Code, standaardUit) : null;
        var regel = new Regel(RegelSoort.Post, g.Naam(post.Code), Totaal: huidig, TotaalVorig: vorig) { Bron = PostBron(g, post.Code), Uitgeklapt = uitgeklapt };
        return [regel, .. uitgeklapt == true ? details : []];
    }

    private static bool Uitgeklapt(RapportGegevens g, string code, bool standaard) =>
        g.Uitklappen?.TryGetValue(code, out var stand) == true ? stand : standaard;

    private static Bron PostBron(RapportGegevens g, string code) =>
        new(Onderwerp.Post, code, null, $"{code} · RGS: {g.Schema.Zoek(code)?.Omschrijving ?? code}");

    private static List<string> Waarschuwingen(RapportGegevens g, IReadOnlyList<Tabel> balans)
    {
        var waarschuwingen = new List<string>();
        if (g.Balans.Ongekoppeld.Aantal > 0)
        {
            waarschuwingen.Add($"{g.Balans.Ongekoppeld.Aantal} rekening(en) met saldo nog niet gerubriceerd.");
        }

        var onbekend = g.Balans.Signalen.Count(s => s.Soort == SignaalSoort.OnbekendeCode);
        if (onbekend > 0)
        {
            waarschuwingen.Add($"{onbekend} rekening(en) gekoppeld aan een RGS-code die niet (meer) bestaat.");
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
        codes.Where(c => HeeftSaldo(g, c.Code));

    private static bool HeeftSaldo(RapportGegevens g, string code) =>
        g.Balans.Totaal(code) is { Aantal: > 0 } t && (t.Huidig != 0 || t.Vorig != 0);

    private static string Jaar(int jaar) => jaar.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tekst tussen vierkante haken in een standaardtekst: die moet de gebruiker nog invullen.</summary>
    [GeneratedRegex(@"\[[^\]]+\]")]
    public static partial Regex InTeVullen();
}
