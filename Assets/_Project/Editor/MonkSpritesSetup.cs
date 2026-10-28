#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.DebugTools;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Monk Sprites (Player)
    /// 1. Imports every frame in Art/Characters/Monk_Sprites the same way as the draft Fighter art: Sprite (Single),
    ///    Full Rect, Bilinear, no mipmaps, max 2048, pivot on the ground between the feet. Pixels Per Unit is 350 so the
    ///    Monk stands the same height on screen as Fighter_Stance (~2 units = the hurtbox).
    /// 2. Fills the PLAYER's Visual → FighterPoseView slots (the Opponent keeps the draft Fighter art):
    ///      Idle, Block, BasicAttack → Attack + Heavy (Elbow Slide falls back to it), TopAttack → Up Special (Uppercut),
    ///      DownAttack → Down Special (Leg Sweep), Throw, Hurt → Hitstun, Downed (KO falls back to it), Jump → Airborne,
    ///      Dash. Wind Up / Recovery / Carry are cleared (they fall back to the pose held / Idle).
    ///    Art Faces Left = off (the Monk is drawn facing right). Fighter → Downed Tip Angle = 0 (the Downed frame is
    ///    already lying down, so it isn't rotated as well).
    /// Re-run after replacing a PNG. Undoable for the scene part. Save the scene afterwards.
    /// Don't re-run "Set Up Fighter Poses" afterwards — it puts the draft art back on both fighters.
    /// Place in: Assets/_Project/Editor/MonkSpritesSetup.cs
    /// </summary>
    public static class MonkSpritesSetup
    {
        const string Folder = "Assets/_Project/Art/Characters/Monk_Sprites";

        // Monk Idle is ~704 px head-to-feet; Fighter_Stance is ~2.02 units tall (2019 px at PPU 1000) → 704 / 2.02 ≈ 350.
        const float PixelsPerUnit = 350f;

        // Frame → pivot (fraction of the canvas): x = between the feet (body centre for Dash / Jump / Downed),
        // y = the ground line. Measured from the PNGs; nudge here if a pose slides or floats.
        static readonly (string name, Vector2 pivot)[] Frames =
        {
            ("Monk_Idle",        new Vector2(0.235f, 0.071f)),
            ("Monk_Block",       new Vector2(0.528f, 0.075f)),
            ("Monk_BasicAttack", new Vector2(0.516f, 0.061f)),
            ("Monk_TopAttack",   new Vector2(0.531f, 0.062f)),
            ("Monk_DownAttack",  new Vector2(0.235f, 0.071f)),
            ("Monk_Throw",       new Vector2(0.238f, 0.072f)),
            ("Monk_Hurt",        new Vector2(0.521f, 0.082f)),
            ("Monk_Downed",      new Vector2(0.479f, 0.063f)),
            ("Monk_Jump",        new Vector2(0.497f, 0.085f)),
            ("Monk_Dash",        new Vector2(0.712f, 0.077f)),
        };

        [MenuItem("KungFu Crashout/Set Up Monk Sprites (Player)", priority = 63)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Monk Sprites", "Stop Play mode first.", "OK"); return; }

            // ── 1. Import settings ──
            string missing = "";
            foreach (var (name, pivot) in Frames)
            {
                string path = FindFrame(name);
                var ti = path != null ? AssetImporter.GetAtPath(path) as TextureImporter : null;
                if (!ti) { missing += $"\n  {name}"; continue; }

                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = PixelsPerUnit;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.filterMode = FilterMode.Bilinear;

                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteAlignment = (int)SpriteAlignment.Custom;
                s.spritePivot = pivot;
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();
            }

            Sprite Load(string name)
            {
                string p = FindFrame(name);
                return p != null ? AssetDatabase.LoadAssetAtPath<Sprite>(p) : null;
            }

            var idle = Load("Monk_Idle");
            if (!idle)
            {
                EditorUtility.DisplayDialog("Monk Sprites", $"Couldn't load Monk_Idle from {Folder}.", "OK");
                return;
            }
            var basic = Load("Monk_BasicAttack");

            // ── 2. The Player's Visual ──
            var player = FindPlayer();
            if (!player)
            {
                EditorUtility.DisplayDialog("Monk Sprites", "No Player fighter found in the open scene (open FightScene).", "OK");
                return;
            }
            var visualT = player.transform.Find("Visual");
            var view = visualT ? visualT.GetComponent<FighterPoseView>() : null;
            if (!view)
            {
                EditorUtility.DisplayDialog("Monk Sprites",
                    $"'{player.name}' has no Visual child with a FighterPoseView. Run Set Up Fighter Poses once first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Set Up Monk Sprites");
            int group = Undo.GetCurrentGroup();

            var so = new SerializedObject(view);
            void Slot(string prop, Sprite sprite) => so.FindProperty(prop).objectReferenceValue = sprite;
            Slot("idle", idle);
            Slot("block", Load("Monk_Block"));
            Slot("windUp", null);
            Slot("attack", basic);
            Slot("heavyAttack", basic);
            Slot("sideSpecialPose", null);                // Elbow Slide → falls back to Heavy Attack (BasicAttack)
            Slot("upSpecialPose", Load("Monk_TopAttack"));
            Slot("downSpecialPose", Load("Monk_DownAttack"));
            Slot("recovery", null);
            Slot("throwPose", Load("Monk_Throw"));
            Slot("carry", null);                          // → Idle
            Slot("hitstun", Load("Monk_Hurt"));
            Slot("downed", Load("Monk_Downed"));
            Slot("airborne", Load("Monk_Jump"));
            Slot("dash", Load("Monk_Dash"));
            Slot("ko", null);                             // → Downed
            so.FindProperty("artFacesLeft").boolValue = false;
            so.ApplyModifiedProperties();

            var sr = view.GetComponent<SpriteRenderer>();
            if (sr)
            {
                Undo.RecordObject(sr, "Monk preview");
                sr.sprite = idle;
                sr.flipX = false; // preview facing right; the pose view flips it at runtime
            }

            var fso = new SerializedObject(player);
            var tip = fso.FindProperty("downedTipAngle");
            if (tip != null) { tip.floatValue = 0f; fso.ApplyModifiedProperties(); }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeObject = view.gameObject;

            Debug.Log($"[Monk] {Frames.Length} frames imported at PPU {PixelsPerUnit} with ground pivots. '{player.name}' now uses the Monk " +
                      "(Art Faces Left off, Downed Tip Angle 0). Opponent unchanged. Save the scene (Ctrl+S)." +
                      (missing.Length > 0 ? "\n  Not found (slot left empty):" + missing : ""));
        }

        /// <summary>
        /// The fighter tagged "Player" or named "Player…". Fallback: a fighter not named Enemy/Opponent.
        /// (Both fighters carry a TrainingDummy in this scene — the Player's is set to idle — so that can't tell them apart.)
        /// </summary>
        static Fighter FindPlayer()
        {
            var all = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var f in all)
                if (f.CompareTag("Player")) return f;
            foreach (var f in all)
                if (f.name.ToLowerInvariant().Contains("player")) return f;
            foreach (var f in all)
            {
                string n = f.name.ToLowerInvariant();
                if (!n.Contains("enemy") && !n.Contains("opponent")) return f;
            }
            return null;
        }

        /// <summary>Path of Monk_X.png / .PNG in the folder (any extension case).</summary>
        static string FindFrame(string name)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { Folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return null;
        }
    }
}
#endif
