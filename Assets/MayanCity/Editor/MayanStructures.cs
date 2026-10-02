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

        static ProBuilderMesh Prism(string name, Transform parent, Vector3 basePos, Vector3 size, Quaternion rot, Material mat)
            => Place(ShapeGenerator.GeneratePrism(PivotLocation.Center, size), name, parent, basePos, rot, mat);

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
        /// with an inner sanctuary holding the Foliated Cross tablet, a sloped mansard roof and a latticed roof comb.
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

            // Inner wall separating the front gallery from the sanctuary, with one central doorway.
            float innerZ = tz + 0.3f;
            Box("Inner_Wall_L", walls, new Vector3(-4.05f, floorY, innerZ), new Vector3(5.5f, wallH, 0.6f), pal.Limestone);
            Box("Inner_Wall_R", walls, new Vector3(4.05f, floorY, innerZ), new Vector3(5.5f, wallH, 0.6f), pal.Limestone);
            Box("Inner_Lintel", walls, new Vector3(0f, floorY + doorH, innerZ), new Vector3(2.6f, wallH - doorH, 0.6f), pal.Limestone);

            // Sanctuary shrine with the Tablet of the Foliated Cross (a cross sprouting maize leaves).
            var shrine = Group("Sanctuary", temple);
            float shrineZ = tz + 2.6f;
            Box("Shrine", shrine, new Vector3(0f, floorY, shrineZ), new Vector3(4.5f, 3f, 2.2f), pal.Stucco);
            float faceZ = shrineZ - 1.1f;
            Box("Tablet", shrine, new Vector3(0f, floorY + 0.5f, faceZ - 0.08f), new Vector3(2.6f, 2.1f, 0.15f), pal.Carving, collider: false);
            Box("Cross_Vertical", shrine, new Vector3(0f, floorY + 0.7f, faceZ - 0.2f), new Vector3(0.35f, 1.6f, 0.1f), pal.Stone, collider: false);
            Box("Cross_Horizontal", shrine, new Vector3(0f, floorY + 1.6f, faceZ - 0.2f), new Vector3(1.5f, 0.3f, 0.1f), pal.Stone, collider: false);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(s < 0 ? "Maize_Leaf_L" : "Maize_Leaf_R", shrine, new Vector3(s * 0.85f, floorY + 1.55f, faceZ - 0.2f),
                    new Vector3(0.18f, 0.6f, 0.08f), pal.Feather, rot: Quaternion.Euler(0f, 0f, -s * 40f), collider: false);
                Box(s < 0 ? "Maize_Ear_L" : "Maize_Ear_R", shrine, new Vector3(s * 0.4f, floorY + 0.9f, faceZ - 0.2f),
                    new Vector3(0.16f, 0.5f, 0.08f), pal.MacawYellow, rot: Quaternion.Euler(0f, 0f, -s * 25f), collider: false);
            }

            // Mansard roof (sloping upper facade) and the latticed roof comb (cresteria).
            var roof = Group("Roof", temple);
            float roofY = floorY + wallH;
            Box("Ceiling_Slab", roof, new Vector3(0f, roofY, tz), new Vector3(tw + 0.2f, 0.3f, td + 0.2f), pal.Limestone);
            Box("Mansard_Roof", roof, new Vector3(0f, roofY + 0.3f, tz), new Vector3(tw + 0.2f, 2.4f, td + 0.2f), pal.Stucco, 1.6f, 2.0f);
            Box("Roof_Cap", roof, new Vector3(0f, roofY + 2.7f, tz), new Vector3(12.8f, 0.3f, 6f), pal.Limestone);

            var comb = Group("Roof_Comb", temple);
            float combY = roofY + 3f;
            Box("Comb_Base", comb, new Vector3(0f, combY, tz), new Vector3(10f, 0.6f, 1.4f), pal.Stone);
            for (int k = 0; k < 6; k++)
                Box($"Comb_Post_{k + 1}", comb, new Vector3(-4.5f + k * 1.8f, combY + 0.6f, tz), new Vector3(0.6f, 5f, 0.9f), pal.Stone);
            for (int k = 0; k < 3; k++)
                Box($"Comb_Bar_{k + 1}", comb, new Vector3(0f, combY + 1.6f + k * 1.3f, tz), new Vector3(10f, 0.45f, 0.9f), pal.Stone);
            Box("Comb_Crest", comb, new Vector3(0f, combY + 5.6f, tz), new Vector3(10.4f, 0.8f, 1.2f), pal.Stucco, 1.2f, 0.1f);

            MayanCityBuilder.Log($"Step 3: Temple of the Foliated Cross built at {root.position} ({root.GetComponentsInChildren<ProBuilderMesh>().Length} ProBuilder shapes)");
            return root.gameObject;
        }

        // ------------------------------------------------------------------ El Caracol

        /// <summary>
        /// Two stacked rectangular platforms with stairways, a round drum, the cylindrical observatory tower
        /// with four doorways and mouldings, an upper drum with three astronomical window slits and a dome.
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

            // Four doorways at the cardinal points.
            for (int k = 0; k < 4; k++)
            {
                var rot = Quaternion.Euler(0f, k * 90f, 0f);
                var dir = rot * Vector3.back;
                var at = new Vector3(c.x, towerY, c.z) + dir * 5.45f;
                Box($"Doorway_{k + 1}", tower, at, new Vector3(1.6f, 2.8f, 0.6f), pal.Dark, rot: Quaternion.LookRotation(dir), collider: false);
                Box($"Door_Lintel_{k + 1}", tower, at + Vector3.up * 2.8f + dir * 0.05f, new Vector3(2.2f, 0.4f, 0.8f), pal.Stone, rot: Quaternion.LookRotation(dir), collider: false);
            }

            float drumY = towerY + 8f;
            Cyl("Upper_Drum", tower, new Vector3(c.x, drumY, c.z), 3.6f, 2.6f, pal.Limestone);
            // The real Caracol's surviving windows frame the equinox sunset and the extremes of Venus.
            float[] windowAngles = { -30f, 0f, 45f };
            for (int k = 0; k < windowAngles.Length; k++)
            {
                var dir = Quaternion.Euler(0f, windowAngles[k], 0f) * Vector3.back;
                Box($"Astronomical_Window_{k + 1}", tower, new Vector3(c.x, drumY + 0.7f, c.z) + dir * 3.45f,
                    new Vector3(0.45f, 1.3f, 0.5f), pal.Dark, rot: Quaternion.LookRotation(dir), collider: false);
            }
            Cyl("Drum_Cornice", tower, new Vector3(c.x, drumY + 2.6f, c.z), 3.9f, 0.3f, pal.Stone);
            Dome("Dome", tower, new Vector3(c.x, drumY + 2.9f - 3.3f, c.z), 3.3f, pal.Limestone);

            MayanCityBuilder.Log($"Step 4: El Caracol built at {root.position} ({root.GetComponentsInChildren<ProBuilderMesh>().Length} ProBuilder shapes)");
            return root.gameObject;
        }

        // ------------------------------------------------------------------ city details

        /// <summary>Sacbe (white causeway) linking both buildings, central altar, stelae and thatched houses.</summary>
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
            Cyl("Central_Altar", root, MayanCityBuilder.Ground(plaza) + Vector3.up * 0.4f, 3f, 0.9f, pal.Stone);
            Cyl("Central_Altar_Top", root, MayanCityBuilder.Ground(plaza) + Vector3.up * 1.3f, 2.4f, 0.3f, pal.Carving);

            var perp = new Vector2(-dir.z, dir.x).normalized;
            var along = new Vector2(dir.x, dir.z).normalized;
            var stelae = Group("Stelae", root);
            int n = 0;
            foreach (var offset in new[] { perp * 16f + along * 10f, perp * 16f - along * 10f, -perp * 16f + along * 10f, -perp * 16f - along * 10f })
            {
                n++;
                var p = MayanCityBuilder.Ground(plaza + offset);
                var facing = Quaternion.LookRotation(new Vector3(-offset.x, 0f, -offset.y));
                var st = Group($"Stela_{n}", stelae);
                st.position = p;
                st.rotation = facing;
                Box("Shaft", st, Vector3.zero, new Vector3(1f, 3.4f, 0.55f), pal.Stone);
                Box("Carved_Face", st, new Vector3(0f, 0.5f, 0.3f), new Vector3(0.8f, 2.6f, 0.08f), pal.Carving, collider: false);
                Box("Cap", st, new Vector3(0f, 3.4f, 0f), new Vector3(1.2f, 0.35f, 0.7f), pal.Limestone, 0.15f, 0.1f);
                Cyl("Altar", st, new Vector3(0f, 0f, 1.8f), 0.8f, 0.6f, pal.Stone, 16);
            }

            var houses = Group("Houses", root);
            var spots = new[] { new Vector2(258f, 232f), new Vector2(226f, 262f), new Vector2(148f, 172f), new Vector2(176f, 150f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var p = MayanCityBuilder.Ground(spots[i]);
                var h = Group($"House_{i + 1}", houses);
                h.position = p;
                h.rotation = Quaternion.LookRotation(new Vector3(plaza.x - spots[i].x, 0f, plaza.y - spots[i].y));
                Box("House_Platform", h, new Vector3(0f, -0.5f, 0f), new Vector3(9f, 1.3f, 7f), pal.Stone, 0.3f, 0.3f);
                Box("Walls", h, new Vector3(0f, 0.8f, 0f), new Vector3(6f, 2.2f, 3.8f), pal.Whitewash);
                Box("Doorway", h, new Vector3(0f, 0.8f, 1.72f), new Vector3(1.1f, 1.8f, 0.4f), pal.Dark, collider: false);
                // Prism ridge runs along local Z, so rotate 90 degrees to put the ridge along the long axis.
                Prism("Thatch_Roof", h, new Vector3(0f, 3f, 0f), new Vector3(5f, 3f, 7.2f), Quaternion.Euler(0f, 90f, 0f), pal.Thatch);
            }

            MayanCityBuilder.Log("Step 5: sacbe, central altar, 4 stelae and 4 thatched houses built");
            return root.gameObject;
        }

        // ------------------------------------------------------------------ placeholders

        static GameObject Prim(string name, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var g = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        static Transform Person(string name, Transform parent, Vector3 pos, float yaw, Material cloth, Palette pal, bool priest = false)
        {
            var t = Group(name, parent);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim("Legs", PrimitiveType.Capsule, t, new Vector3(0f, 0.45f, 0f), new Vector3(0.32f, 0.45f, 0.26f), pal.Skin);
            Prim("Tunic", PrimitiveType.Capsule, t, new Vector3(0f, 1.05f, 0f), new Vector3(0.46f, 0.42f, 0.32f), cloth);
            Prim("Head", PrimitiveType.Sphere, t, new Vector3(0f, 1.6f, 0f), Vector3.one * 0.27f, pal.Skin);
            if (priest)
            {
                Prim("Headdress", PrimitiveType.Cube, t, new Vector3(0f, 1.95f, -0.05f), new Vector3(0.5f, 0.55f, 0.08f), pal.Feather);
                Prim("Cape", PrimitiveType.Cube, t, new Vector3(0f, 1.1f, -0.2f), new Vector3(0.6f, 0.9f, 0.05f), pal.ClothRed);
            }
            return t;
        }

        static Transform Quadruped(string name, Transform parent, Vector3 pos, float yaw, Material body, Material accent, float size, float legLen, bool spots)
        {
            var t = Group(name, parent);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one * size;
            float bodyY = legLen + 0.25f;
            Prim("Body", PrimitiveType.Capsule, t, new Vector3(0f, bodyY, 0f), new Vector3(0.5f, 0.7f, 0.5f), body, Quaternion.Euler(90f, 0f, 0f));
            Prim("Head", PrimitiveType.Sphere, t, new Vector3(0f, bodyY + 0.2f, 0.8f), Vector3.one * 0.42f, body);
            foreach (var x in new[] { -0.15f, 0.15f })
                foreach (var z in new[] { -0.45f, 0.45f })
                    Prim("Leg", PrimitiveType.Cylinder, t, new Vector3(x, legLen / 2f, z), new Vector3(0.12f, legLen / 2f, 0.12f), body);
            Prim("Tail", PrimitiveType.Cylinder, t, new Vector3(0f, bodyY, -0.95f), new Vector3(0.07f, 0.4f, 0.07f), accent, Quaternion.Euler(-60f, 0f, 0f));
            if (spots)
                for (int i = 0; i < 6; i++)
                    Prim("Spot", PrimitiveType.Sphere, t, new Vector3((i % 2 == 0 ? -1 : 1) * 0.2f, bodyY + 0.12f, -0.4f + i * 0.16f), Vector3.one * 0.12f, accent);
            return t;
        }

        static Transform Macaw(string name, Transform parent, Vector3 pos, float yaw, Palette pal)
        {
            var t = Group(name, parent);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim("Body", PrimitiveType.Capsule, t, new Vector3(0f, 0.25f, 0f), new Vector3(0.16f, 0.2f, 0.16f), pal.MacawRed, Quaternion.Euler(15f, 0f, 0f));
            Prim("Head", PrimitiveType.Sphere, t, new Vector3(0f, 0.5f, 0.05f), Vector3.one * 0.13f, pal.MacawRed);
            Prim("Wing_L", PrimitiveType.Cube, t, new Vector3(-0.09f, 0.25f, -0.02f), new Vector3(0.03f, 0.22f, 0.12f), pal.MacawBlue);
            Prim("Wing_R", PrimitiveType.Cube, t, new Vector3(0.09f, 0.25f, -0.02f), new Vector3(0.03f, 0.22f, 0.12f), pal.MacawBlue);
            Prim("Wing_Band", PrimitiveType.Cube, t, new Vector3(0f, 0.33f, -0.07f), new Vector3(0.19f, 0.05f, 0.05f), pal.MacawYellow);
            Prim("Tail", PrimitiveType.Cube, t, new Vector3(0f, 0.02f, -0.12f), new Vector3(0.05f, 0.4f, 0.03f), pal.MacawRed, Quaternion.Euler(-25f, 0f, 0f));
            return t;
        }

        /// <summary>
        /// Simple stand-ins for people and native animals. Swap each one for an Asset Store prefab
        /// (same position/rotation) â€” the rubric expects imported assets here.
        /// </summary>
        public static GameObject BuildPlaceholders()
        {
            var pal = Palette.Load();
            MayanCityBuilder.RemoveRoot(MayanCityBuilder.PlaceholdersName);
            var root = new GameObject(MayanCityBuilder.PlaceholdersName).transform;
            var people = Group("People", root);
            var animals = Group("Animals", root);
            var rnd = new System.Random(19);

            Vector2 plaza = MayanCityBuilder.PlazaCenter;
            Material[] cloths = { pal.ClothWhite, pal.ClothRed, pal.ClothBlue };
            for (int i = 0; i < 10; i++)
            {
                float ang = i * 36f + (float)rnd.NextDouble() * 20f;
                float r = 9f + (float)rnd.NextDouble() * 14f;
                var p = plaza + new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * r;
                Person($"Villager_{i + 1}", people, MayanCityBuilder.Ground(p), (float)rnd.NextDouble() * 360f, cloths[i % cloths.Length], pal);
            }

            var templeGo = GameObject.Find(MayanCityBuilder.TempleName);
            if (templeGo != null)
            {
                var top = templeGo.transform.TransformPoint(new Vector3(0f, 14.5f, -3.2f));
                Person("Priest_AjKin", people, top, templeGo.transform.eulerAngles.y + 180f, pal.ClothWhite, pal, true);
            }

            Quadruped("Jaguar_1", animals, MayanCityBuilder.Ground(new Vector2(118f, 292f)), 120f, pal.Jaguar, pal.JaguarSpots, 1.1f, 0.45f, true);
            Quadruped("Jaguar_2", animals, MayanCityBuilder.Ground(new Vector2(298f, 132f)), 250f, pal.Jaguar, pal.JaguarSpots, 1.0f, 0.45f, true);
            Quadruped("WhiteTailed_Deer", animals, MayanCityBuilder.Ground(new Vector2(108f, 206f)), 80f, pal.Deer, pal.ClothWhite, 1.2f, 0.8f, false);
            Quadruped("Tapir", animals, MayanCityBuilder.Ground(new Vector2(292f, 292f)), 200f, pal.Monkey, pal.Monkey, 1.6f, 0.35f, false);

            var stelae = GameObject.Find(MayanCityBuilder.DetailsName + "/Stelae");
            if (stelae != null)
            {
                int k = 0;
                foreach (Transform s in stelae.transform)
                    if (k++ % 2 == 0) Macaw($"Scarlet_Macaw_{k}", animals, s.position + Vector3.up * 3.75f, s.eulerAngles.y, pal);
            }

            MayanCityBuilder.Log("Step 7: placeholder villagers, priest, jaguars, deer, tapir and macaws placed (replace with Asset Store prefabs)");
            return root.gameObject;
        }
    }
}
