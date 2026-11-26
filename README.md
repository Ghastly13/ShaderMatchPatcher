# ShaderMatchPatcher

A Synthesis patcher for Skyrim SE that copies `BSLightingShaderProperty` settings from vanilla
weapon meshes onto one mod's weapon meshes, where they use the same textures.

## What it does

- Patches **one target plugin per run**: only meshes used by weapons that plugin defines.
  Vanilla meshes are never edited.
- Reads the reference values **live from the load order**: every weapon in the base game,
  DLC and Creation Club (plus any extra plugins you list), using whichever copy of each mesh
  wins, loose or archived.
- Decides values in passes:
  1. **Exact texture** – vanilla shapes with the same diffuse, narrowed by normal map,
     scabbard, weapon type, material and 1st/3rd person (decided by how the record uses the
     mesh). Must agree, or clearly agree by majority.
  2. **Other shapes** – ambiguous shapes follow the vanilla mesh the rest of their mesh
     matched, or the same texture resolved elsewhere in the mod; shapes the author set up
     identically follow each other (scabbards and blades never mix).
  3. **Weapon material** – shapes with the mod's own textures use what at least two vanilla
     shapes of the same `WeapMaterial*` keyword and shader type agree on.
  4. Optionally, the most common vanilla version for anything still ambiguous.
- Inferred results never change the shader type or fill texture slots.
- Geometry-dependent flag bits (Skinned, Vertex_Alpha, Vertex_Colors, Double_Sided), UVs,
  alpha and the diffuse/normal slots are kept from the target.
- **Edits the target mod's loose mesh files in place.** Under MO2 the change lands in the
  mod's own folder (the existing file is opened and rewritten, so nothing goes to overwrite).
  Meshes packed in a BSA are skipped and reported.
- Backs up each mesh's original before its first edit. Later runs work from that original
  rather than the patcher's own edit, skip files already up to date, and put the original
  back when a mesh no longer needs changes. **Restore originals** puts every backup back.
- Writes a CSV report of every decision to the patcher's settings folder.

## Use

1. In Synthesis: add a patcher → **Local Solution** → **Existing**, pick
   `ShaderMatchPatcher.sln` / `ShaderMatchPatcher`.
2. Set **Target mod**.
3. Run Synthesis from MO2 so loose mod files are visible.
4. To undo: tick **Restore originals**, run once, then untick it.

## Development

`dotnet run --project ShaderMatchPatcher -- --selftest <folder> [...]` checks that every
`.nif` under the folders reads and writes back byte-for-byte.

Only Skyrim LE/SE NIFs (20.2.0.7, stream 83/100) are handled; anything unexpected is
skipped and reported rather than guessed.
