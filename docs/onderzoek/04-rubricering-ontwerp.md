# 4. Rubricering: hoe we grootboekrekeningen aan rubrieken koppelen

> Dit is het hart van de applicatie. Het hoofdstuk beschrijft het ontwerp van de
> rubriceringsengine. Het ontwerp sluit aan op wat in de markt gangbaar is ([§1.4](01-gangbare-werkwijze-samenstelsoftware.md#14-rubricering-grootboekrekeningen-koppelen))
> en op de mogelijkheden van RGS en XAF ([hoofdstuk 3](03-gegevensimport-xaf-excel-rgs.md)).

## 4.1 Begrippen

| Begrip | Betekenis |
|---|---|
| **Rubriekenschema** | Hiërarchische indeling van de jaarrekening (balans en W&V) waar rekeningen aan gekoppeld worden. Ook wel "rapportagestructuur" genoemd (groep › subgroep › rubriek) |
| **Rubriek** | Een blad in de boom. **Alleen aan bladeren koppel je rekeningen**, zoals gangbaar [hfst. 1] |
| **Koppeling** | Grootboekrekening van een klant → rubriek. Hoort bij de **klant** en loopt door over de jaren |
| **Koppelsjabloon** | Kantoorbrede koppelregels voor een bekend rekeningschema (bijv. het standaardschema van een boekhoudpakket) |
| **RGS→rubriek-tabel** | Kantoorbrede tabel die aangeeft in welke rubriek een RGS-code (of een voorvoegsel daarvan) landt |
| **Omslagrubriek** | De rubriek waar een saldo heen gaat als het de "verkeerde" kant op staat (bank credit → schulden aan kredietinstellingen) |
| **Gekoppeld %** | Aandeel van de relevante rekeningen dat een (bevestigde of automatische) koppeling heeft |

## 4.2 Het rubriekenschema

### Ontwerpkeuzes
1. **Volg de wettelijke indeling** (Besluit modellen jaarrekening, BW2 Titel 9) op de hogere niveaus, en de **NBA-voorbeeldrapporten** voor de detailrubrieken. Zo landen de rubrieken één-op-één in balans, W&V en toelichting.
2. **Lijn de rubrieken uit op RGS niveau 2 en 3.** RGS volgt zelf de BMJ-indeling [hfst. 3]. Bijna elke rubriek krijgt daardoor een natuurlijk RGS-voorvoegsel. Dat maakt de RGS→rubriek-tabel klein en goed te onderhouden.
3. **Eén schema per familie**: *BV/NV* (micro/klein/middel via presentatievarianten), *IB-ondernemer* (eenmanszaak/VOF/maatschap) en later *Stichting/Vereniging*. De grootte bepaalt de **presentatie** (welke niveaus zichtbaar zijn), niet de koppeling. Een micro-BV en een kleine BV delen dus één schema.
4. **Per rubriek vastleggen**:
   - normaal saldo (D/C);
   - omslagrubriek;
   - of een specificatie verplicht is;
   - type verloopoverzicht;
   - rapportlabels per variant (bijv. "Materiële vaste activa" en bij micro alleen "Vaste activa");
   - *(toekomst)* het NT-taxonomieconcept voor SBR.

### Voorbeeld (bovenste niveaus, BV)

> De RGS-voorvoegsels met ✓ zijn in het onderzoek bevestigd ([§3.4](03-gegevensimport-xaf-excel-rgs.md#34-referentie-grootboekschema-rgs)). De overige zijn indicatief en **moeten worden geverifieerd tegen de RGS 3.8-tabel**.

```
BALANS
├─ Vaste activa
│  ├─ Immateriële vaste activa ........................ BIva ✓
│  │  ├─ Kosten van ontwikkeling ...................... BIvaKoo ✓
│  │  ├─ Goodwill ..................................... BIvaGoo ✓
│  │  └─ Overige immateriële vaste activa ............. BIva (rest)
│  ├─ Materiële vaste activa .......................... BMva ✓
│  │  ├─ Bedrijfsgebouwen en -terreinen ............... BMvaBeg ✓
│  │  ├─ Inventaris / andere vaste bedrijfsmiddelen ... BMvaBei ✓
│  │  └─ …
│  └─ Financiële vaste activa ......................... BFva (verifiëren)
├─ Vlottende activa
│  ├─ Voorraden ....................................... BVrd ✓ (BVrdHan ✓)
│  ├─ Vorderingen ..................................... BVor ✓
│  │  ├─ Handelsdebiteuren ............................ BVorDeb ✓
│  │  └─ Overige vorderingen en overlopende activa .... BVor (rest)
│  └─ Liquide middelen ................................ BLim ✓  (omslag → Schulden aan kredietinstellingen)
├─ Eigen vermogen
│  ├─ Geplaatst kapitaal .............................. BEivGok ✓
│  ├─ Overige reserves ................................ BEivOvr ✓
│  └─ Onverdeelde winst / resultaat boekjaar .......... BEivOre ✓
├─ Voorzieningen ...................................... BVrz (verifiëren)
├─ Langlopende schulden ............................... BLas (verifiëren)
└─ Kortlopende schulden ............................... BSch ✓
   ├─ Schulden aan kredietinstellingen ................ BSchSak ✓
   ├─ Crediteuren ..................................... BSchCre ✓
   ├─ Belastingen en premies sociale verzekeringen .... BSchBep ✓ (Btw, Lhe, Vpb)
   └─ Overige schulden en overlopende passiva ......... BSch (rest)

WINST-EN-VERLIESREKENING (categoriaal)
├─ Netto-omzet ........................................ WOmz ✓
├─ Kostprijs van de omzet ............................. WKpr ✓ (WKprInh ✓)
├─ Personeelskosten (lonen, sociale lasten, pensioen) . WPer ✓
├─ Afschrijvingen (IVA, MVA) .......................... WAfs ✓
├─ Overige bedrijfskosten (huisvesting, …) ............ WBed ✓
├─ Financiële baten en lasten ......................... WFbe ✓
└─ Belastingen ........................................ WBel (verifiëren)
```

Voor **IB-ondernemers** vervangt *Ondernemingsvermogen* (`BEivKap` ✓, met privéstortingen en -opnamen `BEivKapPrs`/`BEivKapPro` ✓) de BV-eigenvermogensrubrieken. De kapitaalsvergelijking per vennoot wordt een verloopoverzicht op die rubriek.

**Benodigde inhoud:** volledig uitgewerkt telt een schema *BV* ongeveer 120 tot 250 bladrubrieken (EK). Dit is inhoudelijk werk voor de vaktechnisch verantwoordelijke, met de NBA-voorbeeldrapporten als basis. Zie [hoofdstuk 8](08-roadmap-risico-s-en-beslispunten.md).

## 4.3 De koppelengine

### Voorrangsregels
Per grootboekrekening van de klant bepaalt de engine één rubriek. De eerste regel die een antwoord geeft, wint:

| # | Bron | Regel | Status na koppelen |
|---|---|---|---|
| 1 | **Handmatig** (klant) | Een eerder door een gebruiker vastgelegde koppeling voor deze klant. **Wordt nooit automatisch overschreven** [hfst. 1] | Bevestigd |
| 2 | **Koppelsjabloon** (kantoor) | Exact rekeningnummer of nummerreeks in het sjabloon dat aan de klant hangt (bijv. "SnelStart standaard"). Optioneel aangevuld met een omschrijvingspatroon | Automatisch |
| 3 | **RGS** | RGS-code van de rekening (uit XAF/Excel, gevalideerd), **langste-voorvoegsel-match** in de RGS→rubriek-tabel: eerst niveau 5, dan 4, 3 en 2. | Automatisch |
| 4 | **Voorstel** | Heuristiek, *nooit automatisch toegepast*: (a) dezelfde nummer- en omschrijvingscombinatie bij andere klanten met hetzelfde pakket of sjabloon; (b) trefwoorden in de omschrijving ("kas", "bank", "debiteuren", "afschr"); (c) `leadCode`/`leadDescription` als hint. Later eventueel AI, zoals sommige pakketten AI-mapping aanbieden | Voorgesteld (gebruiker moet bevestigen) |
| – | Geen | – | **Ongekoppeld** (telt mee als alert en blokkeert het definitieve rapport) |

> **Waarom handmatig > sjabloon > RGS?** Een handmatige keuze is een bewuste vaktechnische
> beslissing voor deze klant. Een sjabloon is een bewuste kantoorkeuze voor een bekend
> rekeningschema. RGS is generiek en soms foutief ingevuld door de klant of het pakket.
> Deze volgorde is ook in de markt gangbaar ([hfst. 1](01-gangbare-werkwijze-samenstelsoftware.md)).

### Pseudocode

```csharp
Rubriek? BepaalRubriek(Grootboekrekening r, Klant k, Rubriekenschema s)
{
    if (k.HandmatigeKoppeling(r) is { } h) return h.Rubriek;                  // 1
    if (k.Koppelsjabloon?.Match(r.Nummer, r.Omschrijving) is { } t) return t;  // 2
    if (Rgs.IsGeldig(r.RgsCode, r.RgsVersie))                                   // 3
        for (var code = r.RgsCode; code.Length >= 4; code = Rgs.Ouder(code))
            if (s.RgsTabel.TryGet(code, out var rub)) return rub;
    return null;                                                                // → voorstel (4) of ongekoppeld
}
```

### Omslag (saldo-afhankelijke rubricering)
- Is het saldo van een rekening tegengesteld aan het *normaal saldo* van zijn rubriek, en heeft die rubriek een **omslagrubriek**, dan telt het saldo in het rapport mee in de omslagrubriek.
- Dit gebeurt **apart voor beginsaldo en eindsaldo** (dus ook voor de vergelijkende cijfers), zoals RGS het voorschrijft [hfst. 3, 7].
- Per rubriek kun je kiezen of de omslag op **rekeningniveau** gebeurt (standaard; bijv. elke bankrekening apart) of op **rubriekniveau** (saldering van alle rekeningen in de rubriek). Dat laatste is alleen toegestaan als verrekening ook wettelijk mag.

### Splitsen
- **Voorkeur:** splitsen met een **herrubriceringsboeking** in het dossier (bijv. € 12.000 van "Lening ING" naar "Kortlopend deel langlopende schulden"). Dat is transparant, staat in het dossier en is per jaar anders. Tools doen het meestal ook zo [hfst. 3, D].
- Optioneel (later) een *vaste* procentuele splitsing in de koppeling, voor bijvoorbeeld een gemengde privé/zakelijk-rekening bij een IB-ondernemer.

### Wanneer draait de engine?
- **Na elke import**, voor nieuwe en nog niet bevestigde rekeningen.
- **Na wijziging van een koppelsjabloon of de RGS-tabel.** Alle *automatische* koppelingen bij de betrokken klanten worden herberekend. De gebruiker ziet eerst een **verschillenoverzicht** ("12 rekeningen bij 4 klanten krijgen een andere rubriek") en bevestigt dat. Handmatige koppelingen blijven ongemoeid.
- **Gesloten dossiers worden nooit herberekend**: zij bewaren een bevroren *koppelingsmomentopname* (zie [§4.5](#45-jaarovergang-en-vergelijkende-cijfers)).

### Leren van handmatige koppelingen
- Bij een handmatige koppeling vraagt de app (alleen voor beheerders): **"Ook opnemen in koppelsjabloon <naam>?"** Zo wordt het sjabloon steeds beter en daalt het handwerk per klant.
- Daarnaast een kantooroverzicht: welke rekeningnummer- en omschrijvingscombinaties worden bij veel klanten met hetzelfde sjabloon handmatig gekoppeld. Dat zijn kandidaten voor sjabloonregels.

## 4.4 Het koppelscherm (functioneel)

Het koppelscherm werkt zo: links de rekeningen, rechts de rubriekenboom, koppelen door te slepen of met een knop, meerdere tegelijk selecteren, en ontkoppelen.

- **Alleen relevante rekeningen tonen.** Dat zijn rekeningen met boekingen of saldo in het huidige of vorige jaar.
- **Kolommen:** nummer, omschrijving, RGS-code, saldo huidig, saldo vorig, rubriek, **bron** (Handmatig, Sjabloon, RGS, Voorstel) en status.
- **Filters:** *ongekoppeld*, *voorgesteld*, *afwijkend van RGS* (rubriek past niet bij de RGS-code), *gewijzigd ten opzichte van vorig jaar*, *B/W-conflict* (een balansrekening gekoppeld aan een W&V-rubriek of andersom).
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

## 4.7 Wat we moeten bouwen en vullen

**Code**
- `Boekerhouden.Mapping` met de engine, de voorstellen en de herberekening met verschillenoverzicht.
- Het koppelscherm.
- Beheerschermen voor rubriekenschema, RGS-tabel, RGS→rubriek-tabel en koppelsjablonen.
- Unit-tests per voorrangsregel, de omslag en de jaarovergang.

**Inhoud**
1. Rubriekenschema *BV* en *IB-ondernemer*.
2. De **RGS→rubriek-tabel** voor RGS 3.8. Begin met de voorvoegsels op niveau 2 en 3 (enkele honderden regels), en verfijn waar een niveau 3-rubriek over meerdere rubrieken verdeeld moet worden.
3. **Koppelsjablonen** voor de standaard rekeningschema's van de pakketten die het kantoor het meest ziet. Haal die uit de XAF-bestanden van echte klanten.
4. Een **testset**: per pakket enkele echte klanten met de rubricering uit het huidige pakket als referentie. De engine moet minstens dezelfde uitkomst geven.
