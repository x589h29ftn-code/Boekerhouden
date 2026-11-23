# Browsercontrole

Een snelle controle van het administratiepakket in de browser (Playwright met Chromium), met alleen verzonnen
testdata uit `tests/Fictief/administratie`. Handig om na een wijziging aan de schermen te zien of alles werkt en om
schermafbeeldingen van voor en na te maken.

```bash
dotnet build src/Boekerhouden.Administratie
scripts/ui/herstart.sh                                  # lege database, app op http://localhost:5090
node scripts/ui/schermafbeeldingen.js /tmp/schermen voor   # PNG's per scherm
scripts/ui/herstart.sh && node scripts/ui/schakelaars.js /tmp/schermen   # alle schakelaars heen en terug, meldt fouten
scripts/ui/herstart.sh && node scripts/ui/druk.js        # dubbelklikken, twee tabbladen, terugknop, trage verbinding
scripts/ui/herstart.sh && node scripts/ui/overgangen.js /tmp/schermen voor   # vensters, menu's, panelen zacht; paginawissel zonder beweging; laadschets en melding licht en donker
scripts/ui/herstart.sh && BREEDTE=1400 node scripts/ui/verwerkbreedte.js /tmp/schermen   # verwerkscherm op 1600/1920/2560 breed: nooit breder dan het venster
scripts/ui/herstart.sh && node scripts/ui/openen.js /tmp/afdruk.xaf   # bestanden openen vanuit Windows (het script van de Windows-app)
scripts/ui/herstart.sh && node scripts/ui/afdrukken.js /tmp/afdruk.xaf /tmp/afdruk   # elk overzicht afdrukken (licht en donker), meldt fouten
scripts/ui/herstart.sh && node scripts/ui/consistentie.js /tmp/afdruk.xaf /tmp/consistentie   # lay-out over 41 schermen op twee maten: scrollen, knoppen, tekens, Engels, labels
```

`herstart.sh` moet vóór elke run: het script maakt de eerste gebruiker aan, en dat kan alleen op een lege database.
In de cloudsessie is Playwright met Chromium al geïnstalleerd (`require('playwright')` werkt overal). Zie
`docs/overdracht.md` voor het PDF-probleem met Chromium en voor schermafbeeldingen met een Windows-lettertype.

De auditfile voor `afdrukken.js` en `consistentie.js` (een volle, verzonnen administratie) maak je met
`BOEKERHOUDEN_AFDRUKGEGEVENS=/tmp/afdruk.xaf dotnet test tests/Boekerhouden.Administratie.Tests --filter Afdrukgegevens`
(met `BOEKERHOUDEN_AFDRUKSCHAAL=15` erbij alle bedragen vijftien keer zo groot: ruim een ton per maand per kostenrekening).
Het script controleert per pagina de afdrukkop, paginanummers, geen menu of knoppen, niets buiten het papier, geen te
smalle kolommen, geen lege eerste pagina en zwart op wit; de PDF's komen in de map, om ook zelf naar te kijken.
