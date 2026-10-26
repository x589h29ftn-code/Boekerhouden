# Administratiepakket: ontwerp en roadmap

*Stand van zaken: 26 oktober 2026*

Naast de samenstel-app komt een **eigen administratiepakket**: een webapp waarin de administratie
van een ondernemer gevoerd wordt. Het doel is **zo veel mogelijk automatisch**: inkoopfacturen
inlezen en herkennen, voorstellen die leren van eerdere keuzes, slimme regels, verkoopfacturen die
vanzelf bij de debiteuren staan, en een bank die zelf aflettert. Wat overblijft, staat in een
korte werklijst. Aan het eind van het jaar gaat de saldibalans zonder export naar het
samenstelpakket.

## 1. Besluiten

| # | Onderwerp | Besluit |
|---|---|---|
| AD1 | Plaats | **Zelfde repository, eigen app**: `src/Boekerhouden.Boekhouding` (domein, geen I/O) en straks `src/Boekerhouden.Administratie` (webapp met een eigen database). RGS, bedragen en de saldibalans komen uit `Boekerhouden.Domain` |
| AD2 | Toegang | **Eerst op kantoor, later online.** Het kantoor boekt eerst zelf op de bestaande server-pc. Ondernemers krijgen toegang via internet zodra hosting, tweestapsverificatie en back-up rond zijn. Rechten zijn daarom vanaf het begin per administratie geregeld. Besluit A2 (alleen binnen kantoor) blijft gelden tot dat moment |
| AD3 | Herkenning (OCR) | **Stapsgewijs, met een uitwisselbare motor.** Eerst e-facturen (UBL/Peppol, exact en gratis) en PDF's met een tekstlaag, daarna Tesseract voor scans (open source, lokaal). Alles zit achter één interface (`IFactuurHerkenner`), zodat later eventueel een AI-model kan worden ingeplugd |
| AD4 | Rekeningschema | **RGS** ([§12 onderzoek](../onderzoek/12-rgs-als-rubriekenschema.md)): elke grootboekrekening heeft een RGS-code. Dan is de saldibalans meteen gerubriceerd voor de jaarrekening |
| AD5 | Bank | **Eerst bestanden**: MT940 en CAMT.053 (alle Nederlandse banken leveren beide). Een directe bankkoppeling (PSD2) kan alleen via een vergunninghouder en kost geld. Die komt later, achter dezelfde interface als de bestanden |
| AD6 | Leren | **Eerste keer kiest de gebruiker, tweede keer een voorstel, derde keer automatisch** (bij dezelfde keuze als de vorige twee keer). Een vaste regel van de gebruiker gaat altijd voor |
| AD7 | Beveiliging | Uitgewerkt in [beveiliging.md](beveiliging.md): wat er op het spel staat, wat gebruikelijk is, maatregelen per fase (kantoor / online) en beslispunten |

## 2. Werkstroom

```
 Inkoop                     Verkoop                    Bank
 ──────                     ───────                    ────
 upload / e-mail / UBL      factuur maken              MT940 / CAMT.053 (later PSD2)
        │                         │                          │
 herkennen (UBL > tekst > OCR)    nummeren, PDF              mutaties inlezen, saldo controleren
        │                         │                          │
 leverancier herkennen            boeken: debiteuren,        afletteren op openstaande posten
 (btw-nr > IBAN > KvK > naam)     omzet, af te dragen btw    (kenmerk, factuurnummer, IBAN, bedrag)
        │                         │                          │
 regels > geleerd > niets         openstaande post           bankregels (rente, kosten, belasting)
        │                                                    │
 automatisch of in "Te verwerken"                            rest: werklijst
        │                                                    ├─ betaling zonder factuur → factuur uploaden
 boeken: kosten, voorbelasting,                              └─ factuur zonder betaling → herinneren/controleren
 crediteuren, openstaande post
                         ╲            │            ╱
                          journaal (inkoop, verkoop, bank, kas, memoriaal)
                                      │
            grootboekkaarten · proefbalans · balans · W&V · btw-aangifte
                                      │
                       saldibalans → samenstelpakket (jaarrekening)
```

## 3. Onderdelen

### 3.1 Grootboek en journaal

- **Rekeningschema** per administratie: nummer, naam, RGS-code en eventueel een **functie**
  (bank, kas, debiteuren, crediteuren, te vorderen btw, af te dragen btw, resultaat). Met de functie
  vinden de automatische boekingen de juiste rekening, zonder vaste nummers.
- **Dagboeken**: inkoop, verkoop, bank (één per bankrekening, met de grootboekrekening van de
  bank), kas en memoriaal.
- **Journaalpost**: dagboek, boekstuknummer, datum, omschrijving en regels (rekening, bedrag
  debet + / credit −, omschrijving, btw-code, relatie, factuurnummer). Een journaalpost is altijd in
  evenwicht, met bedragen in centen en minstens twee regels.
- **Memoriaal**: vrije journaalposten, bijvoorbeeld voor afschrijvingen, overlopende posten,
  correcties en de beginbalans.
- **Overzichten**:
  - **grootboekkaart** per rekening: beginsaldo, mutaties met lopend saldo, eindsaldo;
  - **proefbalans** per datum;
  - **balans en W&V** gegroepeerd op RGS-hoofdpost (niveau 2), met het resultaat;
  - **saldibalans** voor het samenstelpakket (rekening, naam, saldo, RGS-code).
- **Periodes**: boekjaar en maand. Een afgesloten periode (bijvoorbeeld na de btw-aangifte) kan niet meer
  bebouwd worden.

### 3.2 Btw

- **Btw-codes** met soort (hoog 21%, laag 9%, nul, vrijgesteld, verlegd, EU, buiten de EU, geen),
  richting (inkoop of verkoop) en **rubriek van de aangifte** (1a, 1b, 1e, 2a, 3b, 4a, 4b …).
- **Bij boeken**: voorbelasting of af te dragen btw op de btw-rekening. Bij verlegging en bij inkopen uit
  de EU gaan zowel de af te dragen btw als de voorbelasting op dezelfde post.
- **Btw-aangifte** per periode: per rubriek de omzet en de btw, controle op het saldo van de
  btw-rekeningen, en de afdrachtboeking naar de rekening *te betalen omzetbelasting*.

### 3.3 Relaties en openstaande posten

- **Relatie**: naam, één of meer IBAN's, btw-nummer, KvK-nummer, adres, betalingstermijn en de
  **standaard tegenrekening** (wordt geleerd).
- **Openstaande post**: één per factuur, met het oorspronkelijke bedrag, het openstaande bedrag, het factuurnummer, het
  betalingskenmerk en de vervaldatum. Bij debiteuren is het bedrag positief, bij crediteuren
  negatief (de tekenconventie van het grootboek). Een afgeletterde betaling maakt het saldo nihil.
- **Ouderdomsanalyse** van de debiteuren en crediteuren (0–30, 31–60, 61–90, >90 dagen).

### 3.4 Inkoopfacturen: inlezen, herkennen, voorstellen

1. **Inlezen**: uploaden (slepen, meerdere tegelijk), later per e-mail (eigen adres per administratie)
   en via de telefoon (foto).
2. **Herkennen** (`IFactuurHerkenner`, AD3), in volgorde van betrouwbaarheid:
   1. **UBL/e-factuur** (los of als bijlage in de PDF): exacte gegevens, geen OCR nodig;
   2. **PDF met tekstlaag**: tekst eruit, dan tekstanalyse;
   3. **scan of foto**: Tesseract (Nederlands), dan dezelfde tekstanalyse.
   
   De **tekstanalyse** zoekt het factuurnummer, de factuur- en vervaldatum, IBAN, btw-nummer (met
   controle), KvK, het totaal, het btw-bedrag per tarief en het bedrag exclusief btw. Ze controleert ook of
   het geheel klopt: excl + btw = totaal, en btw = tarief × grondslag (met een afrondingsmarge).
3. **Leverancier herkennen**: eerst op btw-nummer, dan IBAN, dan KvK en ten slotte op naam (genormaliseerd,
   zonder "B.V." e.d.). Een onbekende leverancier wordt met de herkende gegevens als nieuwe relatie
   voorgesteld.
4. **Voorstel voor rekening en btw-code**, in deze volgorde:
   1. **Regels van de gebruiker** (§3.6), bijvoorbeeld: *KPN, totaal tussen € 10 en € 30 → telefoonkosten, 21%,
      automatisch*;
   2. **Geleerd** (AD6): de eerste keer kiest de gebruiker. De tweede keer volgt een voorstel. Is de
      keuze twee keer achter elkaar hetzelfde geweest, dan verwerkt het pakket de derde factuur zelf.
      Wijkt de gebruiker af, dan begint de telling opnieuw. Staat er op de factuur een ander btw-tarief dan
      bij de geleerde keuze (9% in plaats van 21%), dan past het voorstel de btw-code aan en verwerkt het
      pakket de factuur niet zelf;
   3. **Niets**: de gebruiker kiest.
5. **Automatisch verwerken** gebeurt alleen als ook alles klopt: leverancier herkend,
   factuurnummer en datum gevonden, totalen sluitend, niet eerder geboekt (zelfde leverancier en
   factuurnummer, of zelfde bedrag en datum) en de periode open. Anders komt de factuur in **Te verwerken**, met
   de reden erbij.
6. **Boeken**: kosten (excl.) debet, voorbelasting debet en crediteuren credit, plus een openstaande post. Het
   document hoort bij de journaalpost en blijft altijd op te vragen.

### 3.5 Verkoopfacturen

- **Factuur maken**: klant, regels (omschrijving, aantal, prijs, btw-code, omzetrekening), korting,
  betalingstermijn, en een **doorlopende nummering** per jaar (bijv. `2026-0001`, zonder gaten).
- Bij **definitief maken**: een PDF in de huisstijl, de boeking (debiteuren debet, omzet en af te
  dragen btw credit) en een **openstaande post in de debiteurenlijst**. Daarna wijzigen kan
  alleen met een creditfactuur.
- **Betalingskenmerk** op de factuur. De klant betaalt met dat kenmerk, en het afletteren is dan 100% zeker.
- **Later**: verzenden per e-mail, UBL meesturen, herinneringen en terugkerende facturen
  (abonnementen).

### 3.6 Regels (automatisering)

Een **verwerkingsregel** heeft voorwaarden en een actie, en werkt voor inkoopfacturen én
bankmutaties:

| Voorwaarde | Voorbeeld |
|---|---|
| Relatie of naam bevat | `KPN` |
| Bedrag (totaal) van–tot | € 10 – € 30 |
| Omschrijving bevat | `rente`, `abonnement` |
| Tegenrekening (IBAN) | `NL..BELA..` (Belastingdienst) |
| Richting | betaling of ontvangst |

**Actie**: grootboekrekening, btw-code, omschrijving en **automatisch verwerken** ja/nee. Regels hebben
een volgorde: de eerste die past, wint. Kenmerkende bankregels worden standaard meegeleverd, zoals
*bankkosten → algemene kosten, geen btw*.

### 3.7 Bank

- **Inlezen**:
  - **MT940** (`:20:`, `:25:`, `:28C:`, `:60F:`, `:61:`, `:86:` en `:62F:`). De `:86:` verschilt per bank: gestructureerd met
    `/IBAN/`, `/NAME/`, `/REMI/` en `/EREF/` (ING, Rabobank, bunq e.a.), of vrije tekst met `IBAN:`, `NAAM:` en
    `OMSCHRIJVING:` (ABN AMRO).
  - **CAMT.053** (XML, ISO 20022): per mutatie het bedrag, debet/credit, de datum, de tegenpartij met IBAN,
    de omschrijving (`Ustrd`), het gestructureerde betalingskenmerk en de end-to-end-id.
  - **Controle**: beginsaldo + mutaties = eindsaldo, en het beginsaldo sluit aan op het vorige afschrift. Een
    mutatie die al eerder is ingelezen, wordt niet nog eens geboekt.
- **Handmatig inboeken** van een mutatie: op een openstaande post, op een grootboekrekening (met btw-code) of
  gesplitst over meerdere regels.
- **Afletteren** (§3.8): wat zeker is, wordt automatisch geboekt. Wat waarschijnlijk is, staat klaar als
  voorstel, met één klik te bevestigen. De rest gaat naar de werklijst.
- **Werklijsten**:
  - **betalingen en ontvangsten zonder factuur**: per mutatie een knop *factuur uploaden*. Een factuur die later
    binnenkomt, wordt automatisch gekoppeld;
  - **openstaande posten zonder betaling**, op vervaldatum;
  - **niet-afgeletterde verschillen**, zoals deelbetalingen.

### 3.8 Afletteren

Per bankmutatie zoekt de afletteraar tussen de openstaande posten met **hetzelfde teken**: een ontvangst bij
debiteuren, een betaling bij crediteuren. Elke kandidaat krijgt een **zekerheid** en een **reden**:

| # | Kenmerk | Zekerheid |
|---|---|---|
| 1 | Gestructureerd **betalingskenmerk** gelijk | Zeker |
| 2 | **Factuurnummer** in de omschrijving, en het bedrag gelijk | Zeker |
| 3 | **IBAN** van de relatie bekend, het bedrag gelijk, en het is de enige open post van die relatie met dat bedrag (ook zonder factuurnummer) | Zeker |
| 4 | Factuurnummer in de omschrijving, maar het bedrag wijkt af (deelbetaling, betalingskorting) | Voorstel |
| 5 | IBAN bekend, en het bedrag gelijk aan meerdere open posten | Voorstel (oudste eerst) |
| 6 | IBAN bekend, en het bedrag gelijk aan de **som van enkele** open posten (verzamelbetaling) | Voorstel |
| 7 | **Naam** van de tegenpartij lijkt op de relatienaam, en het bedrag gelijk | Voorstel |
| 8 | Het bedrag is gelijk en uniek over alle open posten | Voorstel |

- **Zeker** wordt automatisch geboekt, maar alleen als er precies één zekere kandidaat is.
- Bij een **voorstel** bevestigt de gebruiker. Bevestigt hij, dan leert het pakket het IBAN bij de relatie, zodat
  het de volgende keer *zeker* is.
- Een factuurnummer wordt herkend zonder leestekens of hoofdlettergebruik (`2026-001` = `2026001`). Een kort
  nummer (minder dan vier tekens) moet als los woord in de omschrijving staan.

### 3.9 Rechten en gebruikers

- Een gebruiker heeft per administratie de rol **ondernemer** (inlezen, factureren, eigen
  overzichten) of **kantoor** (alles, ook memoriaal, periodes en regels).
- Elke handeling die gegevens wijzigt, staat in het **wijzigingslog** (wie, wat, onderwerp), zoals in de
  samenstel-app.

### 3.10 Koppeling met het samenstelpakket

Aan het eind van het jaar staat de saldibalans **met RGS-codes** in de administratie. Het
samenstelpakket haalt die op als import, zonder sjabloon. De codes zijn hetzelfde, dus het rubriceren is al
gedaan.

## 4. Techniek

- **Zelfde stack** als de samenstel-app ([§7 onderzoek](../onderzoek/07-technische-architectuur.md)): .NET 10, Blazor
  Server, PostgreSQL (een **eigen database** `boekerhouden_administratie`), Identity en het wijzigingslog.
- **Domein zonder I/O** in `Boekerhouden.Boekhouding`, met namespaces `Grootboek`, `Btw`, `Relaties`,
  `Facturen`, `Bank`, `Afletteren`, `Regels` en `Herkenning`. Bankbestanden en UBL worden gelezen uit een `TextReader`/`Stream`.
- **Pakketten** (alle open source, licentie gecontroleerd vóór het toevoegen):

  | Doel | Pakket | Licentie |
  |---|---|---|
  | Tekst uit PDF | PdfPig | Apache-2.0 |
  | OCR | Tesseract (via `TesseractOCR` of de opdrachtregel) | Apache-2.0 |
  | PDF van een verkoopfactuur | de bestaande `PdfDienst` (Chromium) | – |

- **Bedragen**: `decimal`, exact in centen. Btw wordt per regel berekend en per tarief opgeteld, met
  afronding *away from zero*.

## 5. Roadmap in plakjes

| # | Plakje | Resultaat |
|---|---|---|
| A1 ✅ | **Grootboek en journaal** (domein): rekeningschema met RGS en functies, dagboeken, journaalpost met controles, grootboekkaart, proefbalans, balans en W&V per RGS-hoofdpost, saldibalans; btw-codes en het boeken van inkoop- en verkoopfacturen met openstaande posten | Kern van de boekhouding, volledig getest |
| A2 ✅ | **Bankbestanden** (domein): MT940 (gestructureerd en vrije tekst) en CAMT.053, met saldocontrole | Afschriften van alle gangbare banken inlezen |
| A3 ✅ | **Afletteren** (domein): kandidaten met zekerheid en reden (§3.8), verzamelbetalingen | Slimme aflettering, ook zonder factuurnummer |
| A4 ✅ | **Regels en leren** (domein): verwerkingsregels, leergeheugen (1-2-3), advies voor factuur en bankmutatie | *KPN € 10–30 → telefoonkosten*, en zelf leren |
| A5 ✅ | **Herkenning** (domein): tekstanalyse van een factuur, UBL lezen, leverancier herkennen | Gegevens uit e-facturen en PDF-tekst |
| A6 | **Webapp I**: project `Boekerhouden.Administratie`, database, inloggen, administraties met rechten, rekeningschema (vanuit RGS), memoriaal, grootboekkaart, proefbalans, balans en W&V | Werkende boekhouding in de browser |
| A7 | **Inkoop in de app**: uploaden, herkennen (UBL, PdfPig), Te verwerken, voorstel en automatisch boeken, regels beheren | Inkoopfacturen verwerken |
| A8 | **Bank in de app**: inlezen, afletteren (automatisch en met voorstellen), handmatig boeken, werklijsten met *factuur uploaden* | Bank bijna vanzelf bij |
| A9 | **Verkoop**: factuur maken, nummeren, PDF, debiteurenlijst, ouderdomsanalyse | Factureren |
| A10 | **Btw-aangifte en periodes**: aangifte per rubriek, afdrachtboeking, periodes afsluiten | Aangifte klaarzetten |
| A11 | **Scans**: Tesseract voor scans en foto's | Ook papieren bonnen |
| A12 | **Saldibalans naar het samenstelpakket** | Jaarrekening zonder export |
| later | Online voor ondernemers (AD2), facturen per e-mail, directe bankkoppeling (PSD2), herinneringen, terugkerende facturen, kas, AI-herkenning als extra motor, e-facturen verzenden (Peppol) | |

### Stand na A1–A5 (en het begin van A7)

De boekhoudkern staat in `src/Boekerhouden.Boekhouding`, met tests in `tests/Boekerhouden.Boekhouding.Tests` en
verzonnen voorbeeldbestanden in `tests/Fictief/administratie`:

| Onderdeel | Klassen |
|---|---|
| Grootboek | `Rekeningschema`, `Journaal` (boeken met controles, `Kaart`, `Proefbalans`), `Jaaroverzicht`, `Proefbalans.NaarSaldibalans` |
| Btw | `BtwCodes.Standaard`, `BtwBerekening` |
| Facturen | `Factuurboeker` (factuur → journaalpost + openstaande post) |
| Bank | `Bankbestand` (herkent MT940 of CAMT.053), `Mt940Lezer`, `Camt053Lezer`, `Bankboeker` (afletteren, op rekening) |
| Afletteren | `Afletteraar` (kandidaten met zekerheid en reden), `Factuurnummers` |
| Regels | `Verwerkingsregel`, `Leergeheugen`, `Verwerkingsadviseur` |
| Herkenning | `Factuurtekst` (tekstanalyse), `UblFactuurLezer`, `Leverancierherkenning`, `Inkoopverwerker` (herkende factuur → voorstel met belemmeringen), `IFactuurHerkenner` |
| Documenten (`src/Boekerhouden.Documenten`) | `PdfTekst` (tekst uit een PDF met PdfPig, regels op hun plek, begrensd), `PdfFactuurHerkenner` (UBL-bijlage of tekst), `UblHerkenner`, `Factuurherkenning` (motoren op volgorde) |

Proberen op een eigen factuur (die blijft buiten git, bijv. in `testdata/`):

```bash
dotnet run --project src/Boekerhouden.Cli -- herken testdata/factuur.pdf --eigen-naam "Mijn Bedrijf B.V." --leer 4500 I21
```

Dat toont wat het pakket herkent, het inkoopvoorstel in een lege administratie, en daarna drie facturen van dezelfde
leverancier: kiezen, voorstel, automatisch, met de journaalpost.

Nog niet gebouwd: opslag (database), schermen, OCR, verkoopfacturen als document, btw-aangifte en
periodes. Dat volgt vanaf plakje A6.

## 6. Open vragen

1. **Btw-tarieven en -rubrieken** zijn opgenomen zoals in 2026 (21%, 9%, 0%). Controleer de rubrieken van het
   aangifteformulier bij plakje A10.
2. **Drempel voor automatisch verwerken**: nu twee gelijke keuzes op rij. Per administratie instelbaar maken?
3. **Directe bankkoppeling**: welke aanbieder (kosten per rekening per maand), en wanneer?
4. **Hosting voor ondernemers** (AD2): VPS in Nederland, tweestapsverificatie verplicht, back-up buiten de
   server. Een besluit is nodig vóór "later".
