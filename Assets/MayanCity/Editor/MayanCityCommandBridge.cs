using System;
using System.IO;
using UnityEditor;

namespace MayanCityTools
{
    /// <summary>
    /// Lets external tools drive the builder: write a command ("build", "shots", "terrain", "paint",
    /// "temple", "caracol", "details", "vegetation", "placeholders", "lighting") into mayan_cmd.txt
    /// at the project root; results are appended to mayan_log.txt. Safe to delete this file.
    /// </summary>
    [InitializeOnLoad]
    static class MayanCityCommandBridge
    {
        const string CmdFile = "mayan_cmd.txt";
        static double s_Next;

        static MayanCityCommandBridge()
        {
            EditorApplication.update += Poll;
            MayanCityBuilder.Log("Scripts compiled; command bridge ready.");
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < s_Next) return;
            s_Next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(CmdFile)) return;

            string cmd = File.ReadAllText(CmdFile).Trim().ToLowerInvariant();
            File.Delete(CmdFile);
            MayanCityBuilder.Log("Command: " + cmd);
            try
            {
                Run(cmd);
                MayanCityBuilder.Log("DONE " + cmd);
            }
            catch (Exception e)
            {
                MayanCityBuilder.Log("ERROR " + cmd + ": " + e);
            }
        }

        // Re-links each URP asset to its renderer (the originals were missing from version control).
        static void FixRenderers()
        {
            var pairs = new[]
            {
                ("Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset"),
                ("Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/Mobile_Renderer.asset"),
            };
            foreach (var (rpPath, rendererPath) in pairs)
            {
                AssetDatabase.ImportAsset(rendererPath, ImportAssetOptions.ForceUpdate);
                var rp = AssetDatabase.LoadMainAssetAtPath(rpPath);
                var renderer = AssetDatabase.LoadMainAssetAtPath(rendererPath);
                if (rp == null || renderer == null) continue;
                var so = new SerializedObject(rp);
                var list = so.FindProperty("m_RendererDataList");
                if (list.arraySize == 0) list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                so.FindProperty("m_DefaultRendererIndex").intValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rp);
                MayanCityBuilder.Log($"Linked {rendererPath} -> {rpPath}");
            }
            AssetDatabase.SaveAssets();
        }

        static void Diagnose()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            MayanCityBuilder.Log("Current RP: " + (rp != null ? AssetDatabase.GetAssetPath(rp) : "none") +
                                 " | quality level: " + UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()]);
            foreach (var path in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) { MayanCityBuilder.Log(path + ": not loadable"); continue; }
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                for (int i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    MayanCityBuilder.Log($"{path} renderer[{i}] = {(e.objectReferenceValue != null ? e.objectReferenceValue.GetType().Name + " " + AssetDatabase.GetAssetPath(e.objectReferenceValue) : "NULL")}");
                }
            }
            var terrain = MayanCityBuilder.FindTerrain();
            if (terrain != null)
            {
                var td = terrain.terrainData;
                MayanCityBuilder.Log($"Terrain: trees={td.treeInstanceCount}, treeProtos={td.treePrototypes.Length}, detailProtos={td.detailPrototypes.Length}, drawTrees={terrain.drawTreesAndFoliage}");
                foreach (var tp in td.treePrototypes)
                {
                    var mf = tp.prefab != null ? tp.prefab.GetComponent<UnityEngine.MeshFilter>() : null;
                    MayanCityBuilder.Log($"  proto {(tp.prefab != null ? tp.prefab.name : "NULL")} mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount.ToString() : "MISSING")}");
                }
            }
            var pc = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/PC_Renderer.asset");
            MayanCityBuilder.Log("PC_Renderer.asset loads as: " + (pc != null ? pc.GetType().FullName : "null"));
        }

        static void Run(string cmd)
        {
            if (cmd == "diag") { Diagnose(); return; }
            if (cmd == "fixrp") { FixRenderers(); Diagnose(); return; }
            if (cmd != "shots") MayanCityBuilder.EnsureScene();
            switch (cmd)
            {
                case "build": MayanCityBuilder.BuildEverything(); break;
                case "shots": MayanCityBuilder.CaptureScreenshots(); return;
                case "terrain": MayanCityBuilder.BuildTerrain(); break;
                case "paint": MayanCityBuilder.SculptAndPaint(); break;
                case "temple": MayanStructures.BuildTemple(); break;
                case "caracol": MayanStructures.BuildCaracol(); break;
                case "details": MayanStructures.BuildCityDetails(); break;
                case "vegetation": MayanCityBuilder.PlaceVegetation(); break;
                case "placeholders": MayanStructures.BuildPlaceholders(); break;
                case "lighting": MayanCityBuilder.SetupLighting(); break;
                default: MayanCityBuilder.Log("Unknown command: " + cmd); return;
            }
            MayanCityBuilder.SaveScene();
        }
    }
}
