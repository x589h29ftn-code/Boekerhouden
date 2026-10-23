# 5. Functioneel ontwerp

> Dit hoofdstuk vertaalt hoe Visionplanner werkt ([hoofdstuk 1](01-hoe-werkt-visionplanner-compilation.md)),
> de beroepsregels ([hoofdstuk 2](02-vaktechnisch-kader.md)) en de import- en rubriceringslogica
> ([hoofdstukken 3](03-gegevensimport-xaf-excel-rgs.md) en [4](04-rubricering-ontwerp.md)) naar modules
> en schermen voor onze eigen applicatie. Per module staat of die in de **MVP** zit of later volgt.

## 5.1 Modules in één oogopslag

| # | Module | Doel | MVP? |
|---|---|---|---|
| M1 | Klantenmonitor | Overzicht van alle klanten met status, signalen en open taken | ✅ |
| M2 | Klantbeheer | Klantgegevens, rechtsvorm, grootte, functionarissen, Wwft-profiel | ✅ |
| M3 | Dossierbeheer | Dossier aanmaken per boekjaar en opdracht, sjabloon kiezen, vorig jaar doorrollen | ✅ |
| M4 | Import | XAF-auditfile en Excel/CSV-proefbalans inlezen, herimport, importhistorie | ✅ |
| M5 | Rubriceren (koppelen) | Grootboekrekeningen aan rubrieken koppelen via sjabloon, RGS of handmatig, met voorstellen | ✅ |
| M6 | Proefbalans en correcties | Gecorrigeerde proefbalans, memoriaal- en herrubriceringsboekingen | ✅ |
| M7 | Werkprogramma en dossieronderdelen | Fasen, onderdelen en blokken invullen, aftekenen, goedkeuren | ✅ |
| M8 | Cijferbeoordeling | Huidig, vorig, planning en delta per post met toelichting | ✅ |
| M9 | Specificaties en verloopoverzichten | Specificeren van posten, verloop vaste activa en eigen vermogen | ✅ |
| M10 | Rapportgenerator | Financieel rapport samenstellen uit sjabloon, standaardteksten en cijfers, met preview, PDF en Word | ✅ (PDF), Word later |
| M11 | Review en afronding | Reviewpunten, goedkeuring, rapport uitbrengen, dossier sluiten en archiveren | ✅ |
| M12 | Beheer (kantoor) | Rubriekenschema's, RGS-tabel, koppelsjablonen, werkprogramma's, standaardteksten, rapportsjablonen, gebruikers | ✅ (basis) |
| M13 | Kengetallen | Liquiditeit, solvabiliteit, rentabiliteit, marges | Fase 2 |
| M14 | Fiscale positie | Fiscale winstberekening, VPB-berekening, fiscale correcties | Fase 2 |
| M15 | Taken en planning | Taken per klant/dossier, deadlines, urenregistratie | Fase 2 |
| — | SBR/XBRL-deponering, API-koppelingen boekhoudpakketten | | ❌ Buiten scope |

## 5.2 Schermen (afgeleid van de schermafbeeldingen)

### S1 – Klantenmonitor (screenshot 1)
Een tabel met alle klanten:

| Kolom | Betekenis in onze app |
|---|---|
| Klant | Naam + klantnummer |
| Alerts | Aantal signalen. Voorbeelden: ongekoppelde rekeningen met saldo, proefbalans niet in evenwicht, beginbalans sluit niet aan op vorig jaar, specificatie wijkt af van saldo, open reviewpunten, dossier te lang open |
| Geïmporteerd | Datum laatste import. **Rood** als die ouder is dan de drempel (bijv. 90 dagen) of ouder dan de einddatum van het boekjaar |
| Bekeken | Datum waarop de klant voor het laatst is geopend door een gebruiker. Rood als oud |
| Gekoppeld | % van de grootboekrekeningen *met saldo* dat aan een rubriek is gekoppeld. Doel: 100% |
| Abonnement | Bij Visionplanner de actieve modules (dossier en dashboard). In onze app vervangen we dit door **Lopend dossier: status** |
| Open taken | Aantal open taken en reviewpunten |

Met zoeken, filteren (medewerker, status, rechtsvorm), sorteren, een knop "Klant toevoegen" en een selectievakje voor bulkacties.

### S2 – Dossierlijst per klant (screenshot 2)
Kolommen Dossier, Jaar, Aard opdracht, Status, Laatst gewijzigd en Acties (verwijderen alleen als het dossier niet is gesloten).
Knop **+ Dossier** start een wizard:
1. Boekjaar en periode (verkort of verlengd boekjaar mogelijk).
2. Aard opdracht (samenstellen, aangifte, overig) → bepaalt welk werkprogrammasjabloon en rapportsjabloon gekozen worden.
3. Sjabloonversies (de standaard is de nieuwste actieve versie).
4. **Doorrollen vanuit vorig dossier**: vergelijkende cijfers, specificaties (omschrijvingen en vorig-jaarbedragen), verloopoverzichten (eindstand wordt beginstand), aangepaste rapportteksten, terugkerende correctieboekingen en permanente dossierstukken (statuten, opdrachtbevestiging).

### S3 – Dossierwerkruimte (screenshots 3 en 4)
Drie kolommen:

- **Links: dossierboom** met fasen en onderdelen, een statusicoon per onderdeel (open, gereed, goedgekeurd) en een `*` bij verplichte onderdelen. Bovenin de klant, het dossier, de dossierstatus ("DOSSIER GESLOTEN") en een dossierwisselaar.
- **Midden: inhoud van het onderdeel**, met de tabs **Dossier** en **Rapport**:
  - De kop toont de titel en de goedkeuringsregel ("Goedgekeurd door … op …").
  - Een werkbalk voor opmaak van vrije tekst (vet, cursief, lijsten, tabel, link, speciale tekens, standaardtekst invoegen ★), ongedaan maken en opnieuw.
  - Blokken uit het sjabloon:
    - **Toelichting/Vraag** (bijv. "Fundamentele beginselen", "Integriteit van de klant", "Wwft", "Opdrachtaanvaarding"): standaardantwoord als grijze tekst (te accepteren of te overschrijven), vrije notitie en een bijlage.
    - **Aanpak en cijferbeoordeling**: de tabel Post/Grootboekrekening × Planning, Huidig, Vorig, Delta. Uitklapbaar per post naar de grootboekrekeningen, met een notitieveld per post ("Zijn de afschrijvingen beoordeeld en juist geboekt?").
    - **Werkprogramma**: stappen (bijv. "Verloopoverzicht") met instructietekst, antwoordveld, initialen, datum en een afvinkvak.
  - **Tab Rapport**: laat zien hoe het onderdeel in het rapport komt (bijv. "Specificaties van balans → Immateriële vaste activa"). Hier bewerk je de specificatieregels (knop *Specificatie post*), de omschrijvingen en de schakelaar **Toon in rapport**.
- **Rechts: gereedschapsbalk** met panelen die over het onderdeel heen schuiven. Uit de iconen afgeleid (niet bevestigd):
  - ⚖️ proefbalans en rubrieken (koppelen)
  - 📖 standaardteksten of handboek (NV COS / werkprogramma-uitleg)
  - 🏛 jaarrekening/rapport of fiscaal
  - 🔧 correctieboekingen en hulpmiddelen
  - 📎 bijlagen
  - 💬 opmerkingen/reviewpunten
  - ⇄ koppelingen
  - ⚙️ dossierinstellingen

### S4 – Importwizard
1. Bestand kiezen (XAF, XLSX, CSV) en type detecteren.
2. Bij **XAF**: de header tonen (bedrijf, boekjaar, pakket, aanmaakdatum) en controleren of het boekjaar en de klant kloppen (KvK/naam).
   Bij **Excel/CSV**: kolommen toewijzen (rekeningnummer, omschrijving, debet/credit of saldo, B/W, RGS-code), met opgeslagen importprofielen per pakket.
3. Controles: proefbalans in evenwicht, aantal rekeningen, beginbalans gelijk aan eindbalans vorig jaar, resultaat.
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
Klant aanmaken ─► Dossier 2025 aanmaken (sjabloon "Samenstellen BV klein", doorrollen uit 2024)
   │
   ├─ Profiel: klantgegevens, opdracht, rapportinstellingen controleren
   ├─ Voorbereiding: acceptatie (fundamentele beginselen, integriteit, Wwft, opdrachtbevestiging) ✔
   │                 planning (bijzonderheden, verwachtingen per post, deadlines)
   ├─ Import XAF 2025 ─► automatisch rubriceren ─► restant handmatig ─► 100% gekoppeld
   ├─ Uitvoering per post: cijferbeoordeling, werkprogramma-stappen, specificaties, verloopoverzichten,
   │                       correctieboekingen (bijv. afschrijvingen, overlopende posten, VPB)
   ├─ Rapport: teksten nalopen, preview, concept-PDF naar klant
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
| C11 | Delta (huidig − vorig/planning) boven drempel zonder toelichting | Waarschuwing |
| C12 | Import ouder dan X dagen of niet tot einde boekjaar | Waarschuwing |
| C13 | Dossier niet gesloten binnen de termijn na uitbrengen | Waarschuwing |
| C14 | Afrondingsverschillen in rapport groter dan € 1 per totaal | Waarschuwing (automatisch verdelen) |
| C15 | Grootte-indeling (micro/klein/middel) klopt niet met de BW2-criteria op basis van de cijfers | Waarschuwing |
