# Rock 04 prototype source

Source: https://polyhaven.com/a/rock_04
Author: Rob Tuytel. License: CC0 (https://polyhaven.com/license).
Original matched 2048-square maps, downloaded2026-09-19. Published tile width1.5m.
`info.json` and `files.json` preserve source metadata and download URLs/checksums.
All five files were verified against published byte counts and MD5 values.

Color and normal are8-bit RGB, roughness/AO8-bit grayscale. Displacement is
16-bit grayscale+alpha (PNG IHDR bit-depth16, color-type4). Pillow's RGBA
conversion drops the grayscale precision; retain the original PNG as engine input.
The shader requests linear16-bit input and R16F storage for height.

Relief amplitude is authored, not a measured scan depth. A preliminary8-bit-preview
height/GL-normal slope fit suggests roughly52/54mm full interval on U/V; this
is only an approximate correspondence check. Green must be flipped for the
project's world-axis UV convention. AO tests80mm at the documented tile width.
This asset is an unaccepted prototype replacement, not a shipped art decision.
