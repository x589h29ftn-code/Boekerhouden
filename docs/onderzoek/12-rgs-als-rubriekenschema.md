# 12. RGS als rubriekenschema (stand oktober 2026)

> **Aanleiding.** De opdrachtgever gaf aan dat de RGS-indeling in principe de rubrieken zijn.
> Dit hoofdstuk zet op een rij hoe RGS er anno 2026 voor staat en wat dat betekent voor plakje 3
> (rubriceren). Het vult [§3.4](03-gegevensimport-xaf-excel-rgs.md#34-referentie-grootboekschema-rgs) aan en vervangt het eigen rubriekenschema uit
> [hoofdstuk 4](04-rubricering-ontwerp.md).
>
> **Over de bronnen.** De officiële site (referentiegrootboekschema.nl) en boekhoudplaza.nl waren
> vanuit de ontwikkelomgeving niet bereikbaar. De feiten komen uit een openbare, gedocumenteerde
> samenvatting die op 9-9-2026 tegen het officiële werkboek RGS 3.8 en de 3.9-alfa is gecontroleerd [1],
> aangevuld met zoekresultaten van oktober 2026 [2][3]. Wat niet bevestigd is, staat er als *(O)* bij.
> Het werkboek zelf controleren we zodra het in de app is ingelezen (zie [§12.11](#1211-open-punten)).

## 12.1 De kern

- **RGS (Referentie GrootboekSchema)** is de landelijke referentie-indeling van grootboekrekeningen:
  - elke code heeft een vaste omschrijving, een normale saldokant (D/C) en waar nodig een omslagcode;
  - RGS zelf noemt het nadrukkelijk "een referentieclassificatie en geen verplichte standaard" [1].
- **Waarom RGS als rubrieken werkt:**
  - de bovenste niveaus volgen de wettelijke indeling van balans en W&V (Besluit modellen jaarrekening);
  - de **RGS Taxonomie** koppelt elke code aan de SBR-rapporten, zowel de KvK-jaarrekening (micro, klein, middelgroot, groot) als de aangiften IB, Vpb en omzetbelasting [1];
  - een dossier dat op RGS is gerubriceerd, kan dus zowel de jaarrekening (BV) als de winstaangifte (eenmanszaak) vullen. Later kan het ook deponeren en aangeven via SBR.
- **Wat dat oplevert:**
  - **we hoeven geen eigen rubriekenschema te maken en te onderhouden.** Dat was eerder het grootste inhoudelijke werkpakket van plakje 3 (120–250 rubrieken per schema);
  - de Taakgroep RGS werkt de indeling elk jaar bij.

**Besluit B23:** RGS ís het rubriekenschema. Een grootboekrekening van de klant wordt
gekoppeld aan één RGS-code. De hoofdposten en subposten in planning, dossier en rapport zijn de
RGS-niveaus 2 en 3. De presentatie in het rapport (namen, samenvoegen, volgorde) is een aparte laag
erbovenop, zodat we RGS zelf nooit hoeven te wijzigen.

## 12.2 Versies en planning

| Versie | Status | Datum | Opmerking |
|---|---|---|---|
| 3.7 | definitief | 3-3-2025 | Zie [§3.4](03-gegevensimport-xaf-excel-rgs.md#34-referentie-grootboekschema-rgs) |
| **3.8** | **definitief, actueel** | **10-12-2025** | Gebaseerd op NT20 en FT20. Werkboek `RGS 3.8-def.xlsx`. RGS Taxonomie 3.8 (`NT20_RGS_20251210`) [1] |
| 3.9 alfa | ter review | 28-7-2026 | Gebaseerd op NT21 en FT21 [1] |
| 3.9 bèta | gepubliceerd | 9-10-2026 (gepland) | De homepage van de RGS-site meldt "Bètaversie RGS 3.9 gepubliceerd" [3]. De datum komt uit de releasekalender [1] |
| **3.9** | **gepland definitief** | **11-12-2026** | RGS Taxonomie 3.9.b gepland op 6-11-2026, RGS Taxonomie 3.9 op 15-1-2027 [1] |

- **De kern is stabiel sinds 3.0 (2017).** Gewone codes zoals `BLimKasKas` (kas) bestaan in 3.5 en 3.8 gelijk. Per versie veranderen vooral omschrijvingen, omslagcodes, filters, sectorcodes en de markering *Inactief* [1].
- **Wijzigingen in 3.9** (alfa):
  - vooral omschrijvingen;
  - 18 omslagcodes aangepast of vervallen;
  - de normale saldokant (D/C) gewijzigd bij 6 codes;
  - 50 nieuwe codes, onder meer schulden aan aandeelhouders, bestemmingsreserves en -fondsen, en RGS Zorg in het hoofdblad;
  - 42 inactief [1].
- **Ritme:** elk jaar een alfa, een bèta en een definitieve versie, in de pas met de jaarlijkse Nederlandse Taxonomie (NT). NT21 gaat op 9-12-2026 in productie [1].
- **Gevolg voor ons:**
  - nu bouwen en testen met **3.8**;
  - in december het definitieve werkboek **3.9** inlezen;
  - de app ondersteunt meerdere versies naast elkaar en bewaart per koppeling uit welke versie de code komt.

## 12.3 Opbouw: vijf niveaus

| Niveau | Naam | Voorbeeld | Aantal (3.8) | Rol in Boekerhouden |
|---|---|---|---|---|
| 1 | Balans / W&V | `B`, `W` | 2 | Zijde |
| 2 | Hoofdrubriek | `BMva` Materiële vaste activa | 42 | **Hoofdpost**: materieel aanvinken (B9), balansregel |
| 3 | Rubriek | `BMvaBeg` Bedrijfsgebouwen | 295 | **Subpost**: doorklikken, regel in de toelichting |
| 4 | Grootboekrekening | `BMvaBegVvp` Verkrijgingsprijs bedrijfsgebouwen | 1.479 | **Koppelniveau**: hier koppelen we de rekeningen van de klant aan |
| 5 | Mutatie | `BMvaBegVvpBeg` Beginbalans | 3.162 | Alleen voor verloopoverzichten uit boekingen (XAF). Niet koppelen |

*Aantallen gemeten op het werkboek 3.8 [1].*

- **De code:**
  - een `B` of `W`, gevolgd door één groep van drie letters per niveau (bijv. `B` + `Mva` + `Beg` + `Vvp`), dus hoogstens 13 tekens;
  - een code op een dieper niveau begint altijd met de code van zijn ouder, dus de boom volgt direct uit de codes;
  - **de code is de sleutel**: codes mogen niet worden gewijzigd of uitgebreid zolang je naar RGS verwijst [1].
- **Koppelen op niveau 4** is het advies van de beheerder van RGS: een code van niveau 2 of 3 verliest de splitsing die de rapporten (en de aangifte) nodig hebben, en een code van niveau 5 maakt van een mutatie een rekening [1].
- **Wij staan niveau 3 toe**, met een signaal "grof gekoppeld". Voor de jaarrekening is dat genoeg; voor de aangifte soms niet.

### Hoofdrubrieken (niveau 2) en de jaarrekening

De hoofdrubrieken volgen de balans en de W&V van het Besluit modellen jaarrekening. De codes
hieronder zijn tegen het werkboek 3.8 gecontroleerd [1]. Er zijn in totaal 42 hoofdrubrieken; de
overige zijn minder gangbaar, zoals de mutatie fiscale oudedagsreserve (`WMfo`) *(volledige lijst
uit het werkboek halen)*.

| Balans | | | W&V | | |
|---|---|---|---|---|---|
| `BIva` | Immateriële vaste activa | D | `WOmz` | Netto-omzet | C |
| `BMva` | Materiële vaste activa | D | `WWiv` | Wijziging voorraden | C |
| `BVas` | Vastgoedbeleggingen | D | `WOvb` | Overige bedrijfsopbrengsten | C |
| `BFva` | Financiële vaste activa | D | `WKpr` | Kostprijs van de omzet | D |
| `BVrd` | Voorraden | D | `WPer` | Lasten uit hoofde van personeelsbeloningen | D |
| `BPro` | Onderhanden projecten (activa) | D | `WAfs` | Afschrijvingen op immateriële en materiële vaste activa | D |
| `BVor` | Vorderingen | D | `WBed` | Overige bedrijfskosten | D |
| `BEff` | Effecten (kortlopend) | D | `WFbe` | Financiële baten en lasten | C |
| `BLim` | Liquide middelen | D | `WBel` | Belastingen | D |
| `BEiv` | Groepsvermogen - Eigen vermogen - Kapitaal | C | `WNer` | Nettoresultaat | C |
| `BEga` | Egalisatierekening | C | | | |
| `BVrz` | Voorzieningen | C | | | |
| `BLas` | Langlopende schulden | C | | | |
| `BSch` | Kortlopende schulden | C | | | |

- **De balans van een kleine BV** (voorbeeldrapport, [§9](09-analyse-voorbeeldrapporten.md)) volgt deze hoofdrubrieken één-op-één.
- **De W&V met brutomarge** is een samenvatting erboven: kleine en middelgrote rechtspersonen mogen netto-omzet, wijziging voorraden, overige bedrijfsopbrengsten en kostprijs samenvatten (art. 2:397 lid 3 BW), dus `WOmz`, `WWiv`, `WOvb` en `WKpr`. Dat is presentatie, geen rubricering.
- **Eenmanszaak:**
  - het eigen vermogen zit in `BEiv`, maar dan in de familie `BEivKap` (ondernemingsvermogen, privéstortingen `BEivKapPrs`, privéopnamen `BEivKapPro`);
  - de BV-rubrieken `BEivGok` (aandelenkapitaal), agio en overige reserves vallen weg;
  - de fiscale oudedagsreserve (FOR) staat ook onder `BEiv` *(O: code nakijken)*.

### Voorbeelden van subposten (niveau 3)

Uit het werkboek 3.8 [1]: `BMvaBeg` Bedrijfsgebouwen, `BVorDeb` Handelsdebiteuren, `BVorOva`
Overlopende activa, `BLimKas` Kasmiddelen, `BEivGok` Aandelenkapitaal, `BEivKap` Eigen vermogen
natuurlijke personen, `BSchOpa` Overlopende passiva, `WBedAut` Autokosten en andere vervoermiddelen,
`WBedKan` Kantoorkosten, `WBedAlk` Andere kosten.

**Valkuilen in de afkortingen** [1]:
- `WBedAut` is *autokosten*, niet automatisering. Automatisering is `WBedKanKoa`.
- `BEivKap…` is voor natuurlijke personen (eenmanszaak, VOF), niet voor de BV.
- Codes nooit "afmaken" volgens het patroon: altijd opzoeken in het werkboek.

## 12.4 Het werkboek

- **Bestand:** één Excel-werkboek per versie, gratis te downloaden uit de Kennisbank van de RGS-site (`/sites/default/files/kennisbank/RGS%203.8-def.xlsx`) [1].
- **Tabbladen (3.8):**
  - `Recap`: releasenotes en de betekenis van de filters;
  - `Totaal-RGS3.8-def`: het hoofdblad, met 4.980 regels;
  - `RGS3.8-versus-RGS3.7`: de historie per versie vanaf 3.0;
  - `MKB-RGS3.8-def`: de selectie voor het MKB, met 3.705 regels, waarvan 1.155 op niveau 4;
  - `WoCo-RGS3.8-def` en `Zorg-RGS3.8-beta`: sectoren [1].
- **Indeling hoofdblad:** rij 1 is een titel, rij 2 bevat de kolomkoppen [1].

| Kolom | Betekenis | Gebruik in Boekerhouden |
|---|---|---|
| `Referentiecode` | De sleutel | Sleutel van de koppeling |
| `ReferentieOmslagcode` | Code van de tegenrekening bij een omgekeerd saldo | Omslag in het rapport ([§12.6](#126-omslag-dc-en-controles)) |
| `Sortering` | Sorteervolgorde van balans en W&V (sinds 3.0) | Volgorde in de boom en het rapport |
| `Referentienummer` | Voorbeeldnummer: 7 cijfers op niveau 4 (`BMvaBegVvp` = `0202010`), mutaties met `.NN`. **Verandert tussen versies** | Alleen tonen, nooit als sleutel |
| `Omschrijving (verkort)`, `Omschrijving` | Korte en volledige omschrijving | Zoeken en tonen |
| `D/C` | Normale saldokant | Controle en omslag |
| `Nivo` | Niveau 1–5 | Boom |
| `Basis`, `Uitgebr`, `EZ/VOF`, `ZZP`, `WoCo` (3.9: + `Zorg`) | **Kiezen** naar aard: hiermee begin je | Filter per rechtsvorm |
| `Inactief` | Vervallen code; blijft in het werkboek staan (42 in 3.8) | Niet meer koppelen; bestaande koppeling signaleren |
| `BB`, `Agro`, `WKR`, `EZ/VOF`, `BV`, `WoCo`, `Bank`, `OZW-Coop-Sticht-FWO`, `Afrek syst`, `Nivo5`, `Uitbr5`, `Stichting`, `OZW`, `FWO`, `Coop` (3.9: + `Zorg`) | **Wegfilteren**: codes die specifiek zijn voor dat gebruik | Filter per rechtsvorm |

- **Let op:** `EZ/VOF` en `WoCo` komen twee keer voor, eenmaal als kiesfilter en eenmaal als wegfilter. De **positie** van de kolom bepaalt de betekenis. De inleesfunctie moet de kolommen dus op volgorde lezen, niet alleen op naam [1].
- **Omvang:** RGS telt 328 codes met een omslagcode (185 op niveau 4) [1].
- **Extensies:** een achtervoegsel `.` of `:` na de code, bijvoorbeeld per bankrekening. Ze vallen buiten het centrale beheer. Wij gebruiken ze niet: meerdere rekeningen van de klant mogen gewoon aan dezelfde code hangen.

## 12.5 Welke codes per rechtsvorm

De `Recap` van het werkboek beschrijft de werkwijze: kies een basisset en filter daarna weg wat niet van toepassing is [1].

| | BV (micro/klein) | Eenmanszaak (en later VOF) |
|---|---|---|
| Begin met | `Basis` (of het tabblad `MKB`) | `EZ/VOF` (kiesfilter) |
| Weg | `Inactief`, `Nivo5`, `EZ/VOF`-specifiek (privévermogen natuurlijke personen), `Agro`, `Bank`, `OZW-Coop-Sticht-FWO`, `WoCo`-specifiek. `WKR` alleen als de werkkostenregeling niet apart wordt geboekt | `Inactief`, `Nivo5`, **`BV`-specifiek** (o.a. aandelenkapitaal), sectorfilters |
| **Niet** wegfilteren | De `BV`-kolom: die bevat juist het aandelenkapitaal | |
| Codes op niveau 4 (3.8) | 1.050 (gemeten met Boekerhouden) | 928 |

- **Een grootteklasse kent RGS niet.** Micro, klein en middelgroot (BW2 Titel 9; grensbedragen sinds 2024: klein tot € 7,5 mln balanstotaal / € 15 mln omzet / < 50 werknemers) bepalen alleen de **presentatie** en het KvK-rapport, niet de rubricering [1].
- **De `Uitgebreid`-set** voegt onder meer financiële instrumenten en specifieke reserves toe. Voorstel: standaard uit, per klant aan te zetten.
- **"RGS MKB"** is geen versie maar een afgeleide selectie:
  - het officiële werkboek heeft een tabblad `MKB`;
  - boekhoudplaza.nl (GBNED) onderhoudt een eigen *RGS MKB* tot en met niveau 4, met een 5-cijferig decimaal rekeningnummer per code (bijv. `BVorDebHad` Debiteuren = `13011`) [1].

## 12.6 Omslag, D/C en controles

- **Omslag:**
  - staat het saldo van een rekening aan de andere kant dan de `D/C` van zijn code, en heeft de code een omslagcode, dan telt het saldo in het rapport mee onder de omslagcode;
  - voorbeeld: een bank rekening-courant `BLimBanRba` (D) met een negatief saldo gaat naar `BSchSakRba` (schulden aan kredietinstellingen) [1];
  - dat gebeurt **apart voor begin- en eindsaldo**, dus ook voor de vergelijkende cijfers;
  - er is geen marktafspraak over wie de omslag toepast, de leverancier van het bestand of de ontvanger. Wij passen hem altijd zelf toe bij het opbouwen van het rapport [1];
  - RGS adviseert: heeft een rekening een omslagcode, zorg dan dat ook de tegenrekening gekoppeld is. Dat is bij ons automatisch het geval, omdat de omslag een eigenschap van de code is.
- **Controles die RGS mogelijk maakt:**
  - **B/W-conflict**: een rekening met balanssaldo gekoppeld aan een W-code, of andersom (eerste letter);
  - **verkeerde kant**: een saldo tegen de D/C in, zonder omslagcode (bijv. debiteuren credit) → signaal;
  - **I8 "W&V leeg"**: alle W-rekeningen hebben saldo 0, dus de jaarafsluiting is al geboekt ([§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv));
  - **inactieve code**: een koppeling naar een vervallen code (na een versie-update) → signaal met de opvolger uit het historietabblad.

## 12.7 RGS en de rapporten

- **RGS Taxonomie 3.8** (`NT20_RGS_20251210.zip`) koppelt de codes aan 25 SBR-rapporten [1]:
  - 12 KvK-jaarrekeningen (`kvk-rpt-jaarverantwoording-2025-nlgaap-` micro/klein/middelgroot/groot);
  - 4 Belastingdienst-rapporten (`ihz-aangifte-2025` (IB), `vpb-aangifte-2025`, en twee voor de omzetbelasting);
  - 6 voor woningcorporaties en 3 bankrapporten.
- **Er is ook een leesbare koppeltabel in Excel:** "RGS 3.8 mapping naar SBR NT20 concepten", met toelichting, in de Kennisbank [1].
- **Gevolg voor de rapportplakjes (6 en 7):**
  - **jaarrekening BV klein**: de balans- en W&V-regels en hun volgorde volgen uit de RGS-niveaus 2 en 3 (en de `Sortering`). De kantoorpresentatie (namen, samenvoegen, verbergen, besluit D10) ligt erbovenop;
  - **winstaangifte eenmanszaak**: de regels van de IB-aangifte (winst-en-verliesrekening en balans in de aangifte) zijn via de koppeltabel uit de RGS-codes af te leiden. Zo sluit de bijlage bij de aangifte aan op wat in de aangiftesoftware wordt ingevuld;
  - **later deponeren (SBR)**: dezelfde koppeltabel levert de XBRL-concepten. Deponeren blijft voorlopig buiten scope (B11).
- **Kantoorinstellingen per RGS-code** (los van RGS zelf):
  - specificatie verplicht ja/nee;
  - soort verloopoverzicht, bijv. MVA (aanschafwaarde/cumulatieve afschrijving) of rekening-courant;
  - een afwijkend rapportlabel;
  - de werkprogrammastappen per hoofdpost (plakje 5).

## 12.8 RGS in de bestanden van klanten

- **XAF:**
  - XAF 3.2 kent twee manieren om RGS mee te geven: `leadReference`/`leadCrossReference` (code en omslagcode) of `taxoRef`. Pakketten gebruiken beide;
  - XAF 4.0 heeft een vast veld voor de RGS-code en de RGS-versie (zie ook [§3.2](03-gegevensimport-xaf-excel-rgs.md));
  - lees beide plekken en valideer tegen het werkboek [1].
- **RGS Brugstaat:** een standaardbestand met saldi per RGS-code, bedoeld voor fiscale software (IB/Vpb). Mogelijk een extra importroute *(later)* [1].
- **Importsjabloon:** heeft al een optionele vierde kolom *RGS-code* (I11).
- **Referentiecasus** (de aangeleverde kolommenbalans, buiten git):
  - **geen RGS-codes**, en een klassiek decimaal rekeningschema: 0 vaste activa en eigen vermogen, 1 liquide middelen, vorderingen en schulden, 4 kosten, 5 inkoop, 8 omzet, 9 belastingen;
  - **gevolg:** voor dit kantoor is het **koppelsjabloon op rekeningnummer** de belangrijkste route. Wordt hetzelfde schema bij veel klanten gebruikt, dan koppelt één sjabloon het grootste deel automatisch. Het sjabloon groeit door het vastleggen van handmatige koppelingen ([§4.3](04-rubricering-ontwerp.md#43-de-koppelengine)).

## 12.9 Licentie en gebruik

- **Expliciete gebruiksvoorwaarden** voor het opnemen van het werkboek in software hebben we niet gevonden (ook niet in oktober 2026).
- **Wel gevonden:**
  - een secundaire bron meldt dat het gebruik van RGS kosteloos is [4];
  - het werkboek is vrij te downloaden, en RGS noemt zichzelf een referentieclassificatie, geen verplichte standaard [1].
- **Werkwijze:**
  - het **werkboek komt niet in de repository**. De beheerder downloadt het officiële bestand en leest het in de app in (beheerscherm *RGS*). Zo is ook altijd de juiste versie aanwezig;
  - de tests gebruiken een **klein uittreksel** (enkele tientallen codes) in dezelfde opmaak als het werkboek;
  - vóór een eventuele verspreiding van de app buiten het kantoor de voorwaarden navragen bij de Taakgroep RGS.

## 12.10 Gevolgen voor het ontwerp en plakje 3

**Wat verandert ([hoofdstuk 4](04-rubricering-ontwerp.md) is bijgewerkt):**
1. Het eigen rubriekenschema en de RGS→rubriek-tabel **vervallen**. Een koppeling is: grootboekrekening van de klant → RGS-code (niveau 4, of 3 met signaal).
2. Hoofdpost = RGS niveau 2, subpost = niveau 3. Dat sluit precies aan op besluit B9 (materieel per hoofdpost, doorklikken naar subposten).
3. **Voorrang bij automatisch koppelen:** handmatig > koppelsjabloon van het kantoor > RGS-code uit het bestand > voorstel.
4. De omslag, de D/C-controle en de volgorde komen uit RGS.
5. **Rubriekenschema is geen inhoudelijk werkpakket meer.** Ervoor in de plaats komen:
   - het werkboek downloaden en inlezen;
   - de filters per rechtsvorm bevestigen;
   - een koppelsjabloon voor het rekeningschema van het kantoor opbouwen.

**Plakje 3 (gebouwd 23-10-2026):**
1. **RGS inlezen** (beheer):
   - officieel werkboek uploaden en het hoofdblad inlezen (kolommen op positie, kiezen/wegfilteren);
   - meerdere versies naast elkaar, één actieve;
   - controleverslag met aantallen per niveau, inactief en omslag.
2. **Filter per rechtsvorm**: BV en eenmanszaak volgens [§12.5](#125-welke-codes-per-rechtsvorm), met de `Uitgebreid`-set per klant aan te zetten.
3. **Koppelscherm per klant** (vanuit het dossier):
   - rekeningen met saldo huidig en vorig jaar, en de RGS-code met omschrijving;
   - de bron: handmatig, sjabloon, bestand of voorstel;
   - zoeken in RGS op tekst of code, en meerdere voorstellen tegelijk accepteren;
   - gekoppeld % (aantal en bedrag).
4. **Kantoor-koppelsjabloon**: rekeningnummer (plus omschrijving) → RGS-code. Bijgehouden door "Opnemen in sjabloon" bij handmatig koppelen, en in te lezen en te exporteren in Excel.
5. **Voorstellen**: dezelfde rekening bij andere klanten, en tekstovereenkomst met de RGS-omschrijvingen. Een voorstel wordt nooit automatisch bevestigd.
6. **Proefbalans per post**:
   - een boom niveau 2 → 3 → 4 → grootboekrekening, huidig en vorig jaar, met de omslag toegepast;
   - controles: B/W-conflict, verkeerde kant, ongekoppeld, I8.

**Hoe het rubriceerscherm werkt** (wensen van de opdrachtgever, 23-10-2026):
- **Links** staan de grootboekrekeningen: nummer, omschrijving, *→ post* en het saldo. Een voorstel staat cursief met een vraagteken; klik erop om het over te nemen. **Rechts** staat de boom: hoofdposten (RGS niveau 2) en subposten (niveau 3), met de totalen van het boekjaar en het vorige jaar.
- **Rustig scherm:**
  - één regel per rekening en per post; lange namen worden afgekapt;
  - de RGS-code, het volledige pad, de bron en het saldo vorig jaar staan in de tooltip;
  - kleur alleen voor de plek waar je iets neerzet. Knoppen (✎, ＋, ✕, *hierheen*) verschijnen pas als je over een regel gaat;
  - de grootboekcodes (niveau 4) zijn standaard verborgen. Met de schakelaar *grootboekcodes* komen ze erbij. Verborgen staan de rekeningen en eigen posten van een grootboekcode gewoon onder de subpost.
- **Bedragen in hele euro's**, met verdeling van afrondingsverschillen ([§7.6](07-technische-architectuur.md#76-rapportgeneratie-één-model-meerdere-uitvoerformaten)), zodat de boom en de balans blijven sluiten.
- **Slepen:**
  - sleep een rekening (of een selectie) naar een post;
  - sleep je over een hoofdpost (bijv. *Materiële vaste activa*), dan klapt die open en verschijnen de subposten (bijv. *Bedrijfsgebouwen*, *Automobielen en overige transportmiddelen*);
  - neerzetten kan op een subpost, een grootboekcode of een eigen post. Een hoofdpost is te grof.
  - Zonder muis: selecteer de rekeningen en klik bij de post op *hierheen*.
- **Eigen posten:**
  - met ＋ voeg je onder een subpost een eigen post toe, bijv. *Overige vorderingen 1, 2, 3* onder *Overige vorderingen*;
  - zo'n post geldt per klant en verschijnt in de jaarrekening. Voor aangifte en SBR telt hij als de RGS-post erboven;
  - verwijder je de post, dan blijven de rekeningen gekoppeld aan de post erboven.
- **Hernoemen:**
  - met ✎ geef je elke post een eigen naam voor de jaarrekening;
  - dat kan voor deze klant, of (beheerder) **voor alle klanten** als kantoorstandaard, bijv. *Automobielen en overige transportmiddelen* → *Vervoermiddelen*;
  - de klantnaam gaat voor de kantoornaam, en *Standaard* zet de RGS-omschrijving terug.
- **Voorstellen:**
  - volgorde: kantoorsjabloon (zelfde nummer, andere omschrijving), dan andere klanten (zelfde nummer en omschrijving), dan de omschrijving;
  - de omschrijving wordt vergeleken met de RGS-omschrijvingen, met een kleine lijst vaktermen;
  - zijn de rekeningnummers erboven en eronder al aan de balans gekoppeld (of aan de W&V), dan wordt alleen aan die kant gezocht. Daardoor worden de voorstellen beter naarmate er meer gekoppeld is;
  - geven de buren geen uitsluitsel, dan geldt het **decimale rekeningstelsel**: rubriek 0–3 is balans, 4–9 is W&V (zie hieronder). Dat gebeurt alleen als de al gekoppelde rekeningen van de klant dat stelsel volgen (op hooguit één op de tien na). Zo krijgt *9500 Vennootschapsbelasting* geen voorstel meer op de balans;
  - een voorstel wordt nooit automatisch toegepast. *Voorstellen overnemen* doet het in één keer; de bron *voorstel, bevestigd* staat in de tooltip;
  - posten van de functionele W&V-indeling (`WVkf`, `WAkf`) worden niet voorgesteld.
- **Automatisch:**
  - bij het inlezen van een saldibalans koppelt de app wat zeker is: het kantoorsjabloon bij hetzelfde nummer en een vergelijkbare omschrijving, of de RGS-code uit het bestand;
  - handmatige en bevestigde koppelingen blijven altijd staan;
  - in de rondgang met de fictieve bakkerij was een tweede klant met hetzelfde rekeningschema meteen voor 85% gekoppeld.

**Uit de RGS-handleiding** (aangeleverd door de opdrachtgever, oktober 2026):
- **Decimaal stelsel.** Veel rekeningschema's zijn ingedeeld in tien rubrieken: 0 vaste activa, eigen vermogen en langlopende schulden; 1 financiële rekeningen; 2 tussenrekeningen; 3 voorraden; 4 kostensoorten; 5 kostenplaatsen; 6 fabricage; 7 kostprijs; 8 omzet; 9 resultaten. Dus 0–3 balans en 4–9 W&V. Dat gebruiken we als terugval bij de voorstellen (zie boven).
- **"Overige …"-posten** mogen direct worden gekoppeld (de rekening op *Overige vorderingen*) of indirect, via een eigen post eronder. Beide zijn mogelijk; de eigen post geldt voor aangifte en SBR als de RGS-post erboven.
- **Correctieposten** (bijv. cumulatieve afschrijvingen, voorziening dubieuze debiteuren) hebben een eigen RGS-code naast de post waarop ze corrigeren. Ze staan in de boom onder dezelfde subpost. In RGS hebben ze zelf de kant credit (bij een activapost), dus een creditsaldo geeft geen signaal *verkeerde kant*.
- Voor kleine entiteiten volstaan ongeveer **800 regels** op niveau 1–4; met de rechtsvormfilters blijven er voor een BV ruim 1.000 codes op niveau 4 over, waarvan in de praktijk een klein deel wordt gebruikt. Daarom zijn de grootboekcodes standaard verborgen.

**Nog niet gebouwd** (volgt wanneer nodig):
- controle I8 (W&V leeg);
- kantoorsjabloon in en uit Excel;
- de `Uitgebreid`-set per klant;
- hulp bij verfijnen van niveau 3 naar 4 (voor de aangifte).

## 12.11 Open punten

1. ~~Werkboek 3.8~~: aangeleverd en ingelezen (4.979 codes). In december **3.9** inlezen via Beheer › RGS.
2. ~~Koppeling met de rapporten~~: de RGS Taxonomie (3.4 en 3.8, `NT20_RGS_20251210.zip`) is aangeleverd. Die bevat per RGS-code de koppeling naar de KvK-jaarrekening (micro/klein) en de IB- en Vpb-aangifte. Nodig in plakje 6/7; de bestanden blijven buiten git.
3. ~~Hoofdrubrieken en FOR~~: bevestigd uit het werkboek. De FOR is `BEivFirFor` (Fiscale reserves › Fiscale oudedagsreserve); de mutatie FOR staat in de W&V onder `WMfo`.
4. Gebruiksvoorwaarden navragen bij de Taakgroep RGS (alleen nodig bij verspreiding buiten kantoor).
5. **Vraag B3**: gebruikt het kantoor bij (bijna) alle klanten hetzelfde rekeningschema? Dan is één koppelsjabloon het startpunt.

## Bronnen

[1] RGS Skill (Ontos B.V., Max Schoon), referenties `versions-governance`, `structure-and-codes`,
`scope-filters-entities` en `reporting-compliance`, gecontroleerd op 9-9-2026 tegen de officiële
werkboeken `RGS 3.8-def.xlsx` en `RGS 3.9-alfa.xlsx`, de RGS Taxonomie 3.8 en de pagina's van
referentiegrootboekschema.nl, sbr-nl.nl, nltaxonomie.nl en kvk.nl die daar per bewering worden
genoemd. https://github.com/MaxSchoon/rgs-skill (inhoud CC BY 4.0). *Uses the RGS Skill by Ontos B.V.*
— https://github.com/MaxSchoon/rgs-skill ·
[2] https://www.referentiegrootboekschema.nl/actueel (zoekresultaat oktober 2026: alfaversie RGS 3.9 op 28-7-2026; releasekalender RGS 3.9 op 24-8-2026) ·
[3] https://www.referentiegrootboekschema.nl/ (zoekresultaat oktober 2026: "Bètaversie RGS 3.9 gepubliceerd") ·
[4] https://stolwijkkennisnetwerk.nl/snelle-vergelijking-van-je-cijfers-met-rgs/ (gebruik RGS kosteloos; beheer door de RGS-beheergroep) ·
[5] https://www.belastingdienst.nl/wps/wcm/connect/nl/intermediairs/content/referentie-grootboekschema-iets-voor-u (waarom de Belastingdienst RGS ondersteunt)
