#!/bin/bash
# Start het administratiepakket op een lege database (boekerhouden_administratie_ui) op http://localhost:5090, voor
# de browsercontrole. Eerst bouwen: dotnet build src/Boekerhouden.Administratie. Een PDF maken lukt in de cloudsessie
# alleen met de lichte Chromium (zie docs/overdracht.md); die wordt hier ingesteld als hij er is.
set -e
cd "$(dirname "$0")/../.."
for p in $(pgrep -f "net10.0/Boekerhouden.Administratie" || true); do kill "$p" 2>/dev/null || true; done
sleep 2
PGPASSWORD=boekerhouden-dev psql -h localhost -U boekerhouden -d postgres -qc "drop database if exists boekerhouden_administratie_ui with (force);"
schil=$(ls -d /opt/pw-browsers/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell 2>/dev/null | head -1 || true)
export Pdf__Browser="${Pdf__Browser:-$schil}"
ConnectionStrings__Administratie="Host=localhost;Database=boekerhouden_administratie_ui;Username=boekerhouden;Password=boekerhouden-dev" \
    nohup dotnet run --project src/Boekerhouden.Administratie --no-build > /tmp/administratie-ui.log 2>&1 &
for i in $(seq 1 40); do sleep 1; curl -s -o /dev/null http://localhost:5090/ && break; done
echo "klaar: http://localhost:5090 (log in /tmp/administratie-ui.log)"
