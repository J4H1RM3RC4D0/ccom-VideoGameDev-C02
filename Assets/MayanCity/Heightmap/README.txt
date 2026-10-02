Put the Maya Forest height map (.raw or .r16) from the MS Teams "landscapes" folder in this folder.

"Mayan City > Steps > 1 - Create Terrain From Height Map (33x33)" (or "Build Everything") reads the first
.raw/.r16 file found here, detects 8/16-bit and Windows/Mac byte order automatically, and reduces it to a
33x33 heightmap resolution as the assignment requires. With no file here, a procedural jungle height map is used.

To do the import by hand for your write-up screenshots instead: select MayanTerrain > Terrain Settings (gear)
> Import Raw..., pick the file, then run "Steps > 2 - Sculpt City Plateau + Paint Terrain Layers".
Step 2 reduces any hand-imported map to 33x33 and repaints the layers.
