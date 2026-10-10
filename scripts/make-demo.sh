#!/usr/bin/env bash
# Makes the site's demo (src/ValheimDiagrams.Client/wwwroot/demo-world) from a world save folder, such as
# one copied by fetch-world.sh. Commit the result and publish the site to update the live demo.
# Usage: scripts/make-demo.sh <world folder>
set -euo pipefail
if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <world folder>" >&2
    exit 1
fi
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet run --project "$ROOT/tools/MakeDemo" -- "$1" "$ROOT/src/ValheimDiagrams.Client/wwwroot/demo-world"
