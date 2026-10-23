using Boekerhouden.Data;

namespace Boekerhouden.Server.Werkprogramma;

/// <summary>
/// Een eerste werkprogramma per soort opdracht, in eigen woorden opgesteld langs de fasen van Standaard 4410 (docs §2.1
/// en §10.5). Het is een startpunt: de vaktechnisch verantwoordelijke van het kantoor stelt de inhoud vast en past die
/// aan in Beheer › Werkprogramma's. Er is geen tekst uit gelicentieerde werkprogramma's overgenomen.
/// </summary>
public static class StandaardWerkprogramma
{
    public const string Versie = "2026.1";

    public static WerkprogrammaSjabloon Maak(SoortOpdracht soort, DateTimeOffset nu)
    {
        var bv = soort == SoortOpdracht.SamenstellenJaarrekening;
        var onderdelen = new List<SjabloonOnderdeel>
        {
            Onderdeel(Fase.Voorbereiding, "Opdracht en acceptatie", verplicht: true,
                Stap("Opdrachtbevestiging", "Leg vast wanneer de opdrachtbevestiging voor dit boekjaar is getekend. Ga na of het beoogde gebruik, het stelsel, de reikwijdte en de verantwoordelijkheden nog kloppen.", verplicht: true),
                Stap("Fundamentele beginselen", "Zijn er bedreigingen voor integriteit, objectiviteit, deskundigheid en zorgvuldigheid, vertrouwelijkheid of professioneel gedrag? Zo ja: welke maatregelen zijn genomen?", verplicht: true),
                Stap("Integriteit van de cliënt", bv
                    ? "Zijn er aanwijzingen dat de cliënt, het bestuur of de aandeelhouders niet integer handelen?"
                    : "Zijn er aanwijzingen dat de ondernemer niet integer handelt?"),
                Stap("Cliëntenonderzoek (Wwft)", "Is het cliëntenonderzoek actueel: identificatie, uiteindelijk belanghebbenden en risicoprofiel? Is het risicoprofiel ongewijzigd? Zo nee: is verscherpt onderzoek nodig?", verplicht: true),
                Stap("Besluit aanvaarding of voortzetting", "Leg het besluit vast om de opdracht te aanvaarden of voort te zetten, met de afwegingen.", verplicht: true)),
            Onderdeel(Fase.Voorbereiding, bv ? "Kennis van de cliënt" : "Kennis van de ondernemer", verplicht: false,
                Stap("Bedrijfsbeschrijving", "Beschrijf kort de activiteiten, de organisatie en de administratie."),
                Stap("Bijzonderheden in het boekjaar", bv
                    ? "Noteer belangrijke gebeurtenissen: investeringen, financiering, personeel, geschillen, wijzigingen in de groep."
                    : "Noteer belangrijke gebeurtenissen: investeringen, financiering, personeel, en wijzigingen in de privésituatie die de onderneming raken."),
                Stap("Stelsel en grondslagen", bv
                    ? "Welk stelsel is van toepassing (Titel 9 Boek 2 BW of fiscale grondslagen)? Zijn de grondslagen gewijzigd ten opzichte van vorig jaar?"
                    : "Zijn de grondslagen voor de winstbepaling gewijzigd ten opzichte van vorig jaar?")),
            Onderdeel(Fase.Voorbereiding, "Planning", verplicht: true, soort: OnderdeelSoort.MaterielePosten, stappen:
            [
                Stap("Alle rekeningen gerubriceerd", "Koppel alle grootboekrekeningen met saldo aan RGS (Rubriceren).", verplicht: true, controle: StapControle.AllesGekoppeld),
                Stap("Keuze materiële posten", "Kies per hoofdpost of die materieel is. Alleen materiële posten krijgen werk in de uitvoering.", verplicht: true, controle: StapControle.MaterielePostenGekozen),
                Stap("Cijferanalyse", "Vergelijk de cijfers met vorig jaar (Proefbalans) en verklaar opvallende verschillen."),
                Stap("Afspraken", "Leg de afspraken vast over het aanleveren van stukken en de planning."),
            ]),

            Post("BIva", "Immateriële vaste activa",
                Stap("Verloopoverzicht", "Stel het verloop op (begin, investeringen, desinvesteringen, afschrijvingen, eind) en sluit het aan op het grootboek."),
                Stap("Afschrijvingen", "Reken de afschrijvingen na en vergelijk de termijnen met vorig jaar."),
                Stap("Waardering", "Is er aanleiding om aan de waarde te twijfelen?")),
            Post("BMva", "Materiële vaste activa",
                Stap("Verloopoverzicht", "Stel het verloop op (aanschafwaarde, cumulatieve afschrijving, investeringen, desinvesteringen, afschrijvingen) en sluit het aan op het grootboek."),
                Stap("Investeringen", "Sluit de investeringen aan op facturen. Zijn het investeringen, of horen ze bij de kosten?"),
                Stap("Afschrijvingen", "Reken de afschrijvingen na en vergelijk de termijnen met vorig jaar."),
                Stap("Desinvesteringen", "Zijn verkopen en buitengebruikstellingen verwerkt, met het boekresultaat?")),
            Post("BFva", "Financiële vaste activa",
                Stap("Specificatie", "Sluit deelnemingen en verstrekte leningen aan op de onderliggende stukken."),
                Stap("Waardering en rente", "Beoordeel de waardering en of de rente is verwerkt.")),
            Post("BVrd", "Voorraden",
                Stap("Opname of opgave", "Sluit de voorraadopname of de opgave van de cliënt aan op de balans."),
                Stap("Waardering", "Waardering tegen kostprijs of lagere marktwaarde; is er incourante voorraad?")),
            Post("BVor", "Vorderingen",
                Stap("Debiteurenlijst", "Sluit de openstaande-postenlijst aan op het grootboek."),
                Stap("Voorziening dubieuze debiteuren", "Beoordeel de ouderdom en de betalingen na balansdatum; is de voorziening voldoende?"),
                Stap("Overige vorderingen", "Specificeer de overige vorderingen en overlopende activa en sluit ze aan op onderliggende stukken.")),
            Post("BLim", "Liquide middelen",
                Stap("Bank", "Sluit de saldi aan op de bankafschriften per balansdatum."),
                Stap("Kas", "Is er een kasopname of kasstaat per balansdatum?")),
            Post("BEiv", bv ? "Eigen vermogen" : "Ondernemingsvermogen", bv
                ? [
                    Stap("Verloop eigen vermogen", "Stel het verloop op: resultaat vorig boekjaar, dividend, overige mutaties."),
                    Stap("Aandelenkapitaal", "Sluit het geplaatst kapitaal aan op de statuten en het aandeelhoudersregister."),
                ]
                : [
                    Stap("Verloop ondernemingsvermogen", "Stel het verloop op met de privéonttrekkingen en -stortingen."),
                    Stap("Privégebruik", "Beoordeel privégebruik van auto, woning en goederen en de verwerking ervan."),
                ]),
            Post("BVrz", "Voorzieningen",
                Stap("Verloop en onderbouwing", "Stel het verloop op en leg de onderbouwing van elke voorziening vast."),
                Stap("Latente belastingen", "Is een voorziening voor latente belastingen nodig?")),
            Post("BLas", "Langlopende schulden",
                Stap("Leningen", "Sluit de leningen aan op overeenkomsten en aflossingsschema's."),
                Stap("Kortlopend deel", "Is de aflossing van het komende jaar als kortlopend gepresenteerd?")),
            Post("BSch", "Kortlopende schulden",
                Stap("Crediteurenlijst", "Sluit de openstaande-postenlijst aan op het grootboek."),
                Stap("Belastingen en premies", "Sluit omzetbelasting, loonheffingen" + (bv ? " en vennootschapsbelasting" : null) + " aan op de aangiften."),
                Stap("Overlopende passiva", "Zijn alle nog te betalen kosten en vooruitontvangen bedragen opgenomen?")),
            Post("WOmz", "Netto-omzet",
                Stap("Aansluiting omzetbelasting", "Sluit de omzet aan op de aangiften omzetbelasting."),
                Stap("Afgrenzing", "Is de omzet rond de balansdatum in het juiste jaar verwerkt?"),
                Stap("Brutomarge", "Vergelijk de brutomarge met vorig jaar en verklaar verschillen.")),
            Post("WKpr", "Kostprijs van de omzet",
                Stap("Inkoopwaarde", "Analyseer de inkoopwaarde en de marge."),
                Stap("Voorraadmutatie", "Sluit de voorraadmutatie aan op het verloop van de voorraad.")),
            Post("WPer", "Personeelskosten",
                Stap("Lonen", "Sluit de lonen aan op de loonjournaalposten en de aangiften loonheffingen."),
                Stap("Pensioenlasten", "Sluit de pensioenlasten aan op de nota's.")),
            Post("WAfs", "Afschrijvingen",
                Stap("Aansluiting", "Sluit de afschrijvingen aan op de verloopoverzichten van de vaste activa.")),
            Post("WBed", "Overige bedrijfskosten",
                Stap("Vergelijking", "Vergelijk de kosten met vorig jaar en verklaar grote verschillen."),
                Stap("Fiscaal", "Signaleer gemengde en niet-aftrekbare kosten voor de aangifte.")),
            Post("WFbe", "Financiële baten en lasten",
                Stap("Rente", "Sluit de rente aan op de leningen en de bankafschriften.")),
        };

        if (bv)
        {
            onderdelen.Add(Post("WBel", "Belastingen",
                Stap("Vennootschapsbelasting", "Bereken de vennootschapsbelasting en sluit die aan op de balans (fiscale positie).")));
            onderdelen.Add(Onderdeel(Fase.Afronding, "Evaluatie", verplicht: true,
                Stap("Lezen in samenhang", "Lees de samengestelde jaarrekening. Is die op het oog vrij van evidente onjuistheden en volledig?", verplicht: true),
                Stap("Fraude en niet-naleving", "Zijn er aanwijzingen voor fraude of het niet naleven van wet- en regelgeving? Zo ja: leg de stappen vast."),
                Stap("Significante aangelegenheden", "Zijn de significante aangelegenheden vastgelegd en met de cliënt besproken?"),
                Stap("Continuïteit", "Zijn er gebeurtenissen of omstandigheden die twijfel doen rijzen over de continuïteit?")));
            onderdelen.Add(Onderdeel(Fase.Afronding, "Bevestiging klant", verplicht: true,
                Stap("Bevestigingsbrief", "Laat het bestuur de bevestigingsbrief tekenen en leg de datum vast.", verplicht: true)));
            onderdelen.Add(Onderdeel(Fase.Afronding, "Uitbrengen rapport", verplicht: true,
                Stap("Datum verklaring", "De samenstellingsverklaring is gedateerd op of na de datum van de bevestigingsbrief.", verplicht: true),
                Stap("Bespreking", "Het rapport is besproken met de cliënt en verstrekt.")));
        }
        else
        {
            onderdelen.Add(Onderdeel(Fase.Afronding, "Evaluatie", verplicht: true,
                Stap("Lezen in samenhang", "Lees de cijfers en de aangifte in samenhang. Zijn ze op het oog vrij van evidente onjuistheden en volledig?", verplicht: true),
                Stap("Fraude en niet-naleving", "Zijn er aanwijzingen voor fraude of het niet naleven van wet- en regelgeving? Zo ja: leg de stappen vast.")));
            onderdelen.Add(Onderdeel(Fase.Afronding, "Fiscale winst en aangifte", verplicht: true,
                Stap("Fiscale winst", "Bereken de fiscale winst en sluit die aan op het resultaat.", verplicht: true),
                Stap("Ondernemersfaciliteiten", "Beoordeel urencriterium, zelfstandigenaftrek, MKB-winstvrijstelling en investeringsaftrek."),
                Stap("Aangifte", "De aangifte is besproken met de ondernemer en ingediend.", verplicht: true)));
        }

        onderdelen.Add(Onderdeel(Fase.Afronding, "Afsluiting", verplicht: true,
            Stap("Dossier compleet", "Is alle documentatie aanwezig en zijn alle reviewpunten opgelost?", verplicht: true),
            Stap("Dossier gesloten", "Sluit het dossier uiterlijk 60 dagen na de datum van de verklaring.")));

        var sjabloon = new WerkprogrammaSjabloon
        {
            Id = Guid.NewGuid(),
            SoortOpdracht = soort,
            Naam = bv ? "Samenstellen jaarrekening" : "Winstaangifte opstellen",
            Versie = Versie,
            GewijzigdOp = nu,
            GewijzigdDoor = "Standaard",
        };
        foreach (var (onderdeel, i) in onderdelen.Select((o, i) => (o, i)))
        {
            onderdeel.SjabloonId = sjabloon.Id;
            onderdeel.Volgorde = i;
            sjabloon.Onderdelen.Add(onderdeel);
        }

        return sjabloon;
    }

    private static SjabloonOnderdeel Onderdeel(Fase fase, string titel, bool verplicht, params SjabloonStap[] stappen) =>
        Onderdeel(fase, titel, verplicht, OnderdeelSoort.Algemeen, stappen);

    private static SjabloonOnderdeel Onderdeel(Fase fase, string titel, bool verplicht, OnderdeelSoort soort, SjabloonStap[] stappen, string? code = null)
    {
        var onderdeel = new SjabloonOnderdeel { Id = Guid.NewGuid(), Fase = fase, Soort = soort, Titel = titel, Verplicht = verplicht, HoofdpostCode = code };
        foreach (var (stap, i) in stappen.Select((s, i) => (s, i)))
        {
            stap.OnderdeelId = onderdeel.Id;
            stap.Volgorde = i;
            onderdeel.Stappen.Add(stap);
        }

        return onderdeel;
    }

    private static SjabloonOnderdeel Post(string code, string titel, params SjabloonStap[] stappen) =>
        Onderdeel(Fase.Uitvoering, titel, verplicht: false, OnderdeelSoort.Post, stappen, code);

    private static SjabloonStap Stap(string titel, string instructie, bool verplicht = false, StapControle controle = StapControle.Geen) =>
        new() { Id = Guid.NewGuid(), Titel = titel, Instructie = instructie, Verplicht = verplicht, Controle = controle };
}
