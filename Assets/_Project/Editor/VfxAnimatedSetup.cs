#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using KungFuCrashout.Feedback;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → VFX → 4. Set Up Animated VFX (Sprite Sheets)
    ///
    /// The Monk's animated effects (Spritesheets + "Monk VFX notes.txt") replaying the single-frame VFX:
    ///   1. Slices Art/VFX/Animated/VFX_Anim_*.png. These were prepared from the artist's sheets: every frame of a sheet
    ///      cropped to ONE shared box (so frames never jitter or change size), large sheets at half resolution (still
    ///      sharper than they appear on screen), repacked to ≤ 4096 px for phones. Jump Land plays column 1 then column 2;
    ///      Parry Impact gets a blank frame after frame 2 (as in the notes).
    ///   2. Pixels per unit per family, so each effect is the same size in game as the one it replaces (Block Start /
    ///      Idle / Break share one value, as do the four Attack 1 sheets; the held-item set is fitted to the item).
    ///   3. Makes one VfxCue per sheet in Data/VFX/Animated (fps from the notes). New cues copy placement, sorting and
    ///      light from the cue they replace; re-running only refreshes frames / fps / sizes, never your offsets.
    ///   4. Makes VfxSet_Monk_Animated (a copy of VfxSet_Monk with the animated cues) and assigns it to every FighterVfx in
    ///      the open scene (FighterVfx → Animated Set); fills ItemVfx → Animated on the item prefabs.
    /// VfxPlayer → Use Animated VFX (also TUNING → Testing) switches between the animated and the original effects.
    /// Place in: Assets/_Project/Editor/VfxAnimatedSetup.cs
    /// </summary>
    public static class VfxAnimatedSetup
    {
        const string Title = "Animated VFX";
        const string ArtFolder = "Assets/_Project/Art/VFX/Animated";
        const string CueFolder = "Assets/_Project/Data/VFX/Animated";
        const string OldCueFolder = "Assets/_Project/Data/VFX";
        const string StaticSetPath = "Assets/_Project/Data/VFX/VfxSet_Monk.asset";
        const string AnimatedSetPath = "Assets/_Project/Data/VFX/VfxSet_Monk_Animated.asset";
        const string ItemPrefabFolder = "Assets/_Project/Prefabs";

        readonly struct Sheet
        {
            public readonly string name; public readonly int cellW, cellH, cols, count; public readonly float fps; public readonly bool loop;
            public readonly Vector2 pivot; public readonly float ppu; public readonly Vector2 content; public readonly string copyFrom;
            public readonly bool follow; public readonly int[] blanksAfter;
            public Sheet(string name, int cellW, int cellH, int cols, int count, float fps, bool loop, float px, float py, float ppu,
                         float contentW, float contentH, string copyFrom, bool follow, int[] blanksAfter)
            {
                this.name = name; this.cellW = cellW; this.cellH = cellH; this.cols = cols; this.count = count; this.fps = fps;
                this.loop = loop; pivot = new Vector2(px, py); this.ppu = ppu; content = new Vector2(contentW, contentH);
                this.copyFrom = copyFrom; this.follow = follow; this.blanksAfter = blanksAfter;
            }
            public string File => $"{ArtFolder}/VFX_Anim_{name}.png";
            public int Rows => Mathf.CeilToInt(count / (float)cols);
        }

        // Measured from the artist's sheets (cell, frames, pivot, size); fps from "Monk VFX notes.txt".
        static readonly Sheet[] Sheets =
        {
            new Sheet("Attack1_Dust", 485, 178, 8, 9, 15f, false, 0.5f, 0.0112f, 355.3f, 1.354f, 0.491f, "VFX_Dust1", false, new int[] {}),
            new Sheet("Attack1_Strike", 496, 426, 8, 9, 30f, false, 0.5f, 0.5f, 355.3f, 1.386f, 1.186f, "VFX_Strike1", true, new int[] {}),
            new Sheet("Attack1_Impact_Ground", 483, 69, 5, 5, 30f, false, 0.5f, 0.029f, 355.3f, 1.348f, 0.183f, "VFX_Land", false, new int[] {}),
            new Sheet("Attack1_Impact_Slash", 440, 238, 5, 5, 30f, false, 0.5f, 0.5f, 355.3f, 1.227f, 0.659f, "VFX_Hit", false, new int[] {}),
            new Sheet("Dash", 494, 222, 8, 8, 15f, false, 0.5f, 0.009f, 492.86f, 0.995f, 0.443f, "VFX_Dash", false, new int[] {}),
            new Sheet("Jump_Up", 421, 500, 7, 7, 15f, false, 0.5f, 0.002f, 635.93f, 0.656f, 0.782f, "VFX_Jump", false, new int[] {}),
            new Sheet("Jump_Land", 418, 244, 9, 9, 15f, false, 0.5f, 0.0021f, 329.86f, 1.255f, 0.731f, "VFX_Land", false, new int[] {}),
            new Sheet("Block_Start", 573, 456, 5, 5, 15f, false, 0.5f, 0.0044f, 237.78f, 2.393f, 1.899f, "VFX_Block", false, new int[] {}),
            new Sheet("Block_Idle", 542, 350, 5, 5, 8f, true, 0.5f, 0.0057f, 237.78f, 2.265f, 1.455f, "VFX_Block", false, new int[] {}),
            new Sheet("Block_Break", 964, 658, 4, 5, 15f, false, 0.5f, 0.003f, 237.78f, 4.04f, 2.75f, "VFX_Block", false, new int[] {}),
            new Sheet("Parry_Main", 801, 762, 5, 5, 30f, false, 0.5f, 0.0026f, 422.24f, 1.888f, 1.794f, "VFX_Parry", false, new int[] {}),
            new Sheet("Parry_Impact", 842, 751, 4, 9, 30f, false, 0.5f, 0.5f, 570.24f, 1.47f, 1.31f, "VFX_ParryImpact", false, new int[] {2}),
            new Sheet("Hold_Start", 300, 278, 6, 6, 15f, false, 0.5f, 0.5f, 350.0f, 0.834f, 0.771f, "VFX_ItemHeld", false, new int[] {}),
            new Sheet("Hold_Idle", 273, 272, 5, 5, 8f, true, 0.5f, 0.5f, 350.0f, 0.757f, 0.754f, "VFX_ItemHeld", false, new int[] {}),
            new Sheet("Hold_Aim", 300, 303, 5, 5, 15f, true, 0.5f, 0.5f, 350.0f, 0.834f, 0.843f, "VFX_ItemHeld", false, new int[] {}),
            new Sheet("Hold_Hit", 502, 500, 5, 5, 15f, false, 0.5f, 0.5f, 350.0f, 1.411f, 1.411f, "VFX_ItemBreak", false, new int[] {}),
            new Sheet("Hold_Throw_Start", 464, 303, 2, 2, 15f, false, 0.6735f, 0.5f, 350.0f, 1.303f, 0.843f, "VFX_ItemHeld", false, new int[] {}),
            new Sheet("Hold_Throw_Idle", 576, 303, 5, 5, 15f, true, 0.737f, 0.5f, 350.0f, 1.623f, 0.843f, "VFX_ItemHeld", false, new int[] {}),
        };

        [MenuItem("Window/KungFu Crashout/VFX/4. Set Up Animated VFX (Sprite Sheets)", priority = 4)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!AssetDatabase.IsValidFolder(ArtFolder)) { EditorUtility.DisplayDialog(Title, $"{ArtFolder} not found.", "OK"); return; }
            EnsureFolder(CueFolder);

            var cues = new Dictionary<string, VfxCue>();
            var missing = new List<string>();
            int created = 0;
            try
            {
                for (int i = 0; i < Sheets.Length; i++)
                {
                    var s = Sheets[i];
                    EditorUtility.DisplayProgressBar(Title, $"Slicing {s.name}", i / (float)Sheets.Length);
                    var sprites = Slice(s);
                    if (sprites == null) { missing.Add(s.name); continue; }
                    var cue = MakeCue(s, sprites, out bool isNew);
                    if (isNew) created++;
                    cues[s.name] = cue;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            var set = MakeAnimatedSet(cues);
            int fighters = AssignToScene(set);
            int items = AssignToItems(cues);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Animated VFX] {cues.Count} effects sliced ({created} new cues in {CueFolder}); " +
                      $"{(set ? "VfxSet_Monk_Animated ready" : "no VfxSet_Monk to copy")}; Animated Set on {fighters} fighter(s) in the open scene; " +
                      $"item effects on {items} prefab(s)." + (missing.Count > 0 ? $" Missing sheets (left blank): {string.Join(", ", missing)}." : "") +
                      " Save the scene. Switch with VfxPlayer → Use Animated VFX or TUNING → Testing.");
        }

        // ───────────── 1. Slice ─────────────

        static Sprite[] Slice(Sheet s)
        {
            var ti = AssetImporter.GetAtPath(s.File) as TextureImporter;
            if (!ti) return null;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.spritePixelsPerUnit = s.ppu;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.maxTextureSize = 4096;
            var ts = new TextureImporterSettings();
            ti.ReadTextureSettings(ts);
            ts.spriteMeshType = SpriteMeshType.Tight;
            ts.spriteGenerateFallbackPhysicsShape = false;
            ti.SetTextureSettings(ts);

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dp = factory.GetSpriteEditorDataProviderFromObject(ti);
            dp.InitSpriteEditorDataProvider();
            var oldIds = new Dictionary<string, GUID>();
            foreach (var r in dp.GetSpriteRects()) oldIds[r.name] = r.spriteID; // keep ids: references survive a re-run

            int texH = s.Rows * s.cellH;
            var rects = new SpriteRect[s.count];
            for (int k = 0; k < s.count; k++)
            {
                int col = k % s.cols, row = k / s.cols;
                string n = $"VFX_Anim_{s.name}_{k:00}";
                rects[k] = new SpriteRect
                {
                    name = n,
                    rect = new Rect(col * s.cellW, texH - (row + 1) * s.cellH, s.cellW, s.cellH),
                    alignment = SpriteAlignment.Custom,
                    pivot = s.pivot,
                    border = Vector4.zero,
                    spriteID = oldIds.TryGetValue(n, out var id) ? id : GUID.Generate(),
                };
            }
            dp.SetSpriteRects(rects);
            var names = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            dp.Apply();
            ti.SaveAndReimport();

            var byName = AssetDatabase.LoadAllAssetsAtPath(s.File).OfType<Sprite>().ToDictionary(x => x.name);
            return rects.Select(r => byName.TryGetValue(r.name, out var sp) ? sp : null).ToArray();
        }

        // ───────────── 2. Cues ─────────────

        static VfxCue MakeCue(Sheet s, Sprite[] sprites, out bool isNew)
        {
            string path = $"{CueFolder}/VFX_Anim_{s.name}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<VfxCue>(path);
            isNew = !cue;
            if (isNew)
            {
                cue = ScriptableObject.CreateInstance<VfxCue>();
                // Start from the effect it replaces: placement, sorting and light flash stay as tuned.
                var old = AssetDatabase.LoadAssetAtPath<VfxCue>($"{OldCueFolder}/{s.copyFrom}.asset");
                if (old)
                {
                    cue.tint = old.tint; cue.alpha = old.alpha;
                    cue.offset = s.name == "Attack1_Dust" ? Vector2.zero : old.offset; // the new dust is drawn centred
                    cue.flipWithFacing = old.flipWithFacing; cue.paintedFacingLeft = old.paintedFacingLeft;
                    cue.rotation = old.rotation;
                    cue.sortingOffset = old.sortingOffset;
                    cue.light = old.light; cue.lightColour = old.lightColour; cue.lightIntensity = old.lightIntensity;
                    cue.lightRadius = old.lightRadius; cue.lightAngle = old.lightAngle; cue.lightDirection = old.lightDirection;
                    cue.lightTime = old.lightTime; cue.lightOffset = old.lightOffset;
                }
                if (s.name == "Hold_Hit") cue.sortingOffset = 5;                  // in front of the item
                if (s.name.StartsWith("Hold_Throw")) cue.flipWithFacing = false;   // turned along the flight instead
                cue.randomRotation = 0f;
                cue.scale = 1f;
                cue.popAndFade = false;
                cue.followOwner = s.follow;
                cue.drift = Vector2.zero;
                AssetDatabase.CreateAsset(cue, path);
            }

            // Frames (with blank frames from the notes), timing and size: refreshed every run.
            var frames = new List<Sprite>();
            for (int k = 0; k < sprites.Length; k++)
            {
                frames.Add(sprites[k]);
                if (s.blanksAfter.Contains(k + 1)) frames.Add(null);
            }
            cue.frames = frames.ToArray();
            cue.sprite = sprites.FirstOrDefault(x => x);
            cue.fps = s.fps;
            cue.loop = s.loop;
            cue.contentSize = s.content;
            cue.lifetime = s.loop ? 1f : frames.Count / s.fps; // loops played as one-shots (previews) show for a second
            EditorUtility.SetDirty(cue);
            return cue;
        }

        // ───────────── 3. Set ─────────────

        static VfxSet MakeAnimatedSet(Dictionary<string, VfxCue> c)
        {
            var set = AssetDatabase.LoadAssetAtPath<VfxSet>(AnimatedSetPath);
            if (!set)
            {
                var source = AssetDatabase.LoadAssetAtPath<VfxSet>(StaticSetPath);
                if (!source) return null;
                set = Object.Instantiate(source);
                set.hit = null; // light hits: the animated impacts replace the old burst (heavy hits / KO keep theirs)
                AssetDatabase.CreateAsset(set, AnimatedSetPath);
            }
            VfxCue C(string n) => c.TryGetValue(n, out var cue) ? cue : null;

            if (set.lightCombo == null || set.lightCombo.Length == 0) set.lightCombo = new VfxSet.AttackVfx[3];
            var first = set.lightCombo[0];
            first.strike = C("Attack1_Strike");
            first.dust = C("Attack1_Dust");
            set.lightCombo[0] = first;
            set.hitGround = C("Attack1_Impact_Ground");
            set.hitAir = C("Attack1_Impact_Slash");
            set.downSpecialHit = C("Attack1_Impact_Slash"); // the slash along the ground for the leg sweep
            set.blockStart = C("Block_Start");
            set.block = C("Block_Idle");
            set.guardBreak = C("Block_Break");
            set.parry = C("Parry_Main");
            set.parryImpact = C("Parry_Impact");
            set.dash = C("Dash");
            set.jump = C("Jump_Up");
            set.land = C("Jump_Land");
            if (set.blockStart) set.blockStart.next = set.block; // previews: form, then idle
            if (C("Hold_Throw_Start")) C("Hold_Throw_Start").next = C("Hold_Throw_Idle");
            EditorUtility.SetDirty(set);
            return set;
        }

        static int AssignToScene(VfxSet set)
        {
            if (!set) return 0;
            int n = 0;
            foreach (var fv in Object.FindObjectsByType<FighterVfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(fv);
                so.FindProperty("animatedSet").objectReferenceValue = set;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return n;
        }

        static int AssignToItems(Dictionary<string, VfxCue> c)
        {
            VfxCue C(string n) => c.TryGetValue(n, out var cue) ? cue : null;
            void Fill(ItemVfx iv)
            {
                var so = new SerializedObject(iv);
                so.FindProperty("animHoldStart").objectReferenceValue = C("Hold_Start");
                so.FindProperty("animHoldIdle").objectReferenceValue = C("Hold_Idle");
                so.FindProperty("animHoldAim").objectReferenceValue = C("Hold_Aim");
                so.FindProperty("animThrowStart").objectReferenceValue = C("Hold_Throw_Start");
                so.FindProperty("animThrowIdle").objectReferenceValue = C("Hold_Throw_Idle");
                so.FindProperty("animHit").objectReferenceValue = C("Hold_Hit");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ItemPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                var vfx = root.GetComponentsInChildren<ItemVfx>(true);
                if (vfx.Length > 0)
                {
                    foreach (var iv in vfx) Fill(iv);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    n++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            // Items placed in the open scene that aren't prefab instances.
            foreach (var iv in Object.FindObjectsByType<ItemVfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!PrefabUtility.IsPartOfPrefabInstance(iv)) { Fill(iv); EditorSceneManager.MarkSceneDirty(iv.gameObject.scene); }
            return n;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
