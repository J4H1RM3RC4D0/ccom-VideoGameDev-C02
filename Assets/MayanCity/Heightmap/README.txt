MayanJungle_01_250x250_16bit.raw is the Maya Forest height map from the MS Teams "landscapes" folder
(MayanJungle_01_250x250.raw), converted for Unity. The original stores every height three times (RGB,
250 x 250 x 3 = 187,500 bytes); Unity's Import Raw needs a single channel, so each 8-bit height v was
written once as the 16-bit value v * 257 (250 x 250 x 2 = 125,000 bytes). The untouched originals are in
Docs/Landscapes_Original.

"Mayan City > Steps > 1 - Create Terrain From Height Map (33x33)" (or "Build Everything") reads the first
.raw/.r16 file found here, detects 8/16-bit and Windows/Mac byte order automatically, uses it as the valley
floor, raises the mountain ring along the borders (taller where the height map is high), and reduces it to a
33x33 heightmap resolution as the assignment requires. With no file here, a procedural height map is used.

To do the import by hand for your write-up screenshots instead: select MayanTerrain > Terrain Settings (gear)
> Import Raw..., pick the file (Depth: Bit 16, Resolution: 250, Byte Order: Windows), then run
"Steps > 2 - Sculpt City Plateau + Paint Terrain Layers". Step 2 reduces any hand-imported map to 33x33 and
repaints the layers.
