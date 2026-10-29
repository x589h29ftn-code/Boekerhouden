#!/usr/bin/env bash
# Maakt de Windows-app van het administratiepakket: een zip met de app (zelfstandig, zonder .NET-installatie),
# PostgreSQL voor Windows en bureaublad.json. Draait op Linux (ook in CI) en op Windows met Git Bash.
#   scripts/maak-windows-app.sh [uitvoermap]        PG_VERSIE=17.6-1 kiest een andere PostgreSQL-versie
set -euo pipefail

cd "$(dirname "$0")/.."
UIT="${1:-publiceer}"
PG_VERSIE="${PG_VERSIE:-17.6-1}"
APP="$UIT/Boekerhouden Administratie"
ZIP="$UIT/Boekerhouden-Administratie-win-x64.zip"

rm -rf "$APP" "$ZIP"
mkdir -p "$UIT"

echo "App publiceren (win-x64, zelfstandig)…"
dotnet publish src/Boekerhouden.Administratie -c Release -r win-x64 --self-contained true -o "$APP" -p:DebugType=none

echo "PostgreSQL $PG_VERSIE voor Windows ophalen…"
PG_ZIP="$UIT/postgresql-$PG_VERSIE-windows-x64-binaries.zip"
if [ ! -f "$PG_ZIP" ]; then
  curl -fL --retry 3 -o "$PG_ZIP" "https://get.enterprisedb.com/postgresql/postgresql-$PG_VERSIE-windows-x64-binaries.zip"
fi
# Alleen wat de database nodig heeft: geen pgAdmin, documentatie, headers of symbolen.
unzip -q "$PG_ZIP" 'pgsql/bin/*' 'pgsql/lib/*' 'pgsql/share/*' -x 'pgsql/lib/*.lib' 'pgsql/bin/stackbuilder*' -d "$APP"
test -f "$APP/pgsql/bin/initdb.exe"

cp scripts/windows/bureaublad.json scripts/windows/LEESMIJ.txt "$APP/"

echo "Zip maken…"
(cd "$UIT" && zip -qr "$(basename "$ZIP")" "$(basename "$APP")")
echo "Klaar: $ZIP ($(du -h "$ZIP" | cut -f1))"
