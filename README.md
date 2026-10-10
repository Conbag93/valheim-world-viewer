# Valheim world viewer

A top-down viewer for everything built in a Valheim 1.0 world, storey by storey, over a map of the
biomes you've explored. It's a static Blazor WebAssembly site: saves are read in the visitor's
browser and kept there (IndexedDB). Nothing is uploaded, and there's no server.

## Running it

```
cd src/ValheimDiagrams.Client
dotnet watch
```

Open http://localhost:5216 and choose a world folder: the one named after the world, inside
`worlds_local`, holding `.chunk` and `_main.*` files.

| Where saves live | |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local` |
| Linux / Steam Deck | `~/.config/unity3d/IronGate/Valheim/worlds_local` |
| macOS | `~/Library/Application Support/IronGate/Valheim/worlds_local` |
| Dedicated server | its `worlds_local`; `scripts/fetch-world.sh <WorldName>` copies one from the server's container into `world/` |

Saves from before Valheim 1.0 (a flat `.fwl` + `.db` pair) can't be read; loading them in the game once converts them.

## Publishing

`.github/workflows/pages.yml` publishes to GitHub Pages when run by hand (Actions → Deploy to GitHub
Pages → Run workflow, or `gh workflow run pages.yml`); pushes don't publish. It AOT-compiles the client, points `<base href>` at the repo's sub-path, and copies `index.html` to `404.html` so links
into the app (`/world/...`) still load.

To try a published build locally:

```
dotnet publish src/ValheimDiagrams.Client -c Release -o publish
python3 -m http.server -d publish/wwwroot 5219
```

Publishing AOT-compiles to WebAssembly (`RunAOTCompilation`; needs `dotnet workload install wasm-tools`):
about 1.5 min, a 15 MB runtime download, but imports and terrain drawing are roughly 10x faster than
`dotnet watch`'s interpreter (a 28 MB world with 790,000 objects imports in about 0.5 s).

## Layout

| Project | Role |
|---|---|
| `ValheimDiagrams.Core` | Reading saves and making sense of them, with no UI or storage: `.chunk`/`.fwl2`/`.db2` readers, prefab names, build detection, storeys, the biome generator and terrain tiles |
| `ValheimDiagrams.Client` | The site: `Home` (worlds, import), `WorldPage` (overview, builds, ceiling slider), `WorldStore` (browser storage and queries), `DiagramViewer`/`DiagramCanvas`/`ShapeLayer` (pan/zoom SVG), `TerrainLayer` |
| `ValheimDiagrams.Shared` | The DTOs the UI draws (`DiagramDto`, `WindowDto`, `BuildDto`, …) |
| `research/worldgen` | The Python reference port of Valheim's biome generator and notes on the save format |

## Viewing a world

- **Overview**: every build on one map. Builds are clusters of player-built pieces with less than 32 m between them (`BuildFinder`); dungeon interiors, which the game places about 5 km up, are kept as separate builds.
- **Builds and saved views**: one window of pieces at a time (capped at 10,000). **Load what's in view** loads whatever area is on screen; **Save view…** names it.
- **Ceiling slider**, after Minutor's depth slider: everything at or below the ceiling is drawn, highest on top, and pieces below the current storey are darkened. Shift+wheel moves it ±0.5 m, `[` / `]` jump a storey, "This storey only" hides what's beneath.
- **Biomes**, only on explored ground. Valheim doesn't save biomes: `BiomeGenerator` recomputes them from the seed, and `TerrainRenderer` draws 256 px tiles at 1–64 m per pixel, which the browser keeps. Each import checks the generator against the save's own locations (every location may only sit in certain biomes); hover the Biomes toggle for the score, which should be all of them.
- Coordinates are Valheim's world x (east) and z (north); the grid lines up with the floor tiles.
