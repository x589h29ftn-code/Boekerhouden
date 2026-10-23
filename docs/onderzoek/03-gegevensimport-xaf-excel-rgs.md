# 3. Gegevensimport: XAF-auditfile, Excel/CSV-proefbalans en RGS

> Wat moet de applicatie kunnen inlezen, in welk formaat, en hoe maken we daar een betrouwbare
> proefbalans van? Notatie: **[n]** = bron (lijst onderaan), **(EK)** = eigen kennis of afleiding,
> **(O)** = onbevestigd. Een deel van de technische details komt uit openbare GitHub-repositories
> van derden met kopieën van XSD's en voorbeeldbestanden. Valideer die tegen de officiële bron
> (odb.belastingdienst.nl en referentiegrootboekschema.nl) voordat je bouwt.

## 3.1 Importbronnen in scope

| Bron | Prioriteit | Waarom |
|---|---|---|
| **Standaard importsjabloon** (Excel/CSV: boekjaar + grootboeknummer, omschrijving, bedrag) | **MVP, als eerste** | Werkt voor elk pakket, eenvoudig, controleert op saldo nihil. Ook voor vorige jaren en historie ([§3.3](#33-standaard-importsjabloon-excelcsv)) |
| **XAF 3.2** (XML Auditfile Financieel) | MVP | Vrijwel elk Nederlands boekhoudpakket kan het exporteren. Bevat rekeningschema, beginbalans, alle boekingen en soms RGS-codes |
| **XAF 4.0** | MVP | **Vanaf 1-1-2027 accepteert de Belastingdienst alleen XAF 4.0** [17][18][46]. Pakketten stappen massaal over (o.a. Visma eAccounting, Dynamics 365 [20][47]) |
| **XAF 3.1** | MVP (zelfde parser) | Oudere exports en vorig-jaarbestanden |
| **RGS-referentietabel** (Excel van referentiegrootboekschema.nl) | MVP (beheer) | Basis voor automatisch rubriceren |
| Overige Excel/CSV-exports met kolomtoewijzer | Later | Voor exports die niet in het sjabloon passen |
| **RGS Brugstaat** (XML) | Later (O) | Officiële export van saldi per RGS-code, gebruikt door CBS en fiscale software [31][32][45]. Mogelijk een handig tweede formaat |
| API-koppelingen (Exact, Twinfield, …) | ❌ Buiten scope | |

## 3.2 XML Auditfile Financieel (XAF)

### Versies en herkenning

| Versie | Namespace (root-element `auditfile`) |
|---|---|
| 3.1 | `http://www.auditfiles.nl/XAF/3.1` [34] |
| 3.2 | `http://www.auditfiles.nl/XAF/3.2` [14][18] |
| 4.0 | `http://www.odb.belastingdienst.nl/Belastingdienst/BCPP/1.1/structures/XmlauditfileXAF_4.0` [15] (verifiëren tegen de ODB) |

- **Beheer:** de Belastingdienst, via odb.belastingdienst.nl/auditfiles en de Stuurgroep Auditfiles [16][21].
- **Herkenning:** de parser leest eerst alleen het root-element en de namespace (XmlReader), en kiest daarna de juiste mapping. Elk bestand wordt gevalideerd tegen de officiële XSD van zijn versie.

### Structuur XAF 3.2 (bevestigd met XSD en test-XML [14])

```
auditfile
├─ header: fiscalYear, startDate, endDate, curCode, dateCreated, softwareDesc, softwareVersion
└─ company: companyIdent, companyName, taxRegistrationCountry, taxRegIdent, streetAddress, postalAddress
   ├─ customersSuppliers/customerSupplier: custSupID, custSupName, commerceNr, custSupTp, …
   ├─ generalLedger/ledgerAccount: accID, accDesc, accTp (B | M | P),
   │      leadCode, leadDescription, leadReference, leadCrossRef,
   │      taxonomy* (taxoRef, entryPoint*(entryPointRef, conceptRef, domainMember…)), changeInfo, glAccountHistory
   ├─ vatCodes/vatCode: vatID, vatDesc, vatToPayAccID, vatToClaimAccID
   ├─ periods/period: periodNumber (0–999), periodDesc, startDatePeriod, endDatePeriod
   ├─ openingBalance: opBalDate, opBalDesc, linesCount, totalDebit, totalCredit,
   │      obLine*(nr, accID, amnt, amntTp D|C), obSubledgers?
   └─ transactions: linesCount, totalDebit, totalCredit,
          journal*(jrnID, desc, jrnTp, offsetAccID, bankAccNr,
            transaction*(nr, desc, periodNumber, trDt, amnt, amntTp, sourceID, userID,
              trLine*(nr, accID, docRef, effDate, desc, amnt, amntTp, custSupID, invRef,
                      costID, projID, vat(vatID, vatPerc, vatAmnt, vatAmntTp), currency(curCode, curAmnt), …)))
```

- **`accTp`**: B = balans, P = winst-en-verlies. M komt in de 3.2-XSD voor, maar de betekenis is (O); vermoedelijk "gemengd". Kruiscontrole met de eerste letter van de RGS-code (B/W).
- **`jrnTp`** (dagboektype) kent o.a. B, P, S, T, Y, Z en O [14][44]. De volledige lijst is (O). Voor de proefbalans is dit veld niet nodig; voor drill-down is het handig.

### Verschillen in XAF 4.0 [15][16][19]
- Ongeveer 90 velden in plaats van ongeveer 250.
- **`ledgerAccount/RGScode`** is een vast veld voor RGS, en **`header/RGSVersion`** geeft de RGS-versie.
- `accTp` kent alleen nog **B/P**. De velden `lead*` zijn verdwenen. Er is een veld voor het KvK-nummer (`Commercenr`) en `periodDesc` vervalt.
- Volgens de geraadpleegde XSD-kopie heeft `openingBalance` geen `opBalDate` meer (O).

### Waar staat de RGS-code? (cruciaal voor automatisch rubriceren)
Pakketten doen het verschillend [7][22][23][24][34][19].

**Parservolgorde** (eerste geldige treffer wint):

1. `ledgerAccount/RGScode` (4.0)
2. `taxonomy/taxoRef` of `conceptRef` (3.2, volgens de editie van 2017)
3. `leadReference` (3.x in de praktijk: Exact, Minox, CASH). Daarnaast `leadCrossRef` als omslagcode
4. `leadCode` (alleen als het een geldige RGS-code is)

Elke gevonden code wordt **gevalideerd tegen de RGS-tabel**. Pakketten exporteren soms codes die niet in RGS staan [43]. Een ongeldige code wordt bewaard, maar niet gebruikt voor rubricering, en levert een melding op.
**Niet vertrouwen op `leadCode`/`leadDescription` als rubricering.** In Exact is dat een eigen verdichting van het pakket; in een demobestand staat "Geplaatst kapitaal" onder leadCode "Materiële vaste activa" [35]. Gebruik het hooguit als hint voor een voorstel.

### Minimaal voorbeeld (XAF 3.2, vereenvoudigd)

```xml
<auditfile xmlns="http://www.auditfiles.nl/XAF/3.2">
 <header>
  <fiscalYear>2025</fiscalYear><startDate>2025-01-01</startDate><endDate>2025-12-31</endDate>
  <curCode>EUR</curCode><dateCreated>2026-02-15</dateCreated>
  <softwareDesc>PakketX</softwareDesc><softwareVersion>1.0</softwareVersion>
 </header>
 <company>
  <companyIdent>12345678</companyIdent><companyName>Voorbeeld BV</companyName>
  <taxRegistrationCountry>NL</taxRegistrationCountry><taxRegIdent>NL001234567B01</taxRegIdent>
  <generalLedger>
   <ledgerAccount><accID>0500</accID><accDesc>Geplaatst kapitaal</accDesc><accTp>B</accTp>
    <leadReference>BEivGokGea</leadReference></ledgerAccount>
   <ledgerAccount><accID>1100</accID><accDesc>ING Bank</accDesc><accTp>B</accTp>
    <leadReference>BLimBanRba</leadReference><leadCrossRef>BSchSakRba</leadCrossRef></ledgerAccount>
   <ledgerAccount><accID>8000</accID><accDesc>Omzet</accDesc><accTp>P</accTp>
    <leadReference>WOmzNop</leadReference></ledgerAccount>
  </generalLedger>
  <openingBalance><opBalDate>2025-01-01</opBalDate><linesCount>2</linesCount>
   <totalDebit>5000.00</totalDebit><totalCredit>5000.00</totalCredit>
   <obLine><nr>1</nr><accID>1100</accID><amnt>5000.00</amnt><amntTp>D</amntTp></obLine>
   <obLine><nr>2</nr><accID>0500</accID><amnt>5000.00</amnt><amntTp>C</amntTp></obLine>
  </openingBalance>
  <transactions><linesCount>2</linesCount><totalDebit>1000.00</totalDebit><totalCredit>1000.00</totalCredit>
   <journal><jrnID>BNK</jrnID><desc>Bank</desc><jrnTp>B</jrnTp>
    <transaction><nr>25-0001</nr><desc>Ontvangst</desc><periodNumber>1</periodNumber><trDt>2025-01-15</trDt>
     <trLine><nr>1</nr><accID>1100</accID><docRef>F25-001</docRef><effDate>2025-01-15</effDate>
      <desc>Ontvangst</desc><amnt>1000.00</amnt><amntTp>D</amntTp></trLine>
     <trLine><nr>2</nr><accID>8000</accID><docRef>F25-001</docRef><effDate>2025-01-15</effDate>
      <desc>Omzet</desc><amnt>1000.00</amnt><amntTp>C</amntTp></trLine>
    </transaction></journal></transactions>
 </company>
</auditfile>
```
*(Ingekort voorbeeld uit het onderzoek. Valideer de elementvolgorde tegen de XSD.)*

### Van XAF naar proefbalans (algoritme)

```
voor elke rekening a:
  begin(a)            = Σ obLine(a)      (D = +, C = −)
  mut(a, p).debet     = Σ trLine(a) met amntTp = D en transaction.periodNumber = p
  mut(a, p).credit    = Σ trLine(a) met amntTp = C en transaction.periodNumber = p
  eind(a, t/m p)      = begin(a) + Σ_{q ≤ p} (mut(a, q).debet − mut(a, q).credit)
```

**Aandachtspunten** (op basis van de structuur en voorbeeldbestanden [34][35]; uitwerking EK):

1. **Beginbalans kan op twee plekken staan.**
   - Twinfield zet hem als boeking in **periode 0** ("Openingsbalans") [34]; andere pakketten in `openingBalance`.
   - Sommige leveren een lege `openingBalance` met totalen 0.
   - Detecteer dubbeltelling (beide gevuld) en ontbreken (geen van beide). Laat de gebruiker de bron kiezen en onthoud die keuze per klant.
2. **W&V-rekeningen horen geen beginsaldo te hebben.** Staat er toch een saldo, of is Σ obLine ≠ 0, dan is het resultaat van vorig jaar niet verwerkt. Meld dat, en stel voor het resultaat naar een rekening "resultaat vorig boekjaar" of naar het eigen vermogen te boeken.
3. **Afsluitperioden.**
   - Periode 0 en perioden > 12 (13, 14; de XSD staat tot 999 toe) maken we per klant **configureerbaar**: meenemen of niet.
   - Afsluitboekingen die de W&V naar het eigen vermogen overboeken, zouden de W&V op nul zetten. Detecteer een boeking die alle W&V-rekeningen op 0 brengt en sluit die standaard uit.
4. **Valuta:** `amnt` is in de valuta van `header/curCode`; `currency/curAmnt` is alleen informatief.
5. **Integriteitscontroles** (blokkerend):
   - `linesCount`, `totalDebit` en `totalCredit` van `openingBalance` en `transactions` kloppen met de werkelijke regels;
   - elk `accID` uit de regels bestaat in `generalLedger`;
   - Σ debet = Σ credit per transactie en totaal.
6. **Boekjaar en klant:** `header/fiscalYear` en de start- en einddatum moeten overeenkomen met het dossier. `companyName` en het KvK-nummer worden vergeleken met de klant (waarschuwing bij verschil).
7. **Prestaties:** streaming verwerken (XmlReader). Per rekening en periode aggregeren tijdens het lezen. Transacties alleen opslaan als drill-down gewenst is (bulk insert). Geen DOM laden: bestanden kunnen honderden MB groot zijn.
8. **Pakketeigenaardigheden** van SnelStart, AFAS, e-Boekhouden en King zijn nog (O). Daarom is een set echte testbestanden per pakket nodig (zie [hoofdstuk 8](08-roadmap-risico-s-en-beslispunten.md)).

## 3.3 Standaard importsjabloon (Excel/CSV)

> **Besluit (23-10-2026):** de applicatie accepteert een eigen, eenvoudig importsjabloon. De
> gebruiker vult het **boekjaar** in en een overzicht van **grootboeknummer, omschrijving en
> bedrag**. De import gaat alleen door als het **saldo nihil** is.

Dit sjabloon is de **eerste importroute die we bouwen**. Elk pakket kan een saldibalans naar Excel
exporteren, dus het werkt altijd. XAF volgt daarna voor automatische RGS-codes en drill-down
naar boekingen.

> **Gebouwd in plakje 1** (`src/Boekerhouden.Import`): inlezen van .xlsx en .csv, controles I1–I6 en I11,
> en het genereren van een leeg sjabloon. Proberen kan met
> `boekerhouden controleer <bestand> [--boekjaar 2025]` en `boekerhouden sjabloon <uit.xlsx>`.

### Opbouw van het sjabloon

De applicatie levert het sjabloon zelf via de knop *Download importsjabloon* (`.xlsx`).
CSV met dezelfde kolommen kan ook.

```
     A                    B                                   C
1    Boekjaar             2025
2    Klant (optioneel)    1234 – Voorbeeld B.V.
3
4    Grootboeknummer      Omschrijving                        Bedrag
5    0100                 Inventaris aanschafwaarde            12.500,00
6    0110                 Inventaris cum. afschrijving         -4.250,00
7    1100                 ING Bank                             18.322,15
8    1600                 Crediteuren                          -9.871,40
9    0500                 Geplaatst kapitaal                  -18.000,00
…    …                    …                                   …
     8000                 Omzet                              -245.000,00
     4000                 Lonen en salarissen                  98.400,00
                                                   Totaal  →        0,00
```

| Veld | Verplicht | Regels |
|---|---|---|
| **Boekjaar** (cel B1) | ✅ | Vier cijfers, bijv. `2025`. Bepaalt in welk jaar de saldi landen. **Mag ook bij het importeren worden opgegeven** (nodig voor exports zonder boekjaarcel). Staat het op beide plekken, dan moeten ze gelijk zijn |
| Klant (cel B2) | – | Optioneel; ter controle dat het bestand bij de juiste klant hoort (waarschuwing bij afwijking) |
| **Grootboeknummer** | ✅ | Als tekst (voorloopnullen en nummers als `0210.01` blijven behouden), uniek in het bestand |
| **Omschrijving** | ✅ | Wordt de omschrijving van de grootboekrekening bij de klant |
| **Bedrag** | ✅ | Eindsaldo per einde boekjaar. **Debet = positief, credit = negatief.** Hoogstens 2 decimalen. Nederlandse (`1.234,56`) en Engelse (`1,234.56`) notatie worden herkend; ook `(125,40)` en `125,40-` als negatief. Een leeg bedrag telt als nihil |
| RGS-code (kolom D) | – | Optioneel. Is hij ingevuld en geldig, dan rubriceert de applicatie de rekening automatisch ([§4.3](04-rubricering-ontwerp.md#43-de-koppelengine)) |

**Gangbare kolomnamen** worden ook herkend, zodat een export uit een boekhoudpakket vaak zonder
aanpassing kan worden ingelezen (vergelijking zonder hoofdletters, spaties en punten):

| Kolom | Ook herkend als |
|---|---|
| Grootboeknummer | Rekening, Rekeningnummer, Rekeningnr, Rek.nr., Grootboekrekening, Grootboek, Nummer, Nr |
| Omschrijving | Omschr, Naam, Rekeningnaam, Grootboekomschrijving |
| Bedrag | Saldo, Eindsaldo |
| RGS-code | elke kolomnaam die met "RGS" begint |

De kopregel mag op elke rij staan en de kolommen in elke volgorde. Een regel zonder grootboeknummer
waarvan de omschrijving met "Totaal" begint, wordt overgeslagen (totaalregel onder de tabel).
Exports met aparte debet- en creditkolommen passen nog niet; daarvoor komt later de kolomtoewijzer.

*Bedrag* is het saldo zoals in de saldibalans: balansrekeningen met hun eindstand, W&V-rekeningen
met hun jaartotaal. Omdat elke boeking in evenwicht is, is de som van alle saldi altijd nihil. Een
verschil betekent dat er regels ontbreken of verkeerd zijn overgenomen, bijvoorbeeld een vergeten minteken.

### Controles bij het importeren (in deze volgorde)

| # | Controle | Bij afwijking |
|---|---|---|
| I1 | Bestand leesbaar (.xlsx of .csv), kolomkoppen *Grootboeknummer, Omschrijving, Bedrag* (of een synoniem) aanwezig | ❌ Import stopt, met een melding welke kolom ontbreekt |
| I2 | Boekjaar ingevuld (in het bestand of bij het importeren) en geldig; beide gelijk als het op twee plekken staat | ❌ Import stopt |
| I3 | Elke regel heeft een grootboeknummer en een omschrijving; lege regels worden overgeslagen | ❌ Import stopt, met de regelnummers |
| I4 | Grootboeknummers zijn uniek | ❌ Import stopt, met de dubbele nummers |
| I5 | Bedragen zijn geldige getallen met hoogstens 2 decimalen | ❌ Import stopt, met de regelnummers |
| **I6** | **Som van alle bedragen = € 0,00 (saldo nihil)** | ❌ **Import stopt** en toont het verschil (bijv. "Saldo is € 125,40 debet; de saldibalans sluit niet") |
| I7 | Klant in cel B2 komt overeen met de geselecteerde klant | ⚠️ Waarschuwing, de gebruiker bevestigt |
| I8 | Alle W&V-rekeningen hebben saldo 0, dus de jaarafsluiting is al geboekt | ⚠️ Waarschuwing: "W&V lijkt leeg, is het resultaat al naar het eigen vermogen geboekt?" |
| I9 | Er zijn al gegevens voor dit jaar | ⚠️ Vraag: vervangen? De oude import blijft in de historie bewaard; koppelingen en correctieboekingen blijven staan |
| I10 | Verschillen met de vorige import of het vorige jaar: nieuwe rekeningen, verdwenen rekeningen, andere omschrijving bij hetzelfde nummer | ℹ️ Overzicht ter informatie |
| I11 | RGS-code (indien ingevuld) heeft een geldig formaat (letters, begint met B of W) | ⚠️ Waarschuwing; de code wordt genegeerd |

*I1–I6 en I11 zijn gebouwd in plakje 1, I7, I9 en I10 in plakje 2 (in het importscherm van een dossier). I8 volgt in plakje 3, als bekend is welke rekeningen W&V zijn.* Zo werken I7, I9 en I10 in de app:

- **I7**: de klant in het bestand wordt vergeleken met naam en klantnummer van de gekozen klant (hoofdletters, spaties en leestekens tellen niet mee).
- **I9**: bij een tweede import voor hetzelfde jaar ziet de gebruiker wanneer en door wie de vorige is ingelezen (en of het hetzelfde bestand is). Inlezen kan pas na het aanvinken van *waarschuwingen gelezen*.
- **I10**: vergelijkt met de vorige import van hetzelfde jaar, of anders met het vorige boekjaar. Omschrijvingen worden vergeleken met de bij de klant bekende omschrijving. Een historisch jaar overschrijft die omschrijving niet.
- Het boekjaar kiest de gebruiker in het scherm: het dossierjaar of een van de vier jaren ervoor (vergelijkende cijfers en meerjarenoverzicht). Alle fouten worden in één keer gemeld, zodat de gebruiker ze in één ronde kan herstellen.

**Pas als I1–I6 slagen**, wordt er geïmporteerd:
1. Grootboekrekeningen worden aangemaakt of bijgewerkt bij de klant, met nummer als sleutel.
2. De saldi worden opgeslagen als eindsaldo van het boekjaar (importbatch *Sjabloon*).
3. De rubriceringsengine draait. Het scherm toont direct **"Gekoppeld: x%"**, met een knop naar de ongekoppelde rekeningen.

### Hetzelfde sjabloon voor vorige jaren
Met hetzelfde sjabloon en een ander **boekjaar** lees je ook in:
- **vergelijkende cijfers** voor het eerste dossier van een klant;
- **historische jaren** voor de 5-jaarskengetallen en het meerjarenoverzicht ([§9.4](09-analyse-voorbeeldrapporten.md#94-gegevens-die-we-nodig-hebben-naast-de-proefbalans)).

Zo is ook de overstap vanuit het huidige pakket opgelost: per klant de saldibalansen van de vorige jaren in het sjabloon zetten en importeren.

### Beperkingen (bewust)
- **Alleen eindsaldi**: geen perioden en geen boekingen, dus geen drill-down naar transacties. Dat kan wel met XAF.
- **Beginsaldi en verloopoverzichten** komen uit het vorige jaar (de import of het dossier van vorig jaar). Voor het eerste jaar importeer je vorig jaar ook met het sjabloon.

### Overige Excel/CSV-exports (later, optioneel)
Voor exports die niet in het sjabloon passen (andere kolomvolgorde, aparte debet/credit-kolommen,
meerdere perioden) kan later een **kolomtoewijzer** komen:
- kolommen aanwijzen en opslaan als importprofiel per pakket;
- tekenconventie kiezen;
- daarna dezelfde controles I1–I10.

Tot die tijd zet de gebruiker zo'n export in het standaardsjabloon om (kopiëren en plakken).

## 3.4 Referentie GrootboekSchema (RGS)

> **Bijgewerkt 23-10-2026:** RGS is het rubriekenschema van Boekerhouden (B23). De stand van zaken
> anno 2026 (3.9 bèta, filters per rechtsvorm, werkboek, RGS Taxonomie) en de gevolgen voor het
> ontwerp staan in [hoofdstuk 12](12-rgs-als-rubriekenschema.md). Waar die afwijken, gaat hoofdstuk 12 voor.

### Wat, wie, welke versie
- **RGS** is een gestandaardiseerd grootboekschema met een unieke referentiecode per rekening. Het is ontwikkeld met Belastingdienst, CBS, KvK, banken, softwareleveranciers en accountants [41][50].
  - Beheer: de **Taakgroep RGS** binnen SBR [7].
  - *RGS MKB* is een afgeleide van GBNED (boekhoudplaza.nl) en geen officiële versie [9].
- **Versies:**
  - 3.7: definitief 3-3-2025 [4];
  - **3.8: definitief 10-12-2025**, met RGS Taxonomie 3.8 op 2-2-2026 [1][2]. **Dit is de nieuwste definitieve versie (oktober 2026)**;
  - 3.9: alfa 28-7-2026, bèta gepubliceerd (gepland 9-10-2026), **definitief gepland op 11-12-2026** [3][7] ([§12.2](12-rgs-als-rubriekenschema.md#122-versies-en-planning)).
- **Download:** een Excel-werkboek met o.a. de tabbladen `Totaal-RGS3.8-def`, `MKB-RGS3.8-def` en `Recap` (releasenotes) [7][8]; daarnaast een XBRL-taxonomie [7].
- **Licentie: niet gevonden.** Het bestand is vrij downloadbaar, maar we vonden geen expliciete gebruiksvoorwaarden voor opname in software. **Navragen bij de Taakgroep RGS** (open vraag).

### Opbouw van de code [5][6][7]

| Niveau | Betekenis | Voorbeeld |
|---|---|---|
| 1 | Balans (B) of W&V (W) | `B` |
| 2 | Hoofdrubriek | `BIva` (immateriële vaste activa) |
| 3 | Rubriek | `BIvaKoo` (kosten van ontwikkeling) |
| 4 | Grootboekrekening | `BIvaKooVvp` (verkrijgingsprijs) |
| 5 | Mutatie | `…Beg` (beginbalans), `…Inv`, `…Afs`, … |

- De code begint met B of W en heeft daarna segmenten van drie tekens. **Een code op een dieper niveau begint altijd met de code van zijn ouder**, dus rubriceren kan met *langste-voorvoegsel-match*.
- RGS 3.5 had 4.740 codes, waarvan 2.973 op niveau 5 [11].
- De niveaustructuur volgt de BMJ-indeling van balans en W&V [boekhoudplaza].

**Velden in het werkboek** [7][5]:

| Veld | Betekenis |
|---|---|
| `Referentiecode` | **Sleutel**, bijv. `BMvaBeiCae` |
| `ReferentieOmslagcode` | Tegenrekening bij een omgekeerd saldo |
| `Sortering` | Sorteersleutel |
| `Referentienummer` | Voorbeeldnummer. **Verschilt per versie: nooit als sleutel gebruiken** [12][7] |
| `Omschrijving` (kort en lang) | |
| `D/C` | Normale saldokant |
| `Nivo` | Niveau |
| `Basis`, `Uitgebr`, `EZ/VOF`, `ZZP`, `WoCo` | **Kiesfilters**: hiermee begin je (in 3.9 komt `Zorg` erbij) |
| `BB`, `Agro`, `WKR`, `EZ/VOF`, `BV`, `WoCo`, `Bank`, … | **Wegfilters**: codes specifiek voor dat gebruik. `EZ/VOF` en `WoCo` staan in beide groepen; de positie bepaalt de betekenis ([§12.4](12-rgs-als-rubriekenschema.md#124-het-werkboek)) |
| `Inactief` | Vervallen code |

**Omslagcode** [5][7][9]: een bankrekening-courant `BLimBanRba` (D) heeft omslag `BSchSakRba` (rekening-courant kredietinstellingen, C). De omslag wordt **apart voor begin- en eindsaldo** toegepast [7]. Dat is precies het mechanisme voor de "omslagrubriek" in ons datamodel.

### Voorbeeldcodes (indicatief; verifiëren tegen de RGS 3.8-tabel)

| Post | RGS-code | Bron |
|---|---|---|
| Kas | `BLimKasKas` (D) | [12][7] |
| Bank rekening-courant | `BLimBanRba` (D), omslag `BSchSakRba` | [12][7] |
| Debiteuren | `BVorDeb` / `BVorDebHad` | [12][7] |
| Voorraad handelsgoederen | `BVrdHan` / `BVrdHanVoo` | [12] |
| Bedrijfsgebouwen | `BMvaBeg` (+ `Vvp` verkrijgingsprijs, `Cae` cumulatieve afschrijving) | [13] |
| Inventaris | `BMvaBei` / `BMvaBeiVvp` / `BMvaBeiCae` | [12] |
| Ontwikkelingskosten | `BIvaKoo` / `BIvaKooVvp` / `BIvaKooCae` | [13] |
| Goodwill | `BIvaGoo` (…) | [13][10] |
| Geplaatst kapitaal | `BEivGok` / `BEivGokGea` (C) | [7] |
| Overige reserves | `BEivOvr` | [13] |
| Onverdeelde winst / resultaat boekjaar | `BEivOre` / `BEivOreRvh` | [13] |
| Ondernemingsvermogen (IB) en privé | `BEivKap` / `BEivKapOnd`, `BEivKapPrs` / `BEivKapPro` | [12] |
| Crediteuren | `BSchCre` / `BSchCreHac` | [7] |
| Btw | `BSchBepBtw` | [12][7] |
| Loonheffing | `BSchBepLhe` | [12] |
| Vpb-schuld | `BSchBepVpb` | [12] |
| Omzet | `WOmzNop`, `WOmzNoh`, `WOmzNod` | [12] |
| Inkoopwaarde handelsgoederen | `WKprInh` | [12] |
| Lonen / sociale lasten | `WPerLes` / `WPerSol` | [12] |
| Afschrijvingen MVA / IVA | `WAfsAmv` / `WAfsAiv` | [12] |
| Huisvestingskosten | `WBedHui` | [12] |
| Rentelasten | `WFbeRls` | [12] |

### RGS-versies in de software
- Bewaar **per grootboekrekening de RGS-code én de RGS-versie** waaruit hij kwam (XAF 4.0 levert `RGSVersion`; bij 3.x en Excel vraagt de import erom, standaard de actuele versie).
- Bij een nieuwe RGS-versie importeert de beheerder het nieuwe werkboek. Vervallen codes (`Inactief`) en hernoemingen worden via een conversietabel (op basis van het `Recap`-tabblad) omgezet. Koppelingen worden alleen herberekend voor automatisch gekoppelde rekeningen, met een overzicht van de verschillen.

### Welke pakketten leveren RGS mee?

| Pakket | RGS in export | Bron |
|---|---|---|
| Exact Online | Ja, XAF 3.2 met RGS in `leadReference` | [26][34][35] |
| AFAS Profit | Ja (RGS 3.7, saldi per RGS) | [28] |
| SnelStart | Ja (RGS-koppeling per rekening) | [29][49] |
| Moneybird | Ja, sinds 1-7-2024 verplicht bij nieuwe rekeningen | [30] |
| Yuki | Ja, XAF 3.2 met RGS | [25] |
| Visma eAccounting | XAF 4.0.3 | [20] |
| Minox, CASH | Ja (`leadReference`, CASH in 4.0 `RGScode`) | [24][19] |
| Twinfield | RGS als rapportagestructuur; in XAF (O) | [27] |
| e-Boekhouden, King | (O) | – |

**Conclusie:** bij de meeste moderne pakketten levert de XAF een RGS-code mee. Daarmee kunnen we het grootste deel van de rekeningen automatisch rubriceren. Voor het restant zijn kantoorsjablonen en handmatig koppelen nodig (zie [hoofdstuk 4](04-rubricering-ontwerp.md)).

## 3.5 Testdata die we nodig hebben

- **Echte XAF-bestanden** (geanonimiseerd of met toestemming) uit Exact Online, Twinfield, SnelStart, AFAS, e-Boekhouden, Moneybird, Yuki en eventueel King. Bij voorkeur van klanten waarvan we het eindresultaat (het rapport uit het huidige pakket) al hebben. Dat geeft een *golden master*: onze proefbalans en rubricering moeten hetzelfde resultaat geven.
- **Minstens één XAF 4.0-bestand** per pakket dat het al ondersteunt.
- Openbare voorbeeldbestanden op GitHub (bijv. Exact 3.2 en Twinfield 3.1 [34], de XSD en test-XML 3.2 [14]) zijn bruikbaar voor de eerste parsertests. Het zijn derde-partijbronnen, maar de officiële XSD's halen we bij de ODB.

## Open vragen

1. Licentie- en gebruiksvoorwaarden voor het opnemen van de RGS-tabel in eigen software (Taakgroep RGS). Werkwijze tot dan: het werkboek niet in git, de beheerder leest het in ([§12.9](12-rgs-als-rubriekenschema.md#129-licentie-en-gebruik)).
2. De officiële XAF 4.0-XSD (incl. 4.0.3) en de verplichte velden. Verifiëren op odb.belastingdienst.nl.
3. De betekenis van `accTp = M` (3.2) en de volledige lijst van `jrnTp`-waarden.
4. Hoe de XAF-exports van SnelStart, AFAS, e-Boekhouden en King er in de praktijk uitzien (beginbalans, afsluitperioden, plaats van RGS).
5. Het formaat van de RGS Brugstaat (XML) en of die een zinvol tweede importformaat is.

## Bronnen

[1] https://www.referentiegrootboekschema.nl/Actueel/definitieve-versie-rgs-38-gepubliceerd ·
[2] https://www.referentiegrootboekschema.nl/definitieve-versie-rgs-38 ·
[3] https://www.referentiegrootboekschema.nl/actueel ·
[4] https://www.referentiegrootboekschema.nl/Actueel/definitieve-versie-rgs-37-gepubliceerd ·
[5] https://www.referentiegrootboekschema.nl/beschrijving-velddefinities-rgs ·
[6] https://www.referentiegrootboekschema.nl/opbouw-rgs ·
[7] https://github.com/MaxSchoon/rgs-skill (derde partij, gecontroleerd tegen RGS 3.8-def.xlsx) ·
[8] https://www.referentiegrootboekschema.nl/sites/default/files/kennisbank/RGS%203.8-def.xlsx ·
[9] https://www.boekhoudplaza.nl/wiki_uitleg/107&bronw=1/RGS_MKB_implementatie_voor_leveranciers_van_boekhoudsoftware.htm ·
[10] https://www.boekhoudplaza.nl/rgs_rekeningen/BIvaGooAkp&KB=X/Actuele_kostprijs_goodwill.htm ·
[11] https://boekhoudplaza.nl/upload/boekhoudplaza/rgs/versie35/Toelichting%20%20releasenotes%20bij%20definitieve%20versie%20RGS%203.5.pdf ·
[12] https://github.com/onesteinbv/addons-generic (l10n_nl_rgs; oudere RGS 3.x) ·
[13] https://github.com/Ekwo-ai/ekwo-os (RGS 3.8 MKB-selectie, derde partij) ·
[14] https://github.com/BananaAccounting/Netherlands (XAF 3.2 XSD + test-XML) ·
[15] https://github.com/samhoven16/boekhouding-engine (kopie XAF 4.0 XSD) ·
[16] https://odb.belastingdienst.nl/auditfiles/achtergrond-informatie-xaf4-0/ ·
[17] https://www.softwarepakketten.nl/swpakketten/auditfiles/auditfile_financieel.php?bronw=6 ·
[18] https://pypi.org/project/pyxaf/ ·
[19] https://www.cashweb.nl/handleiding/v3/s0680.htm ·
[20] https://eaccounting.nl/productnieuws/exportbestand-auditfile-bijgewerkt-naar-versie-xaf-4.0 ·
[21] https://www.sra.nl/nieuwsoverzicht/2024/consultatieversie-auditfile-financieel-4-xaf-beschikbaar ·
[22] https://www.referentiegrootboekschema.nl/rgs-en-de-koppeling-tussen-sbr-en-auditfile-financieel ·
[23] https://www.boekhoudplaza.nl/bericht/1453&bronw=1/RGS_in_de_Auditfile_FInancieel_XAF.htm ·
[24] https://www.cbs.nl/-/media/cbsvooruwbedrijf/productiestatistiek-rgs/minox_boekhoudsoftware_rgsready.pdf ·
[25] https://www.cbs.nl/-/media/cbsvooruwbedrijf/productiestatistiek-rgs/yuki_boekhoudsoftware_rgsready.pdf ·
[26] https://www.cbs.nl/-/media/cbsvooruwbedrijf/productiestatistiek-rgs/exactonline_boekhoudsoftware_rgsready.pdf ·
[27] https://www.nextens.nl/content/uploads/Handleiding_Faas-20230117.pdf ·
[28] https://help.afas.nl/help/NL/SE/Fin_Config_Ledger_RGS.htm ·
[29] https://kennisplein.snelstart.nl/klanten/s/article/rgs-en-snelstart ·
[30] https://developer.moneybird.com/changelog/2024-03-24 ·
[31] https://www.cbs.nl/en-gb/participants-survey/businesses/more-about-cbs-surveys/how-to-complete-the-questionnaire-automatically-using-rgs/rgs-bridge-statement ·
[32] https://helpcenter.nextens.nl/Html/portal/importeren-rgs-brugstaat.html ·
[34] https://github.com/AnalyticsLibrary/Analytics (voorbeeld-XAF's Exact 3.2 / Twinfield 3.1) ·
[35] https://github.com/melvinfolkers/auditfiles ·
[41] https://odb.belastingdienst.nl/wp-content/uploads/2024/01/2014_02_19_n.a.v._de_bijenkomst_SBR_koppeling_in_Auditfile_Financieel.pdf ·
[43] https://github.com/shipdocs/boekhoudenvoorniks (derde partij) ·
[44] https://github.com/Uniconta/Uniconta-Pages ·
[45] https://www.boekhoudplaza.nl/wiki_uitleg/125/RGS_brugstaat_en_formaat.htm ·
[46] https://www.accountancyvanmorgen.nl/2026/06/26/xaf-4-0-komt-eraan-maar-de-accountant-zit-nog-te-stoeien-met-excel/ ·
[47] https://learn.microsoft.com/en-us/dynamics365/release-plan/2025wave2/enterprise-resource-planning/dynamics365-finance/use-regulatory-update-audit-file-financial-xaf-4-netherlands-2026 ·
[49] https://www.cbs.nl/-/media/cbsvooruwbedrijf/productiestatistiek-rgs/snelstart_boekhoudsoftware_rgsready.pdf ·
[50] https://www.belastingdienst.nl/wps/wcm/connect/nl/intermediairs/content/referentie-grootboekschema-iets-voor-u
