using Boekerhouden.Data;

namespace Boekerhouden.Server.Grondslagen;

/// <summary>
/// De meegeleverde grondslagenbibliotheek voor de jaarrekening van een kleine BV, met per grondslag een tekst voor
/// commerciële grondslagen (Titel 9 Boek 2 BW, RJk) en voor fiscale grondslagen (art. 2:396 lid 6 BW, Besluit fiscale
/// waarderingsgrondslagen). In eigen woorden opgesteld; het kantoor past de teksten aan in Beheer › Grondslagen.
/// Tekst tussen [vierkante haken] vult de gebruiker in het dossier in.
/// </summary>
public static class StandaardGrondslagen
{
    public const string Algemeen = "Algemeen";
    public const string Waardering = "Grondslagen voor de waardering van activa en passiva";
    public const string Resultaat = "Grondslagen voor de bepaling van het resultaat";

    public static IReadOnlyList<GrondslagSjabloon> Maak(DateTimeOffset nu)
    {
        var lijst = new List<GrondslagSjabloon>();

        void Voeg(string sleutel, string groep, string titel, GrondslagOpname opname, string codes, string? commercieel, string? fiscaal, bool jaargebonden = false) =>
            lijst.Add(new GrondslagSjabloon
            {
                Sleutel = sleutel,
                Groep = groep,
                Titel = titel,
                Opname = opname,
                Codes = codes,
                TekstCommercieel = commercieel,
                TekstFiscaal = fiscaal,
                Jaargebonden = jaargebonden,
                Volgorde = lijst.Count + 1,
                GewijzigdOp = nu,
                GewijzigdDoor = "Standaard",
            });

        Voeg("algemeen", Algemeen, "Algemeen", GrondslagOpname.Altijd, "",
            "De jaarrekening is opgesteld volgens Titel 9 Boek 2 BW en de stellige uitspraken van de Richtlijnen voor de jaarverslaggeving voor kleine rechtspersonen (RJk). "
            + "Activa en passiva worden gewaardeerd tegen nominale waarde, tenzij hierna een andere grondslag is vermeld.\n"
            + "Een actief staat in de balans als het waarschijnlijk is dat de toekomstige economische voordelen naar de vennootschap zullen toevloeien en de waarde betrouwbaar kan worden vastgesteld. "
            + "Een verplichting staat in de balans als het waarschijnlijk is dat de afwikkeling gepaard gaat met een uitstroom van middelen en de omvang betrouwbaar kan worden vastgesteld. "
            + "Baten en lasten worden toegerekend aan de periode waarop zij betrekking hebben.",
            "De jaarrekening is opgesteld op fiscale grondslagen, zoals een kleine rechtspersoon mag op grond van artikel 2:396 lid 6 BW en het Besluit fiscale waarderingsgrondslagen. "
            + "De waardering van activa en passiva en de bepaling van het resultaat sluiten aan bij de grondslagen voor de vennootschapsbelasting. "
            + "Activa en passiva worden gewaardeerd tegen nominale waarde, tenzij hierna een andere grondslag is vermeld.\n"
            + "Baten en lasten worden toegerekend aan de periode waarop zij betrekking hebben.");
        Voeg("vergelijking", Algemeen, "Vergelijking met het voorgaande jaar", GrondslagOpname.Altijd, "",
            "De grondslagen van waardering en resultaatbepaling zijn ongewijzigd ten opzichte van het voorgaande jaar. Waar nodig zijn de vergelijkende cijfers anders gerubriceerd, zodat ze vergelijkbaar zijn.",
            "De grondslagen van waardering en resultaatbepaling zijn ongewijzigd ten opzichte van het voorgaande jaar. Waar nodig zijn de vergelijkende cijfers anders gerubriceerd, zodat ze vergelijkbaar zijn.");
        Voeg("schattingen", Algemeen, "Schattingen", GrondslagOpname.Altijd, "",
            "Bij het opstellen van de jaarrekening vormt de leiding zich oordelen en maakt zij schattingen en veronderstellingen die van invloed zijn op de toepassing van de grondslagen en op de gerapporteerde waarde van activa, verplichtingen, baten en lasten. "
            + "De werkelijke uitkomsten kunnen afwijken van deze schattingen. Schattingen en veronderstellingen worden voortdurend beoordeeld.",
            null);
        Voeg("continuiteit", Algemeen, "Continuïteit", GrondslagOpname.Optioneel, "",
            "De jaarrekening is opgesteld uitgaande van de continuïteit van de vennootschap. [Licht toe welke omstandigheden twijfel doen rijzen over de continuïteit en welke maatregelen de leiding neemt.]",
            "De jaarrekening is opgesteld uitgaande van de continuïteit van de vennootschap. [Licht toe welke omstandigheden twijfel doen rijzen over de continuïteit en welke maatregelen de leiding neemt.]",
            jaargebonden: true);
        Voeg("stelselwijziging", Algemeen, "Stelselwijziging", GrondslagOpname.Optioneel, "",
            "In het boekjaar is de grondslag voor [post] gewijzigd, omdat [reden]. Het cumulatieve effect op het eigen vermogen aan het begin van het boekjaar bedraagt € [bedrag]; de vergelijkende cijfers zijn aangepast.",
            "In het boekjaar is de grondslag voor [post] gewijzigd, omdat [reden]. Het cumulatieve effect op het eigen vermogen aan het begin van het boekjaar bedraagt € [bedrag]; de vergelijkende cijfers zijn aangepast.",
            jaargebonden: true);
        Voeg("schattingswijziging", Algemeen, "Schattingswijziging", GrondslagOpname.Optioneel, "",
            "In het boekjaar is de schatting van [onderwerp] herzien. De wijziging is verwerkt vanaf het boekjaar; het effect op het resultaat bedraagt € [bedrag].",
            "In het boekjaar is de schatting van [onderwerp] herzien. De wijziging is verwerkt vanaf het boekjaar; het effect op het resultaat bedraagt € [bedrag].",
            jaargebonden: true);
        Voeg("foutenherstel", Algemeen, "Foutenherstel", GrondslagOpname.Optioneel, "",
            "In het boekjaar is een fout uit een voorgaand jaar ontdekt en hersteld: [aard van de fout]. Het herstel is verwerkt alsof de fout niet was gemaakt; de vergelijkende cijfers zijn aangepast. "
            + "Het effect op het eigen vermogen aan het begin van het boekjaar bedraagt € [bedrag] en op het resultaat van het voorgaande jaar € [bedrag].",
            "In het boekjaar is een fout uit een voorgaand jaar ontdekt en hersteld: [aard van de fout]. De vergelijkende cijfers zijn aangepast. "
            + "Het effect op het eigen vermogen aan het begin van het boekjaar bedraagt € [bedrag] en op het resultaat van het voorgaande jaar € [bedrag].",
            jaargebonden: true);
        Voeg("gebeurtenissen", Algemeen, "Gebeurtenissen na balansdatum", GrondslagOpname.Optioneel, "",
            "Gebeurtenissen die nadere informatie geven over de situatie op balansdatum en die bekend worden tot het opmaken van de jaarrekening, zijn in de jaarrekening verwerkt. "
            + "Gebeurtenissen die geen nadere informatie geven over de situatie op balansdatum zijn niet verwerkt, maar worden toegelicht als ze van belang zijn: [toelichting].",
            "Gebeurtenissen die nadere informatie geven over de situatie op balansdatum en die bekend worden tot het opmaken van de jaarrekening, zijn in de jaarrekening verwerkt. "
            + "Gebeurtenissen die geen nadere informatie geven over de situatie op balansdatum zijn niet verwerkt, maar worden toegelicht als ze van belang zijn: [toelichting].",
            jaargebonden: true);
        Voeg("verbonden", Algemeen, "Transacties met verbonden partijen", GrondslagOpname.Optioneel, "",
            "Transacties met verbonden partijen worden toegelicht als ze van betekenis zijn en niet onder normale marktvoorwaarden zijn aangegaan: [toelichting].",
            "Transacties met verbonden partijen worden toegelicht als ze van betekenis zijn en niet onder normale marktvoorwaarden zijn aangegaan: [toelichting].");

        Voeg("iva", Waardering, "Immateriële vaste activa", GrondslagOpname.BijPost, "BIva",
            "De immateriële vaste activa worden gewaardeerd tegen verkrijgingsprijs onder aftrek van cumulatieve afschrijvingen en, als dat nodig is, bijzondere waardeverminderingen. "
            + "De afschrijvingen worden lineair berekend over de verwachte economische levensduur.",
            "De immateriële vaste activa worden gewaardeerd tegen verkrijgingsprijs onder aftrek van fiscale afschrijvingen. "
            + "Op goodwill wordt per jaar ten hoogste 10% van de verkrijgingsprijs afgeschreven, op de overige immateriële vaste activa ten hoogste 20%.");
        Voeg("mva", Waardering, "Materiële vaste activa", GrondslagOpname.BijPost, "BMva",
            "De materiële vaste activa worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs onder aftrek van cumulatieve afschrijvingen en, als dat nodig is, bijzondere waardeverminderingen. "
            + "De afschrijvingen worden lineair berekend over de verwachte economische levensduur, rekening houdend met de restwaarde. Op terreinen wordt niet afgeschreven.",
            "De materiële vaste activa worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs onder aftrek van afschrijvingen volgens de fiscale regels, rekening houdend met de restwaarde. "
            + "Op bedrijfsmiddelen wordt per jaar ten hoogste 20% van de verkrijgingsprijs afgeschreven. "
            + "Op gebouwen wordt niet verder afgeschreven dan tot de bodemwaarde; voor de vennootschapsbelasting is dat 100% van de WOZ-waarde. Op terreinen wordt niet afgeschreven.");
        Voeg("vastgoed", Waardering, "Vastgoedbeleggingen", GrondslagOpname.BijPost, "BVas",
            "Vastgoedbeleggingen worden gewaardeerd tegen verkrijgingsprijs onder aftrek van cumulatieve afschrijvingen en bijzondere waardeverminderingen. [Of: tegen actuele waarde; waardeveranderingen lopen via de winst-en-verliesrekening, met een herwaarderingsreserve.]",
            "Vastgoedbeleggingen worden gewaardeerd tegen verkrijgingsprijs onder aftrek van afschrijvingen volgens de fiscale regels. Er wordt niet verder afgeschreven dan tot de bodemwaarde: 100% van de WOZ-waarde.");
        Voeg("fva", Waardering, "Financiële vaste activa", GrondslagOpname.BijPost, "BFva",
            "Deelnemingen waarin invloed van betekenis wordt uitgeoefend, worden gewaardeerd volgens de nettovermogenswaarde; andere deelnemingen tegen verkrijgingsprijs. "
            + "Verstrekte leningen en overige vorderingen worden gewaardeerd tegen geamortiseerde kostprijs, wat doorgaans gelijk is aan de nominale waarde, onder aftrek van een voorziening voor oninbaarheid als dat nodig is.",
            "Deelnemingen worden gewaardeerd tegen verkrijgingsprijs. Verstrekte leningen worden gewaardeerd tegen nominale waarde, onder aftrek van een voorziening voor oninbaarheid als dat nodig is.");
        Voeg("latentie", Waardering, "Latente belastingen", GrondslagOpname.BijPost, "BFvaLbv,BVorLbv,BVrzVvbVlb",
            "Voor tijdelijke verschillen tussen de waardering van activa en passiva in de jaarrekening en de fiscale waardering wordt een voorziening voor latente belastingverplichtingen gevormd. "
            + "Latente belastingvorderingen, ook voor te verrekenen verliezen, worden opgenomen voor zover het waarschijnlijk is dat er voldoende fiscale winst beschikbaar zal zijn. "
            + "De berekening is gebaseerd op de belastingtarieven die op balansdatum gelden of al wettelijk zijn vastgesteld. Latente belastingen worden gewaardeerd tegen nominale waarde.",
            null);
        Voeg("voorraden", Waardering, "Voorraden", GrondslagOpname.BijPost, "BVrd",
            "Voorraden worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs volgens de FIFO-methode, of tegen de lagere opbrengstwaarde. Bij de waardering wordt rekening gehouden met incourantheid.",
            "Voorraden worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs, of tegen de lagere marktwaarde. Bij de waardering wordt rekening gehouden met incourantheid.");
        Voeg("projecten", Waardering, "Onderhanden projecten", GrondslagOpname.BijPost, "BPro",
            "Onderhanden projecten worden gewaardeerd tegen de gerealiseerde projectkosten, vermeerderd met de toegerekende winst naar rato van de voortgang en verminderd met voorziene verliezen en gedeclareerde termijnen.",
            "Onderhanden projecten worden gewaardeerd tegen de gerealiseerde projectkosten, vermeerderd met de winst die naar rato van de voortgang aan het boekjaar is toe te rekenen, en verminderd met gedeclareerde termijnen.");
        Voeg("vorderingen", Waardering, "Vorderingen", GrondslagOpname.BijPost, "BVor",
            "Vorderingen worden bij eerste verwerking gewaardeerd tegen reële waarde en daarna tegen geamortiseerde kostprijs, wat doorgaans gelijk is aan de nominale waarde. Een voorziening wegens oninbaarheid wordt in mindering gebracht.",
            "Vorderingen worden gewaardeerd tegen nominale waarde, onder aftrek van een voorziening wegens oninbaarheid voor zover dat nodig is.");
        Voeg("effecten", Waardering, "Effecten", GrondslagOpname.BijPost, "BEff",
            "Effecten die tot de handelsportefeuille behoren, worden gewaardeerd tegen reële waarde; overige effecten tegen verkrijgingsprijs of lagere marktwaarde.",
            "Effecten worden gewaardeerd tegen verkrijgingsprijs of lagere marktwaarde.");
        Voeg("liquide", Waardering, "Liquide middelen", GrondslagOpname.BijPost, "BLim",
            "Liquide middelen worden gewaardeerd tegen nominale waarde. Als liquide middelen niet ter vrije beschikking staan, wordt dat vermeld.",
            "Liquide middelen worden gewaardeerd tegen nominale waarde. Als liquide middelen niet ter vrije beschikking staan, wordt dat vermeld.");
        Voeg("voorzieningen", Waardering, "Voorzieningen", GrondslagOpname.BijPost, "BVrz",
            "Voorzieningen worden gevormd voor in rechte afdwingbare of feitelijke verplichtingen die op balansdatum bestaan, waarvoor waarschijnlijk een uitstroom van middelen nodig is en waarvan de omvang betrouwbaar is te schatten. "
            + "Voorzieningen worden gewaardeerd tegen de nominale waarde van de beste schatting van de bedragen die nodig zijn om de verplichtingen af te wikkelen.",
            "Voorzieningen worden gevormd voor zover dat fiscaal is toegestaan: voor toekomstige uitgaven die hun oorsprong vinden in feiten en omstandigheden van vóór balansdatum, die aan het boekjaar zijn toe te rekenen en waarvan het optreden redelijk zeker is. "
            + "Voorzieningen worden gewaardeerd tegen nominale waarde.");
        Voeg("schulden", Waardering, "Langlopende en kortlopende schulden", GrondslagOpname.BijPost, "BLas,BSch",
            "Schulden worden bij eerste verwerking gewaardeerd tegen reële waarde en daarna tegen geamortiseerde kostprijs, wat doorgaans gelijk is aan de nominale waarde.",
            "Schulden worden gewaardeerd tegen nominale waarde.");

        Voeg("resultaat", Resultaat, "Algemeen", GrondslagOpname.Altijd, "",
            "Het resultaat is het verschil tussen de opbrengstwaarde van de geleverde goederen en verrichte diensten en de kosten en andere lasten over het jaar. "
            + "Winsten worden verantwoord in het jaar waarin ze zijn gerealiseerd; verliezen zodra ze voorzienbaar zijn.",
            "Het resultaat wordt bepaald volgens goed koopmansgebruik, zoals dat geldt voor de fiscale winstbepaling. "
            + "Winsten worden verantwoord in het jaar waarin ze zijn gerealiseerd; verliezen zodra ze voorzienbaar zijn.");
        Voeg("omzet", Resultaat, "Netto-omzet", GrondslagOpname.BijPost, "WOmz",
            "De netto-omzet bestaat uit de opbrengsten van geleverde goederen en verrichte diensten, na aftrek van kortingen en van de belastingen die over de omzet worden geheven. "
            + "Opbrengsten worden verantwoord als de belangrijke risico's en voordelen zijn overgedragen aan de afnemer, of naar rato van de verrichte diensten.",
            "De netto-omzet bestaat uit de opbrengsten van geleverde goederen en verrichte diensten, na aftrek van kortingen en van de belastingen die over de omzet worden geheven.");
        Voeg("kostprijs", Resultaat, "Kostprijs van de omzet", GrondslagOpname.BijPost, "WKpr",
            "De kostprijs van de omzet omvat de kosten die direct aan de netto-omzet zijn toe te rekenen, zoals de inkoopwaarde van de verkochte goederen en de kosten van uitbesteed werk.",
            "De kostprijs van de omzet omvat de kosten die direct aan de netto-omzet zijn toe te rekenen, zoals de inkoopwaarde van de verkochte goederen en de kosten van uitbesteed werk.");
        Voeg("personeel", Resultaat, "Lasten uit hoofde van personeelsbeloningen", GrondslagOpname.BijPost, "WPer",
            "Lonen, salarissen en sociale lasten worden op grond van de arbeidsvoorwaarden verwerkt voor zover ze verschuldigd zijn aan werknemers. "
            + "Pensioenregelingen worden verwerkt volgens de verplichtingenbenadering: de over het boekjaar verschuldigde premie is een last.",
            "Lonen, salarissen en sociale lasten worden verwerkt in het jaar waarin ze verschuldigd zijn. De pensioenpremies zijn een last van het jaar waarop ze betrekking hebben.");
        Voeg("afschrijvingen", Resultaat, "Afschrijvingen", GrondslagOpname.BijPost, "WAfs",
            "De afschrijvingen op immateriële en materiële vaste activa worden berekend vanaf het moment van ingebruikneming, over de verwachte economische levensduur. "
            + "Boekwinsten en -verliezen bij verkoop zijn begrepen onder de afschrijvingen.",
            "De afschrijvingen worden berekend volgens de fiscale regels, vanaf het moment van ingebruikneming. "
            + "Boekwinsten bij verkoop lopen via het resultaat, tenzij ze in een herinvesteringsreserve worden opgenomen.");
        Voeg("financieel", Resultaat, "Financiële baten en lasten", GrondslagOpname.BijPost, "WFbe",
            "Rentebaten en rentelasten worden tijdsevenredig verwerkt, rekening houdend met de effectieve rente van de betreffende activa en passiva.",
            "Rentebaten en rentelasten worden verwerkt in het jaar waarop ze betrekking hebben.");
        Voeg("belastingen", Resultaat, "Belastingen", GrondslagOpname.BijPost, "WBel",
            "De belasting over het resultaat wordt berekend over het resultaat voor belastingen, rekening houdend met fiscale faciliteiten, permanente verschillen en de mutatie in de latente belastingen.",
            "De belasting over het resultaat wordt berekend over het fiscale resultaat tegen de geldende tarieven, rekening houdend met fiscale faciliteiten en niet-aftrekbare kosten. "
            + "Omdat de jaarrekening op fiscale grondslagen is opgesteld, worden geen latente belastingen gevormd.");

        return lijst;
    }
}
