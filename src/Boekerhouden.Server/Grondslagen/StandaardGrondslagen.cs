using Boekerhouden.Data;

namespace Boekerhouden.Server.Grondslagen;

/// <summary>
/// De meegeleverde bibliotheek voor de jaarrekening van een kleine BV: de algemene toelichting en de grondslagen, met
/// per onderdeel een tekst voor commerciële grondslagen (Titel 9 Boek 2 BW, RJk) en voor fiscale grondslagen
/// (art. 2:396 lid 6 BW, Besluit fiscale waarderingsgrondslagen). Opgesteld in eigen woorden aan de hand van
/// modeljaarrekeningen; een kasstroomoverzicht en een staat van baten en lasten horen er niet bij. Het kantoor past
/// de teksten aan in Beheer › Grondslagen. Variabelen als {naam} en {boekjaar} vult het rapport in
/// (<see cref="Domain.Rapport.RapportVariabelen"/>); tekst tussen [vierkante haken] vult de gebruiker in het dossier in.
/// </summary>
public static class StandaardGrondslagen
{
    public const string Toelichting = "Algemene toelichting";
    public const string Algemeen = "Algemene grondslagen";
    public const string Waardering = "Grondslagen voor de waardering van activa en passiva";
    public const string Resultaat = "Grondslagen voor de bepaling van het resultaat";

    /// <summary>Wie een meegeleverde grondslag "gewijzigd" heeft; zo'n grondslag gaat mee met nieuwe standaardteksten.</summary>
    public const string Door = "Standaard";

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
                GewijzigdDoor = Door,
            });

        // Dezelfde tekst bij commerciële en fiscale grondslagen.
        void Beide(string sleutel, string groep, string titel, GrondslagOpname opname, string codes, string tekst, bool jaargebonden = false) =>
            Voeg(sleutel, groep, titel, opname, codes, tekst, tekst, jaargebonden);

        Beide("activiteiten", Toelichting, "Activiteiten", GrondslagOpname.Altijd, "",
            "De activiteiten van {naam} bestaan voornamelijk uit [omschrijving van de activiteiten].");
        Beide("vestiging", Toelichting, "Vestigingsadres, rechtsvorm en inschrijving handelsregister", GrondslagOpname.Altijd, "",
            "{naam} is een besloten vennootschap met beperkte aansprakelijkheid, gevestigd te {plaats}, en is ingeschreven in het handelsregister onder nummer {kvk}.");
        Beide("groepsverhoudingen", Toelichting, "Groepsverhoudingen", GrondslagOpname.Optioneel, "",
            "{naam} behoort tot de groep van [naam moedermaatschappij] te [vestigingsplaats]. Omdat {naam} met haar groepsmaatschappijen ook geconsolideerd als kleine rechtspersoon geldt, "
            + "is consolidatie achterwege gelaten op grond van artikel 2:407 lid 2 sub a BW.");
        Beide("continuiteit", Toelichting, "Continuïteit", GrondslagOpname.Optioneel, "",
            "Het eigen vermogen van {naam} bedraagt per 31 december {boekjaar} € [bedrag] negatief. De vennootschap wordt gefinancierd met [bijv. leningen van de bank en de aandeelhouder]. "
            + "De continuïteit hangt in hoge mate af van de bereidheid van [financiers] om deze financiering voort te zetten; daarover zijn nog geen concrete toezeggingen gedaan. "
            + "Daardoor bestaat er een onzekerheid van materieel belang over de continuïteit van de vennootschap.\n"
            + "De leiding verwacht dat de kasstromen zich de komende jaren positief ontwikkelen door de volgende maatregelen: [maatregelen en plannen]. "
            + "Op grond van de gesprekken met [financiers] verwacht zij dat de financiering wordt voortgezet. De grondslagen van waardering en resultaatbepaling zijn daarom gebaseerd op de veronderstelling van continuïteit.",
            jaargebonden: true);
        Beide("stelselwijziging", Toelichting, "Stelselwijziging", GrondslagOpname.Optioneel, "",
            "In {boekjaar} is de grondslag voor [post] gewijzigd, omdat [reden]. De wijziging is verwerkt als rechtstreekse wijziging van het eigen vermogen per 1 januari {boekjaar}; "
            + "het effect bedraagt € [bedrag]. De vergelijkende cijfers zijn aangepast.",
            jaargebonden: true);
        Voeg("fiscaalvoorheteerst", Toelichting, "Overgang naar fiscale grondslagen", GrondslagOpname.Optioneel, "",
            null,
            "In deze jaarrekening zijn voor het eerst fiscale grondslagen toegepast, zoals artikel 2:396 lid 6 BW toestaat, omdat [reden]. "
            + "De overgang is een stelselwijziging, verwerkt als rechtstreekse wijziging van het eigen vermogen per 1 januari {boekjaar}. Het effect op het eigen vermogen bedraagt € [bedrag]. "
            + "Zoals het Besluit fiscale waarderingsgrondslagen toestaat, zijn de vergelijkende cijfers niet aangepast.",
            jaargebonden: true);
        Beide("schattingswijziging", Toelichting, "Schattingswijziging", GrondslagOpname.Optioneel, "",
            "In {boekjaar} is de schatting van [onderwerp] herzien. De wijziging is verwerkt vanaf {boekjaar}; het effect op het resultaat bedraagt € [bedrag].",
            jaargebonden: true);
        Beide("schattingen", Toelichting, "Schattingen", GrondslagOpname.Altijd, "",
            "Bij het toepassen van de grondslagen vormt de leiding van {naam} zich oordelen en maakt zij schattingen die van wezenlijk belang kunnen zijn voor de bedragen in de jaarrekening. "
            + "Waar dat nodig is voor het inzicht, staat de aard van deze oordelen en schattingen, met de veronderstellingen, bij de toelichting op de betreffende post.");
        Voeg("foutenherstel", Toelichting, "Foutenherstel", GrondslagOpname.Optioneel, "",
            "In {boekjaar} is een fout uit een voorgaand jaar ontdekt en hersteld: [aard van de fout]. Het herstel is verwerkt alsof de fout niet was gemaakt; de vergelijkende cijfers zijn aangepast. "
            + "Het effect op het eigen vermogen per 1 januari {boekjaar} bedraagt € [bedrag] en op het resultaat van het voorgaande jaar € [bedrag].",
            "In {boekjaar} is een fout uit een voorgaand jaar hersteld: [aard van de fout, bijv. een correctie na een boekenonderzoek vennootschapsbelasting]. "
            + "Het herstel is verwerkt in het eigen vermogen per 1 januari {boekjaar}; het effect bedraagt € [bedrag].",
            jaargebonden: true);
        Beide("gebeurtenissen", Toelichting, "Gebeurtenissen na balansdatum", GrondslagOpname.Optioneel, "",
            "Gebeurtenissen die nadere informatie geven over de situatie op balansdatum en die bekend worden tot het opmaken van de jaarrekening, zijn in de jaarrekening verwerkt. "
            + "Gebeurtenissen die geen nadere informatie geven over de situatie op balansdatum zijn niet verwerkt, maar worden toegelicht als ze van belang zijn: [toelichting].",
            jaargebonden: true);
        Beide("verbonden", Toelichting, "Transacties met verbonden partijen", GrondslagOpname.Optioneel, "",
            "Transacties met verbonden partijen worden toegelicht als ze van betekenis zijn en niet onder normale marktvoorwaarden zijn aangegaan: [toelichting].");

        Voeg("algemeen", Algemeen, "Algemeen", GrondslagOpname.Altijd, "",
            "De jaarrekening is opgesteld volgens Titel 9 Boek 2 BW en de Richtlijnen voor de jaarverslaggeving voor kleine rechtspersonen van de Raad voor de Jaarverslaggeving.\n"
            + "Activa en passiva worden in het algemeen gewaardeerd tegen verkrijgings- of vervaardigingsprijs of tegen actuele waarde. Als geen andere grondslag is vermeld, is de waardering tegen verkrijgingsprijs.",
            "De jaarrekening is opgesteld volgens Titel 9 Boek 2 BW, met uitzondering van afdeling 6 over de grondslagen van waardering en resultaatbepaling. "
            + "In plaats daarvan zijn, zoals artikel 2:396 lid 6 BW toestaat, de grondslagen toegepast voor de bepaling van de belastbare winst volgens de Wet op de vennootschapsbelasting 1969. "
            + "Daarbij gelden het Besluit fiscale waarderingsgrondslagen en de handreiking van de Raad voor de Jaarverslaggeving voor fiscale grondslagen bij kleine rechtspersonen.\n"
            + "Activa en passiva worden in het algemeen gewaardeerd tegen verkrijgings- of vervaardigingsprijs, verminderd met afschrijvingen. Als geen andere grondslag is vermeld, is de waardering tegen verkrijgingsprijs.");
        Voeg("afwijking", Algemeen, "Afwijking van betekenis ten opzichte van afdeling 6", GrondslagOpname.Optioneel, "",
            null,
            "Het toepassen van fiscale grondslagen leidt bij [posten, bijv. de bedrijfsgebouwen en de deelnemingen] tot een afwijking van betekenis ten opzichte van de grondslagen van afdeling 6 Titel 9 Boek 2 BW. "
            + "Dat kan gevolgen hebben voor het inzicht in het vermogen.");
        Beide("vergelijking", Algemeen, "Vergelijking met het voorgaande jaar", GrondslagOpname.Altijd, "",
            "De grondslagen van waardering en resultaatbepaling zijn ongewijzigd ten opzichte van het voorgaande jaar, behalve waar een stelselwijziging is vermeld. "
            + "Waar nodig zijn de vergelijkende cijfers anders gerubriceerd, zodat ze vergelijkbaar zijn.");
        Beide("valuta", Algemeen, "Vreemde valuta", GrondslagOpname.Optioneel, "",
            "De jaarrekening is opgesteld in euro's, de functionele en presentatievaluta van {naam}. Transacties in vreemde valuta worden verwerkt tegen de koers op de transactiedatum. "
            + "Monetaire activa en passiva in vreemde valuta worden omgerekend tegen de koers op balansdatum; koersverschillen bij afwikkeling en omrekening komen in de winst-en-verliesrekening. "
            + "Niet-monetaire activa die tegen verkrijgingsprijs in vreemde valuta worden gewaardeerd, worden omgerekend tegen de koers op de transactiedatum.");
        Beide("leasing", Algemeen, "Leasing", GrondslagOpname.Optioneel, "",
            "Bij financiële leasing liggen de voor- en nadelen van het eigendom grotendeels bij de vennootschap. Het actief wordt bij aanvang van het contract geactiveerd tegen de reële waarde "
            + "of de lagere contante waarde van de minimale leasetermijnen. De leasetermijnen worden gesplitst in aflossing en rente: de verplichting zonder rente staat onder de schulden, "
            + "de rente komt over de looptijd ten laste van het resultaat.\n"
            + "Bij operationele leasing liggen de voor- en nadelen van het eigendom grotendeels niet bij de vennootschap. De leasebetalingen komen lineair over de looptijd van het contract ten laste van het resultaat.");
        Beide("bijzondereposten", Algemeen, "Bijzondere posten", GrondslagOpname.Optioneel, "",
            "Bijzondere posten zijn baten of lasten uit de gewone bedrijfsuitoefening die vanwege hun aard, omvang of incidentele karakter apart worden toegelicht, zodat de resultaten vergelijkbaar blijven.");

        Voeg("iva", Waardering, "Immateriële vaste activa", GrondslagOpname.BijPost, "BIva",
            "Immateriële vaste activa worden gewaardeerd tegen verkrijgingsprijs onder aftrek van afschrijvingen en bijzondere waardeverminderingen. "
            + "Goodwill uit overnames wordt geactiveerd en lineair afgeschreven over de verwachte economische levensduur.\n"
            + "Ontwikkelingskosten worden geactiveerd als het waarschijnlijk is dat het project commercieel en technisch slaagt en de kosten betrouwbaar zijn vast te stellen; "
            + "voor het geactiveerde bedrag is een wettelijke reserve gevormd. Onderzoekskosten komen direct ten laste van het resultaat.",
            "Voortbrengingskosten van zelf ontwikkelde immateriële vaste activa komen in het jaar waarin ze worden gemaakt ten laste van het resultaat. "
            + "Goodwill uit overnames wordt geactiveerd en lineair afgeschreven met ten hoogste 10% van de verkrijgingsprijs per jaar, zonder restwaarde. "
            + "Op de overige immateriële vaste activa wordt ten hoogste 20% per jaar afgeschreven.");
        Voeg("mva", Waardering, "Materiële vaste activa", GrondslagOpname.BijPost, "BMva",
            "Bedrijfsgebouwen en terreinen worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs plus bijkomende kosten, onder aftrek van lineaire afschrijvingen over de verwachte gebruiksduur "
            + "en bijzondere waardeverminderingen. Op terreinen wordt niet afgeschreven.\n"
            + "De overige materiële vaste activa worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs inclusief direct toerekenbare kosten, onder aftrek van lineaire afschrijvingen "
            + "over de verwachte economische levensduur, rekening houdend met de restwaarde, en bijzondere waardeverminderingen.\n"
            + "Investeringssubsidies worden in mindering gebracht op de verkrijgings- of vervaardigingsprijs. Kosten van regelmatig onderhoud komen ten laste van het resultaat wanneer ze worden gemaakt.",
            "Bedrijfsgebouwen en terreinen worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs plus bijkomende kosten, onder aftrek van lineaire afschrijvingen. "
            + "Op gebouwen wordt niet verder afgeschreven dan tot de bodemwaarde: 100% van de WOZ-waarde. Op terreinen wordt niet afgeschreven.\n"
            + "De overige materiële vaste activa worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs inclusief direct toerekenbare kosten, onder aftrek van lineaire afschrijvingen "
            + "vanaf het moment van ingebruikneming. De afschrijvingen zijn gebaseerd op de verwachte gebruiksduur, met ten hoogste 20% van de verkrijgingsprijs per jaar, rekening houdend met de restwaarde. "
            + "Bedrijfsmiddelen van geringe waarde (minder dan € 450 per stuk) komen direct ten laste van het resultaat.\n"
            + "Investeringssubsidies worden in mindering gebracht op de verkrijgings- of vervaardigingsprijs.");
        Voeg("vastgoed", Waardering, "Vastgoedbeleggingen", GrondslagOpname.BijPost, "BVas",
            "Vastgoedbeleggingen zijn onroerende zaken die worden aangehouden voor huuropbrengsten, waardestijging of beide. Ze worden bij eerste verwerking gewaardeerd tegen verkrijgingsprijs "
            + "inclusief transactiekosten en daarna tegen actuele waarde, gebaseerd op beschikbare marktgegevens en taxaties. Waardeveranderingen komen in de winst-en-verliesrekening; "
            + "voor het positieve verschil met de verkrijgingsprijs is een herwaarderingsreserve gevormd. Op vastgoedbeleggingen wordt niet afgeschreven.",
            "Vastgoedbeleggingen worden gewaardeerd tegen verkrijgingsprijs onder aftrek van lineaire afschrijvingen. Er wordt niet verder afgeschreven dan tot de bodemwaarde: 100% van de WOZ-waarde. "
            + "Op grond wordt niet afgeschreven.");
        Voeg("fva", Waardering, "Financiële vaste activa", GrondslagOpname.BijPost, "BFva",
            "Deelnemingen waarin invloed van betekenis kan worden uitgeoefend (doorgaans bij 20% of meer van de stemrechten), worden gewaardeerd volgens de nettovermogenswaarde, "
            + "berekend volgens de grondslagen van deze jaarrekening. Is die waarde negatief, dan staat de deelneming op nihil; staat de vennootschap in voor de schulden van de deelneming, "
            + "dan is daarvoor een voorziening gevormd. Deelnemingen zonder invloed van betekenis worden gewaardeerd tegen verkrijgingsprijs.\n"
            + "Vorderingen op groepsmaatschappijen en overige vorderingen worden bij eerste verwerking gewaardeerd tegen reële waarde inclusief transactiekosten en daarna tegen geamortiseerde kostprijs, "
            + "wat doorgaans gelijk is aan de nominale waarde, rekening houdend met waardeverminderingen.",
            "Deelnemingen worden gewaardeerd volgens de fiscale waarderingsgrondslag. Behoort de deelneming tot de fiscale eenheid, dan is de waarde het saldo van haar activa en verplichtingen "
            + "volgens fiscale grondslagen; is dat saldo negatief, dan staat het als passiefpost in de balans. Andere deelnemingen worden gewaardeerd zoals in de aangifte vennootschapsbelasting.\n"
            + "Vorderingen op deelnemingen en de overige vorderingen worden gewaardeerd tegen nominale waarde, onder aftrek van de nodige voorzieningen. "
            + "Effecten onder de financiële vaste activa worden gewaardeerd tegen verkrijgingsprijs of lagere marktwaarde.");
        Voeg("latentie", Waardering, "Latente belastingen", GrondslagOpname.BijPost, "BFvaLbv,BVorLbv,BVrzVvbVlb",
            "Voor tijdelijke verschillen tussen de waardering van activa en passiva in de jaarrekening en de fiscale waardering wordt een voorziening voor latente belastingverplichtingen gevormd. "
            + "Latente belastingvorderingen, ook voor te verrekenen verliezen, worden opgenomen voor zover het waarschijnlijk is dat er voldoende fiscale winst beschikbaar zal zijn. "
            + "De berekening is gebaseerd op de belastingtarieven die op balansdatum gelden of al wettelijk zijn vastgesteld. Latente belastingen worden gewaardeerd tegen nominale waarde.",
            null);
        Voeg("waardevermindering", Waardering, "Bijzondere waardeverminderingen van vaste activa", GrondslagOpname.Optioneel, "WWvi,WWfa",
            "Op elke balansdatum wordt beoordeeld of er aanwijzingen zijn voor een bijzondere waardevermindering van een vast actief. Is dat zo, dan wordt de realiseerbare waarde bepaald: "
            + "de hoogste van de opbrengstwaarde en de bedrijfswaarde. Is de boekwaarde hoger, dan wordt het verschil direct als last verwerkt. "
            + "Een eerder verwerkte bijzondere waardevermindering wordt teruggenomen als die niet meer bestaat, tot ten hoogste de boekwaarde zonder waardevermindering; bij goodwill wordt niet teruggenomen.",
            "Op elke balansdatum wordt beoordeeld of er aanwijzingen zijn dat een vast actief duurzaam minder waard is. Is dat zo, dan wordt het actief afgewaardeerd naar de lagere bedrijfswaarde; "
            + "het verschil komt direct ten laste van het resultaat. Een eerdere afwaardering wordt teruggenomen als die niet meer bestaat, tot ten hoogste de boekwaarde zonder afwaardering.");
        Voeg("voorraden", Waardering, "Voorraden", GrondslagOpname.BijPost, "BVrd",
            "Voorraden worden gewaardeerd tegen verkrijgings- of vervaardigingsprijs volgens de FIFO-methode (first in, first out), of tegen de lagere opbrengstwaarde. "
            + "De vervaardigingsprijs omvat de directe kosten en een opslag voor de indirecte productiekosten. "
            + "De opbrengstwaarde is de geschatte verkoopprijs onder aftrek van direct toerekenbare verkoopkosten; daarbij wordt rekening gehouden met incourantheid.",
            "Voorraden worden gewaardeerd tegen kostprijs of lagere opbrengstwaarde. De kostprijs omvat de verkrijgings- of vervaardigingsprijs en de kosten om de voorraden op hun huidige plaats "
            + "en in hun huidige staat te brengen. De opbrengstwaarde is de geschatte verkoopprijs onder aftrek van direct toerekenbare verkoopkosten; daarbij wordt rekening gehouden met incourantheid.");
        Voeg("projecten", Waardering, "Onderhanden projecten", GrondslagOpname.BijPost, "BPro",
            "Onderhanden projecten in opdracht van derden worden gewaardeerd tegen de gerealiseerde projectkosten, vermeerderd met de toegerekende winst (als die betrouwbaar is vast te stellen) "
            + "en verminderd met voorziene verliezen en gedeclareerde termijnen. Een project met een debetsaldo staat onder de vlottende activa, een project met een creditsaldo onder de kortlopende schulden.",
            "Onderhanden projecten in opdracht van derden worden gewaardeerd tegen de gerealiseerde projectkosten, vermeerderd met de toegerekende winst en verminderd met verwerkte verliezen "
            + "en gedeclareerde termijnen. Een project met een debetsaldo staat onder de vlottende activa, een project met een creditsaldo onder de kortlopende schulden.");
        Voeg("vorderingen", Waardering, "Vorderingen", GrondslagOpname.BijPost, "BVor",
            "Vorderingen worden bij eerste verwerking gewaardeerd tegen reële waarde inclusief transactiekosten en daarna tegen geamortiseerde kostprijs, wat doorgaans gelijk is aan de nominale waarde. "
            + "Voorzieningen wegens oninbaarheid worden in mindering gebracht.",
            "Vorderingen worden gewaardeerd tegen nominale waarde, onder aftrek van een voorziening wegens oninbaarheid die per vordering wordt bepaald. "
            + "Tenzij anders vermeld, hebben de vorderingen een looptijd korter dan een jaar.");
        Voeg("effecten", Waardering, "Effecten", GrondslagOpname.BijPost, "BEff",
            "Beursgenoteerde effecten in de handelsportefeuille worden gewaardeerd tegen reële waarde (de beurswaarde); waardeveranderingen komen direct in de winst-en-verliesrekening. "
            + "Andere effecten worden gewaardeerd tegen verkrijgingsprijs of lagere reële waarde. Transactiekosten komen direct ten laste van het resultaat.",
            "Effecten worden gewaardeerd tegen verkrijgingsprijs of lagere marktwaarde op balansdatum.");
        Beide("liquide", Waardering, "Liquide middelen", GrondslagOpname.BijPost, "BLim",
            "Liquide middelen bestaan uit kas, banktegoeden en deposito's met een looptijd korter dan twaalf maanden en worden gewaardeerd tegen nominale waarde. "
            + "Rekening-courantschulden bij banken staan onder de kortlopende schulden. Als liquide middelen niet ter vrije beschikking staan, wordt dat vermeld.");
        Voeg("herwaardering", Waardering, "Herwaarderingsreserve", GrondslagOpname.BijPost, "BEivHer",
            "De herwaarderingsreserve is gevormd voor het positieve verschil tussen de actuele waarde en de verkrijgingsprijs van activa die tegen actuele waarde worden gewaardeerd, "
            + "na aftrek van latente belastingverplichtingen. Bij verkoop valt het gerealiseerde deel vrij naar de overige reserves.",
            null);
        Voeg("herinvestering", Waardering, "Herinvesteringsreserve", GrondslagOpname.BijPost, "BEivFirHer",
            null,
            "Een boekwinst bij verkoop van een bedrijfsmiddel die fiscaal mag worden gereserveerd, staat in een herinvesteringsreserve zolang het voornemen tot herinvestering bestaat, ten hoogste drie jaar. "
            + "Bij herinvestering wordt de reserve in mindering gebracht op de verkrijgingsprijs van het nieuwe bedrijfsmiddel, voor zover dat fiscaal is toegestaan.");
        Voeg("voorzieningen", Waardering, "Voorzieningen", GrondslagOpname.BijPost, "BVrz",
            "Voorzieningen worden gevormd voor in rechte afdwingbare of feitelijke verplichtingen die op balansdatum bestaan, waarvoor waarschijnlijk een uitstroom van middelen nodig is "
            + "en waarvan de omvang betrouwbaar is te schatten. Ze worden gewaardeerd tegen de beste schatting van de bedragen die nodig zijn om de verplichtingen af te wikkelen, "
            + "tegen nominale waarde tenzij anders vermeld. Een vergoeding die waarschijnlijk van een derde wordt ontvangen, staat als actief in de balans.",
            "Voorzieningen worden gevormd voor toekomstige uitgaven die hun oorsprong vinden in feiten of omstandigheden van vóór balansdatum, die aan die periode zijn toe te rekenen "
            + "en waarvan het optreden redelijk zeker is. Ze worden gewaardeerd tegen de nominale waarde van de beste schatting van de uitgaven die nodig zijn om de verplichtingen af te wikkelen.");
        Voeg("oudedagsverplichting", Waardering, "Oudedagsverplichting", GrondslagOpname.BijPost, "BLasOdv,BSchOdv",
            "De oudedagsverplichting (ODV) is de verplichting aan de directeur-grootaandeelhouder die in 2017 is ontstaan door het afstempelen van het pensioen in eigen beheer. "
            + "De ODV wordt gewaardeerd tegen de contante waarde van de verwachte uitkeringen, tegen een disconteringsvoet van [percentage].",
            "De oudedagsverplichting (ODV) is de verplichting aan de directeur-grootaandeelhouder die in 2017 is ontstaan door het afstempelen van het pensioen in eigen beheer. "
            + "De ODV wordt gewaardeerd tegen de fiscale waarde, opgerent met het U-rendement (een maatstaf op basis van het rendement op Nederlandse staatsobligaties).");
        Voeg("langlopend", Waardering, "Langlopende schulden", GrondslagOpname.BijPost, "BLas",
            "Langlopende schulden worden bij eerste verwerking gewaardeerd tegen reële waarde inclusief direct toerekenbare transactiekosten en daarna tegen geamortiseerde kostprijs. "
            + "Het verschil met de aflossingswaarde komt op basis van de effectieve rente over de looptijd als rentelast in de winst-en-verliesrekening.",
            "Langlopende schulden worden gewaardeerd tegen nominale waarde.");
        Voeg("kortlopend", Waardering, "Kortlopende schulden", GrondslagOpname.BijPost, "BSch",
            "Kortlopende schulden worden bij eerste verwerking gewaardeerd tegen reële waarde en daarna tegen geamortiseerde kostprijs, wat doorgaans gelijk is aan de nominale waarde.",
            "Kortlopende schulden worden gewaardeerd tegen nominale waarde.");

        Voeg("resultaat", Resultaat, "Algemeen", GrondslagOpname.Altijd, "",
            "Het resultaat is het verschil tussen de opbrengstwaarde van de geleverde prestaties en de kosten en andere lasten over het jaar. "
            + "Opbrengsten worden verantwoord in het jaar waarin ze zijn gerealiseerd; verliezen zodra ze voorzienbaar zijn.",
            "Het resultaat wordt bepaald volgens goed koopmansgebruik, zoals dat geldt voor de fiscale winstbepaling: het verschil tussen de opbrengstwaarde van de geleverde prestaties "
            + "en de kosten en andere lasten over het jaar. Opbrengsten worden verantwoord in het jaar waarin ze zijn gerealiseerd; verliezen zodra ze voorzienbaar zijn.");
        Beide("omzet", Resultaat, "Netto-omzet", GrondslagOpname.BijPost, "WOmz",
            "De netto-omzet bestaat uit de opbrengsten uit de levering van goederen en diensten, na aftrek van kortingen en van belastingen die over de omzet worden geheven. "
            + "Opbrengsten uit de verkoop van goederen worden verwerkt zodra de belangrijke rechten en risico's van de eigendom zijn overgedragen aan de koper. "
            + "Opbrengsten uit diensten worden verwerkt naar rato van de verrichte prestaties op balansdatum.");
        Beide("kostprijs", Resultaat, "Kostprijs van de omzet", GrondslagOpname.BijPost, "WKpr",
            "De kostprijs van de omzet omvat de kosten die direct aan de netto-omzet zijn toe te rekenen, zoals de inkoopwaarde van de verkochte goederen en de kosten van uitbesteed werk.");
        Beide("overigeopbrengsten", Resultaat, "Overige bedrijfsopbrengsten", GrondslagOpname.BijPost, "WOvb",
            "Overige bedrijfsopbrengsten zijn opbrengsten die niet rechtstreeks samenhangen met de levering van goederen of diensten in de normale bedrijfsuitoefening. "
            + "Ze worden toegerekend aan het jaar waarop ze betrekking hebben.");
        Beide("kosten", Resultaat, "Kosten", GrondslagOpname.Altijd, "",
            "Kosten worden bepaald op historische basis en toegerekend aan het jaar waarop ze betrekking hebben.");
        Beide("personeel", Resultaat, "Lasten uit hoofde van personeelsbeloningen", GrondslagOpname.BijPost, "WPer",
            "Lonen, salarissen en sociale lasten worden op grond van de arbeidsvoorwaarden verwerkt in de winst-en-verliesrekening voor zover ze verschuldigd zijn aan werknemers.\n"
            + "De pensioenregelingen worden verwerkt volgens de verplichtingenbenadering: de over het jaar verschuldigde premie is een last. Vooruitbetaalde premies staan onder de overlopende activa "
            + "als ze leiden tot terugbetaling of lagere toekomstige betalingen; nog niet betaalde premies staan als verplichting in de balans.");
        Voeg("afschrijvingen", Resultaat, "Afschrijvingen", GrondslagOpname.BijPost, "WAfs",
            "Immateriële en materiële vaste activa worden afgeschreven vanaf het moment dat ze beschikbaar zijn voor het beoogde gebruik, over de verwachte economische levensduur. "
            + "Op terreinen en vastgoedbeleggingen wordt niet afgeschreven. Boekwinsten en -verliezen bij verkoop zijn begrepen onder de afschrijvingen.",
            "Immateriële en materiële vaste activa worden afgeschreven vanaf het moment van ingebruikneming, over de verwachte gebruiksduur en binnen de fiscale grenzen voor gebouwen, "
            + "vastgoed, overige bedrijfsmiddelen en goodwill. Op terreinen wordt niet afgeschreven. Boekwinsten en -verliezen bij verkoop zijn begrepen onder de afschrijvingen; "
            + "voor zover fiscaal toegestaan worden boekwinsten opgenomen in een herinvesteringsreserve.");
        Beide("subsidies", Resultaat, "Overheidssubsidies", GrondslagOpname.Optioneel, "",
            "Exploitatiesubsidies worden als bate verwerkt in het jaar waarin de gesubsidieerde kosten zijn gemaakt of de opbrengsten zijn gederfd, als het waarschijnlijk is dat ze worden ontvangen. "
            + "Subsidies voor investeringen in materiële vaste activa worden in mindering gebracht op het actief en zo via de afschrijvingen verwerkt.");
        Voeg("financieel", Resultaat, "Financiële baten en lasten", GrondslagOpname.BijPost, "WFbe,WOvt",
            "Rentebaten en rentelasten worden tijdsevenredig verwerkt, rekening houdend met de effectieve rente van de betreffende activa en passiva en met de transactiekosten van ontvangen leningen. "
            + "Dividenden van effecten en van deelnemingen die niet tegen nettovermogenswaarde worden gewaardeerd, worden verwerkt zodra het recht erop is verkregen. "
            + "Koersverschillen worden verwerkt in het jaar waarin ze ontstaan.",
            "Rentebaten en rentelasten worden tijdsevenredig verwerkt. Dividenden van effecten worden verwerkt zodra het recht erop is verkregen.");
        Voeg("deelnemingen", Resultaat, "Resultaat uit deelnemingen", GrondslagOpname.BijPost, "WRed",
            "Het resultaat uit deelnemingen die tegen nettovermogenswaarde worden gewaardeerd, is het aandeel van de vennootschap in hun resultaat. "
            + "Bij deelnemingen die tegen verkrijgingsprijs worden gewaardeerd, is het resultaat het gedeclareerde dividend.",
            "Als resultaat uit deelnemingen worden het ontvangen dividend, het gerealiseerde verkoopresultaat en afwaarderingen naar een lagere bedrijfswaarde verwerkt.");
        Voeg("belastingen", Resultaat, "Belastingen", GrondslagOpname.BijPost, "WBel",
            "De belasting over het resultaat wordt berekend over het resultaat voor belastingen, rekening houdend met verrekenbare verliezen uit voorgaande jaren (voor zover niet als "
            + "latente belastingvordering opgenomen), vrijgestelde winstbestanddelen en niet-aftrekbare kosten. Ook het effect van een gewijzigd belastingtarief op de latente belastingen wordt verwerkt.",
            "De belasting over het resultaat wordt berekend over het resultaat voor belastingen, rekening houdend met verrekenbare verliezen uit voorgaande jaren, vrijgestelde winstbestanddelen "
            + "en niet-aftrekbare kosten. Omdat de jaarrekening op fiscale grondslagen is opgesteld, worden geen latente belastingen gevormd.");

        return lijst;
    }
}
