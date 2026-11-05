#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Create Special Moves
    /// Creates (or reuses) the shoto all-rounder's three directional specials in Assets/_Project/Data/Attacks and assigns
    /// them to every Fighter's Side / Up / Down Special slots:
    ///   Special_ElbowSlide — swipe sideways + tap: gap closer, hitbox out while sliding ~2.2 units
    ///   Special_Uppercut   — swipe up + tap: launcher (Uppercut_Launch: nearly straight up, lands on their feet = juggle)
    ///   Special_LegSweep   — swipe down + tap: 180° sweep, low hitbox centred on the fighter (hits both sides)
    /// Existing assets keep your tuning (only created when missing). Undoable for the scene part. Save the scene after.
    /// Place in: Assets/_Project/Editor/SpecialMovesSetup.cs
    /// </summary>
    public static class SpecialMovesSetup
    {
        const string Folder = "Assets/_Project/Data/Attacks";
        const float F = 1f / 60f; // one frame at 60 fps

        [MenuItem("KungFu Crashout/Create Special Moves", priority = 21)]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data")) AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Attacks");

            // Uppercut's launch: nearly vertical, ~2 units high, lands on its feet (Downed Time 0 = juggle, not a stagger).
            var launch = GetOrCreate<ThrowableData>("Uppercut_Launch", t =>
            {
                t.ApplyThrowDefaults();
                t.launchSpeed = 11f;
                t.launchGravity = 30f;
                t.minLaunchAngle = 80f;
                t.maxLaunchAngle = 88f;
                t.koLaunchMultiplier = 1.3f;
                t.groundBounce = 0f;
                t.skidTime = 0f;
                t.downedTime = 0f;          // raise this to make the uppercut a real stagger (lands Downed)
                t.downedDamageMultiplier = 1f;
                t.getUpInvulnerability = 0f;
                t.maxWallBounces = 0;
                t.wallSplat = false;
                t.impactHitstop = 0.08f;
                t.impactShake = 0.15f;
                t.slowMotionTime = 0f;
                t.hitstun = 0.6f;
            });

            var slide = GetOrCreate<AttackData>("Special_ElbowSlide", a =>
            {
                a.damage = 8f; a.weight = HitWeight.Heavy;
                a.startup = 6 * F; a.active = 12 * F; a.recovery = 18 * F;
                a.hitboxOffset = new Vector2(0.7f, 0.1f); a.hitboxSize = new Vector2(1.0f, 0.8f);
                a.knockback = 2.0f; a.hitstun = 0.5f;
                a.chipDamage = 0.15f; a.blockstun = 0.25f; a.blockPushback = 0.9f;
                a.lunge = 0.2f; a.activeTravel = 2.2f;
            });

            var uppercut = GetOrCreate<AttackData>("Special_Uppercut", a =>
            {
                a.damage = 8f; a.weight = HitWeight.Heavy;
                a.startup = 5 * F; a.active = 6 * F; a.recovery = 24 * F;
                a.hitboxOffset = new Vector2(0.6f, 0.5f); a.hitboxSize = new Vector2(0.9f, 1.4f);
                a.knockback = 0.3f; a.hitstun = 0.6f;
                a.chipDamage = 0.15f; a.blockstun = 0.3f; a.blockPushback = 0.5f;
                a.lunge = 0.15f;
                a.launchOnHit = launch;
                a.launchDirection = new Vector2(0.1f, 1f);
            });

            var sweep = GetOrCreate<AttackData>("Special_LegSweep", a =>
            {
                a.damage = 7f; a.weight = HitWeight.Heavy;
                a.startup = 8 * F; a.active = 6 * F; a.recovery = 20 * F;
                a.hitboxOffset = new Vector2(0f, -0.6f); a.hitboxSize = new Vector2(3.6f, 0.6f); // centred = both sides, low
                a.knockback = 1.0f; a.hitstun = 0.55f;
                a.chipDamage = 0.15f; a.blockstun = 0.3f; a.blockPushback = 0.6f;
                a.lunge = 0f;
            });

            AssetDatabase.SaveAssets();

            // Assign to every fighter.
            Undo.SetCurrentGroupName("Create Special Moves");
            int group = Undo.GetCurrentGroup();
            string report = "";
            foreach (var fighter in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(fighter);
                so.FindProperty("sideSpecial").objectReferenceValue = slide;
                so.FindProperty("upSpecial").objectReferenceValue = uppercut;
                so.FindProperty("downSpecial").objectReferenceValue = sweep;
                so.ApplyModifiedProperties();
                report += $"\n  {fighter.name}: Side = Elbow Slide, Up = Uppercut, Down = Leg Sweep";
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Selection.activeObject = slide;

            Debug.Log($"[Specials] Special moves in {Folder}:{report}\n" +
                      "  Test in the editor: U = Elbow Slide, I = Uppercut, O = Leg Sweep. On the phone: swipe + tap with the other finger.\n" +
                      "  Save the scene (Ctrl+S).");
        }

        static T GetOrCreate<T>(string name, System.Action<T> init) where T : ScriptableObject
        {
            string path = $"{Folder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing; // keep the user's tuning
            var asset = ScriptableObject.CreateInstance<T>();
            init(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
#endif
