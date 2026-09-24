"""Import authored leaf atlases and materials for spawn trees.

Requires Pillow and the checked-in leaf source art. No engine or desktop access.
See Assets/textures/trees/README.md for generated and CC0 source provenance.
The runtime geometry recipe owns tree shapes; this tool only prepares materials.
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1] / "Assets"
OUT = ROOT / "textures" / "trees"
MATERIALS = ROOT / "materials" / "trees"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    MATERIALS.mkdir(parents=True, exist_ok=True)
    Image.new("L", (4, 4), 0).save(OUT / "metalness.png")
    pine = Image.open(Path(__file__).parent / "TreeSources" / "pine_needle_tuft_atlas.png").convert("RGBA")
    pine = pine.resize((1024, 1024), Image.Resampling.LANCZOS)
    pine.save(OUT / "pine_needle_tuft_atlas.png", optimize=True)
    pine.getchannel("A").save(OUT / "pine_needle_tuft_opacity.png", optimize=True)
    # Preserve the original in TreeSources. The engine's alpha-weighted mip
    # compiler requires power-of-two dimensions; resample as an import step.
    for kind in ("oak", "ash"):
        atlas = Image.open(Path(__file__).parent / "TreeSources" / f"{kind}_leaf_atlas.png").convert("RGBA")
        atlas = atlas.resize((1024, 1024), Image.Resampling.LANCZOS)
        atlas.save(OUT / f"{kind}_leaf_atlas.png", optimize=True)
        atlas.getchannel("A").save(OUT / f"{kind}_leaf_opacity.png", optimize=True)
        Image.new("L", (4, 4), 166).save(OUT / f"{kind}_leaf_roughness.png")
        (MATERIALS / f"{kind}_spray.vmat").write_text(f'''Layer0
{{
    shader "shaders/foliage.shader"
    F_ALPHA_TEST 1
    F_RENDER_BACKFACES 1
    F_FOLIAGE_ANIMATION 1
    F_TRANSMISSIVE 1
    TextureColor "textures/trees/{kind}_leaf_atlas.png"
    TextureTranslucency "textures/trees/{kind}_leaf_opacity.png"
    TextureRoughness "textures/trees/{kind}_leaf_roughness.png"
    TextureMetalness "textures/trees/metalness.png"
    g_flAlphaTestReference 0.4
    g_flSwayStrength 5.0
    g_flSwaySpeed 0.6
    g_flEdgeAmplitude 0.45
    g_flBranchAmplitude 0.0
    g_flTransmissionScale 0.35
    g_flWrapStrength 0.15
    g_flAmbientBoost 0.0
    g_flNormalVariation 0.025
    g_flMinRoughness 0.5
}}
''', encoding="utf-8", newline="\r\n")
    (MATERIALS / "oak_bark.vmat").write_text('''Layer0
{
    shader "shaders/trees/tree_bark.shader"
    F_BARK_ANIMATION 1
    TextureColor "textures/trees/oak_bark/jolcham_oak_bark_01_diff_2k.jpg"
    TextureNormal "textures/trees/oak_bark/jolcham_oak_bark_01_nor_gl_2k.jpg"
    TextureRoughness "textures/trees/oak_bark/jolcham_oak_bark_01_rough_2k.jpg"
    TextureMetalness "textures/trees/metalness.png"
    g_flSwayStrength 5.0
    g_flSwaySpeed 0.6
}
''', encoding="utf-8", newline="\r\n")
    (MATERIALS / "ash_bark.vmat").write_text('''Layer0
{
    shader "shaders/bark.shader"
    F_BARK_ANIMATION 1
    TextureColor "textures/trees/ash_bark/tree_bark_03_diff_2k.jpg"
    TextureNormal "textures/trees/ash_bark/tree_bark_03_nor_gl_2k.jpg"
    TextureRoughness "textures/trees/ash_bark/tree_bark_03_rough_2k.jpg"
    TextureMetalness "textures/trees/metalness.png"
    g_flSwayStrength 5.0
    g_flSwaySpeed 0.6
}
''', encoding="utf-8", newline="\r\n")
    (MATERIALS / "pine_spray.vmat").write_text('''Layer0
{
    shader "shaders/foliage.shader"
    F_ALPHA_TEST 1
    F_RENDER_BACKFACES 1
    F_FOLIAGE_ANIMATION 1
    F_TRANSMISSIVE 1
    TextureColor "textures/trees/pine_needle_tuft_atlas.png"
    TextureTranslucency "textures/trees/pine_needle_tuft_opacity.png"
    TextureMetalness "textures/trees/metalness.png"
    g_flTintColor "[0.95 1.0 0.92 0]"
    g_flMinRoughness 0.65
    g_flAlphaTestReference 0.4
    g_flSwayStrength 3.0
    g_flSwaySpeed 0.55
    g_flEdgeAmplitude 0.22
    g_flBranchAmplitude 0.0
    g_flTransmissionScale 0.2
    g_flWrapStrength 0.15
    g_flAmbientBoost 0.0
    g_flNormalVariation 0.02
}
''', encoding="utf-8", newline="\r\n")
    (MATERIALS / "pine_bark.vmat").write_text('''Layer0
{
    shader "shaders/bark.shader"
    F_BARK_ANIMATION 1
    TextureColor "textures/trees/pine_bark/pine_bark_diff_2k.jpg"
    TextureNormal "textures/trees/pine_bark/pine_bark_nor_gl_2k.jpg"
    TextureRoughness "textures/trees/pine_bark/pine_bark_rough_2k.jpg"
    TextureMetalness "textures/trees/metalness.png"
    g_flSwayStrength 3.0
    g_flSwaySpeed 0.55
}
''', encoding="utf-8", newline="\r\n")
    print("Imported shared tree materials and leaf opacity.")


if __name__ == "__main__":
    main()
