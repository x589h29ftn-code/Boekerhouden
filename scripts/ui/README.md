# Browsercontrole

Een snelle controle van het administratiepakket in de browser (Playwright met Chromium), met alleen verzonnen
testdata uit `tests/Fictief/administratie`. Handig om na een wijziging aan de schermen te zien of alles werkt en om
schermafbeeldingen van voor en na te maken.

```bash
dotnet build src/Boekerhouden.Administratie
scripts/ui/herstart.sh                                  # lege database, app op http://localhost:5090
node scripts/ui/schermafbeeldingen.js /tmp/schermen voor   # PNG's per scherm
```

`herstart.sh` moet vóór elke run: het script maakt de eerste gebruiker aan, en dat kan alleen op een lege database.
In de cloudsessie is Playwright met Chromium al geïnstalleerd (`require('playwright')` werkt overal). Zie
`docs/overdracht.md` voor het PDF-probleem met Chromium en voor schermafbeeldingen met een Windows-lettertype.
