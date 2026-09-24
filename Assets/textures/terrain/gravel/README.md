# Large-grain gravel source

Generated with the built-in image generation tool on 2026-09-20.
This candidate is bound to Gravel7 rendering, with no automatic world placement. In-game parallax
validation is pending. No existing texture is replaced. Both outputs are1254px
RGB8, despite the requested2048px size; the shader uses the actual source size.

Intended physical scale: 0.8 metre tile with predominantly 3–7 cm angular grains.
The height image is a generated interpretation, not measured geometry. Its
registration and edge continuity require verification before runtime adoption.
The existing shared terrain relief ray uses a35mm height interval and minimum
mip2. Gravel shading derives normal slopes from this same filtered height.
Roughness is0.95; cavity shading is height-based. Generated height is8-bit source
precision even though its runtime output format isR16F.

## Color prompt

Use case: photorealistic-natural. Asset type: seamless square game terrain base-color texture, 2048 by 2048. Primary request: rough large-grain gravel, tightly packed irregular angular crushed stones with chipped edges and gritty mineral surfaces. Orthographic straight-down view covering about 0.8 metre square, individual grains predominantly 3 to 7 centimetres with some smaller fragments filling gaps. Neutral natural gray stones with subtle warm and cool mineral variation. Flat diffuse illumination suitable for PBR albedo; no directional cast shadows or specular highlights. Uniform distribution and scale across the whole tile, continuous wrap-compatible edges. No large boulders, bedrock slabs, plants, leaves, objects, text, borders, or watermark.

## Height prompt

undefined
