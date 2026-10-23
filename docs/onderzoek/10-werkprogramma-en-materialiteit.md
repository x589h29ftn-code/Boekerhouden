# 10. Werkprogramma-engine en materialiteit

> **Besluit (23-10-2026):**
> - Het kantoor maakt een **eigen werkprogramma** in een sjabloon.
> - Wordt een post als **materieel** aangevinkt, dan verschijnt het werkprogramma voor die post in het uitvoeringsdeel (samenstellen).
> - Gebruikers kunnen per dossier zelf **stappen toevoegen en verwijderen**.

## 10.1 Begrippen

| Begrip | Betekenis |
|---|---|
| **Werkprogrammasjabloon** | Het kantoorwerkprogramma per opdrachttype, bijv. *Samenstellen BV* en *Winstaangifte EZ/VOF*. Het heeft versies en wordt beheerd door de vaktechnisch beheerder |
| **Post** | Een onderdeel in de uitvoeringsboom, gekoppeld aan één of meer rubrieken. Bijv. *Immateriële vaste activa*, *Vorderingen*, *Liquide middelen*, *Netto-omzet*. Dit zijn de groene regels onder *Uitvoering* in de schermafbeelding |
| **Postprogramma** | De set standaardstappen die bij een post hoort, in het sjabloon |
| **Stap** | Eén werkprogramma-regel: instructie, antwoordveld, uitgevoerd door/op, eventueel bijlage, status |
| **Materialiteit** | Het bedrag waarboven een afwijking het beeld van de jaarrekening kan beïnvloeden. Wordt per dossier in de planning vastgesteld |
| **Materiële post** | Een post die in de planning als *materieel* (belangrijke post) is aangevinkt. **Alleen voor deze posten wordt het volledige postprogramma geactiveerd** |

## 10.2 Werking in het dossier

```
PLANNING                                         UITVOERING
┌───────────────────────────────────────────┐    ┌──────────────────────────────────────────┐
│ Materialiteit: € 25.000 (berekening ▾)    │    │ Balans activa                            │
│                                           │    │  ├ Vorderingen              ● materieel  │
│ Post               Huidig   Vorig  Mat.   │    │  │   Aanpak en cijferbeoordeling          │
│ IVA                28.000  35.000  [ ]    │──► │  │   Werkprogramma (6 stappen)            │
│ Vorderingen       380.023 415.957  [x] ⚑  │    │  │     + Stap toevoegen                   │
│ Liquide middelen  188.328 351.907  [x] ⚑  │    │  ├ Liquide middelen         ● materieel  │
│ Voorraden          45.000  45.000  [ ]    │    │  │   …                                    │
│ …                                         │    │  └ Voorraden                ○ niet mat.  │
│ ⚑ = voorstel van systeem (saldo > mat.)   │    │      Aanpak en cijferbeoordeling          │
└───────────────────────────────────────────┘    └──────────────────────────────────────────┘
```

1. **Materialiteit vaststellen** (planning).
   - Het systeem rekent een voorstel uit volgens de kantoormethode, bijv. een percentage van omzet, balanstotaal of resultaat. Die methode is in te stellen (zie de vragenlijst).
   - De gebruiker legt het definitieve bedrag vast, met een toelichting.
2. **Posten classificeren** (planning).
   - Een tabel toont alle posten met huidig saldo, vorig saldo, delta en een vinkje **Materieel**.
   - Het systeem zet een **voorstelvlag** ⚑ bij posten waarvan het saldo of de mutatie boven de materialiteit (of een ingestelde drempel) ligt. **De gebruiker beslist altijd zelf**, en kan per post een korte motivering geven ("waarom wel/niet").
3. **Vinkje aan** → het postprogramma uit het sjabloon wordt als **kopie** in het dossier gezet, onder die post in *Uitvoering*.
   - De kopie is een *momentopname*. Latere sjabloonwijzigingen veranderen een lopend dossier niet, tenzij de gebruiker kiest voor "bijwerken vanuit sjabloon".
4. **Vinkje uit** →
   - zijn er nog geen stappen ingevuld, dan verdwijnt het programma;
   - zijn er wel stappen ingevuld, dan vraagt het systeem: *"Ingevulde stappen bewaren als 'niet van toepassing'?"*. Er gaat nooit stilletjes dossierdocumentatie verloren.
5. **Niet-materiële posten** krijgen alleen het blok *Aanpak en cijferbeoordeling* (planning, huidig, vorig, delta, toelichting). Daarnaast eventueel een **basisprogramma** dat altijd geldt (bijv. "Liquide middelen: banksaldi aansluiten op bankafschriften"). Of dat zo moet, staat in de vragenlijst.

## 10.3 Stappen toevoegen en verwijderen

| Actie | Gedrag | Vastlegging |
|---|---|---|
| **Stap toevoegen** | Vrije stap onder een post: titel, instructie, eventueel verplicht | Gemarkeerd als *eigen stap*; wie en wanneer |
| **Stap verwijderen** (standaardstap) | *Soft delete*: de stap verdwijnt uit de weergave. Er is een filter "toon verwijderde stappen" voor reviewer en accountant | Reden verplicht (instelbaar), wie en wanneer. Telt mee in het review-overzicht ("3 standaardstappen verwijderd") |
| **Stap verwijderen** (eigen stap, nog niet ingevuld) | Echt verwijderen | Alleen in de audit log |
| **Volgorde wijzigen** | Slepen | – |
| **Stap voorstellen voor sjabloon** | Een eigen stap kan worden aangeboden aan de vaktechnisch beheerder | Beheerder neemt hem op in een nieuwe sjabloonversie |

**Rechten** (voorstel, bevestigen in de vragenlijst):
- iedereen mag stappen toevoegen;
- **standaardstappen verwijderen mag alleen een reviewer of accountant**, of een medewerker met verplichte reden die de reviewer later ziet;
- **verplichte stappen** (gemarkeerd in het sjabloon) kunnen niet verwijderd worden, alleen op "n.v.t." gezet met een reden.

## 10.4 Het sjabloon (beheer)

```
Werkprogrammasjabloon "Samenstellen BV" v2027.1
├─ Profiel ─ Cliënt · Opdracht · Rapport
├─ Voorbereiding
│  ├─ Acceptatie: Scope opdracht* · Akkoord acceptatie*   (vragen + standaardantwoorden)
│  └─ Planning: Materialiteit* · Classificatie posten* · Bijzonderheden
├─ Uitvoering
│  ├─ Post "Immateriële vaste activa"   rubrieken: IVA*
│  │    altijd:      Cijferbeoordeling
│  │    materieel:   1 Verloopoverzicht opvragen/opstellen …  2 Investeringen aansluiten op facturen …
│  │                 3 Afschrijvingen narekenen …  4 Bijzondere waardevermindering beoordelen …
│  ├─ Post "Vorderingen"  …
│  └─ …
├─ Rapport ─ Financiële rapportage
└─ Afronding ─ Evaluatie uitvoering · Bevestiging klant* · Uitbrengen rapport* · Afsluiting*
```

Per stap legt het sjabloon vast:
- titel en instructie (rich text);
- type antwoord: tekst, ja/nee/n.v.t., bedrag of bijlage verplicht;
- standaardantwoord (de grijze tekst uit de schermafbeelding, "Er is voldaan aan …");
- *verplicht* ja/nee;
- **activering**: *altijd* of *alleen bij materieel*;
- eventueel een voorwaarde (rechtsvorm, grootte, saldo ≠ 0);
- een verwijzing naar regelgeving.

Posten worden met een **rubriekselectie** aan het rubriekenschema gekoppeld. Zo weet het systeem welk saldo bij de post hoort en welke cijfertabel getoond wordt.

## 10.5 Uitbreiding datamodel

Aanvulling op [hoofdstuk 6](06-datamodel.md):

- **SjabloonPost**: Id, SjabloonId, OuderOnderdeelId, Titel, Rubrieken (selectie), Volgorde.
- **SjabloonStap**: Id, SjabloonPostId/OnderdeelId, Titel, Instructie, AntwoordType, Standaardantwoord, Verplicht, **Activering** (`Altijd`/`AlleenMaterieel`), Conditie, Volgorde.
- **DossierMaterialiteit**: DossierId, Methode, Basisbedrag, Percentage, VoorgesteldBedrag, **VastgesteldBedrag**, Toelichting, VastgesteldDoor/Op.
- **DossierPost**: DossierId, SjabloonPostId, **IsMaterieel**, VoorstelMaterieel (berekend), Motivering, GewijzigdDoor/Op.
- **DossierStap**: Id, DossierPostId/OnderdeelId, BronSjabloonStapId (*null* = eigen stap), Titel, Instructie, Verplicht, Volgorde, Status (`Open`/`Gereed`/`NVT`), Antwoord, UitgevoerdDoor/Op, **Verwijderd**, VerwijderdDoor/Op, **VerwijderReden**.

## 10.6 Controles

- **Afronden** van *Planning* kan pas als de materialiteit is vastgesteld en elke post geclassificeerd is (expliciet materieel of niet).
- **Goedkeuren** van een post kan pas als alle niet-verwijderde stappen *Gereed* of *NVT* zijn, en er geen open reviewpunten zijn.
- **Review-overzicht** per dossier toont:
  - posten waarvan de classificatie afwijkt van het systeemvoorstel;
  - verwijderde standaardstappen met hun reden;
  - eigen toegevoegde stappen.
