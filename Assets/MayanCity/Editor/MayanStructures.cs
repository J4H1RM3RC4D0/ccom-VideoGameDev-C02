using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace MayanCityTools
{
    /// <summary>
    /// ProBuilder models for the city. Every building is assembled from ProBuilder shapes
    /// (cubes, tapered cubes, stairs, cylinders, prisms and an icosphere dome), grouped by part.
    /// Local convention: each building's front (main stairway) faces local -Z, toward the plaza.
    /// </summary>
    public static class MayanStructures
    {
        // ------------------------------------------------------------------ ProBuilder helpers

        static Transform Group(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        // Moves the pivot to the bottom-centre, parents the shape and applies the material.
        static ProBuilderMesh Place(ProBuilderMesh pb, string name, Transform parent, Vector3 basePos, Quaternion rot, Material mat, bool collider = true)
        {
            var p = pb.positions.ToArray();
            var b = new Bounds(p[0], Vector3.zero);
            foreach (var v in p) b.Encapsulate(v);
            var offset = new Vector3(b.center.x, b.min.y, b.center.z);
            for (int i = 0; i < p.Length; i++) p[i] -= offset;
            pb.positions = p;

            pb.gameObject.name = name;
            pb.transform.SetParent(parent, false);
            pb.transform.localPosition = basePos;
            pb.transform.localRotation = rot;
            pb.SetMaterial(pb.faces, mat);
            pb.ToMesh();
            pb.Refresh();
            pb.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) pb.gameObject.AddComponent<MeshCollider>();
            return pb;
        }

        /// <summary>Cube, optionally tapered (talud) by insetting the top face.</summary>
        static ProBuilderMesh Box(string name, Transform parent, Vector3 basePos, Vector3 size, Material mat,
            float insetX = 0f, float insetZ = 0f, Quaternion? rot = null, bool collider = true)
        {
            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            if (insetX > 0f || insetZ > 0f)
            {
                var p = pb.positions.ToArray();
                float sx = (size.x - 2f * insetX) / size.x, sz = (size.z - 2f * insetZ) / size.z;
                for (int i = 0; i < p.Length; i++)
                    if (p[i].y > 0f) { p[i].x *= sx; p[i].z *= sz; }
                pb.positions = p;
            }
            return Place(pb, name, parent, basePos, rot ?? Quaternion.identity, mat, collider);
        }

        static ProBuilderMesh Cyl(string name, Transform parent, Vector3 basePos, float radius, float height, Material mat, int sides = 32)
            => Place(ShapeGenerator.GenerateCylinder(PivotLocation.Center, sides, radius, height, 0, 1), name, parent, basePos, Quaternion.identity, mat);

        static ProBuilderMesh Stairs(string name, Transform parent, Vector3 basePos, Vector3 size, int steps, Material mat)
            => Place(ShapeGenerator.GenerateStair(PivotLocation.Center, size, steps, true), name, parent, basePos, Quaternion.identity, mat);

        static ProBuilderMesh Dome(string name, Transform parent, Vector3 basePos, float radius, Material mat)
        {
            var pb = ShapeGenerator.GenerateIcosahedron(PivotLocation.Center, radius, 2);
            foreach (var f in pb.faces) f.smoothingGroup = 1;
            return Place(pb, name, parent, basePos, Quaternion.identity, mat);
        }

        // Sloped balustrades (alfardas) flanking a stairway that rises along +Z.
        static void Alfardas(Transform parent, float centerZ, float run, float rise, float stairWidth, Material mat)
        {
            float len = Mathf.Sqrt(run * run + rise * rise);
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(-angle, 0f, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                var mid = new Vector3(s * (stairWidth / 2f + 0.6f), rise / 2f, centerZ);
                Box(s < 0 ? "Alfarda_L" : "Alfarda_R", parent, mid - rot * Vector3.up * 0.25f, new Vector3(1.2f, 0.9f, len), mat, rot: rot);
            }
        }

        static Transform NewRoot(string name, Vector2 xz, Vector2 facing)
        {
            MayanCityBuilder.RemoveRoot(name);
            var root = new GameObject(name).transform;
            root.position = MayanCityBuilder.Ground(xz);
            var f = MayanCityBuilder.FrontDir(xz, facing);
            // local -Z (the front) must point toward `facing`
            root.rotation = Quaternion.LookRotation(-f, Vector3.up);
            return root;
        }

        // ------------------------------------------------------------------ Temple of the Foliated Cross

        /// <summary>
        /// Stepped pyramid (4 tapered tiers with cornices), central stairway with alfardas, a three-door temple
        /// (an open gallery inside) and a sloped mansard roof.
        /// </summary>
        public static GameObject BuildTemple()
        {
            var pal = Palette.Load();
            var root = NewRoot(MayanCityBuilder.TempleName, MayanCityBuilder.TemplePos, MayanCityBuilder.PlazaCenter);

            const float W = 42f, D = 36f, tierH = 3.5f, shrink = 3.2f;
            const int tiers = 4;

            var pyramid = Group("Pyramid", root);
            Box("Foundation", pyramid, new Vector3(0f, -3f, 0f), new Vector3(W + 3f, 3.3f, D + 3f), pal.Stone);
            for (int i = 0; i < tiers; i++)
            {
                float w = W - 2f * shrink * i, d = D - 2f * shrink * i, y = i * tierH;
                Box($"Tier_{i + 1}", pyramid, new Vector3(0f, y, 0f), new Vector3(w, tierH - 0.35f, d), pal.Stone, 0.6f, 0.6f);
                Box($"Tier_{i + 1}_Cornice", pyramid, new Vector3(0f, y + tierH - 0.35f, 0f), new Vector3(w - 0.8f, 0.35f, d - 0.8f), pal.Limestone);
            }
            float topY = tiers * tierH;                   // 14 m
            float topD = D - 2f * shrink * tiers;         // 10.4 m

            // The stairway projects well out from the pyramid so its slope clears every tier's cornice.
            var stair = Group("Main_Stairway", root);
            float zFront = -D / 2f - 13f, zTop = -topD / 2f + 0.3f, run = zTop - zFront;
            Stairs("Steps", stair, new Vector3(0f, 0f, zFront + run / 2f), new Vector3(9f, topY, run), 36, pal.Limestone);
            Alfardas(stair, zFront + run / 2f, run, topY, 9f, pal.Stone);
            Box("Landing", stair, new Vector3(0f, -0.3f, zFront - 2.5f), new Vector3(14f, 0.6f, 5f), pal.Limestone);

            var temple = Group("Temple", root);
            const float tz = 0.3f, tw = 15.6f, td = 9.6f, floorY = 14.5f, wallH = 4.2f;
            Box("Plinth", temple, new Vector3(0f, topY, tz), new Vector3(tw, 0.5f, td), pal.Limestone);

            var walls = Group("Walls", temple);
            Box("Back_Wall", walls, new Vector3(0f, floorY, tz + td / 2f - 0.5f), new Vector3(tw, wallH, 1f), pal.Limestone);
            Box("Side_Wall_L", walls, new Vector3(-tw / 2f + 0.5f, floorY, tz), new Vector3(1f, wallH, td), pal.Limestone);
            Box("Side_Wall_R", walls, new Vector3(tw / 2f - 0.5f, floorY, tz), new Vector3(1f, wallH, td), pal.Limestone);

            // Front facade: four piers framing three doorways, topped by a carved lintel.
            const float doorW = 2.2f, doorH = 2.9f;
            float pierW = (tw - 3f * doorW) / 4f, frontZ = tz - td / 2f + 0.5f;
            for (int k = 0; k < 4; k++)
            {
                float x = -tw / 2f + pierW / 2f + k * (pierW + doorW);
                Box($"Pier_{k + 1}", walls, new Vector3(x, floorY, frontZ), new Vector3(pierW, doorH, 1f), pal.Limestone);
            }
            Box("Front_Lintel", walls, new Vector3(0f, floorY + doorH, frontZ), new Vector3(tw, wallH - doorH, 1f), pal.Stucco);
            for (int k = 0; k < 7; k++)
                Box($"Glyph_Block_{k + 1}", walls, new Vector3(-6f + k * 2f, floorY + doorH + 0.25f, frontZ - 0.55f), new Vector3(0.8f, 0.8f, 0.15f), pal.Carving, collider: false);

            // Mansard roof (sloping upper facade) and the latticed roof comb (cresteria).
            var roof = Group("Roof", temple);
            float roofY = floorY + wallH;
            Box("Ceiling_Slab", roof, new Vector3(0f, roofY, tz), new Vector3(tw + 0.2f, 0.3f, td + 0.2f), pal.Limestone);
            Box("Mansard_Roof", roof, new Vector3(0f, roofY + 0.3f, tz), new Vector3(tw + 0.2f, 2.4f, td + 0.2f), pal.Stucco, 1.6f, 2.0f);
            Box("Roof_Cap", roof, new Vector3(0f, roofY + 2.7f, tz), new Vector3(12.8f, 0.3f, 6f), pal.Limestone);

            MayanCityBuilder.Log($"Step 3: Temple of the Foliated Cross built at {root.position} ({root.GetComponentsInChildren<ProBuilderMesh>().Length} ProBuilder shapes)");
            return root.gameObject;
        }

        // ------------------------------------------------------------------ El Caracol

        /// <summary>
        /// Two stacked rectangular platforms with stairways, a round drum, the cylindrical observatory tower
        /// with mouldings, an upper drum and a dome.
        /// </summary>
        public static GameObject BuildCaracol()
        {
            var pal = Palette.Load();
            var root = NewRoot(MayanCityBuilder.CaracolName, MayanCityBuilder.CaracolPos, MayanCityBuilder.PlazaCenter);

            var lower = Group("Lower_Platform", root);
            Box("Foundation", lower, new Vector3(0f, -3f, 0f), new Vector3(40f, 3.3f, 34f), pal.Stone);
            Box("Platform", lower, Vector3.zero, new Vector3(36f, 4.4f, 30f), pal.Stone, 0.5f, 0.5f);
            Box("Cornice", lower, new Vector3(0f, 4.4f, 0f), new Vector3(35.6f, 0.4f, 29.6f), pal.Limestone);
            Stairs("Stairway", lower, new Vector3(0f, 0f, -17.75f), new Vector3(11f, 4.8f, 5.5f), 10, pal.Limestone);
            Alfardas(lower, -17.75f, 5.5f, 4.8f, 11f, pal.Stone);

            var upper = Group("Upper_Platform", root);
            Box("Platform", upper, new Vector3(0f, 4.8f, 2f), new Vector3(22f, 3.6f, 20f), pal.Stone, 0.4f, 0.4f);
            Box("Cornice", upper, new Vector3(0f, 8.4f, 2f), new Vector3(21.6f, 0.35f, 19.6f), pal.Limestone);
            Stairs("Stairway", upper, new Vector3(0f, 4.8f, -10f), new Vector3(7f, 3.95f, 4f), 8, pal.Limestone);
            Alfardas(upper.transform, -10f, 4f, 3.95f, 7f, pal.Stone);
            // the alfardas above were placed from y=0; lift them onto the lower platform
            foreach (Transform t in upper) if (t.name.StartsWith("Alfarda")) t.localPosition += new Vector3(0f, 4.8f, 0f);

            var tower = Group("Observatory_Tower", root);
            var c = new Vector3(0f, 8.75f, 3f);
            Cyl("Base_Drum", tower, c, 7.5f, 2.2f, pal.Stone);
            Cyl("Base_Drum_Cornice", tower, c + Vector3.up * 2.2f, 7.8f, 0.3f, pal.Limestone);
            float towerY = c.y + 2.5f;
            Cyl("Tower", tower, new Vector3(c.x, towerY, c.z), 5.6f, 7.5f, pal.Limestone);
            Cyl("Medial_Moulding", tower, new Vector3(c.x, towerY + 5.1f, c.z), 5.9f, 0.4f, pal.Stone);
            Cyl("Top_Cornice", tower, new Vector3(c.x, towerY + 7.5f, c.z), 6.0f, 0.5f, pal.Stone);

            float drumY = towerY + 8f;
            Cyl("Upper_Drum", tower, new Vector3(c.x, drumY, c.z), 3.6f, 2.6f, pal.Limestone);
            Cyl("Drum_Cornice", tower, new Vector3(c.x, drumY + 2.6f, c.z), 3.9f, 0.3f, pal.Stone);
            Dome("Dome", tower, new Vector3(c.x, drumY + 2.9f - 3.3f, c.z), 3.3f, pal.Limestone);

            MayanCityBuilder.Log($"Step 4: El Caracol built at {root.position} ({root.GetComponentsInChildren<ProBuilderMesh>().Length} ProBuilder shapes)");
            return root.gameObject;
        }

        // ------------------------------------------------------------------ city details

        /// <summary>Sacbe (white causeway) linking both buildings across the plaza.</summary>
        public static GameObject BuildCityDetails()
        {
            var pal = Palette.Load();
            MayanCityBuilder.RemoveRoot(MayanCityBuilder.DetailsName);
            var root = new GameObject(MayanCityBuilder.DetailsName).transform;

            Vector2 plaza = MayanCityBuilder.PlazaCenter;
            Vector2 a = MayanCityBuilder.TemplePos + (plaza - MayanCityBuilder.TemplePos).normalized * 37f;
            Vector2 b = MayanCityBuilder.CaracolPos + (plaza - MayanCityBuilder.CaracolPos).normalized * 24f;
            Vector2 mid = (a + b) / 2f;
            var dir = new Vector3(b.x - a.x, 0f, b.y - a.y);
            var ground = MayanCityBuilder.Ground(mid);
            Box("Sacbe_Causeway", root, ground + Vector3.down * 0.2f, new Vector3(6f, 0.6f, dir.magnitude), pal.Limestone,
                0.3f, 0f, Quaternion.LookRotation(dir));

            MayanCityBuilder.Log("Step 5: sacbe causeway built");
            return root.gameObject;
        }

        // ------------------------------------------------------------------ Asset Store characters

        // "Animals FREE - Animated Low Poly 3D Models" (ithappy) and "Robot Humanoid lowpoly" (Quad.Vertex).
        const string TigerPrefab = "Assets/ithappy/Animals_FREE/Prefabs/Tiger_001.prefab";
        const string RobotPrefab = "Assets/Quad.Vertex/Prefab/Robot_grey.prefab";

        // The tiger prefab ships with keyboard player controls; in the scene the tigers only play their Animator.
        static readonly string[] PlayerControlScripts = { "MovePlayerInput", "CreatureMover" };

        /// <summary>
        /// Places the imported Asset Store characters: a group of tigers at the base of El Caracol and one
        /// robot humanoid at the base of the Temple of the Foliated Cross. Each local position is relative to
        /// its building, whose local -Z faces the plaza.
        /// </summary>
        public static GameObject BuildCharacters()
        {
            MayanCityBuilder.RemoveRoot(MayanCityBuilder.PlaceholdersName);
            MayanCityBuilder.RemoveRoot(MayanCityBuilder.CharactersName);
            var root = new GameObject(MayanCityBuilder.CharactersName).transform;

            var caracol = GameObject.Find(MayanCityBuilder.CaracolName);
            var temple = GameObject.Find(MayanCityBuilder.TempleName);
            int tigers = 0, robots = 0;

            if (caracol != null)
            {
                var group = Group("Tigers", root);
                // (local x, local z, yaw relative to the building)
                var spots = new[] { new Vector3(-12f, -23f, 200f), new Vector3(11f, -24.5f, 150f), new Vector3(-24f, -9f, 250f), new Vector3(23.5f, -4f, 100f) };
                foreach (var s in spots)
                {
                    var t = PlaceAsset(TigerPrefab, $"Tiger_{++tigers}", group, caracol.transform, s, 2.6f, false);
                    if (t == null) { tigers--; break; }
                }
            }

            if (temple != null)
            {
                var group = Group("Robots", root);
                // beside the foot of the main stairway, facing the plaza
                if (PlaceAsset(RobotPrefab, "Robot_Humanoid", group, temple.transform, new Vector3(10f, -31f, 180f), 2f, true) != null) robots++;
            }

            MayanCityBuilder.Log($"Step 7: placed {tigers} tigers (Animals FREE) at El Caracol and {robots} Robot Humanoid at the Temple of the Foliated Cross");
            return root.gameObject;
        }

        // Instantiates a prefab next to a building, scales it to a real-world size (length for animals,
        // height for humanoids) and stands it on the terrain.
        static GameObject PlaceAsset(string prefabPath, string name, Transform parent, Transform building, Vector3 local, float size, bool sizeIsHeight)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                MayanCityBuilder.Log("WARNING: missing Asset Store prefab " + prefabPath + " (import the package first)");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.SetParent(parent, false);
            ConvertToUrp(go);
            foreach (var script in PlayerControlScripts)
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && mb.GetType().Name == script) Object.DestroyImmediate(mb);

            var b = WorldBounds(go);
            float current = sizeIsHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            if (current > 0.001f) go.transform.localScale *= size / current;

            Vector3 world = building.TransformPoint(new Vector3(local.x, 0f, local.y));
            go.transform.rotation = building.rotation * Quaternion.Euler(0f, local.z, 0f);
            go.transform.position = MayanCityBuilder.Ground(new Vector2(world.x, world.z));
            go.transform.position += Vector3.up * (go.transform.position.y - WorldBounds(go).min.y);
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // Some packages ship Built-in "Standard" materials, which render pink in URP. Switch them to URP Lit,
        // keeping the albedo, normal and specular maps, the colour and cutout transparency (the same thing
        // Unity's material converter does). Cutout foliage is also made double-sided.
        static void ConvertToUrp(GameObject go)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    bool specular = m.shader.name == "Standard (Specular setup)";
                    if (m.shader.name != "Standard" && !specular) continue;

                    var tex = m.GetTexture("_MainTex");
                    var col = m.GetColor("_Color");
                    bool cutout = m.HasProperty("_Mode") && Mathf.RoundToInt(m.GetFloat("_Mode")) == 1;
                    float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;

                    m.shader = lit;
                    m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", col);
                    if (m.GetTexture("_BumpMap") != null) m.EnableKeyword("_NORMALMAP");
                    if (specular)
                    {
                        m.SetFloat("_WorkflowMode", 0f);
                        m.EnableKeyword("_SPECULAR_SETUP");
                        if (m.GetTexture("_SpecGlossMap") != null) m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                    if (cutout)
                    {
                        m.SetFloat("_AlphaClip", 1f);
                        m.SetFloat("_Cutoff", cutoff);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.SetOverrideTag("RenderType", "TransparentCutout");
                        m.renderQueue = 2450; // AlphaTest
                        m.SetFloat("_Cull", 0f); // leaves are visible from both sides
                        m.doubleSidedGI = true;
                    }
                    EditorUtility.SetDirty(m);
                    MayanCityBuilder.Log($"Converted material {m.name} to URP Lit");
                }
        }

        // ------------------------------------------------------------------ Asset Store plants

        // "Splash of Color - Unique Photogrammetry Plants". The plain medium-resolution prefabs are used because the
        // LOD versions swap to billboards that only face the camera in Play mode (they look broken in the editor).
        const string PlantFolder = "Assets/Splash of Color - Unique Photogrammetry Plants/Prefabs/Medium Resolution Prefabs";

        /// <summary>
        /// Scatters whole plants from the photogrammetry pack in clusters across the valley and up the gentler
        /// mountain slopes, keeping them off the plaza, the buildings and the trails.
        /// </summary>
        public static GameObject BuildPlants()
        {
            MayanCityBuilder.RemoveRoot(MayanCityBuilder.PlantsName);
            var terrain = MayanCityBuilder.FindTerrain();
            if (terrain == null) return null;
            var td = terrain.terrainData;

            // Whole plants only: skip the single-leaf, flower and hanging-vine pieces.
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PlantFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !System.IO.Path.GetFileNameWithoutExtension(p).Contains("Leaf") && !p.Contains("Flower") && !p.Contains("Hanging Luna Vine"))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null)
                .ToArray();
            if (prefabs.Length == 0)
            {
                MayanCityBuilder.Log("WARNING: no plant prefabs found in " + PlantFolder + " (import the package first)");
                return null;
            }

            var root = new GameObject(MayanCityBuilder.PlantsName).transform;
            var rnd = new System.Random(7);
            var size = MayanCityBuilder.TerrainSize;
            const int target = 450;
            int placed = 0;
            for (int attempt = 0; attempt < 20000 && placed < target; attempt++)
            {
                float u = 0.03f + 0.94f * (float)rnd.NextDouble(), v = 0.03f + 0.94f * (float)rnd.NextDouble();
                var p = new Vector2(u * size.x, v * size.z);
                if (MayanCityBuilder.PathDistance(p) < 5f || td.GetSteepness(u, v) > 35f) continue;
                if (MayanCityBuilder.PlazaWeight(p, 0.5f) > 0.02f) continue;
                // clusters: plants grow thick in some patches and thin out in others
                if (Mathf.PerlinNoise(p.x * 0.02f + 5f, p.y * 0.02f + 9f) < (float)rnd.NextDouble() * 0.9f) continue;

                var prefab = prefabs[rnd.Next(prefabs.Length)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = prefab.name;
                go.transform.SetParent(root, false);
                ConvertToUrp(go);

                // keep each plant's own shape; scale so its largest side is 3 - 6.5 m (large jungle ferns and bushes)
                var b = WorldBounds(go).size;
                float largest = Mathf.Max(b.x, Mathf.Max(b.y, b.z));
                float wanted = 3f + 3.5f * (float)rnd.NextDouble();
                if (largest > 0.001f) go.transform.localScale *= wanted / largest;
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                go.transform.position = MayanCityBuilder.Ground(p) + Vector3.down * 0.1f;
                placed++;
            }

            MayanCityBuilder.Log($"Step 6: scattered {placed} plants ({prefabs.Length} prefab variants, Splash of Color - Unique Photogrammetry Plants)");
            return root.gameObject;
        }
    }
}
