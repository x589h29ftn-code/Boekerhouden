# Beveiliging van het administratiepakket

*Stand van zaken: 26 oktober 2026; besluiten B1–B5 in §7.*

Een administratiepakket bevat de hele financiële huishouding van een onderneming:

- bankmutaties, facturen en IBAN's;
- namen en adressen, en daarmee persoonsgegevens (AVG);
- straks ook toegang van ondernemers via internet.

Dit hoofdstuk beschrijft wat er kan misgaan, wat gebruikelijk is bij online boekhoudpakketten, en welke maatregelen
we nemen, per fase:

- **fase 1**: alleen op kantoor (besluit AD2), zoals de samenstel-app;
- **fase 2**: online voor ondernemers.

De maatregelen bouwen voort op de samenstel-app ([§7.8 onderzoek](../onderzoek/07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)).

## 1. Wat beschermen we, en tegen wie?

| Wat | Waarom gevoelig |
|---|---|
| Boekhouding (journaal, facturen, bank) | Bedrijfsgeheim. Moet **juist en volledig** blijven: de Belastingdienst en de jaarrekening steunen erop |
| Persoonsgegevens | Namen, adressen, IBAN's, soms gegevens van particulieren (zoals een factuur op naam van een persoon). Vallen onder de AVG |
| Documenten (facturen, bonnen) | Bevatten alles hierboven, en zijn een **aanvalsroute**: een upload kan kwaadaardig zijn |
| Inloggegevens en bankkoppeling | Daarmee kan iemand alles inzien |

| Bedreiging | Voorbeeld | Kans (fase 1 / fase 2) |
|---|---|---|
| **Factuurfraude** | Een "leverancier" stuurt een factuur met een ander IBAN, of een valse factuur van een bestaande leverancier | middel / **hoog** |
| **Inloggen met gestolen wachtwoord** | Phishing, of een wachtwoord dat ergens anders is gelekt (*credential stuffing*) | laag / **hoog** |
| **Bij een andere administratie kijken** | Een ondernemer verandert een ID in de adresbalk en ziet de cijfers van een andere klant | – / **hoog**, met de grootste impact |
| **Kwaadaardige upload** | Een PDF of XML die de lezer laat vastlopen, geheugen opvreet of bestanden van de server probeert te lezen (XXE) | laag / middel |
| **Ransomware of diefstal** | De server-pc wordt versleuteld of gestolen | middel / middel |
| **Fout of manipulatie in de boekhouding** | Achteraf een geboekte factuur wijzigen, boekingen verwijderen | middel / middel |
| **Onbedoeld lekken** | Klantgegevens in logbestanden, in git of in een AI-dienst | middel / middel |
| **Kwetsbaar onderdeel** | Een lek in een gebruikt pakket (NuGet) of in de server | laag / middel |

## 2. Wat is gebruikelijk voor zo'n pakket?

Gangbare online boekhoudpakketten in Nederland doen in het algemeen het volgende:

1. **Tweestapsverificatie**, voor iedereen verplicht of sterk aangeraden (authenticator-app; steeds vaker
   *passkeys*).
2. **Hosting in Nederland of de EU**, in een gecertificeerd datacenter, met versleutelde opslag en back-ups op
   een tweede locatie.
3. **Een ISO 27001-certificaat en/of een ISAE 3402 type II- of SOC 2-rapport** van een onafhankelijke
   auditor, zodat accountants en klanten kunnen steunen op de beheersing.
4. **Een verwerkersovereenkomst** met elke klant, een privacyverklaring en een procedure voor datalekken.
5. **Jaarlijkse pentest** en een *responsible disclosure*-regeling (`/.well-known/security.txt`).
6. **Onwijzigbare boekingen** met een doorlopende audit trail (fiscale bewaarplicht en controleerbaarheid), en
   een export als **XAF-auditfile**.
7. **Een bankkoppeling via een PSD2-vergunninghouder**: alleen meelezen, met toestemming die na een vaste
   termijn opnieuw gegeven moet worden.
8. **Rechten per gebruiker en per administratie**, en signalen bij afwijkende IBAN's en dubbele facturen.

Voor een klein kantoor is een certificaat (punt 3) in het begin niet nodig. Omdat het er op termijn mogelijk wel
komt (B4), volgen we nu al dezelfde maatregelen en leggen we ze vast met bewijs (§4.13). Dan is een certificaat of
rapport later vooral een kwestie van laten toetsen.

## 3. Wettelijk kader

| Regel | Wat het voor ons betekent |
|---|---|
| **AVG** | Bij het voeren van de administratie van een klant is het kantoor meestal **verwerker** en de ondernemer **verwerkingsverantwoordelijke**, dus een verwerkersovereenkomst per klant. Verder: verwerkingsregister, dataminimalisatie (geen BSN, geen kopie-ID), beveiliging "passend bij het risico" (art. 32), en een **datalek binnen 72 uur melden** bij de Autoriteit Persoonsgegevens (art. 33) |
| **Fiscale bewaarplicht** (art. 52 AWR) | **7 jaar** bewaren, in een vorm die binnen redelijke tijd te controleren is. Boekingen mogen daarom niet ongemerkt veranderen, en de administratie moet te exporteren zijn (XAF-auditfile) |
| **Btw-aftrek** | Alleen met een factuur **op naam van de onderneming**, met de verplichte gegevens. Het pakket gaat na of de naam van de onderneming op de factuur staat (`OpEigenNaam`), maar houdt automatisch verwerken daarom niet tegen: de naam op de factuur wijkt in de praktijk vaak af (handelsnaam, afkorting) zonder dat er iets mis is (besluit gebruiker, oktober 2026) |
| **Wwft en de NVKS** (kantoor) | De beroepsregels van het kantoor: toegangsbeheer, vertrouwelijkheid en dossiervorming. Dezelfde maatregelen dekken dit |
| **NIS2 / Cyberbeveiligingswet** | Niet van toepassing op een kantoor van deze omvang. Gebruik het wel als checklist |

## 4. Maatregelen per onderdeel

Legenda:

- ✅ gebouwd;
- **F1** nodig vóór gebruik op kantoor (vanaf plakje A6);
- **F2** nodig vóór de ondernemers online gaan.

### 4.1 Inloggen en sessies

| Maatregel | Fase |
|---|---|
| Wachtwoorden van minstens 12 tekens, met een lijst van verboden wachtwoorden, gehasht met PBKDF2 (Identity), en lockout na 5 pogingen. Zoals in de samenstel-app | ✅ (samenstel-app), F1 |
| **Tweestapsverificatie** (B1) met een authenticator-app (TOTP) **én passkeys** (WebAuthn: vingerafdruk, gezichtsherkenning of een beveiligingssleutel; bestand tegen phishing). De gebruiker kiest. Herstelcodes worden eenmalig getoond. Verplicht voor iedereen die online inlogt; op kantoor verplicht voor de beheerder en aan te zetten voor de anderen | ✅ (A6b; verplicht voor de beheerder), iedereen F2 |
| **Rate limiting** op inloggen en wachtwoord vergeten, per IP en per account (ASP.NET Core `RateLimiter`), tegen credential stuffing | F2 |
| Controle tegen **gelekte wachtwoorden**: een offline lijst, of de *k-anonymity*-dienst van een externe bron (dan gaan alleen de eerste 5 tekens van de hash naar buiten) | F2 |
| **Uitnodigen met een eenmalige link** die na 48 uur verloopt, in plaats van een tijdelijk wachtwoord (online) | F2 |
| Sessies verlopen na inactiviteit: op kantoor na 10 uur, online na 30–60 minuten, en altijd na 12 uur | F1 / F2 |
| **Opnieuw bevestigen** (wachtwoord of tweede factor) voor gevoelige handelingen: IBAN van een relatie wijzigen, gebruikers en rechten, export van de hele administratie, regels voor automatisch verwerken | F2 |
| Een e-mail bij inloggen vanaf een nieuw apparaat, en een overzicht van actieve sessies met "overal uitloggen" | F2 |
| Inloggen geslaagd en mislukt in het wijzigingslog | ✅ (samenstel-app), F1 |

### 4.2 Scheiding van administraties (multi-tenancy)

Dit is het belangrijkste punt zodra ondernemers online gaan. Eén fout betekent dat klant A de cijfers van klant B
ziet. Daarom nemen we drie lagen maatregelen, plus tests die bewijzen dat ze werken:

1. **In de code**: elke tabel met administratiegegevens heeft een `AdministratieId`, en EF Core filtert daar
   altijd op (een *global query filter*). Diensten krijgen de administratie uit de sessie, nooit uit de URL of uit
   wat de browser stuurt. Bij elke aanroep wordt gecontroleerd of de gebruiker lid is van die administratie.
2. **In de database**: PostgreSQL **Row Level Security** als tweede slot. De app zet per verzoek
   `app.administratie`, en een policy laat alleen die rijen zien. Een vergeten filter in de code geeft dan nog
   steeds geen gegevens van een ander.
3. **Rechten per administratie**:

   | Rol | Mag |
   |---|---|
   | Ondernemer | Documenten inlezen, factureren, bank bijwerken, eigen overzichten |
   | Ondernemer, alleen inzien | Overzichten en documenten bekijken (bijv. een compagnon of financier) |
   | Kantoor | Alles in de administraties die aan hem zijn toegewezen, ook memoriaal, periodes en regels |
   | Beheerder | Gebruikers, administraties aanmaken en toewijzen, systeeminstellingen. Ziet niet automatisch alle cijfers |

4. **Negatieve tests** horen bij elke dienst: probeer met een gebruiker van administratie A gegevens van B te lezen
   of te wijzigen, en verwacht een weigering. Zonder zo'n test wordt een dienst niet opgeleverd.
5. **ID's zijn GUID's**, dus niet te raden. Een weigering zegt "niet gevonden", zodat niet te zien is dat iets
   bestaat.

*Fase: F1 (ook op kantoor een goede gewoonte); tests en RLS verplicht vóór F2.*

*Gebouwd in A6a:* punt 1 (`Toegang` en de query filters in `AdministratieDbContext`, dat bovendien weigert op te
slaan in een andere dan de geopende administratie), punt 3 (rollen inzien, ondernemer en kantoor; de beheerder ziet
geen cijfers zonder lidmaatschap), punt 4 (`ToegangTests`) en punt 5. Row Level Security (punt 2) volgt vóór F2.

### 4.3 Integriteit van de boekhouding

| Maatregel | Fase |
|---|---|
| Een journaalpost is altijd in evenwicht en heeft een uniek boekstuknummer per dagboek (`Journaalcontrole`) | ✅ |
| **Geboekt is geboekt**: een journaalpost wordt nooit gewijzigd of verwijderd (✅ A6a: de app kan het niet en de database weigert het), maar gecorrigeerd met een tegenboeking (memoriaal of creditfactuur). Verkoopfacturen hebben een doorlopende nummering zonder gaten | F1 |
| **Afgesloten periodes** (na de btw-aangifte) zijn niet meer te bebouwen. Heropenen kan alleen door het kantoor, met een reden in het log | F1 |
| **Hash-keten** over het journaal: elke post krijgt een SHA-256 over de inhoud plus de hash van de vorige post. Een controle in Beheer laat zien of er buiten de app iets in de database is veranderd | ✅ (A6a; ketencontrole op de pagina Journaal) |
| **Wijzigingslog alleen toevoegen**: de databaserol van de app mag in de logtabel alleen `INSERT` (geen `UPDATE` of `DELETE`) | ✅ (A6a, met een trigger die `UPDATE` en `DELETE` weigert; aparte databaserollen F2) |
| **Documenten onveranderlijk**: opgeslagen onder hun SHA-256, gekoppeld aan de journaalpost, en nooit overschreven | F1 |
| Export als **XAF-auditfile** en als archief (PDF's, bijlagen en JSON) voor de bewaarplicht | F1 (A12) |

### 4.4 Financiële fraude en fouten

Bij gangbare pakketten zijn dit de signalen die het meeste schade voorkomen:

| Maatregel | Fase |
|---|---|
| **Afwijkend IBAN**: een bekende leverancier (herkend op btw-nummer, KvK of naam) met een ander IBAN op de factuur wordt nooit automatisch verwerkt. De melding vraagt om navraag via een bekend telefoonnummer (`Inkoopverwerker`) | ✅ |
| **Dubbele factuur**: zelfde leverancier en factuurnummer (ongeacht leestekens), of zelfde datum en bedrag | ✅ |
| **Onbekende leverancier**: wordt nooit automatisch verwerkt. De gebruiker bevestigt eerst de nieuwe relatie | ✅ |
| **Niet op naam van de onderneming**: geen belemmering voor automatisch verwerken (zie hierboven) | ✅ |
| **Bedraglimiet voor automatisch verwerken** (B2): standaard **geen** limiet. Per administratie is er een limiet in te stellen; daarboven bevestigt altijd een mens | ✅ |
| **IBAN van een relatie wijzigen**: alleen met opnieuw bevestigen (wachtwoord), met oud en nieuw IBAN in het log. Een melding aan de andere gebruikers van de administratie, en online vier ogen | ✅ bevestigen en log, F2 melding en vier ogen |
| **Afwijkend bedrag**: na minstens drie facturen van een leverancier is een bedrag van meer dan twee keer het hoogste eerdere bedrag een signaal in Te verwerken | ✅ |
| **Het pakket betaalt niet zelf.** Betaalbestanden (SEPA) worden in de bank ondertekend, met de bankpas of app van de ondernemer | uitgangspunt |

### 4.5 Uploads en documenten

Documenten van buiten zijn de grootste technische aanvalsroute.

| Maatregel | Fase |
|---|---|
| **XML zonder DTD** (`DtdProcessing.Prohibit`, geen resolver) bij UBL, CAMT.053 en elke andere XML: geen XXE of *billion laughs* | ✅ |
| **Grenzen**: een PDF hoogstens 20 MB en 50 pagina's, en bijlagen in een PDF hoogstens 5 MB. Een bestand wordt begrensd ingelezen, dus nooit eerst helemaal in het geheugen | ✅ |
| Een onleesbare of beveiligde PDF geeft een nette melding (`DocumentException`) en laat de app nooit vallen | ✅ |
| **Type op inhoud, niet op extensie**: PDF aan `%PDF-`, XML aan de root, en afbeeldingen aan hun *magic bytes*. Al het andere wordt geweigerd | ✅ PDF/XML, F1 afbeeldingen |
| **Herkennen in een apart proces** met een tijd- en geheugenlimiet (bijv. 30 seconden en 512 MB), zodat een kwaadaardige PDF de app niet kan raken. Online in een aparte container zonder netwerk | F2 |
| Uploads buiten de webmap (in de database, met de SHA-256 als sleutel). Downloaden met `X-Content-Type-Options: nosniff` en een CSP-sandbox; alleen een PDF wordt getoond, al het andere is `Content-Disposition: attachment`. Nooit HTML of SVG van een gebruiker in de app tonen | ✅ |
| **Virusscan** (B3) met ClamAV (open source) op de server, **vóórdat** het pakket een bestand opent. Een besmet bestand gaat in quarantaine en wordt niet verwerkt. Is de scanner niet bereikbaar, dan wacht het bestand tot een geslaagde scan (*fail closed*). Elke upload in de app gaat eerst langs de scanner; zonder ingestelde scanner weigert de app buiten Development elke upload | ✅ koppeling en upload, F1 installatie op de server-pc |
| **Facturen per e-mail**: alleen bijlagen van toegestane typen, controle op SPF, DKIM en DMARC, en een lijst met bekende afzenders per administratie. De rest gaat naar quarantaine | F2 |

### 4.6 Herkenning, AI en bankkoppeling

- **Herkenning gebeurt lokaal** (UBL, PdfPig en straks Tesseract): er gaat geen factuur naar buiten.
- **Een AI-model als extra motor** (besluit AD3) kan alleen met:
  - een verwerkersovereenkomst;
  - verwerking in de EU;
  - de toezegging dat er niet op de gegevens getraind wordt;
  - een instelling per administratie (standaard uit);
  - een vermelding in de privacyverklaring.
- **Bankkoppeling (PSD2)** alleen via een vergunninghouder met **alleen leesrechten**:
  - de tokens staan versleuteld (ASP.NET Core Data Protection);
  - de toestemming verloopt na de wettelijke termijn, en de ondernemer geeft opnieuw toestemming;
  - het pakket krijgt nooit inloggegevens van de bank.

### 4.7 De webapplicatie

| Maatregel | Fase |
|---|---|
| Alleen **HTTPS** (TLS 1.2 of hoger), met HSTS | ✅ (samenstel-app), F1 |
| **Beveiligingsheaders**: een strikte `Content-Security-Policy` (`default-src 'self'`, `frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin` en `Permissions-Policy` (camera alleen voor het fotograferen van een bon) | ✅ (A6a; zonder inline scripts) |
| **Cookies** met `Secure`, `HttpOnly` en `SameSite=Lax`/`Strict`, plus antiforgery voor formulieren | ✅ (A6a: `HttpOnly`, `SameSite=Strict`, antiforgery; `Secure` zodra HTTPS) |
| **Blazor Server**: elke actie controleert op de server opnieuw het recht (nooit vertrouwen op wat de browser laat zien), met grenzen aan de berichtgrootte en het aantal circuits per gebruiker, en geen `MarkupString` met invoer van gebruikers | F1 |
| **Database alleen via EF Core** met parameters, nooit SQL aan elkaar plakken | ✅ |
| Foutpagina's zonder technische details; details alleen in het serverlog | ✅ (samenstel-app) |
| **Data Protection-sleutels** bewaard en versleuteld (DPAPI of een certificaat), zodat cookies een herstart overleven en de sleutels niet leesbaar zijn | ✅ (A6a: in de database; op Windows met DPAPI versleuteld) |

### 4.8 Opslag, sleutels en logboeken

- **Versleutelde schijf** op de server (BitLocker; op Linux LUKS), en een **versleutelde back-up**, zoals in de
  samenstel-app (§7.5 onderzoek).
- **De database luistert alleen lokaal.** Er zijn aparte databaserollen voor de app (geen superuser, geen DDL),
  voor migraties en voor de back-up.
- **Geheimen nooit in git**: verbindingsgegevens en sleutels staan in de omgeving of in een versleuteld bestand
  op de server. Secret scanning in GitHub staat aan.
- **Logboeken zonder klantgegevens**: geen bedragen, IBAN's, namen of documentinhoud in het technische log.
  Die staan alleen in het wijzigingslog in de database, en dat is beveiligd.
- **Geen echte documenten in de repository**: echte facturen voor tests staan in `testdata/` (buiten git), zoals
  de afspraak al is.

### 4.9 Beschikbaarheid en herstel

| Maatregel | Fase 1 | Fase 2 |
|---|---|---|
| Back-up | Elke nacht, versleuteld, op 3 plekken waarvan één offline (USB-wissel) | Ook doorlopend: PostgreSQL WAL-archivering (herstel tot op het kwartier), naar een tweede locatie met *object lock* (niet te wissen, tegen ransomware) |
| Hersteltest | Twee keer per jaar | Elk kwartaal, automatisch op een testserver |
| Bewaking | Status van de back-up in Beheer | Uptime, schijfruimte, certificaat, mislukte inlogpogingen en back-up, met een melding per e-mail of telefoon |
| Herstel na een ramp | Terugzetten op een vervangende pc (doel: binnen 1 werkdag) | Nieuwe server en de laatste back-up (doel: 4 uur) |

### 4.10 Ontwikkeling en onderdelen van derden

- **Alleen open-source pakketten**, met een licentiecontrole vóór het toevoegen (werkafspraak) en vaste versies in
  `Directory.Packages.props`.
- In CI: `dotnet list package --vulnerable --include-transitive`, Dependabot-meldingen, code scanning (de
  analyzers staan al op *warnings as errors*) en secret scanning.
- **Elke wijziging via een pull request met tests.** Voor rechten zijn negatieve tests verplicht (§4.2).
- Een **SBOM** (CycloneDX) bij elke release, zodat bij een nieuw lek direct te zien is of het pakket geraakt is.

### 4.11 Online gaan (fase 2): de server

- **Hosting in Nederland** (B5). De partij is nog niet gekozen. Keuzecriteria om de partijen in kaart te brengen:

  | Criterium | Eis |
  |---|---|
  | Locatie | Datacenter én back-up in Nederland (twee locaties) |
  | Zeggenschap | Bij voorkeur een Nederlands of Europees bedrijf zonder moederbedrijf buiten de EU (geen toegang van buitenlandse overheden via hun wetgeving) |
  | Zekerheid | ISO 27001 (en liefst ISAE 3402 of SOC 2), verwerkersovereenkomst onder Nederlands recht |
  | Techniek | VPS of beheerde server met snapshots, privénetwerk, basisbescherming tegen DDoS en IPv6. Bij voorkeur beheerde PostgreSQL met herstel tot op het moment (PITR) |
  | Beheer | Nederlandstalige support, een duidelijke exit (data mee kunnen nemen), een voorspelbare prijs |
- **Netwerk**:
  - alleen poort 443 open, voor een reverse proxy (bijv. Caddy, met automatische certificaten);
  - de app en de database zijn alleen intern bereikbaar;
  - beheer alleen via een VPN (WireGuard), met inloggen op sleutel (geen wachtwoorden, geen root).
- **Onderhoud**: automatische beveiligingsupdates voor het besturingssysteem, en updates van de app via een vaste
  releaseprocedure.
- **Testen en melden**:
  - een **pentest** door een externe partij vóór de livegang, en daarna jaarlijks;
  - `security.txt` en een *responsible disclosure*-pagina.
- **Bescherming tegen overbelasting**: basisbescherming tegen DDoS van de hoster, en rate limiting in de app.

### 4.12 Organisatie

- **Verwerkersovereenkomst** met elke ondernemer, plus een privacyverklaring en het verwerkingsregister.
- **Incidentprocedure**:
  - wie beslist, wie meldt bij de Autoriteit Persoonsgegevens (binnen 72 uur), wie informeert de klant;
  - een draaiboek en een oefening per jaar.
- **Toegangsreview** elk kwartaal: wie heeft toegang tot welke administratie? Vertrokken medewerkers en
  beëindigde klanten gaan er direct uit.
- **Einde van de klantrelatie**:
  - export (XAF, PDF's en bijlagen) aan de ondernemer;
  - daarna alleen-lezen tot het einde van de bewaartermijn;
  - dan verwijderen, met een vermelding in het log.
- **Bewustwording**: phishing en factuurfraude kort uitleggen aan medewerkers en ondernemers. Het pakket helpt
  met de signalen uit §4.4.

### 4.13 Voorbereiden op certificering (B4)

ISO 27001 of een ISAE 3402-rapport toetst of maatregelen **bestaan, werken en worden bijgehouden**. Daarom
houden we vanaf nu bij:

- een **register van maatregelen**: per maatregel uit dit hoofdstuk het thema uit ISO 27001:2022 bijlage A
  (organisatorisch, mensen, fysiek, technologisch), de eigenaar, de status en waar het bewijs staat;
- een **risicoanalyse** (§1), die elk jaar en bij grote wijzigingen (zoals online gaan) wordt herzien;
- **bewijs uit het werk zelf**:
  - pull requests met review en tests (wijzigingsbeheer);
  - het wijzigingslog (toegang en handelingen);
  - de verslagen van de hersteltests (back-up);
  - de toegangsreviews (elk kwartaal);
  - het pentestrapport en de opvolging ervan;
- **leveranciersbeheer**: de hoster, de bankkoppeling en eventuele AI-diensten, elk met een verwerkersovereenkomst
  en hun eigen certificaten;
- een **jaarlijkse managementreview** van één pagina: incidenten, risico's, wat er verbeterd is.

Een ISAE 3402 type II toetst over een periode (meestal een jaar) of de maatregelen werkten. Begin daarom ruim vóór
het moment waarop een klant of accountant erom vraagt.

## 5. Wat er nu al gebouwd is

1. **XML veilig lezen** (UBL, CAMT.053): geen DTD, geen externe verwijzingen.
2. **Begrensd inlezen** van PDF's (grootte, pagina's, bijlagen), met een nette fout bij een beschadigd of
   beveiligd bestand.
3. **Type op inhoud** voor PDF en XML.
4. **Virusscan vóór het openen** met ClamAV (clamd), fail closed; te proberen met
   `boekerhouden herken factuur.pdf --clamav 127.0.0.1:3310`.
5. **Signalen in het inkoopvoorstel**:
   - afwijkend IBAN bij een bekende leverancier;
   - dubbele factuur;
   - onbekende leverancier;
   - bedrag boven de limiet (als die is ingesteld);
   - gesloten periode.

   Elk signaal houdt automatisch verwerken tegen.
6. **Scheiding van administraties** (A6a): alleen via `Toegang`, query filter per administratie, negatieve tests.
7. **Onwijzigbaar journaal** (A6a): hash-keten met volgnummers, triggers tegen wijzigen en verwijderen van boekingen
   en het wijzigingslog, en een ketencontrole die ook vindt wat er met uitgeschakelde triggers is veranderd.
8. **Beveiligingsheaders** (A6a): strikte CSP zonder inline scripts, `frame-ancestors 'none'`, `X-Frame-Options:
   DENY`, `nosniff`, `Referrer-Policy` en `Permissions-Policy`; Data Protection-sleutels in de database.
9. **Tweestapsverificatie** (A6b): authenticator-app met herstelcodes en passkeys; verplicht voor de beheerder.
10. **Inkoop** (A7): virusscan vóór elke upload met quarantaine, opslag op hash, documenten alleen via de
    toegangscontrole en met een CSP-sandbox, het signaal *afwijkend bedrag*, de bedraglimiet per administratie, en
    het IBAN van een relatie alleen wijzigen na het wachtwoord, met oud en nieuw in het log. Regels en instellingen
    alleen door het kantoor.
11. **Factuurviewer** (A7): pdf.js staat lokaal in de app (geen CDN), zodat de CSP alleen eigen scripts blijft
    toestaan; `isEvalSupported` staat uit. De tekst uit een aangewezen gebied haalt de server zelf uit de PDF,
    via `Toegang` (een buitenstaander krijgt niets). Het externe venster praat alleen met vensters van dezelfde
    site (`BroadcastChannel`) en haalt de PDF via hetzelfde beveiligde adres.
12. **Leren met herbevestiging**: een geleerde leverancier met een hoger bedrag (boven 1,25 keer het hoogste
    bevestigde), een ander IBAN of een nieuwe postcode komt ter controle naar boven, en daarna moet het patroon
    opnieuw worden bevestigd voordat het pakket weer zelf boekt.
13. **Kantoorinstellingen**: alleen de beheerder; dat controleert de dienst zelf (rol uit de gebruikersopslag),
    niet alleen de pagina. Elke wijziging komt met oud en nieuw in het log, ook bij een administratie.
14. **Documentmap**: de map moet een volledig pad zijn zonder `..`; de variabelen (naam van de administratie,
    leverancier) worden ontdaan van padtekens en verboden namen, zodat een naam nooit buiten de gekozen map kan
    schrijven. Bestaande bestanden worden nooit overschreven. Online (F2) mag alleen de beheerder een map kiezen,
    of verdwijnt de keuze: dan schrijft een server niet naar een map die een gebruiker opgeeft.
15. **Windows-app voor de tests**: geen netwerk: geen webserver en geen databaseserver, alleen een SQLite-bestand
    in de map van de Windows-gebruiker. Er is geen inloggen; wie de pc-sessie heeft, is de gebruiker (de pc zelf
    beschermt met Windows-aanmelding en BitLocker). Boekingen en het log blijven onwijzigbaar (triggers, hash-keten),
    uploads worden niet door het pakket gescand (eigen bestanden op de eigen pc; de virusscanner van Windows doet
    dat al), en een document opent alleen als PDF of XML. Bedoeld
    voor tests op één pc, niet voor een kantoor met meerdere gebruikers (daarvoor de server, §4.11).

Bekende beperking: de databaserol van de app is eigenaar van de tabellen en kan de triggers dus uitschakelen. De
hash-keten maakt zo'n wijziging zichtbaar; aparte databaserollen (app zonder eigenaarschap) volgen vóór F2.

## 6. Volgorde

| Plakje | Beveiliging die erbij hoort |
|---|---|
| A6 webapp ✅ | Administraties met rechten, global query filters, negatieve tests, beveiligingsheaders, Data Protection-sleutels, tweestapsverificatie met TOTP en passkeys (verplicht voor de beheerder), geboekt = onwijzigbaar, hash-keten, log alleen toevoegen. Nog open: het register van maatregelen (§4.13) |
| A7 inkoop ✅ | Scan vóór elke upload, quarantaine, uploads op type en grootte, opslag op hash, bedraglimiet als optie per administratie, afwijkend bedrag, IBAN wijzigen met bevestiging. Nog open: ClamAV installeren op de server-pc, melding aan andere gebruikers bij een IBAN-wijziging |
| A8 bank | Dubbele mutaties weigeren (sleutel per mutatie ✅ in het domein), bankregels alleen door het kantoor |
| A10 periodes | Afgesloten periodes, heropenen met een reden |
| vóór online (F2) | Tweestapsverificatie voor iedereen, Row Level Security, rate limiting, herkennen in een apart proces, hoster kiezen met de criteria uit §4.11, pentest, verwerkersovereenkomsten |

## 7. Besluiten (26 oktober 2026)

| # | Onderwerp | Besluit |
|---|---|---|
| B1 | Tweede factor | **TOTP én passkeys**. De gebruiker kiest; verplicht voor de beheerder en voor iedereen online |
| B2 | Bedraglimiet automatisch verwerken | **Standaard geen limiet**, maar per administratie in te stellen als optie |
| B3 | Virusscan | **Al op kantoor**: ClamAV op de server-pc. Elk bestand wordt gescand voordat het pakket het opent |
| B4 | Certificering | **Mogelijk op termijn** (ISO 27001 of ISAE 3402). Daarom nu al het register van maatregelen en bewijs bijhouden (§4.13) |
| B5 | Hosting | **In Nederland**. De partijen zijn nog niet in kaart gebracht; de criteria staan in §4.11 |
