# 10. Werkprogramma en materiële posten

> **Besluiten (23-10-2026):**
> 1. Het kantoor maakt een **eigen werkprogramma** in een sjabloon.
> 2. **De gebruiker kiest welke posten materieel zijn.** Het systeem rekent niets uit en doet geen voorstel.
> 3. Materieel aanvinken gebeurt **per hoofdpost**. Binnen de hoofdpost kun je **doorklikken naar de subposts** (en verder naar de grootboekrekeningen).
> 4. Een **materiële post** krijgt zijn werkprogramma in het uitvoeringsdeel. Stappen zijn per dossier **toe te voegen en te verwijderen**.
> 5. Een **niet-materiële post** heeft **geen vragen en geen werk**. Hij is alleen zichtbaar in het **rapport**, waar je de presentatie en opmaak kunt aanpassen.

## 10.1 Begrippen

| Begrip | Betekenis |
|---|---|
| **Werkprogrammasjabloon** | Het kantoorwerkprogramma per opdrachttype: *Samenstellen jaarrekening* (BV) en *Winstaangifte opstellen* (eenmanszaak; VOF later). Het heeft versies en wordt beheerd door de vaktechnisch beheerder |
| **Hoofdpost** | Een post op het niveau waarop materieel wordt gekozen, bijv. *Materiële vaste activa*, *Vorderingen*, *Liquide middelen*, *Kortlopende schulden*, *Netto-omzet*, *Personeelskosten*. Gekoppeld aan een tak van het rubriekenschema |
| **Subpost** | Een onderdeel van een hoofdpost, bijv. bij *Vorderingen*: *Handelsdebiteuren*, *Groepsmaatschappijen*, *Overlopende activa*. Dit volgt de rubriekenboom |
| **Postprogramma** | De standaardstappen voor een hoofdpost in het sjabloon. Ze worden alleen geactiveerd als de post materieel is |
| **Stap** | Eén werkprogramma-regel: instructie, antwoord, uitgevoerd door/op, eventueel bijlage, status |

## 10.2 Werking in het dossier

```
PLANNING – keuze materiële posten                UITVOERING (alleen materiële posten)
┌────────────────────────────────────────────┐   ┌──────────────────────────────────────────────┐
│ Hoofdpost            Huidig    Vorig  Mat. │   │ ● Vorderingen                                 │
│ ▸ Mat. vaste activa   85.000   92.000 [x]  │   │   ▾ Cijfers (doorklikken)                     │
│ ▾ Vorderingen        160.000  140.000 [x]  │──►│       Handelsdebiteuren      120.000   95.000 │
│     Handelsdebiteuren 120.000  95.000      │   │         1300 Debiteuren      135.000  105.000 │
│     Groepsmaatsch.    10.000   25.000      │   │         1310 Voorz. dubieus  -15.000  -10.000 │
│     Overlopende act.  30.000   20.000      │   │       Groepsmaatschappijen    10.000   25.000 │
│ ▸ Liquide middelen    64.000   81.000 [ ]  │   │       Overlopende activa      30.000   20.000 │
│ ▸ Kortl. schulden    110.000   98.000 [x]  │   │   Werkprogramma (5 stappen)  + Stap toevoegen │
│ …                                          │   │ ● Kortlopende schulden                        │
│ Toelichting keuze (optioneel): …           │   │   …                                           │
└────────────────────────────────────────────┘   └──────────────────────────────────────────────┘
                                                  RAPPORT (alle posten)
                                                  └ Presentatie en opmaak per post aanpassen
```
*(Fictieve bedragen.)*

1. **Keuze materiële posten** (planning).
   - Een tabel toont alle hoofdposten met huidig en vorig saldo. Met ▸/▾ klap je een hoofdpost open naar de subposts, en een subpost naar de grootboekrekeningen.
   - De gebruiker zet per hoofdpost het vinkje **Materieel**. Er is geen berekening en geen voorstel.
   - Optioneel een vrij tekstveld *Toelichting op de keuze*, voor de dossierdocumentatie.
2. **Vinkje aan** → het postprogramma uit het sjabloon wordt als **kopie** onder die hoofdpost in *Uitvoering* gezet.
   - Latere sjabloonwijzigingen veranderen een lopend dossier niet.
   - De post krijgt daar ook de cijfers met **doorklikken** (hoofdpost → subposts → grootboekrekeningen, huidig, vorig en verschil) en een notitieveld per post.
3. **Vinkje uit** →
   - zijn er nog geen stappen ingevuld, dan verdwijnt het programma;
   - zijn er wel stappen ingevuld, dan vraagt het systeem of die bewaard moeten blijven (als "niet van toepassing") of verwijderd. Ingevulde documentatie verdwijnt nooit ongemerkt.
4. **Niet-materiële posten** verschijnen **niet** in *Uitvoering*: geen vragen, geen stappen, geen cijferbeoordeling. Ze zijn alleen zichtbaar in **Rapport**, waar de presentatie kan worden aangepast ([§10.4](#104-rapport-presentatie-en-opmaak-per-post)).
5. **De systeemcontroles gelden altijd voor alle posten**, materieel of niet: alles gekoppeld, balans sluit, specificatie sluit aan op saldo, verloop sluit aan. Dat zijn automatische aansluitingen, geen werk voor de gebruiker.

## 10.3 Stappen toevoegen en verwijderen (materiële posten)

| Actie | Gedrag | Vastlegging |
|---|---|---|
| **Stap toevoegen** | Vrije stap onder de hoofdpost, eventueel gekoppeld aan een subpost: titel, instructie | Gemarkeerd als *eigen stap*; wie en wanneer |
| **Standaardstap verwijderen** | De stap verdwijnt uit de weergave, maar blijft bewaard en is zichtbaar via "toon verwijderde stappen" | Wie, wanneer en (instelbaar verplicht) een reden. Zichtbaar in het review-overzicht |
| **Eigen stap verwijderen** (nog niet ingevuld) | Echt verwijderen | Alleen in de audit log |
| **Volgorde wijzigen** | Slepen | – |
| **Stap voorstellen voor sjabloon** | Een eigen stap aanbieden aan de vaktechnisch beheerder | Opname in een nieuwe sjabloonversie |

**Rechten** (zie vraag C5 in de [vragenlijst](11-vragenlijst.md)):
- iedereen mag stappen toevoegen;
- standaardstappen verwijderen volgens het rechtenbesluit;
- stappen die in het sjabloon **verplicht** zijn, kunnen niet verwijderd worden, alleen op "n.v.t." met een reden.

## 10.4 Rapport: presentatie en opmaak per post

Voor **elke** post, materieel of niet, is er in *Rapport* een presentatie-editor. Dat is de tab *Rapport* uit de schermafbeelding.

| Wat | Mogelijkheden |
|---|---|
| **Specificatieregels** | Standaard de grootboekrekeningen van de post, zoals in de voorbeeldrapporten ([§9.1](09-analyse-voorbeeldrapporten.md#91-opbouw-voorbeeld-a-bv-klein)). Per regel: omschrijving aanpassen, regels **samenvoegen**, **verbergen** of **volgorde** wijzigen, subkoppen en subtotalen toevoegen, of handmatige specificatieregels (die moeten aansluiten op het saldo) |
| **Toon in rapport** | Specificatie aan of uit in het rapport. Hij blijft wel in het dossier |
| **Verloopoverzicht** | Aan of uit (bijv. MVA, rekening-courant, voorzieningen, eigen vermogen); kolommen en regels kiezen |
| **Toelichtende tekst** | Tekstblok boven of onder de specificatie (standaardtekst met variabelen, of vrije tekst). Doorrollen naar volgend jaar als voorstel |
| **Opmaak** | Kop en omschrijving van de post, nieuwe pagina ervoor ja/nee, tabellen bij elkaar houden. Huisstijl (lettertype, kleuren) komt centraal uit het rapportsjabloon, zodat rapporten uniform blijven |

Aanpassingen worden **per klant onthouden** en rollen door naar het volgende jaar. Zo hoef je
samenvoegingen en omschrijvingen maar één keer in te stellen.

*Gebouwd (plakje 6):* in het dossier onder *Rapport* (`/dossiers/{id}/rapport`) werk je **in het rapport
zelf**, als laatste controle vóór de PDF. Links staat de inhoud (klik = naar die plek), rechts het rapport
zoals het wordt afgedrukt, met een knop **PDF**. Wat je in de viewer ziet, staat precies zo in de PDF; de
bedieningselementen (driehoekjes, knoppen) worden niet afgedrukt.

| In de viewer | Hoe | Geldt voor |
|---|---|---|
| **In- en uitklappen** | Driehoekje voor een post. In de balans en W&V klapt een post uit naar zijn subposten (bijv. *Overige bedrijfskosten* → huisvestingskosten, autokosten, verkoopkosten); in een specificatie klapt een groep in tot één regel. Standaard: balans en specificaties uit-, W&V ingeklapt | De klant, vanaf dit jaar |
| **Namen aanpassen** | Klik op de naam van een post, eigen post of rekening; Enter bewaart, Escape annuleert, leeg = de standaardnaam. Een postnaam geldt in het hele rapport (en bij het rubriceren) | De klant, vanaf dit jaar |
| **Toelichting** | Onder elke specificatie *toelichting toevoegen* (zichtbaar bij aanwijzen), of op de tekst klikken om die aan te passen. Een toelichting rolt door naar volgende jaren; leegmaken haalt hem vanaf dit jaar weg | Dit dossier en volgende jaren |
| **Uitsplitsen** | Bij een rekening *uitsplitsen*, bijv. *Nog te betalen kosten* in accountantskosten, energie en overige. Per regel een omschrijving, het bedrag en het bedrag vorig jaar, met een **controletelling** op het saldo van beide jaren. Sluit het niet aan, dan staat het verschil in het rapport als *Nog niet gespecificeerd* (de totalen blijven kloppen) en volgt een waarschuwing. Een uitsplitsing van vorig jaar is het voorstel (met die bedragen als vorig jaar) | Dit dossier |

Achter de schakelaar *presentatie aanpassen* staat per hoofdpost met saldo of de **specificatie** in het
rapport komt, en per rekening of hij **in Overige** meetelt (de regel *Overige* van zijn groep, zodat de
specificatie blijft aansluiten). Rekeningen onder een eigen post staan als één regel met de naam van de
eigen post. Alles staat in het wijzigingslog. Nog niet gebouwd: regels samenvoegen of in een andere
volgorde, verloopoverzichten en opmaak per post (nieuwe pagina ervoor).

**Per boekjaar, nooit met terugwerkende kracht.** Elke aanpassing in het rapport geldt vanaf het boekjaar van
het dossier waarin je hem maakt: namen van posten en rekeningen (ook kantoorbreed), rekeningen in *Overige*,
specificatie aan of uit, in- en uitklappen, toelichtingen en grondslagen (open of dicht, eigen tekst). Eerdere
jaren blijven zoals ze waren. Volgende jaren nemen de aanpassing over, tenzij ze zelf iets anders hebben; zo gaat
het rapport van vorig jaar bij de jaarwisseling vanzelf mee. Technisch: per instelling versies met
`VanafBoekjaar` (in een jaar geldt de versie met het hoogste jaar tot en met dat jaar); een versie die gelijk is
aan wat ervoor gold, vervalt vanzelf. De grondslagenbibliotheek van het kantoor werkt hetzelfde: in Beheer ›
Grondslagen kies je het boekjaar, en een aanpassing geldt vanaf dat jaar (zie [§9.3](09-analyse-voorbeeldrapporten.md#93-catalogus-van-rapportbouwstenen)).
De rubricering (koppelingen en eigen posten) geldt per klant voor alle jaren: de vergelijkende cijfers volgen
zo de indeling van dit jaar. Een uitgebracht rapport wordt bevroren bij het afronden van het dossier (plakje 8).

## 10.5 Het sjabloon (beheer)

```
Werkprogrammasjabloon "Samenstellen jaarrekening" (BV) v2027.1
├─ Profiel ─ Cliënt · Opdracht · Rapport
├─ Voorbereiding
│  ├─ Acceptatie: Scope opdracht* · Akkoord acceptatie*      (vragen + standaardantwoorden)
│  └─ Planning: Keuze materiële posten* · Bijzonderheden
├─ Uitvoering  (alleen materiële hoofdposten worden getoond)
│  ├─ Hoofdpost "Materiële vaste activa"   rubrieken: MVA*
│  │    stappen: 1 Verloopoverzicht aansluiten op grootboek …  2 Investeringen aansluiten op facturen …
│  │             3 Afschrijvingen narekenen …  4 Desinvesteringen en boekresultaat …
│  ├─ Hoofdpost "Vorderingen"   rubrieken: Vorderingen*
│  │    stappen: 1 Debiteurenlijst aansluiten … (subpost Handelsdebiteuren)  2 Voorziening dubieuze debiteuren …
│  └─ …
├─ Rapport ─ Financiële rapportage (presentatie per post, alle posten)
└─ Afronding ─ Evaluatie uitvoering · Bevestiging klant* · Uitbrengen rapport* · Afsluiting*
```

Per stap legt het sjabloon vast:
- titel en instructie;
- type antwoord (tekst, ja/nee/n.v.t., bedrag, bijlage verplicht);
- standaardantwoord;
- *verplicht* ja/nee;
- optioneel de **subpost** waar de stap over gaat;
- eventueel een voorwaarde (rechtsvorm, grootte);
- een verwijzing naar regelgeving.

**Hoofdposten** worden met een **rubriekselectie** aan het rubriekenschema gekoppeld. De subposts volgen automatisch uit de rubriekenboom onder die selectie.

## 10.6 Uitbreiding datamodel

Aanvulling op [hoofdstuk 6](06-datamodel.md):

- **SjabloonPost**: Id, SjabloonId, Titel, Rubrieken (selectie, bepaalt de subposts), Volgorde.
- **SjabloonStap**: Id, SjabloonPostId/OnderdeelId, Titel, Instructie, AntwoordType, Standaardantwoord, Verplicht, SubpostRubriekId (optioneel), Conditie, Volgorde.
- **DossierPost**: DossierId, SjabloonPostId, **IsMaterieel**, GewijzigdDoor/Op.
- **DossierPlanning**: DossierId, ToelichtingKeuzeMaterielePosten (optioneel).
- **DossierStap**: Id, DossierPostId/OnderdeelId, BronSjabloonStapId (*null* = eigen stap), SubpostRubriekId, Titel, Instructie, Verplicht, Volgorde, Status (`Open`/`Gereed`/`NVT`), Antwoord, UitgevoerdDoor/Op, **Verwijderd**, VerwijderdDoor/Op, **VerwijderReden**.
- **PresentatieInstelling** (per klant, doorrollend): KlantId, RubriekId, Specificatie-instellingen (omschrijvingen, samenvoegingen, verborgen regels, volgorde, subkoppen), ToonInRapport, VerloopoverzichtAan, Tekstblokken, NieuwePagina.

## 10.7 Controles

- **Planning afronden** kan pas als voor elke hoofdpost met saldo bewust is gekozen: het vinkje staat aan, of de keuze "niet materieel" is bevestigd met een tweede klik op "keuze bevestigd". Zo wordt geen post per ongeluk overgeslagen.
- **Goedkeuren** van een materiële post kan pas als alle niet-verwijderde stappen *Gereed* of *NVT* zijn en er geen open reviewpunten zijn.
- **Review-overzicht** per dossier toont:
  - de gekozen materiële posten;
  - verwijderde standaardstappen met hun reden;
  - eigen toegevoegde stappen.

## 10.8 Gebouwd in plakje 5

**Werkprogramma in het dossier** (`/dossiers/{id}/werkprogramma`):
- Links de onderdelen per fase (voorbereiding, uitvoering, afronding) met de voortgang (*2/5*, *gereed*, *gereviewd*) en open reviewpunten; rechts het gekozen onderdeel.
- Bij de eerste keer openen krijgt het dossier een **kopie** van het kantoorsjabloon voor de soort opdracht; de versie staat bovenaan. Latere wijzigingen in het sjabloon veranderen het dossier niet.
- **Planning**: per RGS-hoofdpost (niveau 2) met saldo een vinkje *materieel*, met doorklikken naar de subposten. *Overige posten: niet materieel* bevestigt de rest in één keer. Een toelichting op de keuze is optioneel.
  - Vinkje aan: de post komt in de uitvoering, met de stappen uit het sjabloon (of zonder stappen als het sjabloon die post niet kent), de cijfers (subposten en rekeningen, huidig, vorig, verschil) en een notitie *cijferbeoordeling*.
  - Vinkje uit: zonder invulling verdwijnt de post; met invulling (antwoord, status, notitie, bijlage of reviewpunt) kies je tussen *bewaren als niet van toepassing* en *verwijderen*.
- **Stappen**: aftekenen met een vinkje (wie en wanneer), *niet van toepassing* met een verplichte reden, een antwoord, bijlagen. Eigen stappen toevoegen; verwijderen met reden (vraag C5: iedereen, reden verplicht), herstellen, volgorde ↑/↓. Verplichte stappen kunnen niet weg. Verwijderde stappen blijven zichtbaar via de schakelaar *verwijderde stappen*.
- **Controles bij aftekenen**: *Alle rekeningen gerubriceerd* kan pas als alles met saldo is gekoppeld; *Keuze materiële posten* pas als elke hoofdpost met saldo een keuze heeft (§10.7).
- **Review** (vraag C6): per stap *uitgevoerd* (aftekenen), per onderdeel *gereviewd* door een gebruiker met de rol reviewer of accountant, als alle stappen gereed of n.v.t. zijn en er geen open reviewpunten zijn. Daarna is het onderdeel vergrendeld tot de review wordt teruggezet. Een nieuw reviewpunt laat de review vervallen. Goedkeuring van het hele dossier door de accountant volgt in plakje 8.
- **Reviewpunten** per onderdeel, eventueel bij een stap; oplossen met een reactie, heropenen.
- **Bijlagen** bij een stap, tot 25 MB, opgeslagen in de database met SHA-256 (vraag E2). Downloaden alleen na inloggen, altijd als bestand.
- Alles staat in het wijzigingslog.

**Beheer › Werkprogramma's**: per soort opdracht de versie, onderdelen en stappen (titel, instructie, verplicht) aanpassen. In de uitvoering hoort elk onderdeel bij een RGS-hoofdpost. De planning kan niet weg.

**Meegeleverde inhoud** (versie 2026.1): een eerste werkprogramma voor *Samenstellen jaarrekening* (BV) en *Winstaangifte opstellen* (eenmanszaak), in eigen woorden opgesteld langs de fasen van Standaard 4410 ([§2.1](02-vaktechnisch-kader.md#21-standaard-4410-samenstellingsopdrachten)). Er is geen tekst uit gelicentieerde werkprogramma's overgenomen. Het is een startpunt: de vaktechnisch verantwoordelijke stelt de inhoud vast.

**Klantenoverzicht** (S1): per klant het laatste dossier met gekoppeld %, de voortgang van het werkprogramma (stappen klaar) en signalen: geen saldibalans, rekeningen met saldo niet gekoppeld, open reviewpunten, keuze materiële posten niet afgetekend. Met de schakelaar *alleen met signalen*. Dezelfde regel staat bovenaan het dossieroverzicht.

**Nog niet gebouwd**: antwoordtypes (ja/nee, bedrag), voorwaarden per stap (rechtsvorm, grootte), verwijzing naar regelgeving, *stap voorstellen voor het sjabloon* en het review-overzicht per dossier. De tab *Rapport* met de presentatie per post is gebouwd in plakje 6 ([§10.4](#104-rapport-presentatie-en-opmaak-per-post)).

