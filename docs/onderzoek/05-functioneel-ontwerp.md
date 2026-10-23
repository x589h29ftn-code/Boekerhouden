# 5. Functioneel ontwerp

> **Bijgewerkt op 23-10-2026**: meerdere gebruikers met wachtwoord (M0), het standaard importsjabloon (M4), werk alleen voor door de gebruiker gekozen materiële posten (M7/M8), PDF en Word (M10), en kengetallen en Vpb in de MVP (M13/M14). Opdrachttypen: *Samenstellen jaarrekening* en *Winstaangifte opstellen*. Rechtsvormen in de MVP: BV klein en eenmanszaak; BV micro en VOF later. Alleen binnen kantoor. Deponeren valt buiten de scope.

> Dit hoofdstuk vertaalt de marktreferentie ([hoofdstuk 1](01-gangbare-werkwijze-samenstelsoftware.md)),
> de beroepsregels ([hoofdstuk 2](02-vaktechnisch-kader.md)) en de import- en rubriceringslogica
> ([hoofdstukken 3](03-gegevensimport-xaf-excel-rgs.md) en [4](04-rubricering-ontwerp.md)) naar modules
> en schermen voor onze eigen applicatie. Per module staat of die in de **MVP** zit of later volgt.

## 5.1 Modules in één oogopslag

| # | Module | Doel | MVP? |
|---|---|---|---|
| M1 | Klantenmonitor | Overzicht van alle klanten met status, signalen en open taken | ✅ |
| M2 | Klantbeheer | Klantgegevens, rechtsvorm, grootte, functionarissen, Wwft-profiel | ✅ |
| M3 | Dossierbeheer | Dossier aanmaken per boekjaar en opdracht, sjabloon kiezen, vorig jaar doorrollen | ✅ |
| M4 | Import | **Standaard importsjabloon** (boekjaar + grootboeknummer, omschrijving, bedrag, controle op saldo nihil; [§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv)) en XAF-auditfile. Herimport, importhistorie, ook voor vorige jaren | ✅ (sjabloon eerst) |
| M5 | Rubriceren (koppelen) | Grootboekrekeningen aan rubrieken koppelen via sjabloon, RGS of handmatig, met voorstellen | ✅ |
| M6 | Proefbalans en correcties | Gecorrigeerde proefbalans, memoriaal- en herrubriceringsboekingen | ✅ |
| M0 | Inloggen en gebruikers | Eigen account met wachtwoord, rollen, lockout, audit log (besluit B1, [§7.8](07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)) | ✅ |
| M7 | Werkprogramma en dossieronderdelen | Fasen, onderdelen en stappen invullen. **De gebruiker vinkt per hoofdpost aan of die materieel is**, met doorklikken naar de subposts. Alleen materiële posten krijgen werk. Stappen toevoegen en verwijderen, aftekenen, goedkeuren ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)) | ✅ |
| M8 | Cijferbeoordeling | Huidig, vorig en verschil per materiële post, met doorklikken en toelichting | ✅ |
| M9 | Rapportpresentatie per post | Voor **alle** posten: specificaties (uit het grootboek, samenvoegen, omschrijving aanpassen, verbergen), verloopoverzichten, toelichtende tekst, opmaak ([§10.4](10-werkprogramma-en-materialiteit.md#104-rapport-presentatie-en-opmaak-per-post)) | ✅ |
| M10 | Rapportgenerator | Financieel rapport samenstellen uit sjabloon, bouwstenen ([hoofdstuk 9](09-analyse-voorbeeldrapporten.md)), standaardteksten en cijfers, met preview, PDF en Word | ✅ PDF + Word (besluit B5) |
| M11 | Review en afronding | Reviewpunten, goedkeuring, rapport uitbrengen, dossier sluiten en archiveren | ✅ |
| M12 | Beheer (kantoor) | Rubriekenschema's, RGS-tabel, koppelsjablonen, werkprogramma's, standaardteksten, rapportsjablonen, gebruikers | ✅ (basis) |
| M13 | Kengetallen en meerjarenoverzicht | Liquiditeit, solvabiliteit, omloopsnelheid, rentabiliteit, marges, fte-kengetallen over 5 jaar, verschillenanalyse | ✅ (staat in het voorbeeldrapport BV) |
| M14 | Fiscale positie | Vpb-berekening (incl. fiscale eenheid-variant), belastinglast en belastingpositie. Fiscale winst IB: zie vragenlijst D6 | ✅ Vpb; IB volgens D6 |
| M15 | Taken en planning | Taken per klant/dossier, deadlines, urenregistratie | Fase 2 |
| — | Deponeren (SBR/XBRL, publicatiestukken), API-koppelingen boekhoudpakketten | | ❌ Buiten scope |

## 5.2 Schermen (afgeleid van de schermafbeeldingen)

### S1 – Klantenmonitor (screenshot 1)
Een tabel met alle klanten:

| Kolom | Betekenis in onze app |
|---|---|
| Klant | Naam + klantnummer |
| Alerts | Aantal signalen. Voorbeelden: ongekoppelde rekeningen met saldo, proefbalans niet in evenwicht, beginbalans sluit niet aan op vorig jaar, specificatie wijkt af van saldo, open reviewpunten, dossier te lang open |
| Geïmporteerd | Datum laatste import. **Rood** als die ouder is dan de drempel (bijv. 90 dagen) of ouder dan de einddatum van het boekjaar |
| Laatst bewerkt | Datum en medewerker van de laatste wijziging. Rood als het lang geleden is |
| Gekoppeld | % van de grootboekrekeningen *met saldo* dat aan een rubriek is gekoppeld. Doel: 100% |
| Lopend dossier | Boekjaar, opdrachttype en status van het lopende dossier |
| Open taken | Aantal open taken en reviewpunten |

Met zoeken, filteren (medewerker, status, rechtsvorm), sorteren, een knop "Klant toevoegen" en een selectievakje voor bulkacties.

### S2 – Dossierlijst per klant (screenshot 2)
Kolommen Dossier, Jaar, Soort opdracht, Status, Laatst gewijzigd en Acties (verwijderen alleen als het dossier niet is gesloten).
Knop **+ Dossier** start een wizard:
1. Boekjaar en periode (verkort of verlengd boekjaar mogelijk).
2. Soort opdracht: **Samenstellen jaarrekening** (BV) of **Winstaangifte opstellen** (eenmanszaak). Dit bepaalt welk werkprogrammasjabloon en rapportsjabloon gekozen worden.
3. Sjabloonversies (de standaard is de nieuwste actieve versie).
4. **Doorrollen vanuit vorig dossier**: vergelijkende cijfers, specificaties (omschrijvingen en vorig-jaarbedragen), verloopoverzichten (eindstand wordt beginstand), aangepaste rapportteksten, terugkerende correctieboekingen en permanente dossierstukken (statuten, opdrachtbevestiging).

### S3 – Dossierwerkruimte (screenshots 3 en 4)
Drie kolommen:

- **Links: dossierboom** met fasen en onderdelen, een statusicoon per onderdeel (open, gereed, goedgekeurd) en een `*` bij verplichte onderdelen. Bovenin de klant, het dossier, de dossierstatus ("DOSSIER GESLOTEN") en een dossierwisselaar.
- **Midden: inhoud van het onderdeel**, met de tabs **Dossier** en **Rapport**. Een niet-materiële post heeft alleen de tab Rapport en staat niet onder *Uitvoering* ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)):
  - De kop toont de titel en de goedkeuringsregel ("Goedgekeurd door … op …").
  - Een werkbalk voor opmaak van vrije tekst (vet, cursief, lijsten, tabel, link, speciale tekens, standaardtekst invoegen ★), ongedaan maken en opnieuw.
  - Blokken uit het sjabloon:
    - **Toelichting/Vraag** (bijv. "Fundamentele beginselen", "Integriteit van de klant", "Wwft", "Opdrachtaanvaarding"): standaardantwoord als grijze tekst (te accepteren of te overschrijven), vrije notitie en een bijlage.
    - **Aanpak en cijferbeoordeling** (alleen materiële posten): de tabel hoofdpost → subposts → grootboekrekeningen × Huidig, Vorig, Verschil (doorklikken), met een notitieveld per post ("Zijn de afschrijvingen beoordeeld en juist geboekt?").
    - **Werkprogramma**: stappen (bijv. "Verloopoverzicht") met instructietekst, antwoordveld, initialen, datum en een afvinkvak.
  - **Tab Rapport** (alle posten): laat zien hoe de post in het rapport komt (bijv. "Specificaties van balans → Immateriële vaste activa"). Hier pas je de presentatie en opmaak aan: specificatieregels, omschrijvingen, samenvoegen, **Toon in rapport**, verloopoverzicht en tekstblokken ([§10.4](10-werkprogramma-en-materialiteit.md#104-rapport-presentatie-en-opmaak-per-post)).
- **Rechts: gereedschapsbalk** met panelen die over het onderdeel heen schuiven:
  - ⚖️ proefbalans en rubrieken (koppelen)
  - 📖 standaardteksten of handboek (NV COS / werkprogramma-uitleg)
  - 🏛 jaarrekening/rapport of fiscaal
  - 🔧 correctieboekingen en hulpmiddelen
  - 📎 bijlagen
  - 💬 opmerkingen/reviewpunten
  - ⇄ koppelingen
  - ⚙️ dossierinstellingen

### S4 – Importwizard
1. Bestand kiezen: **standaard importsjabloon** (XLSX/CSV) of XAF. Het type wordt automatisch herkend. Knop *Download importsjabloon*.
2. Bij het **sjabloon**: boekjaar en klant uit het bestand tonen ter bevestiging, en de eerste regels plus het totaal laten zien.
   Bij **XAF**: de header tonen (bedrijf, boekjaar, pakket, aanmaakdatum) en controleren of het boekjaar en de klant kloppen (KvK/naam).
3. Controles ([§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv)): verplichte kolommen, unieke grootboeknummers, geldige bedragen en **saldo nihil**. Bij een verschil stopt de import en wordt het verschil getoond. Daarnaast: beginbalans gelijk aan eindbalans vorig jaar (XAF), en een waarschuwing als de W&V leeg lijkt.
4. Verschillenoverzicht bij herimport: nieuwe rekeningen, vervallen rekeningen, gewijzigde saldi.
5. Bevestigen. De importbatch wordt opgeslagen, en de rubriceringsengine draait voor nieuwe of ongekoppelde rekeningen.

### S5 – Koppelscherm (rubriceren)
Twee panelen:
- **Links**: lijst grootboekrekeningen met nummer, omschrijving, RGS-code, saldo, huidige rubriek, bron (sjabloon, RGS, vorig jaar, handmatig, voorstel) en status. Filters: *ongekoppeld*, *voorgesteld*, *gewijzigd t.o.v. vorig jaar*, *saldo ≠ 0*.
- **Rechts**: de rubriekenboom met totalen.
- **Acties**: slepen naar een rubriek, meerdere tegelijk koppelen, voorstel accepteren of afwijzen, "toepassen op alle jaren", en "opslaan in kantoorkoppelsjabloon" (alleen voor beheerders).
- **Voortgangsbalk**: "Gekoppeld: 97% (3 rekeningen met saldo ongekoppeld)".

### S6 – Correctieboekingen
Journaalpostinvoer met regels (rekening, omschrijving, debet, credit) en een balanscontrole (Σ = 0). Soort boeking, een vinkje "terugkerend" (wordt automatisch meegenomen in het volgende dossier) en een vinkje "verwerkt in administratie klant". Overzicht van alle correcties met hun effect op het resultaat en het eigen vermogen.

### S7 – Rapporteditor
- Inhoudsopgave van het rapport (secties aan/uit volgens het sjabloon en de voorwaarden).
- Per tekstsectie: de standaardtekst met ingevulde variabelen. Bewerken maakt een dossierspecifieke versie, met de optie "terugzetten naar standaard".
- Cijfersecties (balans, W&V, specificaties) worden gegenereerd en zijn niet vrij te bewerken. Wijzigen kan alleen via correcties of specificaties.
- Live preview (gepagineerd), daarna **PDF genereren** (concept met watermerk, of definitief bij het uitbrengen).

### S8 – Beheer
- **Rubriekenschema-editor** (boom), RGS-import (Excel uit RGS-download) en het beheer van de **RGS→rubriek-tabel**.
- **Koppelsjablonen** per boekhoudpakket.
- **Werkprogramma-editor**: fasen, onderdelen, blokken, verplicht, voorwaarden en versies.
- **Standaardtekstbibliotheek** met variabelenlijst en preview.
- **Rapportsjablonen** en **huisstijl** (logo, lettertype, kleuren, kop- en voettekst).
- **Gebruikers en rollen**.

## 5.3 Werkstroom (happy flow)

```
Klant aanmaken ─► Dossier 2025 aanmaken (soort opdracht "Samenstellen jaarrekening", BV klein, doorrollen uit 2024)
   │
   ├─ Profiel: klantgegevens, opdracht, rapportinstellingen controleren
   ├─ Voorbereiding: acceptatie (fundamentele beginselen, integriteit, Wwft, opdrachtbevestiging) ✔
   ├─ Import importsjabloon (of XAF) 2025, saldo nihil ✔ ─► automatisch rubriceren ─► restant handmatig ─► 100% gekoppeld
   ├─ Planning: gebruiker vinkt materiële hoofdposten aan (doorklikken naar subposts)
   ├─ Uitvoering, alleen materiële posten: cijferbeoordeling en werkprogramma-stappen (toevoegen/verwijderen),
   │                       correctieboekingen (bijv. afschrijvingen, overlopende posten, Vpb)
   ├─ Rapport, alle posten: presentatie en opmaak per post, teksten nalopen, preview, concept-PDF/Word naar klant
   ├─ Afronding: evaluatie, bevestiging klant (bevestigingsverklaring), review en goedkeuring,
   │             definitief rapport uitbrengen (PDF + hash vastgelegd)
   └─ Dossier sluiten (binnen termijn) ─► alleen-lezen, gearchiveerd
```

## 5.4 Bedrijfsregels en controles (de "alerts")

| Code | Controle | Ernst |
|---|---|---|
| C01 | Proefbalans import in evenwicht (Σ debet = Σ credit) | Blokkerend voor import |
| C02 | Alle rekeningen met saldo ≠ 0 gekoppeld aan een rubriek | Blokkerend voor definitief rapport |
| C03 | Beginbalans huidig jaar = gecorrigeerde eindbalans vorig dossier (per rekening of rubriek) | Waarschuwing; blokkerend tenzij toegelicht |
| C04 | Resultaat volgens W&V = resultaat in eigen vermogen (onverdeeld resultaat / kapitaalsvergelijking) | Blokkerend |
| C05 | Balans: totaal activa = totaal passiva (na rubricering en omslag) | Blokkerend |
| C06 | Specificatie per rubriek sluit aan op het rubrieksaldo (huidig en vorig) | Blokkerend als "specificatie verplicht" |
| C07 | Verloopoverzicht sluit aan op het balanssaldo | Blokkerend |
| C08 | Correctieboekingen in evenwicht | Blokkerend bij opslaan |
| C09 | Alle verplichte onderdelen (`*`) gereed en goedgekeurd vóór uitbrengen | Blokkerend |
| C10 | Geen open reviewpunten bij goedkeuren of uitbrengen | Blokkerend |
| C11 | Materiële post met groot verschil (huidig − vorig) zonder toelichting | Waarschuwing |
| C12 | Import ouder dan X dagen of niet tot einde boekjaar | Waarschuwing |
| C13 | Dossier niet gesloten binnen de termijn na uitbrengen | Waarschuwing |
| C14 | Afrondingsverschillen in rapport groter dan € 1 per totaal | Waarschuwing (automatisch verdelen) |
| C15 | Grootte-indeling (micro/klein/middel) klopt niet met de BW2-criteria op basis van de cijfers | Waarschuwing |
