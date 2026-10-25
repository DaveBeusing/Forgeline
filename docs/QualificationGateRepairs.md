# Shared PR qualification repairs

This prerequisite repairs the three monotonic-tick CA1512 build failures using the standard checked argument guard. It changes neither regional update state nor accepted tick ordering.

The resource-field fixture now records pixel-texture bindings instead of reaching the interface's unsupported default. Existing root, selection, visibility and draw-sharing assertions remain; the test additionally requires two textured parts, four bound material textures, one shared instanced draw, and no bindings or draws for a hidden field.

Lossless texture storage addresses the **compiled catalog** budget in #344. Compiler 1.12.2 may wrap an RGBA8 display-image semantic payload in bounded ZLib storage (payload marker 4). The decoded payload must be a single version-3 semantic texture; nested wrappers reject. Declared encoded/decoded sizes are checked before allocation, the decoded payload is capped at 32 MiB, exact decoded length and normal subresource validation remain mandatory, and nonbeneficial compression keeps the original payload. Payload versions 1–3 remain readable.

Only RGBA8 display images are selected automatically. World materials, BC7 textures, normal/control maps, all source pixels, dimensions, mip levels, alpha, GPU formats and **decoded GPU residency** are unchanged. No lossless UI page is converted to BC7. Catalog accounting still counts every stored container byte; this storage saving is not a VRAM saving. The 56 MiB gameplay catalog limit, studio budget and 32 MiB UI residency limit remain unchanged.

Regression evidence includes all three actual 1024-square UI pages compared byte-for-byte against uncompressed source imports. Their GPU data remains 12 MiB in total; encoded containers must total below 2 MiB. Corrupt/truncated data, invalid lengths, overlong/short decoded streams, nested wrappers and invalid semantic offsets reject. Full production asset compilation/qualification and both window modes must still pass before integration.
