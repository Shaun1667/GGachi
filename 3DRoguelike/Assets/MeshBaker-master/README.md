![GitHub License](https://img.shields.io/github/license/OlegDzhuraev/MeshBaker)

# Mesh Baker tool
Allows you to combine several meshes into one, and combine their textures to atlases for optimization purposes.

<p align="center">
    <img src="https://dzhuraev.com/GithubData/MeshBakerTool01.png" width="410" height="419" alt="Mesh Baker Tool">
</p>

# Features
- Supports almost unlimited meshes per combine (but it is necessary to use common sense, personally determining the necessary limit).
- Supports bake of Albedo, Normal, Specular/Metallic and AO maps to texture atlases. 
- Generates mesh UVs for Lightmapper (optional).
- Supports mesh save to the Assets folder.
- Supports generated meshes with up to 4 billion vertices (but not guaranteed to rendered on all platforms. In this case, try to keep vertex count lower than 65536).

# URP version (modified)
This copy is modified to work with URP:
- Supports URP Lit materials (`_BaseMap`, `_BaseColor`, URP keywords). Built-in Standard materials still work.
- Reads textures from the source files (or a GPU copy), so the original textures' Read/Write import settings are not changed.
- Keeps each texture's resolution in the atlas (no downscale to the smallest one) and sets the atlas import Max Size correctly.
- Material color (`_BaseColor` / `_Color`) is multiplied into the albedo atlas.
- Selecting a parent object bakes all child meshes. Original objects stay; only their MeshRenderers are disabled, so colliders keep working.
- Re-baking with the same folder and postfix keeps the same asset GUIDs.
- Baking can be called from code: `InsaneOne.DevTools.MeshBakerCore.Bake(renderers, new MeshBakeSettings { ... })`.

# Limitations
Limitations may be removed in the future versions.
- Each mesh must have one material (one submesh).
- Mesh UVs should be inside 0..1 (tiled textures are not supported in the atlas).

# How to
1. Open **Tools -> Mesh Baker** in the top menu. 
2. Select required meshes on scene (or a parent object).
3. Change parameters to fit your needs.
4. Press **Bake** in the tool interface.

# Recommendations
- Textures are better to have square aspect ratio (non-square is supported, but not in the most efficient way).
- If all textures do not fit into the max atlas size, they are scaled down proportionally.
- Color atlases bigger than 4096 are saved as JPG to keep file size small; normal atlases are always PNG.
