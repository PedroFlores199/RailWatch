#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project src/RailWatch.Api --urls http://127.0.0.1:5090
