using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Brings the art team's VFX folder into the project and wires it up. Safe to run again: nothing you tuned is overwritten.
    ///   1. Import VFX Folder…      — copies the PNGs into Art/VFX (keeping Monk/…, Break/…), sets them up as sprites
    ///                                (same pixels-per-unit as the Monk, pivot at the bottom for ground effects),
    ///                                makes a VFX Cue per effect in Data/VFX and the VfxSet_Monk asset (only if missing).
    ///   2. Add VFX To Open Scene   — VfxPlayer (pooled, Sprite-Unlit material), a Global Light 2D if there's none,
    ///                                and FighterVfx (VfxSet_Monk) on every Fighter. Save the scene afterwards.
    ///   3. Add Item VFX To Item Prefabs — ItemVfx (held glow, break burst, landing dust) on Prefabs/ItemPrefab/*.
    /// Re-importing replaces only images whose pixels changed (the .meta, and every reference, is kept).
    /// Place in: Assets/_Project/Editor/VfxSetup.cs
    /// </summary>
    public static class VfxSetup
    {
        const string Menu = "Window/KungFu Crashout/VFX/";
        const string ArtRoot = "Assets/_Project/Art/VFX";
        const string DataRoot = "Assets/_Project/Data/VFX";
        const string SetPath = DataRoot + "/VfxSet_Monk.asset";
        const string ItemPrefabFolder = "Assets/_Project/Prefabs/ItemPrefab";
        const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        const string PrefKey = "KFC.VfxSourceFolder";
        const string ImportedTag = "kfc-vfx";
        const float PixelsPerUnit = 350f; // the Monk's sprites

        // ───────────── 1. Import ─────────────

        [MenuItem(Menu + "1. Import VFX Folder…", priority = 1)]
        static void ImportFolder()
        {
            string start = EditorPrefs.GetString(PrefKey, System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
            string src = EditorUtility.OpenFolderPanel("Pick the VFX folder from the art team (the one holding Monk/ and Break/)", start, "");
            if (string.IsNullOrEmpty(src)) return;
            EditorPrefs.SetString(PrefKey, src);

            var files = Directory.GetFiles(src, "*.png", SearchOption.AllDirectories);
            if (files.Length == 0) { EditorUtility.DisplayDialog("Import VFX", "No .png files in that folder.", "OK"); return; }

            int added = 0, updated = 0, same = 0;
            var imported = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < files.Length; i++)
                {
                    string rel = Path.GetRelativePath(src, files[i]).Replace('\\', '/');
                    string dst = ArtRoot + "/" + rel;
                    EditorUtility.DisplayProgressBar("Import VFX", rel, i / (float)files.Length);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));

                    if (File.Exists(dst))
                    {
                        if (File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(files[i]))) same++;
                        else { File.Copy(files[i], dst, true); updated++; }
                    }
                    else { File.Copy(files[i], dst); added++; }
                    imported.Add(dst);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.Refresh();

            foreach (var path in imported) ConfigureSprite(path);
            AssetDatabase.Refresh();

            int cues = MakeCues(out var made);
            MakeSet(made);
            AssetDatabase.SaveAssets();

            Debug.Log($"[VFX] Imported {imported.Count} images into {ArtRoot} ({added} new, {updated} updated, {same} unchanged). " +
                      $"{cues} new cue(s) in {DataRoot}. Next: VFX → 2. Add VFX To Open Scene (with FightScene open).");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(SetPath);
        }

        /// <summary>Sprite import settings, applied once per file (later Inspector tweaks are kept).</summary>
        static void ConfigureSprite(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti || ti.userData == ImportedTag) return;

            string n = Path.GetFileNameWithoutExtension(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect; // big soft glows: a quad is cheaper than a tight mesh
            s.spriteAlignment = (int)(IsGroundEffect(n) ? SpriteAlignment.BottomCenter : SpriteAlignment.Center);
            ti.SetTextureSettings(s);

            ti.userData = ImportedTag;
            ti.SaveAndReimport();
        }

        /// <summary>Effects that sit on the floor: dust, landing rings, lotus effects, the break burst.</summary>
        static bool IsGroundEffect(string n) =>
            n.Contains("Dust") || n.Contains("Jump_Impact") || n.EndsWith("_Effect") || n.Contains("Break");

        // ───────────── Cues (starting values; tune them in the Inspector) ─────────────

        struct CueSpec
        {
            public string name, sprite;
            public System.Action<VfxCue> tune;
        }

        static readonly Color PaleYellow = new Color(1f, 0.95f, 0.72f);
        static readonly Color WarmYellow = new Color(1f, 0.84f, 0.45f);
        static readonly Color HitWhite = new Color(1f, 0.93f, 0.78f);
        static readonly Color BlockBlue = new Color(0.45f, 0.8f, 1f);
        static readonly Color ParryYellow = new Color(1f, 0.85f, 0.3f);
        static readonly Color HoldOrange = new Color(1f, 0.6f, 0.25f);

        static readonly CueSpec[] Specs =
        {
            new CueSpec { name = "VFX_Strike1", sprite = "VFX_Monk_Attack_1_Strike", tune = c => {
                c.lifetime = 0.18f; c.startScale = 0.7f; c.peakScale = 1.1f; c.endScale = 1f; c.popTime = 0.05f; c.fadeStart = 0.35f;
                c.sortingOffset = 5; Light(c, PaleYellow, 1.2f, 1.5f, 0.1f); } },
            new CueSpec { name = "VFX_Dust1", sprite = "VFX_Monk_Attack_1_Dust", tune = c => {
                c.lifetime = 0.35f; c.startScale = 0.8f; c.peakScale = 1f; c.endScale = 1.15f; c.fadeStart = 0.3f;
                c.offset = new Vector2(-0.2f, 0f); c.drift = new Vector2(-0.6f, 0f); c.sortingOffset = -1; c.alpha = 0.9f; } },
            new CueSpec { name = "VFX_Strike2", sprite = "VFX_Monk_Attack_2_Strike", tune = c => {
                c.lifetime = 0.2f; c.startScale = 0.65f; c.peakScale = 1.1f; c.endScale = 1f; c.popTime = 0.05f; c.fadeStart = 0.35f;
                c.sortingOffset = 5; Light(c, WarmYellow, 1.6f, 2.2f, 0.12f); } },
            new CueSpec { name = "VFX_Dust2", sprite = "VFX_Monk_Attack_2_Dust", tune = c => {
                c.lifetime = 0.4f; c.startScale = 0.8f; c.peakScale = 1f; c.endScale = 1.15f; c.fadeStart = 0.3f;
                c.offset = new Vector2(-0.3f, 0f); c.drift = new Vector2(-0.8f, 0f); c.sortingOffset = -1; c.alpha = 0.9f; } },
            new CueSpec { name = "VFX_Hit", sprite = "VFX_Monk_Hit", tune = c => {
                c.lifetime = 0.2f; c.startScale = 0.5f; c.peakScale = 1.15f; c.endScale = 1.05f; c.popTime = 0.04f; c.fadeStart = 0.4f;
                c.randomRotation = 8f; c.sortingOffset = 30; Light(c, HitWhite, 2f, 2.5f, 0.12f); } },
            new CueSpec { name = "VFX_HitHeavy", sprite = "VFX_Monk_Hit", tune = c => {
                c.scale = 1.3f; c.lifetime = 0.26f; c.startScale = 0.5f; c.peakScale = 1.2f; c.endScale = 1.05f; c.popTime = 0.05f; c.fadeStart = 0.4f;
                c.randomRotation = 6f; c.sortingOffset = 30; Light(c, HitWhite, 2.5f, 3f, 0.16f); } },
            new CueSpec { name = "VFX_KO", sprite = "VFX_Monk_Hit", tune = c => {
                c.scale = 1.7f; c.lifetime = 0.45f; c.startScale = 0.4f; c.peakScale = 1.25f; c.endScale = 1.1f; c.popTime = 0.07f; c.fadeStart = 0.5f;
                c.sortingOffset = 31; Light(c, HitWhite, 3f, 4f, 0.3f); } },
            new CueSpec { name = "VFX_Block", sprite = "VFX_Monk_Block_Effect", tune = c => {
                c.flipWithFacing = false; c.lifetime = 0.3f; c.startScale = 0.85f; c.peakScale = 1.05f; c.endScale = 1f; c.fadeStart = 0.5f;
                c.sortingOffset = 3; Light(c, BlockBlue, 1.8f, 2.5f, 0.15f); c.lightOffset = new Vector2(0f, 0.8f); } },
            new CueSpec { name = "VFX_Parry", sprite = "VFX_Monk_Parry_Effect", tune = c => {
                c.flipWithFacing = false; c.lifetime = 0.38f; c.startScale = 0.8f; c.peakScale = 1.1f; c.endScale = 1f; c.fadeStart = 0.5f;
                c.sortingOffset = 3; Light(c, ParryYellow, 2.5f, 3f, 0.2f); c.lightOffset = new Vector2(0f, 0.8f); } },
            new CueSpec { name = "VFX_ParryImpact", sprite = "VFX_Monk_Parry_Impact", tune = c => {
                c.lifetime = 0.22f; c.startScale = 0.4f; c.peakScale = 1.25f; c.endScale = 1.1f; c.popTime = 0.04f; c.fadeStart = 0.4f;
                c.randomRotation = 25f; c.sortingOffset = 31; } },
            new CueSpec { name = "VFX_Dash", sprite = "VFX_Monk_Dash_Dust", tune = c => {
                c.lifetime = 0.3f; c.startScale = 0.8f; c.peakScale = 1f; c.endScale = 1.1f; c.fadeStart = 0.3f;
                c.offset = new Vector2(-0.4f, 0f); c.drift = new Vector2(-1f, 0f); c.sortingOffset = -1; c.alpha = 0.85f; } },
            new CueSpec { name = "VFX_Jump", sprite = "VFX_Monk_Jump_Dust", tune = c => {
                c.flipWithFacing = false; c.lifetime = 0.3f; c.startScale = 0.8f; c.peakScale = 1f; c.endScale = 1.1f; c.fadeStart = 0.3f;
                c.drift = new Vector2(0f, 0.4f); c.sortingOffset = -1; c.alpha = 0.85f; } },
            new CueSpec { name = "VFX_Land", sprite = "VFX_Monk_Jump_Impact", tune = c => {
                c.flipWithFacing = false; c.lifetime = 0.28f; c.startScale = 0.6f; c.peakScale = 1.05f; c.endScale = 1.15f; c.fadeStart = 0.35f;
                c.sortingOffset = 1; c.alpha = 0.9f; } },
            new CueSpec { name = "VFX_Knockdown", sprite = "VFX_Monk_Jump_Impact", tune = c => {
                c.flipWithFacing = false; c.scale = 1.4f; c.lifetime = 0.35f; c.startScale = 0.6f; c.peakScale = 1.1f; c.endScale = 1.2f; c.fadeStart = 0.35f;
                c.sortingOffset = 1; } },
            // Items: sizes are relative to the item (Scale 1 = as wide as the item).
            new CueSpec { name = "VFX_ItemHeld", sprite = "VFX_Monk_Holding", tune = c => {
                c.flipWithFacing = false; c.scale = 1.5f; c.alpha = 0.85f; c.startScale = 0.6f; c.peakScale = 1f; c.popTime = 0.12f;
                c.pulseSpeed = 2.5f; c.pulseAmount = 0.08f; c.sortingOffset = -2; Light(c, HoldOrange, 0.7f, 1.5f, 0.1f); } },
            new CueSpec { name = "VFX_ItemBreak", sprite = "VFX_ObjectBreak_Light", tune = c => {
                c.flipWithFacing = false; c.scale = 1.6f; c.lifetime = 0.45f; c.startScale = 0.6f; c.peakScale = 1.05f; c.endScale = 1.15f; c.fadeStart = 0.35f;
                c.sortingOffset = 2; } },
            new CueSpec { name = "VFX_ItemLand", sprite = "VFX_Monk_Jump_Impact", tune = c => {
                c.flipWithFacing = false; c.scale = 1.2f; c.lifetime = 0.25f; c.startScale = 0.6f; c.peakScale = 1f; c.endScale = 1.1f; c.fadeStart = 0.35f;
                c.sortingOffset = -1; c.alpha = 0.8f; } },
        };

        static void Light(VfxCue c, Color colour, float intensity, float radius, float time)
        {
            c.light = true; c.lightColour = colour; c.lightIntensity = intensity; c.lightRadius = radius; c.lightTime = time;
        }

        /// <summary>Makes each missing cue. Returns how many were new; 'all' gets every cue by name (new or existing).</summary>
        static int MakeCues(out Dictionary<string, VfxCue> all)
        {
            all = new Dictionary<string, VfxCue>();
            EnsureFolder(DataRoot);
            int made = 0;
            foreach (var spec in Specs)
            {
                string path = $"{DataRoot}/{spec.name}.asset";
                var cue = AssetDatabase.LoadAssetAtPath<VfxCue>(path);
                if (!cue)
                {
                    var sprite = FindSprite(spec.sprite);
                    if (!sprite) { Debug.LogWarning($"[VFX] {spec.sprite}.png not found under {ArtRoot}: {spec.name} not made."); continue; }
                    cue = ScriptableObject.CreateInstance<VfxCue>();
                    cue.sprite = sprite;
                    spec.tune(cue);
                    AssetDatabase.CreateAsset(cue, path);
                    made++;
                }
                else if (!cue.sprite)
                {
                    cue.sprite = FindSprite(spec.sprite);
                    EditorUtility.SetDirty(cue);
                }
                all[spec.name] = cue;
            }
            return made;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Sprite FindSprite(string fileName)
        {
            foreach (var guid in AssetDatabase.FindAssets(fileName + " t:Sprite", new[] { ArtRoot }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == fileName) return AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
            return null;
        }

        static VfxCue Cue(Dictionary<string, VfxCue> all, string name) => all.TryGetValue(name, out var c) ? c : null;

        static void MakeSet(Dictionary<string, VfxCue> cues)
        {
            if (AssetDatabase.LoadAssetAtPath<VfxSet>(SetPath)) return; // keep your tuning
            var set = ScriptableObject.CreateInstance<VfxSet>();
            var a1 = new VfxSet.AttackVfx { strike = Cue(cues, "VFX_Strike1"), dust = Cue(cues, "VFX_Dust1"), scale = 1f };
            var a2 = new VfxSet.AttackVfx { strike = Cue(cues, "VFX_Strike2"), dust = Cue(cues, "VFX_Dust2"), scale = 1f };
            var big = a2; big.scale = 1.3f;
            set.lightCombo = new[] { a1, a2, big };
            set.heavy = big;
            set.special = big;
            set.hit = Cue(cues, "VFX_Hit");
            set.heavyHit = Cue(cues, "VFX_HitHeavy");
            set.ko = Cue(cues, "VFX_KO");
            set.block = Cue(cues, "VFX_Block");
            set.parry = Cue(cues, "VFX_Parry");
            set.parryImpact = Cue(cues, "VFX_ParryImpact");
            set.dash = Cue(cues, "VFX_Dash");
            set.jump = Cue(cues, "VFX_Jump");
            set.land = Cue(cues, "VFX_Land");
            set.knockdown = Cue(cues, "VFX_Knockdown");
            AssetDatabase.CreateAsset(set, SetPath);
        }

        // ───────────── 2. Scene ─────────────

        [MenuItem(Menu + "2. Add VFX To Open Scene", priority = 2)]
        static void AddToScene()
        {
            var set = AssetDatabase.LoadAssetAtPath<VfxSet>(SetPath);
            if (!set)
            {
                EditorUtility.DisplayDialog("Add VFX", "VfxSet_Monk not found. Run VFX → 1. Import VFX Folder first.", "OK");
                return;
            }
            var scene = EditorSceneManager.GetActiveScene();
            var log = new List<string>();

            // VfxPlayer
            var player = Object.FindAnyObjectByType<VfxPlayer>(FindObjectsInactive.Include);
            if (!player)
            {
                var go = new GameObject("VfxPlayer");
                Undo.RegisterCreatedObjectUndo(go, "Add VfxPlayer");
                player = go.AddComponent<VfxPlayer>();
                log.Add("VfxPlayer");
            }
            var pso = new SerializedObject(player);
            var mat = pso.FindProperty("spriteMaterial");
            if (!mat.objectReferenceValue)
            {
                mat.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
                pso.ApplyModifiedProperties();
            }

            // Global Light 2D
            bool hasGlobal = Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                   .Any(l => l.lightType == Light2D.LightType.Global);
            if (!hasGlobal)
            {
                var go = new GameObject("Global Light 2D");
                Undo.RegisterCreatedObjectUndo(go, "Add Global Light 2D");
                var l = go.AddComponent<Light2D>();
                var lso = new SerializedObject(l);
                lso.FindProperty("m_LightType").intValue = (int)Light2D.LightType.Global;
                lso.FindProperty("m_Intensity").floatValue = 1f;
                lso.ApplyModifiedPropertiesWithoutUndo();
                log.Add("Global Light 2D (intensity 1, so the stage looks the same as before)");
            }

            // FighterVfx on every fighter
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var fv = f.GetComponent<FighterVfx>();
                if (!fv) { fv = Undo.AddComponent<FighterVfx>(f.gameObject); log.Add($"FighterVfx on {f.name}"); }
                if (!fv.Set)
                {
                    var so = new SerializedObject(fv);
                    so.FindProperty("set").objectReferenceValue = set;
                    so.ApplyModifiedProperties();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = player.gameObject;
            Debug.Log(log.Count > 0
                ? $"[VFX] {scene.name}: added {string.Join(", ", log)}. Save the scene (Ctrl+S)."
                : $"[VFX] {scene.name}: already set up, nothing changed.");
        }

        // ───────────── 3. Items ─────────────

        [MenuItem(Menu + "3. Add Item VFX To Item Prefabs", priority = 3)]
        static void AddToItemPrefabs()
        {
            int added = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ItemPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!root.GetComponent<Carryable>()) continue;
                    total++;
                    if (AddItemVfx(root)) { added++; PrefabUtility.SaveAsPrefabAsset(root, path); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log($"[VFX] ItemVfx added to {added} of {total} item prefab(s) in {ItemPrefabFolder}.");
        }

        /// <summary>Adds ItemVfx with the default cues if missing. True if something changed. (Make Item Prefab calls this.)</summary>
        public static bool AddItemVfx(GameObject item)
        {
            if (!item.GetComponent<Carryable>()) return false;
            bool changed = false;
            var iv = item.GetComponent<ItemVfx>();
            if (!iv) { iv = item.AddComponent<ItemVfx>(); changed = true; }

            var so = new SerializedObject(iv);
            changed |= Fill(so, "heldGlow", "VFX_ItemHeld");
            changed |= Fill(so, "breakBurst", "VFX_ItemBreak");
            changed |= Fill(so, "landDust", "VFX_ItemLand");
            so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        static bool Fill(SerializedObject so, string prop, string cueName)
        {
            var p = so.FindProperty(prop);
            if (p.objectReferenceValue) return false;
            var cue = AssetDatabase.LoadAssetAtPath<VfxCue>($"{DataRoot}/{cueName}.asset");
            if (!cue) return false;
            p.objectReferenceValue = cue;
            return true;
        }

        // ───────────── All ─────────────

        [MenuItem(Menu + "Set Up Everything (2 + 3)", priority = 20)]
        static void SceneAndItems()
        {
            AddToScene();
            AddToItemPrefabs();
        }
    }
}
