using System.Globalization;

namespace Boekerhouden.Domain.Rapport;

/// <summary>Een alinea van de samenstellingsverklaring, met een kopje (of zonder).</summary>
public sealed record Alinea(string? Kop, string Tekst);

/// <summary>De samenstellingsverklaring van de accountant (BV): aanhef, alinea's en ondertekening.</summary>
/// <param name="Ondertekening">Plaats en datum, bijv. "Zwolle, 24 oktober 2026"; met [datum] zolang die ontbreekt.</param>
/// <param name="Accountant">Naam en titel, bijv. "J. de Vries AA"; null zolang die niet is gekozen.</param>
public sealed record VerklaringBlok(string Aan, IReadOnlyList<Alinea> Alineas, string Ondertekening, string Kantoor, string? Accountant) : Blok;

/// <summary>Wat de verklaring nodig heeft naast de gegevens van het rapport.</summary>
/// <param name="Accountant">Naam en titel van de accountant die tekent; null = nog niet gekozen.</param>
/// <param name="Fiscaal">De jaarrekening is opgesteld op fiscale grondslagen (anders Titel 9 Boek 2 BW).</param>
/// <param name="Erkenning">De datum waarop het bestuur zijn verantwoordelijkheid erkende; de verklaring is niet eerder gedateerd.</param>
/// <param name="Toelichting">Een eigen toelichtende alinea (optioneel), bijv. over een bijzondere gebeurtenis.</param>
public sealed record VerklaringGegevens(
    string Kantoor,
    string? Plaats,
    string? Accountant,
    DateOnly? Datum,
    bool Fiscaal,
    DateOnly? Erkenning,
    string? Toelichting = null);

/// <summary>
/// De samenstellingsverklaring bij een samengestelde jaarrekening, langs de onderdelen die Standaard 4410 vraagt
/// (opdracht, werkzaamheden, verantwoordelijkheid, beroepsregels), in eigen woorden. Staat de alinea over de
/// continuïteit in de algemene toelichting, dan wijst de verklaring daarop.
/// </summary>
public static class Samenstellingsverklaring
{
    public const string Titel = "Samenstellingsverklaring van de accountant";

    private static readonly CultureInfo Nederlands = CultureInfo.GetCultureInfo("nl-NL");

    public static VerklaringBlok Maak(RapportGegevens g, VerklaringGegevens v, bool continuiteit)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(v);
        var naam = g.Kaart?.StatutaireNaam ?? g.Klantnaam;
        var zetel = g.Kaart?.StatutaireZetel ?? g.Plaats;
        var stelsel = v.Fiscaal
            ? "De jaarrekening is opgesteld op fiscale grondslagen, zoals artikel 2:396 lid 6 van het Burgerlijk Wetboek toestaat."
            : "De jaarrekening is opgesteld volgens Titel 9 Boek 2 van het Burgerlijk Wetboek.";
        var alineas = new List<Alinea>
        {
            new("Opdracht",
                $"Wij hebben de jaarrekening {g.Boekjaar} van {naam}{(zetel is null ? null : $" te {zetel}")} samengesteld op basis van de gegevens die u ons heeft verstrekt. " +
                $"De jaarrekening bestaat uit de balans per 31 december {g.Boekjaar} en de winst-en-verliesrekening over {g.Boekjaar}, met de toelichting. " +
                $"In de toelichting staan de grondslagen voor financiële verslaggeving die zijn toegepast. {stelsel}"),
            new("Onze werkzaamheden",
                "Wij hebben deze opdracht uitgevoerd volgens Nederlands recht, waaronder Standaard 4410 'Samenstellingsopdrachten', die voor ons geldt. " +
                "Volgens die standaard hebben wij u geholpen bij het opstellen en presenteren van de jaarrekening, met onze kennis van administratieve verwerking en financiële verslaggeving. " +
                "Een samenstellingsopdracht is geen controle- of beoordelingsopdracht: wij hebben geen werkzaamheden verricht om een oordeel of conclusie te kunnen geven over de vraag of de jaarrekening een getrouw beeld geeft."),
            new("Uw verantwoordelijkheid",
                "U bent verantwoordelijk voor de juistheid en volledigheid van de gegevens die u ons heeft verstrekt, en voor de jaarrekening die daarop is gebaseerd."),
            new("Beroepsregels",
                "Bij deze opdracht hebben wij de ethische voorschriften gevolgd die voor ons gelden: integriteit, objectiviteit, vakbekwaamheid en zorgvuldigheid, geheimhouding en professioneel gedrag. " +
                "U en andere gebruikers van de jaarrekening mogen er daarom van uitgaan dat wij de opdracht met deskundigheid en zorg hebben uitgevoerd."),
        };

        if (continuiteit)
        {
            alineas.Add(new("Onzekerheid over de continuïteit",
                "Wij vestigen de aandacht op de alinea 'Continuïteit' in de algemene toelichting van de jaarrekening. Daarin staat dat er onzekerheid bestaat over de voortzetting van de onderneming en hoe het bestuur daarmee omgaat."));
        }

        if (!string.IsNullOrWhiteSpace(v.Toelichting))
        {
            alineas.Add(new("Toelichting", v.Toelichting.Trim()));
        }

        var datum = v.Datum is { } d ? d.ToString("d MMMM yyyy", Nederlands) : "[datum]";
        return new VerklaringBlok($"Aan: het bestuur van {naam}", alineas, $"{v.Plaats ?? "[plaats]"}, {datum}", v.Kantoor, v.Accountant);
    }

    /// <summary>Wat er aan de verklaring nog ontbreekt of niet klopt (voor de waarschuwingen in het rapport).</summary>
    public static IEnumerable<string> Waarschuwingen(VerklaringGegevens v)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Datum is null || v.Accountant is null)
        {
            yield return "Samenstellingsverklaring: datum en accountant nog invullen.";
        }

        if (v.Erkenning is null)
        {
            yield return "De erkenning door het bestuur is nog niet vastgelegd.";
        }
        else if (v.Datum < v.Erkenning)
        {
            yield return $"De samenstellingsverklaring is gedateerd vóór de erkenning door het bestuur ({v.Erkenning.Value.ToString("d MMMM yyyy", Nederlands)}).";
        }
    }
}
