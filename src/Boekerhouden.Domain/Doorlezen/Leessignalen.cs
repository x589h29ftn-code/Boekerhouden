using System.Globalization;
using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Domain.Doorlezen;

/// <summary>Iets wat bij het doorlezen opvalt in de cijfers of het rapport; de gebruiker handelt het af.</summary>
/// <param name="Sleutel">Vast per soort signaal en post, bijv. "verschil:BVor"; de afhandeling hangt hieraan.</param>
/// <param name="Post">De RGS-hoofdpost waar het signaal over gaat, als dat er één is.</param>
public sealed record Leessignaal(string Sleutel, string Titel, string Tekst, string? Post = null);

/// <param name="Naam">De naam van een post zoals in het rapport.</param>
/// <param name="Grondslagen">Alle grondslagen uit het rapport (opgenomen of niet); null als er (nog) geen zijn gekozen.</param>
/// <param name="Waarschuwingen">Wat het rapport onvolledig maakt.</param>
/// <param name="Toegelicht">Hoofdposten met een cijferbeoordeling; daar is een verschil al verklaard.</param>
public sealed record LeesGegevens(
    RapportSoort Soort,
    RgsSchema Schema,
    PostBalans Balans,
    Func<string, string> Naam,
    IReadOnlyCollection<GrondslagBlok>? Grondslagen,
    IReadOnlyList<string> Waarschuwingen,
    IReadOnlySet<string> Toegelicht);

/// <summary>
/// De signalen bij het doorlezen: vaste controles op samenhang in de cijfers en het rapport. Ze zijn een hulp, geen
/// oordeel; elk signaal wordt afgehandeld als in orde (met een toelichting) of als bespreekpunt met de klant.
/// </summary>
public static class Leessignalen
{
    /// <summary>Een verschil met vorig jaar telt vanaf dit deel van het bedrag van vorig jaar.</summary>
    public const decimal VerschilPercentage = 0.25m;

    /// <summary>... en vanaf dit deel van de omvang (het grootste van balanstotaal en omzet).</summary>
    public const decimal VerschilAandeel = 0.05m;

    public const decimal MinimaalVerschil = 1_000m;

    /// <summary>Afwijking van de brutomarge ten opzichte van vorig jaar, in procentpunten (als fractie).</summary>
    public const decimal MargeAfwijking = 0.05m;

    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    private static readonly string[] VasteActiva = ["BMva", "BIva"];

    private static readonly string[] RekeningCourant = ["BVorOvrRcb", "BVorOvrRca", "BVorOvrLvb", "BVorVaoVuh", "BSchOvsRcb", "BSchOvsSaa"];

    private static readonly string[] RenteRekeningCourant = ["WFbeRlmRrb", "WFbeRlmRva", "WFbeRlsRsd", "WFbeRlsRsa"];

    public static IReadOnlyList<Leessignaal> Bepaal(LeesGegevens g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var b = g.Balans;
        var jaarrekening = g.Soort == RapportSoort.Jaarrekening;
        var signalen = new List<Leessignaal>();

        signalen.AddRange(g.Waarschuwingen.Distinct().Select(w => new Leessignaal("rapport:" + Kort(w), w, "Uit de opbouw van het rapport.")));

        var vermogen = b.Totaal("BEiv").Huidig;
        if (vermogen > 0)
        {
            var continuiteit = g.Grondslagen?.Any(x => x.Sleutel == "continuiteit" && x.Opgenomen) == true;
            if (!jaarrekening)
            {
                signalen.Add(new("negatief-vermogen", "Negatief ondernemingsvermogen",
                    $"Het ondernemingsvermogen is {Bedragen.Heel(-vermogen)}. Hoe is de financiering geregeld?", "BEiv"));
            }
            else if (!continuiteit)
            {
                signalen.Add(new("negatief-vermogen", "Negatief eigen vermogen",
                    $"Het eigen vermogen is {Bedragen.Heel(-vermogen)} en de alinea over de continuïteit staat niet in de algemene toelichting. Is de continuïteit gewaarborgd?", "BEiv"));
            }
        }

        var belasting = b.Totaal("WBel").Huidig;
        var voorBelasting = b.ResultaatHuidig + belasting;
        if (jaarrekening && voorBelasting > 0 && belasting == 0)
        {
            signalen.Add(new("winst-zonder-vpb", "Winst zonder vennootschapsbelasting",
                $"Het resultaat vóór belastingen is {Bedragen.Heel(voorBelasting)}, maar er is geen belasting geboekt. Verrekenbare verliezen, fiscale eenheid, of nog te boeken?"));
        }

        var rc = RekeningCourant.Select(c => (Code: c, Saldo: b.Totaal(c).Huidig)).Where(x => x.Saldo != 0).ToList();
        if (rc.Count > 0 && RenteRekeningCourant.All(c => b.Totaal(c).Huidig == 0))
        {
            signalen.Add(new("rc-zonder-rente", "Rekening-courant met bestuurder of aandeelhouder zonder rente",
                $"Saldo {string.Join(", ", rc.Select(x => $"{g.Naam(x.Code)} {Bedragen.Heel(Math.Abs(x.Saldo))}"))}; op de rentecodes voor bestuurders en aandeelhouders staat niets. Is er (zakelijke) rente afgesproken?",
                rc[0].Code[..4]));
        }

        var afschrijvingen = b.Totaal("WAfs").Huidig;
        var vasteActiva = VasteActiva.Select(b.Totaal).ToList();
        if (afschrijvingen != 0 && vasteActiva.All(t => t.Huidig == 0 && t.Vorig == 0))
        {
            signalen.Add(new("afschrijving-zonder-activa", "Afschrijvingen zonder vaste activa",
                $"Er is {Bedragen.Heel(afschrijvingen)} afgeschreven, maar er staan geen vaste activa op de balans.", "WAfs"));
        }
        else if (b.Totaal("BMva").Huidig > 0 && afschrijvingen == 0)
        {
            signalen.Add(new("activa-zonder-afschrijving", "Vaste activa zonder afschrijvingen",
                $"Er staan materiële vaste activa op de balans ({Bedragen.Heel(b.Totaal("BMva").Huidig)}), maar er is niets afgeschreven.", "BMva"));
        }

        if (Brutomarge(b) is ({ } margeNu, { } margeVorig) && Math.Abs(margeNu - margeVorig) >= MargeAfwijking)
        {
            signalen.Add(new("brutomarge", "Brutomarge wijkt af van vorig jaar",
                $"Brutomarge {Procent(margeNu)} tegen {Procent(margeVorig)} vorig jaar. Is het verschil verklaard (prijzen, inkoop, voorraad)?", "WKpr"));
        }

        var hoofdposten = g.Schema.Hoofdposten.Where(p => b.Totaal(p.Code) is { Aantal: > 0 } t && (t.Huidig != 0 || t.Vorig != 0)).ToList();
        var metVorigJaar = hoofdposten.Any(p => b.Totaal(p.Code).Vorig != 0);
        var omvang = Math.Max(hoofdposten.Where(p => p.IsBalans && p.Kant != Saldokant.Credit).Sum(p => Math.Max(0, b.Totaal(p.Code).Huidig)), -b.Totaal("WOmz").Huidig);
        foreach (var post in hoofdposten)
        {
            var t = b.Totaal(post.Code);
            var teken = post.Kant == Saldokant.Credit ? -1 : 1;
            var (huidig, vorig) = (teken * t.Huidig, teken * t.Vorig);
            var verschil = huidig - vorig;
            if (metVorigJaar && !g.Toegelicht.Contains(post.Code)
                && Math.Abs(verschil) >= Math.Max(MinimaalVerschil, VerschilAandeel * omvang)
                && (vorig == 0 || Math.Abs(verschil) >= VerschilPercentage * Math.Abs(vorig)))
            {
                var procent = vorig == 0 ? "nieuw" : (verschil > 0 ? "+" : "") + Procent(verschil / Math.Abs(vorig));
                signalen.Add(new($"verschil:{post.Code}", $"{g.Naam(post.Code)}: groot verschil met vorig jaar",
                    $"{Bedragen.Heel(huidig)} tegen {Bedragen.Heel(vorig)} vorig jaar ({procent}). Waar komt het verschil vandaan?", post.Code));
            }

            if (post.IsBalans && post.Code != "BEiv" && !post.IsNormaleKant(t.Huidig))
            {
                signalen.Add(new($"andere-kant:{post.Code}", $"{g.Naam(post.Code)} staat aan de andere kant",
                    $"Het saldo is {(t.Huidig > 0 ? "debet" : "credit")} ({Bedragen.Heel(Math.Abs(t.Huidig))}). Hoort het bij deze post, of bij een post aan de andere kant van de balans?", post.Code));
            }

            if (jaarrekening && g.Grondslagen is { } grondslagen && post.IsBalans && t.Huidig != 0)
            {
                var horend = grondslagen.Where(x => x.Codes.Any(c => post.Code.StartsWith(c, StringComparison.Ordinal) || c.StartsWith(post.Code, StringComparison.Ordinal))).ToList();
                if (horend.Count > 0 && !horend.Any(x => x.Opgenomen))
                {
                    signalen.Add(new($"grondslag:{post.Code}", $"{g.Naam(post.Code)} zonder grondslag",
                        $"De grondslag voor deze post ({string.Join(", ", horend.Select(x => x.Titel))}) staat niet in de algemene toelichting.", post.Code));
                }
            }
        }

        return signalen;
    }

    /// <summary>De brutomarge (omzet min kostprijs, als deel van de omzet) dit en vorig jaar; null zonder omzet of kostprijs.</summary>
    public static (decimal? Huidig, decimal? Vorig) Brutomarge(PostBalans balans)
    {
        ArgumentNullException.ThrowIfNull(balans);
        var omzet = balans.Totaal("WOmz");
        var kostprijs = balans.Totaal("WKpr");
        if (kostprijs.Huidig == 0 && kostprijs.Vorig == 0)
        {
            return (null, null);
        }

        static decimal? Marge(decimal omzet, decimal kostprijs) => omzet == 0 ? null : (-omzet - kostprijs) / -omzet;
        return (Marge(omzet.Huidig, kostprijs.Huidig), Marge(omzet.Vorig, kostprijs.Vorig));
    }

    private static string Procent(decimal fractie) => fractie.ToString("0.0%", Nederlands);

    private static string Kort(string tekst) => tekst.Length <= 150 ? tekst : tekst[..150];
}
