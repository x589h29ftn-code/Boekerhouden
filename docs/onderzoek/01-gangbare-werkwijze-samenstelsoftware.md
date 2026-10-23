# 1. Gangbare werkwijze van samenstelsoftware

> **Doel van dit hoofdstuk.** Dit is achtergrond. Het beschrijft in algemene termen hoe de
> samenstelsoftware op de Nederlandse markt doorgaans werkt. Het is gebaseerd op marktonderzoek
> (oktober 2026) en op schermvoorbeelden uit de praktijk. *Boekerhouden* is **een eigen ontwerp**.
> Het gaat uit van de werkwijze van het kantoor en de beroepsregels, en is geen kopie van een
> bestaand pakket. Leveranciers en hun documentatie noemen we hier bewust niet.

## 1.1 Opbouw in drie lagen

```
                ┌──────────────────────── Klantniveau ──────────────────────────────┐
 Boekhouding ─► │ Administratie: rekeningschema + saldi per periode per boekjaar    │
 (saldibalans,  │ Koppeling: grootboekrekening → rubriek (kantoorsjabloon + klant)  │
  auditfile)    │ Klantenoverzicht: signalen, laatste import, % gekoppeld, taken    │
                └───────────────┬───────────────────────────────────────────────────┘
                                │ cijfers huidig jaar en vorig jaar
                ┌───────────────▼──────── Dossierniveau ────────────────────────────┐
                │ Dossier per boekjaar en soort opdracht, op basis van een          │
                │ werkprogramma: Profiel → Voorbereiding (acceptatie, planning) →   │
                │ Uitvoering per post → Rapport → Afronding                         │
                │ Rapportsjabloon + standaardteksten → financieel rapport (PDF)     │
                └───────────────────────────────────────────────────────────────────┘
```

- De **import en de koppeling** van grootboekrekeningen aan rubrieken horen bij de **klant**. Daardoor rollen ze door over de jaren.
- Het **dossier** hoort bij één boekjaar en één opdracht.
- **Documenten** (bijlagen, gegenereerde rapporten) staan bij het dossier, vaak in een gekoppelde documentomgeving.

## 1.2 Werkstroom van een samenstellingsdossier

- **Profiel:**
  - cliëntgegevens;
  - opdracht, met de opdrachtverantwoordelijken en het boekjaar (ook verkort, verlengd of gebroken);
  - rapportinstellingen, zoals vergelijkende cijfers tonen of verbergen.
- **Soort opdracht en werkprogramma:**
  - een kantoor kiest per dossier de soort opdracht, bijvoorbeeld samenstellen of aangifte;
  - daarbij hoort een werkprogramma;
  - brancheorganisaties leveren gelicentieerde werkprogramma's die in pakketten geïntegreerd kunnen worden. Die zijn risicogericht, met stuurvragen.
- **Fasen:** acceptatie (inclusief Wwft) → planning (bedrijfsverkenning, initiële cijferbeoordeling, keuze van de belangrijke posten) → uitvoering per post → rapport → afronding (evaluatie, bevestiging klant, uitbrengen, afsluiting).
- **Per post** in de uitvoering:
  - een cijferbeoordeling (huidig, vorig, verschil, met doorklikken naar de grootboekrekeningen);
  - werkprogramma-stappen met aftekening;
  - een tab met de rapportweergave van die post (specificaties, *toon in rapport*).
- **Rollen en goedkeuring:**
  - medewerkers krijgen per dossier een rol;
  - onderdelen worden afgetekend en goedgekeurd ("Goedgekeurd door … op …");
  - verplichte onderdelen zijn gemarkeerd.
- **Afsluiten:**
  - dossiers sluiten automatisch, vaak **60 dagen** na de datum van de verklaring;
  - een gesloten dossier wordt meestal niet heropend; je kunt het wel kopiëren.

## 1.3 Import

| Bron | Gangbare werkwijze |
|---|---|
| **XML Auditfile Financieel (XAF)** | Upload of lokale connector. Bevat rekeningschema, saldi en boekingen |
| **Excel-sjabloon** | Een vast sjabloon met klantnaam, boekjaar, perioden, en blokken *balans* en *winst & verlies*. Balansregels zijn standen; W&V-regels zijn mutaties die worden opgeteld. Een rekening wordt alleen ingelezen als er een bedrag op staat |
| **API-koppelingen** | Met de grote boekhoudpakketten. Buiten onze scope |

- **Herimport:** eerder geïmporteerde boekjaren blijven bewaard, en koppelingen per rekening blijven staan.
- **Nieuwe rekeningen** komen ongekoppeld binnen. Daardoor daalt het "gekoppeld %".

## 1.4 Rubricering: grootboekrekeningen koppelen

1. **Structuur:** groep › subgroep › rubriek. Een rekening koppel je altijd aan het **laagste niveau** (de rubriek).
2. **Koppelscherm:**
   - links de rekeningen, rechts de rubriekenstructuur;
   - koppelen door te slepen of met een knop, meerdere tegelijk selecteren, ontkoppelen;
   - alleen rekeningen met boekingen worden getoond.
3. **Sjabloon met overrides:**
   - een koppeling in het kantoorsjabloon geldt voor alle klanten die aan dat sjabloon hangen;
   - een koppeling die je bij de klant aanpast, wordt daarna **niet meer overschreven** door sjabloonwijzigingen;
   - de omschrijving van een rubriek kun je per klant aanpassen.
4. **RGS:**
   - rekeningen met een (geldige) RGS-code worden automatisch gekoppeld;
   - de rest koppel je handmatig.
5. **AI-ondersteuning:** sommige pakketten kennen ontbrekende RGS-codes toe met AI. Dat wordt gepresenteerd als besparing van handwerk.
6. **Gekoppeld %:** per klant zichtbaar in het klantenoverzicht. Een klik toont de ongekoppelde rekeningen.

## 1.5 Rapport

- **Rapportsjablonen** per kantoor, met genummerde onderdelen.
- **Specificatie per post:**
  - de grootboekrekeningen van de post worden naar specificatieregels gesleept;
  - een specificatie kan in het dossier blijven zonder in het rapport te verschijnen.
- **Mutatie- of verloopoverzicht** op een specificatieregel:
  - begin- en eindstand komen uit de balans;
  - een **rood verschil** verschijnt als het overzicht niet aansluit.
- **Standaardteksten met variabelen**, zoals klantgegevens, datums en bedragen van posten.
- **Samenstellingsverklaring:** keuze uit modelteksten van de beroepsorganisaties, of een eigen tekst.
- **Huisstijl:** marges, lettertype, kleuren, achtergrond.
- **Uitvoer:** PDF. Digitaal accorderen en ondertekenen door de klant is een aparte functie.

## 1.6 Overige gangbare functies

- **Klantenoverzicht** met signalen of alerts, laatste import, % gekoppeld, dossierstatus en open taken. De signalen zijn per sjabloon instelbaar.
- **Checklists:**
  - groepen met vragen, als keuzevraag (ja/nee/n.v.t.) of open vraag;
  - zichtbaarheid met voorwaarden.
- **Afrondingsmemorandum:** een samenvatting van het dossier op één pagina voor de opdrachtverantwoordelijke.
- **Labels**, en het gelijktijdig werken van meerdere gebruikers met synchronisatie op de achtergrond.

## 1.7 Lessen voor ons ontwerp

| Gangbare functie die wij ook nodig hebben | Bij ons eenvoudiger of anders | Buiten scope |
|---|---|---|
| Klantenoverzicht met signalen en % gekoppeld | Lokale opslag op de kantoorserver | Deponeren (SBR), publicatiestukken |
| Koppeling op klantniveau via sjabloon, RGS en handmatig, met handmatige overrides | **Eigen importsjabloon**: boekjaar + grootboeknummer, omschrijving en bedrag, met controle op saldo nihil | API-koppelingen met boekhoudpakketten |
| Dossier per boekjaar en opdracht, met fasenboom, verplichte onderdelen en goedkeuring | **Alleen materiële posten krijgen werk** ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)) | Klantportaal, digitaal ondertekenen |
| Specificaties en verloopoverzichten, *toon in rapport* | AI-voorstellen eventueel later | Fiscale exports |
| Rapportsjablonen, standaardteksten met variabelen | PDF **en Word** | |
| Dossier alleen-lezen na sluiten | | |

> **Intellectueel eigendom.** Gelicentieerde werkprogramma's van brancheorganisaties en
> NBA-ledenmateriaal nemen we niet over zonder licentie. Teksten en vormgeving van bestaande
> pakketten kopiëren we niet. Boekerhouden krijgt een eigen werkprogramma, eigen teksten en
> een eigen vormgeving.
