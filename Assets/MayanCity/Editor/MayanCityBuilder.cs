using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MayanCityTools
{
    /// <summary>
    /// Builds the "small ancient Mayan city" scene for Challenge 02 (CCOM4302):
    /// height-map terrain (33x33), painted terrain layers, grass details, jungle trees,
    /// ProBuilder buildings (Temple of the Foliated Cross, El Caracol) and placeholder characters.
    /// Every step is also available on its own under the "Mayan City" menu.
    /// </summary>
    public static class MayanCityBuilder
    {
        public const string ScenePath = "Assets/Scenes/MayanCity.unity";
        public const string HeightmapDir = "Assets/MayanCity/Heightmap";
        const string TerrainDataPath = MayanTextures.Root + "/MayanTerrainData.asset";
        const string LayerDir = MayanTextures.Root + "/TerrainLayers";
        public const string PrefabDir = MayanTextures.Root + "/Prefabs";
        public const string ScreenshotDir = "Docs/Screenshots";

        public const int HeightmapResolution = 33;
        public static readonly Vector3 TerrainSize = new Vector3(400f, 150f, 400f);

        // City layout in world XZ (terrain sits at the origin).
        public static readonly Rect Plateau = new Rect(125f, 125f, 160f, 155f);
        const float PlateauBlend = 30f;
        public static readonly Vector2 PlazaCenter = new Vector2(210f, 200f);
        public static readonly Vector2 TemplePos = new Vector2(170f, 240f);
        public static readonly Vector2 CaracolPos = new Vector2(250f, 165f);

        public const string TerrainName = "MayanTerrain";
        public const string TempleName = "Temple_of_the_Foliated_Cross";
        public const string CaracolName = "El_Caracol_Observatory";
        public const string DetailsName = "City_Details";
        public const string PlaceholdersName = "Placeholders_ReplaceWithAssetStore";

        // ------------------------------------------------------------------ menu

        [MenuItem("Mayan City/Build Everything", priority = 0)]
        static void BuildEverythingMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildEverything();
        }

        [MenuItem("Mayan City/Steps/1 - Create Terrain From Height Map (33x33)", priority = 20)]
        static void Step1() { EnsureScene(); BuildTerrain(); SaveScene(); }

        [MenuItem("Mayan City/Steps/2 - Sculpt City Plateau + Paint Terrain Layers", priority = 21)]
        static void Step2() { EnsureScene(); SculptAndPaint(); SaveScene(); }

        [MenuItem("Mayan City/Steps/3 - Build Temple of the Foliated Cross", priority = 22)]
        static void Step3() { EnsureScene(); MayanStructures.BuildTemple(); SaveScene(); }

        [MenuItem("Mayan City/Steps/4 - Build El Caracol Observatory", priority = 23)]
        static void Step4() { EnsureScene(); MayanStructures.BuildCaracol(); SaveScene(); }

        [MenuItem("Mayan City/Steps/5 - Build City Details (sacbe, stelae, huts)", priority = 24)]
        static void Step5() { EnsureScene(); MayanStructures.BuildCityDetails(); SaveScene(); }

        [MenuItem("Mayan City/Steps/6 - Paint Trees + Grass Details", priority = 25)]
        static void Step6() { EnsureScene(); PlaceVegetation(); SaveScene(); }

        [MenuItem("Mayan City/Steps/7 - Placeholder People + Animals", priority = 26)]
        static void Step7() { EnsureScene(); MayanStructures.BuildPlaceholders(); SaveScene(); }

        [MenuItem("Mayan City/Steps/8 - Lighting, Sky, Fog + Camera", priority = 27)]
        static void Step8() { EnsureScene(); SetupLighting(); SaveScene(); }

        [MenuItem("Mayan City/Capture Screenshots (Docs/Screenshots)", priority = 40)]
        public static void CaptureScreenshots()
        {
            EnsureScene();
            Directory.CreateDirectory(ScreenshotDir);
            const int w = 1920, h = 1080;
            var go = new GameObject("__ShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 2500f;
            try
            {
                foreach (var view in Views())
                {
                    cam.transform.position = view.pos;
                    cam.transform.LookAt(view.target);
                    cam.fieldOfView = view.fov;
                    bool fog = RenderSettings.fog;
                    if (view.name.Contains("top_down")) RenderSettings.fog = false; // a clear map view for the write-up
                    var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
                    cam.targetTexture = rt;
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                    else cam.Render();
                    RenderSettings.fog = fog;

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);

                    string file = $"{ScreenshotDir}/{view.name}.png";
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    Log("Screenshot saved: " + file);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------ orchestration

        public static void BuildEverything()
        {
            var start = DateTime.Now;
            EnsureScene();
            BuildTerrain();
            SculptAndPaint();
            MayanStructures.BuildTemple();
            MayanStructures.BuildCaracol();
            MayanStructures.BuildCityDetails();
            PlaceVegetation();
            MayanStructures.BuildPlaceholders();
            SetupLighting();
            SaveScene();
            FrameSceneView();
            Log($"Build Everything finished in {(DateTime.Now - start).TotalSeconds:0.0}s");
        }

        public static void EnsureScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath) return;
            EnsureFolder("Assets/Scenes");
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Log("Created scene " + ScenePath);
        }

        public static void SaveScene()
        {
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void RemoveRoot(string name)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) Object.DestroyImmediate(go);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static string LogPath => Path.GetFullPath("mayan_log.txt");

        public static void Log(string msg)
        {
            Debug.Log("[MayanCity] " + msg);
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { /* log file is optional */ }
        }

        // ------------------------------------------------------------------ terrain

        public static Terrain FindTerrain()
        {
            var go = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == TerrainName);
            return go != null ? go.GetComponent<Terrain>() : null;
        }

        public static float GroundY(float x, float z)
        {
            var t = FindTerrain();
            return t == null ? 0f : t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        public static Vector3 Ground(Vector2 xz) => new Vector3(xz.x, GroundY(xz.x, xz.y), xz.y);

        /// <summary>Step 1: import the RAW height map (or a procedural fallback) and reduce it to 33x33.</summary>
        public static Terrain BuildTerrain()
        {
            EnsureFolder(MayanTextures.Root);
            EnsureFolder(HeightmapDir);

            float[,] src = LoadRawHeightmap(out string source);
            if (src == null)
            {
                src = ProceduralHeights(257);
                source = "procedural jungle height map (no RAW file found in " + HeightmapDir + ")";
            }
            var heights = Resample(src, HeightmapResolution);

            RemoveRoot(TerrainName);
            AssetDatabase.DeleteAsset(TerrainDataPath);

            var td = new TerrainData();
            td.heightmapResolution = HeightmapResolution;
            td.size = TerrainSize;
            td.alphamapResolution = 256;
            td.baseMapResolution = 512;
            td.SetDetailResolution(256, 16);
            td.SetHeights(0, 0, heights);
            AssetDatabase.CreateAsset(td, TerrainDataPath);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = TerrainName;
            var terrain = go.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 1000f;
            terrain.detailObjectDistance = 160f;
            terrain.detailObjectDensity = 1f;
            terrain.treeDistance = 2000f;
            terrain.treeBillboardDistance = 2000f;

            Log($"Step 1: terrain created from {source}; heightmap resolution {td.heightmapResolution}x{td.heightmapResolution}, size {TerrainSize}");
            return terrain;
        }

        static float[,] LoadRawHeightmap(out string source)
        {
            source = null;
            string dir = Path.GetFullPath(HeightmapDir);
            if (!Directory.Exists(dir)) return null;
            string file = Directory.GetFiles(dir).FirstOrDefault(f =>
                f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".r16", StringComparison.OrdinalIgnoreCase));
            if (file == null) return null;

            byte[] bytes = File.ReadAllBytes(file);
            int s16 = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2f));
            int s8 = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length));
            float[,] result;
            if (s16 * s16 * 2 == bytes.Length)
            {
                // 16-bit: pick the byte order (Windows vs Mac) that gives the smoother surface.
                var little = Read16(bytes, s16, false);
                var big = Read16(bytes, s16, true);
                bool useBig = Roughness(big) < Roughness(little);
                result = useBig ? big : little;
                source = $"RAW '{Path.GetFileName(file)}' (16-bit {(useBig ? "Mac" : "Windows")} byte order, {s16}x{s16})";
            }
            else if (s8 * s8 == bytes.Length)
            {
                result = new float[s8, s8];
                for (int y = 0; y < s8; y++)
                    for (int x = 0; x < s8; x++)
                        result[y, x] = bytes[y * s8 + x] / 255f;
                source = $"RAW '{Path.GetFileName(file)}' (8-bit, {s8}x{s8})";
            }
            else
            {
                Log($"WARNING: '{Path.GetFileName(file)}' is not a square 8/16-bit RAW ({bytes.Length} bytes); using procedural heights.");
                return null;
            }
            return result;
        }

        static float[,] Read16(byte[] b, int n, bool bigEndian)
        {
            var h = new float[n, n];
            for (int i = 0; i < n * n; i++)
            {
                int v = bigEndian ? (b[2 * i] << 8) | b[2 * i + 1] : b[2 * i] | (b[2 * i + 1] << 8);
                h[i / n, i % n] = v / 65535f;
            }
            return h;
        }

        static float Roughness(float[,] h)
        {
            int n = h.GetLength(0);
            double sum = 0;
            for (int y = 0; y < n; y++)
                for (int x = 1; x < n; x++)
                    sum += Mathf.Abs(h[y, x] - h[y, x - 1]);
            return (float)sum;
        }

        // A valley enclosed by a thin band of mountain ranges along the borders, with the highest peaks at the corners.
        static float[,] ProceduralHeights(int n)
        {
            var h = new float[n, n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f), v = y / (n - 1f);

                    // gentle rolling valley floor
                    float floor = 0.5f * Mathf.PerlinNoise(u * 3f + 13.7f, v * 3f + 4.1f) + 0.5f * Mathf.PerlinNoise(u * 7f + 2.3f, v * 7f + 9.9f);

                    // ridged noise gives sharp crests; a slow noise varies the height of each range
                    float ridge = 0f, amp = 0.6f, freq = 5f;
                    for (int o = 0; o < 3; o++)
                    {
                        float r = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(u * freq + 31.1f + o * 7f, v * freq + 17.3f) - 1f);
                        ridge += amp * r * r;
                        amp *= 0.45f;
                        freq *= 2.1f;
                    }
                    float rangeHeight = Mathf.Lerp(0.45f, 1f, Mathf.PerlinNoise(u * 2.5f + 71f, v * 2.5f + 5f));

                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float band = 1f - Smooth(0.02f, 0.15f, edge);
                    float cu = 1f - Mathf.Min(u, 1f - u) * 2f, cv = 1f - Mathf.Min(v, 1f - v) * 2f;
                    float corner = Mathf.Pow(cu * cv, 3f);

                    float mountains = band * (0.25f + 0.55f * ridge * rangeHeight) + corner * (0.25f + 0.35f * ridge);
                    h[y, x] = Mathf.Clamp01(0.04f + 0.04f * floor + mountains);
                }
            return h;
        }

        static float[,] Resample(float[,] src, int n)
        {
            int sy = src.GetLength(0), sx = src.GetLength(1);
            var dst = new float[n, n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x / (n - 1f) * (sx - 1), fy = y / (n - 1f) * (sy - 1);
                    int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                    int x1 = Mathf.Min(x0 + 1, sx - 1), y1 = Mathf.Min(y0 + 1, sy - 1);
                    float tx = fx - x0, ty = fy - y0;
                    dst[y, x] = Mathf.Lerp(Mathf.Lerp(src[y0, x0], src[y0, x1], tx), Mathf.Lerp(src[y1, x0], src[y1, x1], tx), ty);
                }
            return dst;
        }

        static float PlateauDistance(Vector2 p)
        {
            float dx = Mathf.Max(Plateau.xMin - p.x, 0f, p.x - Plateau.xMax);
            float dz = Mathf.Max(Plateau.yMin - p.y, 0f, p.y - Plateau.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool InPlateau(Vector2 p, float margin) => PlateauDistance(p) <= margin;

        static void FlattenPlateau(float[,] h)
        {
            int n = h.GetLength(0);
            float sum = 0f;
            int count = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (PlateauDistance(CellToWorld(x, y, n)) <= 0f) { sum += h[y, x]; count++; }
            float target = count > 0 ? sum / count : 0.2f;

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = PlateauDistance(CellToWorld(x, y, n));
                    float t = 1f - Smooth(0f, PlateauBlend, d);
                    h[y, x] = Mathf.Lerp(h[y, x], target, t);
                }
        }

        static Vector2 CellToWorld(int x, int y, int n) => new Vector2(x / (n - 1f) * TerrainSize.x, y / (n - 1f) * TerrainSize.z);

        // Trails that leave the city through the jungle (plus the plaza links).
        static readonly Vector2[][] Paths =
        {
            new[] { new Vector2(210f, 200f), new Vector2(196f, 120f), new Vector2(172f, 60f), new Vector2(180f, 0f) },
            new[] { new Vector2(210f, 200f), new Vector2(300f, 215f), new Vector2(350f, 250f), new Vector2(400f, 262f) },
            new[] { new Vector2(210f, 200f), new Vector2(120f, 175f), new Vector2(60f, 190f), new Vector2(0f, 170f) },
        };

        public static float PathDistance(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var line in Paths)
                for (int i = 0; i < line.Length - 1; i++)
                {
                    Vector2 a = line[i], b = line[i + 1], ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
            return best;
        }

        /// <summary>Step 2: sculpt the flat city plateau and paint five terrain layers.</summary>
        public static void SculptAndPaint()
        {
            var terrain = FindTerrain() ?? BuildTerrain();
            var td = terrain.terrainData;

            int r = td.heightmapResolution;
            var h = td.GetHeights(0, 0, r, r);
            if (r != HeightmapResolution)
            {
                // A RAW imported by hand through Terrain Settings: reduce it to 33x33 as the assignment asks.
                h = Resample(h, HeightmapResolution);
                td.heightmapResolution = HeightmapResolution;
                td.size = TerrainSize;
                Log($"Step 2: resized hand-imported height map from {r}x{r} to 33x33");
            }
            FlattenPlateau(h);
            td.SetHeights(0, 0, h);

            var layers = new[]
            {
                Layer("L0_JungleGrass", MayanTextures.GroundGrass(), 12f),
                Layer("L1_JungleFloor", MayanTextures.JungleFloor(), 9f),
                Layer("L2_DirtTrail", MayanTextures.Dirt(), 6f),
                Layer("L3_Rock", MayanTextures.Rock(), 14f),
                Layer("L4_PlazaLimestone", MayanTextures.PlazaStone(), 8f),
            };
            td.terrainLayers = layers;

            int ar = td.alphamapResolution;
            var alpha = new float[ar, ar, layers.Length];
            for (int y = 0; y < ar; y++)
                for (int x = 0; x < ar; x++)
                {
                    float u = x / (ar - 1f), v = y / (ar - 1f);
                    var p = new Vector2(u * TerrainSize.x, v * TerrainSize.z);
                    float n = Mathf.PerlinNoise(p.x * 0.03f, p.y * 0.03f);
                    float steep = td.GetSteepness(u, v);

                    float plaza = PlazaWeight(p, n);
                    float trail = 1f - Smooth(3f, 6.5f, PathDistance(p) + (n - 0.5f) * 3f);
                    float hN = td.GetInterpolatedHeight(u, v) / TerrainSize.y;
                    // grassy foothills, rock on the steeper upper slopes and peaks
                    float rock = Mathf.Max(Smooth(32f, 45f, steep + (n - 0.5f) * 10f), Smooth(0.30f, 0.50f, hN + (n - 0.5f) * 0.12f));
                    float floor = Smooth(0.35f, 0.75f, Mathf.PerlinNoise(p.x * 0.015f + 50f, p.y * 0.015f + 50f));

                    float rem = 1f;
                    float wPlaza = plaza; rem -= wPlaza;
                    float wTrail = trail * rem; rem -= wTrail;
                    float wRock = rock * rem; rem -= wRock;
                    float wFloor = floor * 0.8f * rem; rem -= wFloor;

                    alpha[y, x, 0] = rem;
                    alpha[y, x, 1] = wFloor;
                    alpha[y, x, 2] = wTrail;
                    alpha[y, x, 3] = wRock;
                    alpha[y, x, 4] = wPlaza;
                }
            td.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(td);
            Log("Step 2: plateau flattened, 5 terrain layers painted (grass, jungle floor, dirt trail, rock, plaza limestone)");
        }

        // Paved limestone around the buildings, the central plaza and along the causeway; grass elsewhere.
        static float PlazaWeight(Vector2 p, float n)
        {
            Vector2 a = TemplePos, b = CaracolPos, ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = Mathf.Min(
                Vector2.Distance(p, a + ab * t) - 12f,
                Mathf.Min(Vector2.Distance(p, TemplePos) - 36f,
                Mathf.Min(Vector2.Distance(p, CaracolPos) - 30f, Vector2.Distance(p, PlazaCenter) - 26f)));
            float edge = 1f - Smooth(0f, 9f, d + (n - 0.5f) * 10f);
            float overgrown = Mathf.Lerp(0.6f, 1f, Smooth(0.3f, 0.6f, Mathf.PerlinNoise(p.x * 0.07f + 40f, p.y * 0.07f + 9f)));
            return edge * overgrown;
        }

        static TerrainLayer Layer(string name, Texture2D tex, float tile)
        {
            EnsureFolder(LayerDir);
            string path = $"{LayerDir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // ------------------------------------------------------------------ vegetation

        /// <summary>Step 6: Paint Details (grass brush). Trees are left for Asset Store models.</summary>
        public static void PlaceVegetation()
        {
            var terrain = FindTerrain();
            if (terrain == null) { BuildTerrain(); SculptAndPaint(); terrain = FindTerrain(); }
            var td = terrain.terrainData;

            td.SetTreeInstances(new TreeInstance[0], false);
            td.treePrototypes = new TreePrototype[0];
            if (AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.DeleteAsset(PrefabDir); // old generated tree prefabs

            var grass = new DetailPrototype
            {
                prototypeTexture = MayanTextures.GrassBlades(),
                renderMode = DetailRenderMode.Grass,
                usePrototypeMesh = false,
                healthyColor = new Color(0.50f, 0.72f, 0.32f),
                dryColor = new Color(0.62f, 0.60f, 0.30f),
                minWidth = 0.8f,
                maxWidth = 1.6f,
                minHeight = 0.5f,
                maxHeight = 1.2f,
                noiseSpread = 0.3f,
            };
            td.detailPrototypes = new[] { grass };

            int dr = td.detailResolution;
            var map = new int[dr, dr];
            for (int y = 0; y < dr; y++)
                for (int x = 0; x < dr; x++)
                {
                    float u = (x + 0.5f) / dr, v = (y + 0.5f) / dr;
                    var p = new Vector2(u * TerrainSize.x, v * TerrainSize.z);
                    if (PathDistance(p) < 4f || td.GetSteepness(u, v) > 28f) continue;
                    if (PlazaWeight(p, Mathf.PerlinNoise(p.x * 0.03f, p.y * 0.03f)) > 0.3f) continue;
                    float n = Mathf.PerlinNoise(p.x * 0.05f + 21f, p.y * 0.05f + 12f);
                    map[y, x] = n < 0.3f ? 0 : Mathf.RoundToInt(2f + n * 6f);
                }
            td.SetDetailLayer(0, 0, 0, map);
            EditorUtility.SetDirty(td);
            Log($"Step 6: painted grass details ({dr}x{dr} detail map); no trees");
        }

        // ------------------------------------------------------------------ lighting & camera

        /// <summary>Step 8: sun, procedural sky, humid-jungle fog and the main camera.</summary>
        public static void SetupLighting()
        {
            RemoveRoot("Sun");
            RemoveRoot("Main Camera");

            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.82f);
            light.intensity = 1.4f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, 330f, 0f);

            RenderSettings.sun = light;
            RenderSettings.skybox = MayanTextures.Skybox();
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.70f, 0.80f, 0.78f);
            RenderSettings.fogDensity = 0.0016f;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 2500f;
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            var view = Views()[0];
            camGo.transform.position = view.pos;
            camGo.transform.LookAt(view.target);

            DynamicGI.UpdateEnvironment();
            Log("Step 8: sun, procedural sky, fog and main camera configured");
        }

        public struct View { public string name; public Vector3 pos, target; public float fov; }

        public static Vector3 FrontDir(Vector2 from, Vector2 to)
        {
            var d = (to - from).normalized;
            return new Vector3(d.x, 0f, d.y);
        }

        public static List<View> Views()
        {
            Vector3 plaza = Ground(PlazaCenter), temple = Ground(TemplePos), caracol = Ground(CaracolPos);
            Vector3 fT = FrontDir(TemplePos, PlazaCenter), fC = FrontDir(CaracolPos, PlazaCenter);
            Vector3 up = Vector3.up;
            return new List<View>
            {
                new View { name = "01_overview", pos = plaza + new Vector3(-130f, 70f, -120f), target = plaza + up * 8f, fov = 55f },
                new View { name = "02_temple_foliated_cross", pos = temple + fT * 58f + Vector3.Cross(up, fT) * 16f + up * 18f, target = temple + up * 13f, fov = 50f },
                new View { name = "03_el_caracol", pos = caracol + fC * 52f - Vector3.Cross(up, fC) * 14f + up * 14f, target = caracol + up * 11f, fov = 50f },
                new View { name = "04_terrain_top_down", pos = new Vector3(200f, 520f, 199f), target = new Vector3(200f, 0f, 200f), fov = 50f },
                new View { name = "05_plaza_eye_level", pos = plaza - fT * 22f + up * 1.7f, target = temple + up * 14f, fov = 60f },
                new View { name = "06_valley_from_the_mountains", pos = Ground(new Vector2(45f, 350f)) + up * 6f, target = plaza + up * 10f, fov = 55f },
            };
        }

        static void FrameSceneView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            var view = Views()[0];
            sv.LookAt(view.target, Quaternion.LookRotation(view.target - view.pos), 170f);
            sv.Repaint();
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
    }
}
