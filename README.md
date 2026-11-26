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
- Keeps a cache of each mesh's original, so later runs work from the original rather than
  the patcher's own output, and restores it when a mesh no longer needs changes.
- Writes a CSV report of every decision to the patcher's settings folder.

## Use

1. In Synthesis: add a patcher → **Local Solution** → **Existing**, pick
   `ShaderMatchPatcher.sln` / `ShaderMatchPatcher`.
2. Set **Target mod**, and **Output folder** to a dedicated MO2 mod folder
   (empty = the Data folder, i.e. MO2's overwrite).
3. Run Synthesis from MO2 so loose mod files are visible.
4. Enable the output mod below the target mod.

## Development

`dotnet run --project ShaderMatchPatcher -- --selftest <folder> [...]` checks that every
`.nif` under the folders reads and writes back byte-for-byte.

Only Skyrim LE/SE NIFs (20.2.0.7, stream 83/100) are handled; anything unexpected is
skipped and reported rather than guessed.
