#!/bin/bash
# Zorgt dat Claude Code-cloudsessies kunnen bouwen en testen:
# installeert de .NET 10 SDK (uit de Ubuntu-pakketbron) en herstelt de NuGet-pakketten.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(pwd)}"

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  SUDO=""
  if [ "$(id -u)" -ne 0 ]; then
    SUDO="sudo"
  fi
  $SUDO apt-get update -qq >&2
  DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq dotnet-sdk-10.0 >&2
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet restore "$PROJECT_DIR/Boekerhouden.slnx" >&2

echo ".NET SDK $(dotnet --version) klaar; pakketten hersteld voor Boekerhouden.slnx."
