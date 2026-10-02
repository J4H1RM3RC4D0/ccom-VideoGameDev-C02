using System.IO;
using UnityEditor;
using UnityEngine;

namespace MayanCityTools
{
    /// <summary>
    /// Procedurally generates the seamless textures and URP materials used by the Mayan city scene,
    /// so the project does not depend on any downloaded texture pack.
    /// </summary>
    public static class MayanTextures
    {
        public const string Root = "Assets/MayanCity/Generated";
        public const string TexDir = Root + "/Textures";
        public const string MatDir = Root + "/Materials";

        delegate Color PixelFn(float u, float v);

        // ------------------------------------------------------------------ noise

        static int Mod(int a, int m) => ((a % m) + m) % m;

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665 + 1013904223);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        // Value noise that repeats every `period` cells, so textures tile without seams.
        static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = Mod(x0, period), xb = Mod(x0 + 1, period);
            int ya = Mod(y0, period), yb = Mod(y0 + 1, period);
            float a = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
            float b = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        public static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(u * p, v * p, p, seed + o * 31);
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        // Cut stone blocks laid in staggered courses, like Mayan masonry.
        static Color Blocks(float u, float v, int rows, int blocksPerRow, Color light, Color dark, Color mortar, int seed)
        {
            int r = Mathf.FloorToInt(v * rows);
            float fv = v * rows - r;
            float bu = u * blocksPerRow + ((r & 1) == 1 ? 0.5f : 0f);
            int b = Mathf.FloorToInt(bu);
            float fu = bu - b;
            int bi = Mod(b, blocksPerRow);

            float du = Mathf.Min(fu, 1f - fu) / blocksPerRow;
            float dv = Mathf.Min(fv, 1f - fv) / rows;
            float d = Mathf.Min(du, dv);

            float jitter = Hash(bi, Mod(r, rows), seed);
            float n = Fbm(u, v, 8, 4, seed);
            float grain = Fbm(u, v, 64, 2, seed + 5);
            Color c = Mix(dark, light, 0.25f + 0.45f * jitter + 0.5f * (n - 0.5f) + 0.2f * (grain - 0.5f));

            const float m = 0.012f;
            c *= Mathf.Lerp(0.72f, 1f, Smooth(m, m * 3.5f, d));
            if (d < m) c = Mix(mortar, c, 0.25f * n);
            c.a = 1f;
            return c;
        }

        // ------------------------------------------------------------------ texture assets

        static Texture2D GetOrCreate(string name, int size, PixelFn fn)
        {
            string path = $"{TexDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = fn(x / (float)size, y / (float)size);
            return WritePng(path, size, px, false);
        }

        static Texture2D WritePng(string path, int size, Color[] px, bool alpha)
        {
            MayanCityBuilder.EnsureFolder(TexDir);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = alpha;
            imp.isReadable = alpha;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Building stone: weathered grey-beige limestone blocks.
        public static Texture2D StoneBlocks() => GetOrCreate("Stone_Blocks", 512, (u, v) =>
        {
            Color c = Blocks(u, v, 4, 2, new Color(0.80f, 0.76f, 0.66f), new Color(0.52f, 0.50f, 0.45f), new Color(0.28f, 0.27f, 0.24f), 11);
            float moss = Smooth(0.58f, 0.75f, Fbm(u, v, 4, 4, 77));
            return Mix(c, new Color(0.30f, 0.36f, 0.22f), moss * 0.45f);
        });

        // Lighter, cleaner limestone for temple walls and cornices.
        public static Texture2D Limestone() => GetOrCreate("Limestone_Blocks", 512, (u, v) =>
            Blocks(u, v, 4, 2, new Color(0.93f, 0.90f, 0.82f), new Color(0.72f, 0.69f, 0.62f), new Color(0.45f, 0.43f, 0.38f), 23));

        // Red-painted stucco (Palenque temples were originally painted red).
        public static Texture2D Stucco() => GetOrCreate("Stucco_Red", 256, (u, v) =>
        {
            float n = Fbm(u, v, 6, 5, 31);
            float worn = Smooth(0.55f, 0.72f, Fbm(u, v, 3, 4, 32));
            Color red = Mix(new Color(0.55f, 0.20f, 0.14f), new Color(0.70f, 0.30f, 0.20f), n);
            return Mix(red, new Color(0.82f, 0.78f, 0.70f), worn * 0.7f);
        });

        public static Texture2D Thatch() => GetOrCreate("Thatch", 256, (u, v) =>
        {
            float streak = Fbm(u * 1f, v * 0.08f + u * 0.02f, 32, 3, 41);
            float n = Fbm(u, v, 8, 3, 42);
            return Mix(new Color(0.42f, 0.33f, 0.18f), new Color(0.78f, 0.66f, 0.40f), streak * 0.8f + n * 0.3f);
        });

        public static Texture2D Bark() => GetOrCreate("Bark", 256, (u, v) =>
        {
            float streak = Fbm(u, v * 0.1f, 32, 3, 51);
            float n = Fbm(u, v, 8, 3, 52);
            return Mix(new Color(0.22f, 0.17f, 0.12f), new Color(0.50f, 0.44f, 0.36f), streak * 0.8f + n * 0.3f);
        });

        public static Texture2D Leaves() => GetOrCreate("Leaves", 256, (u, v) =>
        {
            float n = Fbm(u, v, 16, 4, 61);
            float spots = Smooth(0.6f, 0.8f, Fbm(u, v, 32, 2, 62));
            return Mix(Mix(new Color(0.10f, 0.25f, 0.08f), new Color(0.25f, 0.45f, 0.14f), n), new Color(0.40f, 0.55f, 0.20f), spots * 0.5f);
        });

        // ---- terrain layers

        public static Texture2D GroundGrass() => GetOrCreate("Terrain_JungleGrass", 512, (u, v) =>
        {
            float n = Fbm(u, v, 4, 5, 101);
            float blades = Fbm(u, v, 128, 2, 102);
            Color c = Mix(new Color(0.16f, 0.30f, 0.08f), new Color(0.34f, 0.48f, 0.15f), n * 1.2f - 0.1f);
            return Mix(c, new Color(0.48f, 0.58f, 0.24f), Smooth(0.62f, 0.8f, blades) * 0.6f);
        });

        public static Texture2D JungleFloor() => GetOrCreate("Terrain_JungleFloor", 512, (u, v) =>
        {
            float n = Fbm(u, v, 6, 5, 111);
            float litter = Fbm(u, v, 64, 3, 112);
            Color soil = Mix(new Color(0.18f, 0.13f, 0.08f), new Color(0.30f, 0.23f, 0.13f), n);
            Color leaves = Mix(new Color(0.35f, 0.24f, 0.10f), new Color(0.20f, 0.30f, 0.10f), Fbm(u, v, 16, 2, 113));
            return Mix(soil, leaves, Smooth(0.5f, 0.7f, litter));
        });

        public static Texture2D Dirt() => GetOrCreate("Terrain_Dirt", 512, (u, v) =>
        {
            float n = Fbm(u, v, 8, 5, 121);
            float pebbles = Smooth(0.72f, 0.82f, Fbm(u, v, 64, 2, 122));
            Color c = Mix(new Color(0.36f, 0.26f, 0.16f), new Color(0.58f, 0.45f, 0.30f), n);
            return Mix(c, new Color(0.66f, 0.62f, 0.55f), pebbles * 0.7f);
        });

        public static Texture2D Rock() => GetOrCreate("Terrain_Rock", 512, (u, v) =>
        {
            float n = Fbm(u, v, 4, 6, 131);
            float ridge = 1f - Mathf.Abs(2f * Fbm(u, v, 6, 3, 132) - 1f);
            float cracks = Smooth(0.92f, 0.985f, ridge);
            Color c = Mix(new Color(0.30f, 0.29f, 0.27f), new Color(0.62f, 0.60f, 0.55f), n * 1.3f - 0.15f);
            return Mix(c, new Color(0.12f, 0.12f, 0.11f), cracks * 0.8f);
        });

        public static Texture2D PlazaStone() => GetOrCreate("Terrain_PlazaLimestone", 512, (u, v) =>
        {
            Color c = Blocks(u, v, 4, 4, new Color(0.84f, 0.80f, 0.70f), new Color(0.62f, 0.58f, 0.50f), new Color(0.35f, 0.33f, 0.28f), 141);
            float grime = Smooth(0.5f, 0.8f, Fbm(u, v, 4, 4, 142));
            return Mix(c, new Color(0.35f, 0.38f, 0.25f), grime * 0.35f);
        });

        // Alpha-cut grass blades for the Paint Details grass brush.
        public static Texture2D GrassBlades()
        {
            const string path = TexDir + "/Detail_GrassBlades.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            var px = new Color[size * size];
            var rnd = new System.Random(7);
            for (int b = 0; b < 22; b++)
            {
                float x0 = 8f + (float)rnd.NextDouble() * 112f;
                float h = 60f + (float)rnd.NextDouble() * 66f;
                float w = 4f + (float)rnd.NextDouble() * 5f;
                float bend = ((float)rnd.NextDouble() - 0.5f) * 36f;
                Color c0 = new Color(0.12f, 0.28f, 0.06f), c1 = new Color(0.55f, 0.72f, 0.30f);
                for (int y = 0; y < (int)h && y < size; y++)
                {
                    float t = y / h;
                    float cx = x0 + bend * t * t;
                    float half = w * (1f - t) * 0.5f + 0.5f;
                    for (int x = Mathf.FloorToInt(cx - half); x <= Mathf.CeilToInt(cx + half); x++)
                    {
                        if (x < 0 || x >= size) continue;
                        Color c = Color.Lerp(c0, c1, t);
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
                }
            }
            return WritePng(path, size, px, true);
        }

        // ------------------------------------------------------------------ materials

        public static Material Mat(string name, Color color, Texture2D tex = null, float tiling = 1f, float smoothness = 0.12f)
        {
            MayanCityBuilder.EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", Vector2.one * tiling);
            }
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Skybox()
        {
            MayanCityBuilder.EnsureFolder(MatDir);
            string path = $"{MatDir}/Sky_Jungle.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_SkyTint", new Color(0.45f, 0.55f, 0.65f));
            m.SetColor("_GroundColor", new Color(0.62f, 0.72f, 0.70f));
            m.SetFloat("_AtmosphereThickness", 1.15f);
            m.SetFloat("_Exposure", 1.25f);
            m.SetFloat("_SunSize", 0.04f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }

    /// <summary>Shared material palette, created on demand.</summary>
    public class Palette
    {
        public Material Stone, Limestone, Stucco, Dark, Carving, Thatch, Whitewash, Bark, Leaves, LeavesLight, Wood;
        public Material Skin, ClothWhite, ClothRed, ClothBlue, Feather, Jaguar, JaguarSpots, Deer, MacawRed, MacawBlue, MacawYellow, Monkey;

        public static Palette Load()
        {
            return new Palette
            {
                Stone = MayanTextures.Mat("Stone_Weathered", Color.white, MayanTextures.StoneBlocks(), 0.5f),
                Limestone = MayanTextures.Mat("Limestone", Color.white, MayanTextures.Limestone(), 0.5f),
                Stucco = MayanTextures.Mat("Stucco_Red", Color.white, MayanTextures.Stucco(), 0.35f),
                Dark = MayanTextures.Mat("Opening_Dark", new Color(0.05f, 0.045f, 0.04f), null, 1f, 0f),
                Carving = MayanTextures.Mat("Carving_Tablet", new Color(0.62f, 0.64f, 0.56f), MayanTextures.Limestone(), 1.5f),
                Thatch = MayanTextures.Mat("Thatch", Color.white, MayanTextures.Thatch(), 0.6f, 0.05f),
                Whitewash = MayanTextures.Mat("Whitewash", new Color(0.92f, 0.88f, 0.80f), MayanTextures.Stucco(), 0.2f, 0.05f),
                Bark = MayanTextures.Mat("Bark", Color.white, MayanTextures.Bark(), 1f, 0.05f),
                Leaves = MayanTextures.Mat("Leaves_Dark", new Color(0.85f, 0.95f, 0.85f), MayanTextures.Leaves(), 1f, 0.2f),
                LeavesLight = MayanTextures.Mat("Leaves_Palm", new Color(1.1f, 1.15f, 0.9f), MayanTextures.Leaves(), 1f, 0.25f),
                Wood = MayanTextures.Mat("Wood", new Color(0.45f, 0.33f, 0.22f), MayanTextures.Bark(), 1f, 0.1f),
                Skin = MayanTextures.Mat("PH_Skin", new Color(0.55f, 0.36f, 0.24f)),
                ClothWhite = MayanTextures.Mat("PH_ClothWhite", new Color(0.90f, 0.88f, 0.80f)),
                ClothRed = MayanTextures.Mat("PH_ClothRed", new Color(0.65f, 0.18f, 0.12f)),
                ClothBlue = MayanTextures.Mat("PH_ClothMayaBlue", new Color(0.25f, 0.55f, 0.65f)),
                Feather = MayanTextures.Mat("PH_QuetzalFeather", new Color(0.10f, 0.55f, 0.30f), null, 1f, 0.5f),
                Jaguar = MayanTextures.Mat("PH_Jaguar", new Color(0.85f, 0.60f, 0.22f)),
                JaguarSpots = MayanTextures.Mat("PH_JaguarSpots", new Color(0.12f, 0.08f, 0.05f)),
                Deer = MayanTextures.Mat("PH_Deer", new Color(0.55f, 0.40f, 0.26f)),
                MacawRed = MayanTextures.Mat("PH_MacawRed", new Color(0.80f, 0.08f, 0.06f), null, 1f, 0.4f),
                MacawBlue = MayanTextures.Mat("PH_MacawBlue", new Color(0.10f, 0.30f, 0.80f), null, 1f, 0.4f),
                MacawYellow = MayanTextures.Mat("PH_MacawYellow", new Color(0.95f, 0.78f, 0.10f), null, 1f, 0.4f),
                Monkey = MayanTextures.Mat("PH_HowlerMonkey", new Color(0.12f, 0.09f, 0.07f)),
            };
        }
    }
}
