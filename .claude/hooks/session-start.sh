#!/bin/bash
# Zorgt dat Claude Code-cloudsessies kunnen bouwen en testen:
# installeert de .NET 10 SDK (uit de Ubuntu-pakketbron), herstelt de NuGet-pakketten en tools,
# en start PostgreSQL met de ontwikkelrol en -database (nodig voor de server en de servertests).
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(pwd)}"
SUDO=""
ALS_POSTGRES=(runuser -u postgres --)
if [ "$(id -u)" -ne 0 ]; then
  SUDO="sudo"
  ALS_POSTGRES=(sudo -u postgres)
fi

PAKKETTEN=()
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  PAKKETTEN+=(dotnet-sdk-10.0)
fi
if ! command -v pg_ctlcluster >/dev/null 2>&1; then
  PAKKETTEN+=(postgresql)
fi
if [ ${#PAKKETTEN[@]} -gt 0 ]; then
  $SUDO apt-get update -qq >&2
  DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq "${PAKKETTEN[@]}" >&2
fi

# PostgreSQL starten en de ontwikkelrol (mag databases maken, voor de tests) en -database aanmaken.
CLUSTER=$(pg_lsclusters --no-header | awk 'NR==1 {print $1" "$2}')
if [ -n "$CLUSTER" ]; then
  # shellcheck disable=SC2086
  $SUDO pg_ctlcluster $CLUSTER start >&2 2>/dev/null || true
  "${ALS_POSTGRES[@]}" psql -qtAc "SELECT 1 FROM pg_roles WHERE rolname = 'boekerhouden'" | grep -q 1 \
    || "${ALS_POSTGRES[@]}" psql -qc "CREATE ROLE boekerhouden LOGIN CREATEDB PASSWORD 'boekerhouden-dev'" >&2
  "${ALS_POSTGRES[@]}" psql -qtAc "SELECT 1 FROM pg_database WHERE datname = 'boekerhouden'" | grep -q 1 \
    || "${ALS_POSTGRES[@]}" psql -qc "CREATE DATABASE boekerhouden OWNER boekerhouden" >&2
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet restore "$PROJECT_DIR/Boekerhouden.slnx" >&2
(cd "$PROJECT_DIR" && dotnet tool restore >&2)

echo ".NET SDK $(dotnet --version) klaar; pakketten hersteld; PostgreSQL draait (rol en database 'boekerhouden')."
