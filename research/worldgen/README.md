# World generation research

Where the "terrain" for a Valheim 1.0 world comes from, worked out from the 1.0.17 server's
`assembly_valheim.dll` / `assembly_utils.dll` and checked against a real 1.0 save.

| What | Where it lives | Status |
|---|---|---|
| **Biomes** | Not saved: computed from the seed by `WorldGenerator.GetBiome(x, z)` | `biome.py` ports it; matches all 12,286 saved locations (100%) |
| **Base terrain height** | Not saved: `GetBiomeHeight` per biome, plus rivers and lakes from `Pregenerate()` | `base_height` ported (drives ocean/mountain); full heights not yet |
| **Player terrain edits** | `_TerrainCompiler` objects (TerrainComp) in the `.chunk` files: per-zone height/paint deltas | Not read yet |
| **Explored zones** | `_main.N.db2` → ZoneSystem: every 64 m zone ever generated | Read in Python |
| **Locations** (bosses, trader, dungeons, villages) | `_main.N.db2` → ZoneSystem: prefab hash + position for each | Read in Python; names via the Jötunn location list |
| **Global keys** (bosses defeated, …) | `_main.N.db2` → ZoneSystem | Read in Python |
| **A player's own map and pins** | The character file (`characters_local/*.fch`), not the world | Not looked at |

Gotchas found on the way:
- `Mathf.PerlinNoise` is Ken Perlin's 3D improved noise at z = 0 (an extra permutation lookup per corner),
  normalised as `(n + 0.69) / 1.483`, and **mirrored for negative inputs** (`f(|x|, |y|)`). Without the mirroring
  only ~78% of locations matched; with it, 100%.
- Seed offsets come from Unity's `Random.InitState(seed)` (xorshift128) and `Random.Range(int, int)`.
- `_main.N.db2` layout: `int version, double netTime, int length, gzip[length]` → ZoneSystem
  (`int n, n×(short,short) zones; int locationVersion; int k, k×string keys; bool; int m, m×(int hash, vec3 pos, bool)`), then RandEventSystem.
- 1.0 also builds an `AltBiomeWorldData` grid (2048², 12 m) from the same function and may cache it as
  `cache/<World>_biomedatacache.bin`; neither the dedicated server nor this PC had one.

