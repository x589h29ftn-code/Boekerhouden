# 11. Vragenlijst om het plan strakker te maken

> Bij elke vraag staat **waarom** die ertoe doet en een **voorstel** (wat we doen als je niets
> anders aangeeft).
>
> **Prioriteit**
> - **P1**: nodig vóór de start van de bouw (bepaalt architectuur of scope).
> - **P2**: nodig vóór de betreffende fase.
> - **P3**: kan later.
>
> Je kunt antwoorden met het nummer en "akkoord" of een afwijkend antwoord.

## A. Organisatie en gebruik

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| A1 | P1 | Hoeveel gebruikers krijgt het systeem in totaal, en hoeveel werken er tegelijk? | Serverformaat, licenties, aantal rollen | 5–15 gebruikers, maximaal 10 tegelijk |
| A2 | P1 | **Werken medewerkers ook buiten kantoor (thuis, bij klanten)? Moet dat ook zonder internet- of VPN-verbinding kunnen?** | Bepaalt de architectuur: kantoorserver (model A) of offline desktop (model B/C), zie [§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers) | Thuis en bij klanten via VPN of beveiligde toegang; offline niet nodig → model A |
| A3 | P1 | Is er een **kantoorserver** (Windows Server, VM, NAS) of een externe ICT-partner die die kan leveren en beheren? Hoe is de back-up nu geregeld? | Hosting en back-up van de centrale database | Een Windows Server-VM bij de ICT-partner, met nachtelijke back-up en een kopie buiten de server |
| A4 | P1 | Welke **rollen** zijn er, en wie mag wat? Is **review door een tweede persoon** altijd verplicht, of afhankelijk van de klant of het opdrachttype? | Rechtenmodel en goedkeuringsflow | Rollen medewerker, reviewer, accountant en beheerder ([§7.8](07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)). Review verplicht bij samenstellen BV, optioneel bij winstaangifte EZ/VOF |
| A5 | P2 | Moeten medewerkers **alle klanten** zien, of alleen de klanten van hun eigen team of portefeuille? | Autorisatie per klant | Iedereen ziet alles, met een filter "mijn klanten" |
| A6 | P2 | Inloggen: is gebruikersnaam en wachtwoord genoeg, of wil je **tweestapsverificatie** (authenticator-app)? Of inloggen met het **Microsoft 365-account** (SSO)? | Beveiliging, gebruiksgemak | Wachtwoord, plus verplichte tweestapsverificatie bij toegang buiten kantoor. SSO later |
| A7 | P2 | Hoeveel dossiers per jaar, ongeveer verdeeld over **BV micro, BV klein, EZ en VOF**? | Prioriteit van sjablonen en de omvang van de pilot | Graag de aantallen; daarop baseren we de volgorde |

## B. Administraties en import

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| B1 | P1 | **Welke boekhoudpakketten** gebruiken je klanten (top 5 met ongeveer de aantallen)? Voert het kantoor zelf administraties (in welk pakket)? | Testbestanden, koppelsjablonen, XAF-eigenaardigheden | We vragen per pakket 2–3 XAF-bestanden op |
| B2 | P1 | Kun je **10–20 XAF-bestanden** van afgeronde dossiers aanleveren, met het bijbehorende Visionplanner-rapport? Bij voorkeur geanonimiseerd, of met afspraken over vertrouwelijkheid. | Golden-master-tests: onze uitkomst moet gelijk zijn aan die van Visionplanner | Ja, via een beveiligde map, niet via GitHub |
| B3 | P2 | Heeft het kantoor een **eigen standaard-rekeningschema** voor de administraties die het zelf voert? Hebben die rekeningen RGS-codes? | Dat wordt het eerste en beste koppelsjabloon | Als het bestaat, is dat koppelsjabloon nr. 1 |
| B4 | P2 | **Historische cijfers**: het rapport toont kengetallen en een meerjarenoverzicht over 5 jaar. Hoe halen we de 4 vorige jaren binnen voor bestaande klanten? Kan Visionplanner per klant een proefbalans of rubriekensaldi naar Excel exporteren? | Zonder historie zijn hoofdstuk 3, 4 en 6 van het BV-rapport leeg | Eenmalig importeren uit Excel (één kolom per jaar, op rubriekniveau) |
| B5 | P2 | Wat is de **rekening-courant met de directeur/aandeelhouder** en hoe wordt privégebruik geboekt bij EZ/VOF (aparte privérekeningen per vennoot)? | Kapitaalsvergelijking en verloop ondernemingsvermogen | Privérekeningen per vennoot herkennen via rubrieken *Privéonttrekkingen/-stortingen {vennoot}* |

## C. Werkprogramma en materialiteit

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| C1 | P1 | Hoe bepaalt het kantoor de **materialiteit**? Bijv. 1–2% van de omzet, 5% van het resultaat vóór belastingen, of 1% van het balanstotaal. Eén bedrag, of ook een **verwaarloosbaarheidsgrens**? | Voorstelberekening in de planning | Systeemvoorstel: laagste van (1% omzet, 2% balanstotaal), handmatig aan te passen; daarnaast een drempel voor mutaties (bijv. 10% en ≥ € 5.000) |
| C2 | P1 | **Op welk niveau** wordt "materieel" aangevinkt: per hoofdpost (Vorderingen), of per subpost (Handelsdebiteuren, Overlopende activa)? | Opbouw van de posten in het sjabloon | Per hoofdpost, zoals in de dossierboom van Visionplanner. Optioneel per subpost |
| C3 | P1 | Wat gebeurt er bij **niet-materiële posten**? Alleen een cijferbeoordeling (huidig, vorig, delta en toelichting), of ook een kort vast basisprogramma? | Inhoud en omvang van het sjabloon | Cijferbeoordeling, plus een paar "altijd"-stappen voor kritieke posten (liquide middelen aansluiten, eigen vermogen aansluiten, belastingen) |
| C4 | P1 | Moet het systeem een **voorstel** doen voor welke posten materieel zijn, of vinkt de gebruiker alles zelf aan? | UX en review | Voorstel met vlag ⚑; de gebruiker beslist altijd zelf |
| C5 | P2 | **Wie mag standaardstappen verwijderen**: iedereen, of alleen reviewer of accountant? Is een **reden** verplicht? | Dossierkwaliteit en review | Iedereen mag, maar met verplichte reden. De reviewer ziet een overzicht van verwijderde stappen. Verplichte stappen alleen op "n.v.t." zetten |
| C6 | P2 | Hoe wordt **afgetekend**: per stap (initialen en datum) en per post goedkeuren? Twee niveaus (uitgevoerd en gereviewd) of drie (plus accountant)? | Goedkeuringsflow | Per stap "uitgevoerd", per post "gereviewd", en het dossier als geheel "goedgekeurd door accountant" |
| C7 | P2 | Is er een **bestaand werkprogramma** (Word, Excel of het SRA-programma in Visionplanner) waarmee we beginnen? Wie stelt het kantoorwerkprogramma op, en hoeveel uur heeft die persoon? | Inhoud is minstens de helft van het werk ([§8.3 R1](08-roadmap-risico-s-en-beslispunten.md#83-risicos)) | Eén vaktechnisch verantwoordelijke, 1–2 dagen per week tijdens de bouw |
| C8 | P2 | Mogen eigen toegevoegde stappen worden **voorgesteld voor het kantoorsjabloon** (een leerlus)? | Sjabloonbeheer | Ja, de beheerder beslist |

## D. Opdrachttypen en rapporten

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| D1 | P1 | Welke **opdrachttypen** voeren jullie uit? Bijv. samenstellen BV (met verklaring), winstaangifte EZ/VOF (zonder verklaring, "Geen accountantscontrole toegepast"), samenstellen EZ/VOF **met** verklaring, administratieve dienstverlening | Sjablonen voor werkprogramma en rapport, voetteksten | MVP: *Samenstellen BV (micro/klein)* en *Winstaangifte EZ/VOF*. Andere typen later |
| D2 | P1 | **BV micro**: maken jullie daarvoor hetzelfde rapport als BV klein (accountantsrapport en volledige toelichting), of een beknopte versie? Graag een voorbeeld. | Rapportsjabloon micro | Hetzelfde sjabloon als klein, met minder toelichtingen (voorwaarden per grootte) |
| D3 | P1 | **VOF**: hoe presenteren jullie de **winstverdeling en het kapitaal per vennoot**? Graag een voorbeeldrapport VOF (het aangeleverde voorbeeld is een eenmanszaak). | Rapportsjabloon VOF en de rubricering van privérekeningen per vennoot | Verloop ondernemingsvermogen per vennoot naast elkaar, plus een winstverdelingstabel |
| D4 | P1 | **Deponering**: SBR en publicatie vallen buiten de scope. Hoe deponeren jullie straks de BV-jaarrekeningen bij de KvK? Blijft Visionplanner daarvoor in gebruik, of via de KvK-website of een ander pakket? | Anders ontstaat er een gat in het proces | Een publicatieversie (PDF) genereren, en voorlopig deponeren via de KvK-dienst of een bestaand pakket. SBR in een latere fase |
| D5 | P2 | **Fiscale positie BV**: welke correcties komen standaard voor (niet-aftrekbare kosten, boetes, gemengde kosten, giften, KIA, verliesverrekening, fiscale eenheid)? Wordt de Vpb altijd in het rapport berekend? | Omvang van de Vpb-module | De correcties zoals in het voorbeeld, plus verliesverrekening, KIA en de fiscale eenheid-variant |
| D6 | P2 | **Fiscale winst EZ/VOF**: moet het rapport een fiscale winstberekening bevatten (ondernemersaftrek, MKB-winstvrijstelling, FOR), of gebeurt dat alleen in de aangiftesoftware? | Omvang van de IB-module | Niet in het rapport (zoals in het voorbeeld); hooguit een interne berekening in het dossier |
| D7 | P2 | **Kengetallen en meerjarenoverzicht**: altijd dezelfde set als in het voorbeeld? Ook voor EZ/VOF? | Kengetallenbibliotheek | De set uit het voorbeeld voor BV; voor EZ/VOF uit, en per dossier aan te zetten |
| D8 | P2 | **Word-export**: mag een in Word bewerkt rapport het definitieve rapport zijn? Dan moet de definitieve PDF terug in het dossier. Of is Word alleen voor intern gebruik of concepten? | Dossierintegriteit ([§7.6](07-technische-architectuur.md#76-rapportgeneratie-één-model-meerdere-uitvoerformaten)) | Definitief altijd uit de app (PDF). Word alleen voor concepten en nabewerking, met een upload-verplichting als dat het definitieve rapport wordt |
| D9 | P2 | **Huisstijl en handtekening**: kun je logo, kleuren en lettertype als bestanden aanleveren, en de **gescande handtekening** per tekenbevoegde accountant? | Rapportopmaak | Ja. De handtekening staat per gebruiker opgeslagen en wordt alleen geplaatst bij definitief uitbrengen door die accountant |
| D10 | P2 | **W&V-specificaties**: in beide voorbeelden zijn dat de grootboekrekeningen per rubriek. Is dat altijd zo (automatisch, met de omschrijving aan te passen, samen te voegen of te verbergen)? | Specificatielogica | Ja: automatisch uit het grootboek, met per regel een override voor de omschrijving en samenvoegen |
| D11 | P3 | Is er een **Engelstalig rapport** nodig? | Vertaling van teksten en labels | Nee, alleen Nederlands |
| D12 | P3 | Wat is het **concept-proces richting de klant**: concept-PDF mailen en de bevestiging (LOR) per mail of op papier terugkrijgen? Is digitale ondertekening gewenst? | Afrondingsflow | Concept-PDF met watermerk; de ondertekende LOR uploaden als bijlage. Digitaal ondertekenen later |

## E. Dossier, bijlagen en klantgegevens

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| E1 | P2 | Waar staan **klantgegevens en Wwft-dossiers** nu (CRM, Visionplanner Core, Excel, praktijkmanagement)? Is er een eenmalige import van het klantenbestand nodig? | Klantbeheer en migratie | Eenmalige Excel-import van klanten en functionarissen |
| E2 | P2 | Waar staan **bijlagen** nu (SharePoint, fileserver)? Moet onze app ze zelf opslaan, of verwijzen naar een bestaande mappenstructuur? | Opslag en back-up | De app slaat zelf op (vast in het dossier en met hash beveiligd) |
| E3 | P2 | **Gesloten dossiers**: zoals in Visionplanner nooit heropenen (alleen kopiëren), of heropenen door een accountant met reden? | Afsluitbeleid | Heropenen alleen door een accountant, met reden. De wijzigingen worden als addendum gelogd |
| E4 | P3 | Wat gebeurt er met de **oude Visionplanner-dossiers**: alleen PDF-archief, of ook iets anders? | Migratie | PDF-archief per klant, plus de vorig-jaarcijfers via import (B4) |

## F. Project

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| F1 | P1 | **Deadline**: wanneer moet de eerste versie bruikbaar zijn? Bijv. een pilot op boekjaar 2025 in het voorjaar van 2027, of een volledige overstap voor boekjaar 2026 (seizoen 2027–2028)? | Planning en scope | Pilot in het seizoen 2027 parallel aan Visionplanner; overstap voor boekjaar 2026 |
| F2 | P1 | **Wie bouwt**: intern met Claude Code, een freelance .NET-ontwikkelaar, of een bureau? Wat is het **budget** ongeveer? | Planning, review en continuïteit | Eén vaste ontwikkelaar (eventueel freelance) met AI-ondersteuning. De vaktechniek blijft intern |
| F3 | P1 | Is deze **GitHub-repository privé**? Wie krijgt er toegang toe? | Vertrouwelijkheid van het ontwerp; testdata horen er nooit in | Privé. Testdata nooit in de repository, alleen geanonimiseerde fragmenten |
| F4 | P3 | Wil je dit later ook aan **andere kantoren** aanbieden? | Dat verandert de eisen voor multi-tenancy, licenties en support flink | Nee, alleen voor eigen gebruik |
