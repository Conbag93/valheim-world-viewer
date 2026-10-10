# Valheim world viewer

A top-down viewer for Valheim 1.0 worlds: a static Blazor WebAssembly site (no server; saves are read and stored in the browser). Run it with `dotnet watch` from `src/ValheimDiagrams.Client`.

## Chunk format (Valheim 1.0, world version 41)

- **File layout:** each `.chunk` file is `short version, int count`, followed by `count` ZDO records in the `ZDO.Save` format.
- **Record fields:** a ushort flags field; the position (3 floats, or 2 shorts when the SmallPosition flag is set); the prefab as an int `StableHashCode`; an optional packed rotation; and then key/value blocks.
- **Player-built pieces:** these carry the long key `creator`.
- **Parser:** `src/ValheimDiagrams.Core/Save/ChunkReader.cs`.
- **World generation and `_main.N.db2`:** see `research/worldgen/README.md`. Unity's `Mathf.PerlinNoise` mirrors negative inputs; without that, biomes come out wrong.

## Working on the app

- **Don't run `dotnet build` while `dotnet watch` is running.** The two race over `obj/` and crash the watcher. Stop the watcher, build, then restart it.
- **Check for an orphaned app process.** Stopping the watcher can leave the app itself running and holding port 5216. Look for it with `ss -ltnp | grep 5216`.
- **Testing imports in Playwright:** click the folder input, then `browser_file_upload` with the world folder's path; Playwright fills the `webkitdirectory` input with its files.
- **Test saves** go in `world/` (gitignored), e.g. via `scripts/fetch-world.sh <ssh-host> <WorldName>`.
