# 4. Rubricering: hoe we grootboekrekeningen aan rubrieken koppelen

> Dit is het hart van de applicatie. Het hoofdstuk beschrijft het ontwerp van de
> rubriceringsengine. Het ontwerp sluit aan op wat in de markt gangbaar is ([§1.4](01-gangbare-werkwijze-samenstelsoftware.md#14-rubricering-grootboekrekeningen-koppelen))
> en op de mogelijkheden van RGS en XAF ([hoofdstuk 3](03-gegevensimport-xaf-excel-rgs.md)).
>
> **Herzien op 23-10-2026: RGS is het rubriekenschema** (besluit B23, [hoofdstuk 12](12-rgs-als-rubriekenschema.md)).
> Een eigen rubriekenschema en een RGS→rubriek-tabel zijn niet meer nodig. Een rekening van de klant
> wordt gekoppeld aan één RGS-code. De hoofdpost is RGS niveau 2, de subpost niveau 3.

## 4.1 Begrippen

| Begrip | Betekenis |
|---|---|
| **Rubriekenschema** | **RGS** in de actieve versie (nu 3.8), gefilterd per rechtsvorm ([§12.5](12-rgs-als-rubriekenschema.md#125-welke-codes-per-rechtsvorm)) |
| **Hoofdpost** | RGS niveau 2 (bijv. `BMva` Materiële vaste activa). Hier vink je *materieel* aan (B9) |
| **Subpost** | RGS niveau 3 (bijv. `BMvaBeg` Bedrijfsgebouwen). Hierheen klik je door |
| **Koppeling** | Grootboekrekening van een klant → **één RGS-code op niveau 4** (of niveau 3, met het signaal "grof gekoppeld"). Hoort bij de **klant** en loopt door over de jaren |
| **Koppelsjabloon** | Kantoorbrede regels: rekeningnummer (of nummerreeks, eventueel met omschrijvingspatroon) → RGS-code, voor een rekeningschema dat bij meerdere klanten voorkomt |
| **Omslag** | De `ReferentieOmslagcode` uit RGS. Daarheen gaat een saldo dat de "verkeerde" kant op staat (bank credit → schulden aan kredietinstellingen) |
| **Gekoppeld %** | Aandeel van de relevante rekeningen dat een (bevestigde of automatische) koppeling heeft |

## 4.2 Het rubriekenschema: RGS

### Ontwerpkeuzes
1. **RGS ongewijzigd gebruiken.** Codes worden niet aangepast of uitgebreid (een regel van RGS zelf). Extensies gebruiken we niet: meerdere rekeningen van de klant mogen aan dezelfde code hangen.
2. **Per rechtsvorm een filter** op het werkboek, met de standaardinstellingen uit [§12.5](12-rgs-als-rubriekenschema.md#125-welke-codes-per-rechtsvorm):
   - *BV*: `Basis`, zonder de codes voor natuurlijke personen;
   - *eenmanszaak*: `EZ/VOF`, zonder de BV-codes;
   - de `Uitgebreid`-set is per klant aan te zetten.
3. **Koppelen op niveau 4**, zoals RGS adviseert. Niveau 3 mag, met het signaal "grof gekoppeld": voor de jaarrekening volstaat dat, voor de aangifte niet altijd. Niveau 2 en 5 zijn niet toegestaan.
4. **Grootte is presentatie.** Micro, klein en middelgroot veranderen niets aan de koppeling, alleen aan het rapport.
5. **Kantoorinstellingen per RGS-code** staan in een eigen tabel naast RGS:
   - specificatie verplicht;
   - soort verloopoverzicht;
   - afwijkend rapportlabel;
   - werkprogrammastappen (plakje 5).
6. **Versies:**
   - meerdere RGS-versies staan naast elkaar, één is actief;
   - elke koppeling bewaart de versie waaruit de code komt;
   - na het inlezen van een nieuwe versie worden koppelingen aan inactieve codes gesignaleerd, met de opvolger uit het historietabblad.

### Voorbeeld (bovenste niveaus)

Codes gecontroleerd tegen RGS 3.8 ([§12.3](12-rgs-als-rubriekenschema.md#123-opbouw-vijf-niveaus)):

```
B Balans
├─ BIva Immateriële vaste activa
├─ BMva Materiële vaste activa
│  └─ BMvaBeg Bedrijfsgebouwen
│     └─ BMvaBegVvp Verkrijgingsprijs bedrijfsgebouwen      ← koppelniveau (4)
├─ BFva Financiële vaste activa
├─ BVrd Voorraden · BPro Onderhanden projecten
├─ BVor Vorderingen
│  ├─ BVorDeb Handelsdebiteuren ─ BVorDebHad Handelsdebiteuren nominaal
│  └─ BVorOva Overlopende activa
├─ BEff Effecten · BLim Liquide middelen
│  ├─ BLimKas Kasmiddelen ─ BLimKasKas Kas
│  └─ … BLimBanRba Rekening-courant bank (omslag → BSchSakRba)
├─ BEiv Eigen vermogen
│  ├─ BEivGok Aandelenkapitaal (BV)
│  └─ BEivKap Eigen vermogen natuurlijke personen (eenmanszaak: BEivKapPrs privéstortingen, BEivKapPro privéopnamen)
├─ BVrz Voorzieningen · BLas Langlopende schulden
└─ BSch Kortlopende schulden
   └─ BSchOpa Overlopende passiva

W Winst-en-verliesrekening
├─ WOmz Netto-omzet · WWiv Wijziging voorraden · WOvb Overige bedrijfsopbrengsten
├─ WKpr Kostprijs van de omzet
├─ WPer Personeelskosten · WAfs Afschrijvingen
├─ WBed Overige bedrijfskosten
│  ├─ WBedAut Autokosten · WBedKan Kantoorkosten (WBedKanKoa automatisering)
│  └─ WBedAlk Andere kosten (WBedAlkOal algemene kosten)
├─ WFbe Financiële baten en lasten
└─ WBel Belastingen
```

**Benodigde inhoud:** geen eigen schema meer. Wel:
- het officiële werkboek;
- een bevestiging van de filters per rechtsvorm;
- een koppelsjabloon voor het rekeningschema dat het kantoor het meest gebruikt.

## 4.3 De koppelengine

### Voorrangsregels
Per grootboekrekening van de klant bepaalt de engine één RGS-code. De eerste regel die een antwoord geeft, wint:

| # | Bron | Regel | Status na koppelen |
|---|---|---|---|
| 1 | **Handmatig** (klant) | Een eerder door een gebruiker vastgelegde koppeling voor deze klant. **Wordt nooit automatisch overschreven** [hfst. 1] | Bevestigd |
| 2 | **Koppelsjabloon** (kantoor) | Exact rekeningnummer of nummerreeks in het sjabloon dat aan de klant hangt (bijv. "SnelStart standaard"). Optioneel aangevuld met een omschrijvingspatroon | Automatisch |
| 3 | **RGS-code uit het bestand** | RGS-code van de rekening (uit XAF of de vierde kolom van het importsjabloon), gevalideerd tegen de actieve versie. De code moet bestaan, niet inactief zijn en binnen het filter van de rechtsvorm vallen. Een code op niveau 5 wordt teruggebracht naar de ouder op niveau 4; een code op niveau 2 is te grof en wordt een voorstel | Automatisch |
| 4 | **Voorstel** | Heuristiek, *nooit automatisch toegepast*: (a) dezelfde nummer- en omschrijvingscombinatie bij andere klanten; (b) tekstovereenkomst met de RGS-omschrijvingen en trefwoorden ("kas", "bank", "debiteuren", "afschr"); (c) `leadCode`/`leadDescription` als hint. Later eventueel AI | Voorgesteld (gebruiker moet bevestigen) |
| – | Geen | – | **Ongekoppeld** (telt mee als alert en blokkeert het definitieve rapport) |

> **Waarom handmatig > sjabloon > RGS-code uit het bestand?** Een handmatige keuze is een bewuste vaktechnische
> beslissing voor deze klant. Een sjabloon is een bewuste kantoorkeuze voor een bekend
> rekeningschema. RGS is generiek en soms foutief ingevuld door de klant of het pakket.
> Deze volgorde is ook in de markt gangbaar ([hfst. 1](01-gangbare-werkwijze-samenstelsoftware.md)).

### Pseudocode

```csharp
RgsCode? BepaalCode(Grootboekrekening r, Klant k, RgsSchema rgs)   // rgs = actieve versie, gefilterd voor de rechtsvorm
{
    if (k.HandmatigeKoppeling(r) is { } h) return h.Code;                          // 1
    if (kantoor.Koppelsjabloon.Match(r.Nummer, r.Omschrijving) is { } t
        && rgs.IsKoppelbaar(t)) return t;                                          // 2
    if (rgs.Zoek(r.RgsCode) is { Inactief: false } c && rgs.IsKoppelbaar(c.TotNiveau4()))
        return c.TotNiveau4();                                                     // 3 (niveau 5 → ouder)
    return null;                                                                   // → voorstel (4) of ongekoppeld
}
```

### Omslag (saldo-afhankelijke rubricering)
- Is het saldo van een rekening tegengesteld aan de `D/C` van zijn RGS-code, en heeft die code een **omslagcode**, dan telt het saldo in het rapport mee onder de omslagcode (en dus in die hoofd- en subpost).
- Dit gebeurt **apart voor beginsaldo en eindsaldo** (dus ook voor de vergelijkende cijfers), zoals RGS het voorschrijft [hfst. 3, 7].
- Per rubriek kun je kiezen of de omslag op **rekeningniveau** gebeurt (standaard; bijv. elke bankrekening apart) of op **rubriekniveau** (saldering van alle rekeningen in de rubriek). Dat laatste is alleen toegestaan als verrekening ook wettelijk mag.

### Splitsen
- **Voorkeur:** splitsen met een **herrubriceringsboeking** in het dossier (bijv. € 12.000 van "Lening ING" naar "Kortlopend deel langlopende schulden"). Dat is transparant, staat in het dossier en is per jaar anders. Tools doen het meestal ook zo [hfst. 3, D].
- Optioneel (later) een *vaste* procentuele splitsing in de koppeling, voor bijvoorbeeld een gemengde privé/zakelijk-rekening bij een IB-ondernemer.

### Wanneer draait de engine?
- **Na elke import**, voor nieuwe en nog niet bevestigde rekeningen.
- **Na wijziging van een koppelsjabloon of het inlezen van een nieuwe RGS-versie.** Alle *automatische* koppelingen bij de betrokken klanten worden herberekend. De gebruiker ziet eerst een **verschillenoverzicht** ("12 rekeningen bij 4 klanten krijgen een andere rubriek") en bevestigt dat. Handmatige koppelingen blijven ongemoeid.
- **Gesloten dossiers worden nooit herberekend**: zij bewaren een bevroren *koppelingsmomentopname* (zie [§4.5](#45-jaarovergang-en-vergelijkende-cijfers)).

### Leren van handmatige koppelingen
- Bij een handmatige koppeling vraagt de app (alleen voor beheerders): **"Ook opnemen in koppelsjabloon <naam>?"** Zo wordt het sjabloon steeds beter en daalt het handwerk per klant.
- Daarnaast een kantooroverzicht: welke rekeningnummer- en omschrijvingscombinaties worden bij veel klanten met hetzelfde sjabloon handmatig gekoppeld. Dat zijn kandidaten voor sjabloonregels.

## 4.4 Het koppelscherm (functioneel)

> **Gebouwd in plakje 3**, met slepen naar posten, eigen posten en hernoemen; zie [§12.10](12-rgs-als-rubriekenschema.md#1210-gevolgen-voor-het-ontwerp-en-plakje-3). Hieronder het oorspronkelijke ontwerp.

Het koppelscherm werkt zo: links de rekeningen, rechts de RGS-boom (gefilterd voor de rechtsvorm, met zoeken op tekst en code), koppelen door te slepen of met een knop, meerdere tegelijk selecteren, en ontkoppelen.

- **Alleen relevante rekeningen tonen.** Dat zijn rekeningen met boekingen of saldo in het huidige of vorige jaar.
- **Kolommen:** nummer, omschrijving, saldo huidig, saldo vorig, gekoppelde RGS-code met omschrijving en hoofdpost, **bron** (Handmatig, Sjabloon, Bestand, Voorstel) en status.
- **Filters:** *ongekoppeld*, *voorgesteld*, *grof gekoppeld* (niveau 3), *afwijkend van het bestand* (gekoppelde code wijkt af van de RGS-code in het bestand), *gewijzigd ten opzichte van vorig jaar*, *B/W-conflict* (een balanssaldo op een W-code of andersom), *verkeerde kant* (saldo tegen de D/C in, zonder omslag).
- **Gekoppeld-indicator** bovenaan, met een klik naar alleen de ongekoppelde rekeningen. Dat is precies wat de klantenmonitor doet [hfst. 1, 54].

**Definitie van "Gekoppeld %":**

```
relevant    = rekeningen met boekingen of saldo ≠ 0 in het huidige boekjaar
gekoppeld   = relevant ∩ (status Bevestigd of Automatisch)
Gekoppeld % = |gekoppeld| / |relevant| × 100
```
Daarnaast een **bedraggewogen variant**: het ongekoppelde saldo in euro's. Een ongekoppelde rekening van € 3 is minder urgent dan een van € 300.000.

## 4.5 Jaarovergang en vergelijkende cijfers

1. **Koppelingen gelden per klant, met `GeldigVanafBoekjaar`.** Een wijziging in 2025 verandert niets aan een *gesloten* dossier 2024.
2. **Bij het sluiten van een dossier** bevriezen we per rekening de toegepaste rubriek (koppelingsmomentopname). Zo is een uitgebracht rapport altijd exact te reproduceren.
3. **Vergelijkende cijfers** in het nieuwe dossier nemen we over **op rekeningniveau** (de gecorrigeerde eindsaldi van het vorige dossier). Die rubriceren we vervolgens met de **koppeling van het huidige jaar**. Zo staan vorig en huidig jaar altijd in dezelfde indeling.
4. **Herrubricering van vergelijkende cijfers detecteren.** Wijkt de nieuwe indeling van vorig jaar af van het uitgebrachte rapport van vorig jaar (andere rubriek voor dezelfde rekening), dan:
   - toont de software een melding;
   - biedt hij een standaardtekst aan voor de toelichting ("de vergelijkende cijfers zijn waar nodig geherrubriceerd …");
   - verschijnt dit in het afrondingsoverzicht.
5. **Correctieboekingen** van vorig jaar die niet in de administratie van de klant zijn verwerkt: de software controleert of het beginsaldo in de nieuwe import aansluit op het gecorrigeerde eindsaldo van vorig jaar (controle C03). Bij een verschil stelt hij een "beginbalanscorrectie" voor, of markeert hij de correctie als terugkerend.

## 4.6 Valkuilen en hoe we ze afvangen

| Valkuil | Maatregel |
|---|---|
| `leadCode`/`leadDescription` lijkt een rubricering, maar is de eigen verdichting van het pakket [hfst. 3, 35] | Alleen als hint (voorstel), nooit automatisch |
| RGS-code die niet bestaat of uit een andere versie komt [hfst. 3, 43] | Valideren tegen de tabel van de juiste versie. Ongeldig betekent geen automatische koppeling en een melding |
| Klant of pakket heeft de RGS-code verkeerd gekozen | Filter "afwijkend van RGS"; handmatig gaat altijd voor RGS |
| Balans/W&V-conflict (`accTp` ≠ RGS-letter ≠ rubriekzijde) | Blokkerende melding in het koppelscherm |
| Rekeningnummers die per jaar van betekenis wisselen | Wijziging van de omschrijving bij hetzelfde nummer signaleren bij import ("1300 heette vorig jaar 'Debiteuren', nu 'Tussenrekening'") |
| Rekening zonder saldo maar met boekingen | Wel tonen en koppelen (tussenrekeningen moeten 0 zijn; een melding als ze dat niet zijn) |
| Gesaldeerde bankrekeningen (één rekening wisselt van D naar C) | Omslag per saldo, begin en eind apart |
| Sjabloonwijziging raakt veel klanten | Verschillenoverzicht en bevestiging; gesloten dossiers bevroren |
| Code wordt inactief in een nieuwe RGS-versie | Signaal in het koppelscherm, met de opvolger uit het historietabblad als voorstel |
| Afkortingen misleiden (`WBedAut` is autokosten, niet automatisering) | Altijd de omschrijving en de ouder tonen; codes nooit "afmaken" maar kiezen uit de boom |

## 4.7 Wat we moeten bouwen en vullen

**Code (plakje 3, zie [§12.10](12-rgs-als-rubriekenschema.md#1210-gevolgen-voor-het-ontwerp-en-plakje-3))**
- RGS-werkboek inlezen (beheer): versies, filters, controleverslag.
- `Boekerhouden.Mapping`: de engine, voorstellen, herberekening met verschillenoverzicht.
- Het koppelscherm en de proefbalans per hoofd- en subpost, met omslag.
- Beheer van het koppelsjabloon (leren uit handmatige koppelingen; Excel in en uit).
- Unit-tests per voorrangsregel, voor de omslag en voor de jaarovergang.

**Inhoud**
1. Het officiële **RGS-werkboek** (3.8, in december 3.9) downloaden en inlezen.
2. De **filters per rechtsvorm** bevestigen (standaard: [§12.5](12-rgs-als-rubriekenschema.md#125-welke-codes-per-rechtsvorm)).
3. Een **koppelsjabloon** voor het rekeningschema dat het kantoor het meest ziet. Het groeit vanzelf bij het koppelen van de eerste klanten.
4. Een **testset**: enkele echte klanten met de rubricering uit het huidige pakket als referentie (vóór de pilot).
