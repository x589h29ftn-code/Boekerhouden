# Beveiliging van het administratiepakket

*Stand van zaken: 26 oktober 2026*

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

Voor een klein kantoor is een certificaat (punt 3) in het begin niet nodig. Wel volgen we dezelfde soort
maatregelen en leggen we ze vast, zodat later een certificaat of rapport kan.

## 3. Wettelijk kader

| Regel | Wat het voor ons betekent |
|---|---|
| **AVG** | Bij het voeren van de administratie van een klant is het kantoor meestal **verwerker** en de ondernemer **verwerkingsverantwoordelijke**, dus een verwerkersovereenkomst per klant. Verder: verwerkingsregister, dataminimalisatie (geen BSN, geen kopie-ID), beveiliging "passend bij het risico" (art. 32), en een **datalek binnen 72 uur melden** bij de Autoriteit Persoonsgegevens (art. 33) |
| **Fiscale bewaarplicht** (art. 52 AWR) | **7 jaar** bewaren, in een vorm die binnen redelijke tijd te controleren is. Boekingen mogen daarom niet ongemerkt veranderen, en de administratie moet te exporteren zijn (XAF-auditfile) |
| **Btw-aftrek** | Alleen met een factuur **op naam van de onderneming**, met de verplichte gegevens. Het pakket signaleert een factuur op een andere naam (zie §5.4) |
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
| **Tweestapsverificatie verplicht** voor iedereen online. Op kantoor alleen voor de beheerder. Met een authenticator-app (TOTP), daarna ook passkeys (WebAuthn). Herstelcodes eenmalig tonen | beheerder F1, iedereen F2 |
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

### 4.3 Integriteit van de boekhouding

| Maatregel | Fase |
|---|---|
| Een journaalpost is altijd in evenwicht en heeft een uniek boekstuknummer per dagboek (`Journaalcontrole`) | ✅ |
| **Geboekt is geboekt**: een journaalpost wordt nooit gewijzigd of verwijderd, maar gecorrigeerd met een tegenboeking (memoriaal of creditfactuur). Verkoopfacturen hebben een doorlopende nummering zonder gaten | F1 |
| **Afgesloten periodes** (na de btw-aangifte) zijn niet meer te bebouwen. Heropenen kan alleen door het kantoor, met een reden in het log | F1 |
| **Hash-keten** over het journaal: elke post krijgt een SHA-256 over de inhoud plus de hash van de vorige post. Een controle in Beheer laat zien of er buiten de app iets in de database is veranderd | F1 |
| **Wijzigingslog alleen toevoegen**: de databaserol van de app mag in de logtabel alleen `INSERT` (geen `UPDATE` of `DELETE`) | F1 |
| **Documenten onveranderlijk**: opgeslagen onder hun SHA-256, gekoppeld aan de journaalpost, en nooit overschreven | F1 |
| Export als **XAF-auditfile** en als archief (PDF's, bijlagen en JSON) voor de bewaarplicht | F1 (A12) |

### 4.4 Financiële fraude en fouten

Bij gangbare pakketten zijn dit de signalen die het meeste schade voorkomen:

| Maatregel | Fase |
|---|---|
| **Afwijkend IBAN**: een bekende leverancier (herkend op btw-nummer, KvK of naam) met een ander IBAN op de factuur wordt nooit automatisch verwerkt. De melding vraagt om navraag via een bekend telefoonnummer (`Inkoopverwerker`) | ✅ |
| **Dubbele factuur**: zelfde leverancier en factuurnummer (ongeacht leestekens), of zelfde datum en bedrag | ✅ |
| **Onbekende leverancier**: wordt nooit automatisch verwerkt. De gebruiker bevestigt eerst de nieuwe relatie | ✅ |
| **Niet op naam van de onderneming**: wordt niet automatisch verwerkt, met de melding dat de btw dan niet aftrekbaar is | ✅ |
| **Bedraglimiet voor automatisch verwerken**: boven de limiet (instelbaar per administratie) altijd door een mens laten bevestigen | ✅ (domein), F1 instelling |
| **IBAN van een relatie wijzigen**: alleen met opnieuw bevestigen, met een vermelding in het log en een melding aan de andere gebruikers van de administratie. Online met vier ogen | F1 / F2 |
| Afwijkend bedrag: veel hoger dan gebruikelijk bij deze leverancier, als signaal in Te verwerken | F1 (A7) |
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
| Uploads buiten de webmap, met de SHA-256 als bestandsnaam. Downloaden altijd met `Content-Disposition: attachment` en `X-Content-Type-Options: nosniff`. Nooit HTML of SVG van een gebruiker in de app tonen | F1 |
| **Virusscan** met ClamAV (open source) vóór verwerking. Online ook een quarantaine | F2 (optioneel F1) |
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
| **Beveiligingsheaders**: een strikte `Content-Security-Policy` (`default-src 'self'`, `frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin` en `Permissions-Policy` (camera alleen voor het fotograferen van een bon) | F1 |
| **Cookies** met `Secure`, `HttpOnly` en `SameSite=Lax`/`Strict`, plus antiforgery voor formulieren | ✅ (antiforgery), F1 |
| **Blazor Server**: elke actie controleert op de server opnieuw het recht (nooit vertrouwen op wat de browser laat zien), met grenzen aan de berichtgrootte en het aantal circuits per gebruiker, en geen `MarkupString` met invoer van gebruikers | F1 |
| **Database alleen via EF Core** met parameters, nooit SQL aan elkaar plakken | F1 |
| Foutpagina's zonder technische details; details alleen in het serverlog | ✅ (samenstel-app) |
| **Data Protection-sleutels** bewaard en versleuteld (DPAPI of een certificaat), zodat cookies een herstart overleven en de sleutels niet leesbaar zijn | F1 |

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

- **Hosting**: een VPS of beheerde server in Nederland of de EU, bij een partij met een verwerkersovereenkomst en
  ISO 27001.
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

## 5. Wat er nu al gebouwd is

1. **XML veilig lezen** (UBL, CAMT.053): geen DTD, geen externe verwijzingen.
2. **Begrensd inlezen** van PDF's (grootte, pagina's, bijlagen), met een nette fout bij een beschadigd of
   beveiligd bestand.
3. **Type op inhoud** voor PDF en XML.
4. **Signalen in het inkoopvoorstel**:
   - afwijkend IBAN bij een bekende leverancier;
   - dubbele factuur;
   - onbekende leverancier;
   - factuur niet op naam van de onderneming;
   - bedrag boven de limiet;
   - gesloten periode.

   Elk signaal houdt automatisch verwerken tegen.

## 6. Volgorde

| Plakje | Beveiliging die erbij hoort |
|---|---|
| A6 webapp | Administraties met rechten, global query filters, negatieve tests, beveiligingsheaders, Data Protection-sleutels, tweestapsverificatie voor de beheerder, geboekt = onwijzigbaar, hash-keten, log alleen toevoegen |
| A7 inkoop | Uploads op type en grootte, opslag op hash, bedraglimiet instelbaar, afwijkend bedrag, IBAN wijzigen met bevestiging |
| A8 bank | Dubbele mutaties weigeren (sleutel per mutatie ✅ in het domein), bankregels alleen door het kantoor |
| A10 periodes | Afgesloten periodes, heropenen met een reden |
| vóór online (F2) | Tweestapsverificatie voor iedereen, Row Level Security, rate limiting, herkennen in een apart proces, virusscan, hosting volgens §4.11, pentest, verwerkersovereenkomsten |

## 7. Beslispunten

1. **Tweede factor**: alleen een authenticator-app (TOTP), of meteen ook passkeys? *Voorstel:* TOTP in F1, en
   passkeys erbij in F2.
2. **Bedraglimiet voor automatisch verwerken**: *voorstel* standaard € 2.500 per factuur, per administratie
   instelbaar.
3. **Virusscan** al op kantoor (ClamAV op de server-pc), of pas online? *Voorstel:* pas online. Op kantoor
   worden documenten niet geopend, alleen gelezen door de herkenning.
4. **Certificering**: wel of geen ISO 27001 of ISAE 3402 op termijn? Dat hangt af van het aantal ondernemers dat
   online gaat en wat zij vragen.
5. **Hosting**: welke partij in NL/EU? Te kiezen vóór fase 2.
