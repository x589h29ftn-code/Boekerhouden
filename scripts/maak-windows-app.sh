#!/usr/bin/env bash
# Maakt de Windows-app van het administratiepakket: één map (gezipt) met een eigen venster, zonder webserver en zonder
# databaseserver, en zonder .NET-installatie nodig. Draait op Linux (ook in CI) en op Windows met Git Bash.
#   scripts/maak-windows-app.sh [uitvoermap]
set -euo pipefail

cd "$(dirname "$0")/.."
UIT="${1:-publiceer}"
APP="$UIT/Boekerhouden Administratie"
ZIP="$UIT/Boekerhouden-Administratie-win-x64.zip"

rm -rf "$APP" "$ZIP"
mkdir -p "$UIT"

echo "Windows-app publiceren (win-x64, zelfstandig)…"
dotnet publish src/Boekerhouden.Administratie.Windows -c Release -r win-x64 --self-contained true -o "$APP" -p:DebugType=none

# Wat van de webapp meekomt maar hier niet gebruikt wordt.
rm -f "$APP/Boekerhouden.Administratie.exe" "$APP/Boekerhouden.Administratie.runtimeconfig.json" "$APP"/appsettings*.json "$APP/web.config"
test -f "$APP/Boekerhouden.Administratie.Windows.exe"
cp scripts/windows/LEESMIJ.txt "$APP/"

echo "Zip maken…"
(cd "$UIT" && zip -qr "$(basename "$ZIP")" "$(basename "$APP")")
echo "Klaar: $ZIP ($(du -h "$ZIP" | cut -f1))"
