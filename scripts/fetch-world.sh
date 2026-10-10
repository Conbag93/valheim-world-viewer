#!/usr/bin/env bash
# Copies a world save (chunk files and _main.* metadata) from a dedicated server running the
# lloesche/valheim-server Docker image into world/<WorldName>, ready to pick in the viewer.
# Read-only on the server. Usage: scripts/fetch-world.sh <ssh-host> <WorldName>
set -euo pipefail
if [[ $# -ne 2 ]]; then
    echo "Usage: $0 <ssh-host> <WorldName>" >&2
    exit 1
fi
HOST=$1
WORLD=$2
CONTAINER=${VALHEIM_CONTAINER:-valheim-server}
DEST="$(cd "$(dirname "$0")/.." && pwd)/world/$WORLD"

mkdir -p "$DEST"
rm -f "$DEST"/*.chunk "$DEST"/_main.*
ssh -o ConnectTimeout=8 "$HOST" "docker exec $CONTAINER sh -c 'cd /config/worlds_local/$WORLD && tar cf - *.chunk _main.*'" \
    | tar xf - -C "$DEST"
echo "Copied $(ls "$DEST"/*.chunk | wc -l) chunk files to $DEST"
