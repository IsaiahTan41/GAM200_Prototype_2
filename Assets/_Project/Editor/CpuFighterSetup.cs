#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.AI;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → CPU Fighter → Use Monk Copy / Use Rushdown   (open FightScene first)
    ///
    /// USE MONK COPY — the CPU becomes an exact copy of the Player's Monk, for testing while Rushdown's art is in progress:
    ///   • Visual: Player/Visual is copied under the Opponent as "Visual_Monk" (poses incl. top / down hit, the Animator
    ///     and clips, the clip map, the animation-keyed Hitbox, CarryPoint, ThrowPoint). The Opponent's own Visual is kept,
    ///     renamed "Visual_Rushdown" and switched off. The CPU keeps its red outline so the mirror match reads.
    ///   • Fighter: every Monk setting on the Player's Fighter is copied to the Opponent (move set, jump, dash, parry and
    ///     catch windows, knockback, downed angle…). Not copied: who it fights, the stage, the animator link, the name,
    ///     and the debug toggles (hitboxes / state logs). The Opponent's ORIGINAL settings are saved first (once) to
    ///     Data/CpuFighter/Opponent_Original.prefab, so Use Rushdown can put them back exactly.
    ///   • Wiring: the Opponent's Fighter Animator → Visual_Monk's; its Item Holder's Carry Point / Throw Point →
    ///     Visual_Monk's.
    ///   • Archetype: the Opponent loads the Player's archetype (Nam-pi: the Monk attack data, so the clips play),
    ///     Ignore Roster Pick on (the Roster's CPU pick can't swap in Rushdown moves), HUD name "NAM-PI · CPU".
    ///   Re-run it after changing the Player's Monk to refresh the copy.
    ///
    /// USE RUSHDOWN — exactly back to how the Opponent was: Visual_Monk removed, the original Visual back on (and named
    /// "Visual" again), the saved Fighter settings restored, Rushdown archetype, Roster pick respected, no name suffix.
    ///
    /// The behaviour-tree AI needs no change: it drives whichever fighter it's on through the same commands as the
    /// player's input, and reads its moves (reach, heavy, specials) from that fighter.
    /// Place in: Assets/_Project/Editor/CpuFighterSetup.cs
    /// </summary>
    public static class CpuFighterSetup
    {
        const string Menu = "Window/KungFu Crashout/CPU Fighter/";
        const string SnapshotFolder = "Assets/_Project/Data/CpuFighter";
        const string SnapshotPath = SnapshotFolder + "/Opponent_Original.prefab";
        const string MonkVisual = "Visual_Monk";
        const string RushdownVisual = "Visual_Rushdown";
        const string CpuSuffix = " · CPU";

        /// <summary>Fighter settings that are wiring, identity or debug, never character data: never copied.</summary>
        static readonly HashSet<string> NotCopied = new HashSet<string>
        {
            "m_Script", "opponent", "stage", "fighterAnimator", "displayName", "showHitboxesInGame", "logStateChanges",
        };

        // ═════════════ Use Monk Copy ═════════════

        [MenuItem(Menu + "Use Monk Copy", priority = 10090)]
        public static void UseMonkCopy()
        {
            if (!FindFighters(out var player, out var opponent)) return;
            var playerVisual = player.transform.Find("Visual");
            if (!playerVisual) { Fail("The Player has no child called \"Visual\"."); return; }

            Undo.SetCurrentGroupName("CPU Fighter: Use Monk Copy");
            int group = Undo.GetCurrentGroup();
            var log = new List<string>();

            // 1. Save the Opponent's own settings, once (before anything is overwritten).
            if (!AssetDatabase.LoadAssetAtPath<Fighter>(SnapshotPath))
            {
                SaveSnapshot(opponent);
                log.Add($"Opponent's original Fighter settings saved to {SnapshotPath}");
            }

            // 2. Visuals: park the original, replace any old copy with a fresh one.
            var original = opponent.transform.Find(RushdownVisual) ?? opponent.transform.Find("Visual");
            Component outlineToCopy = null;
            if (original)
            {
                Undo.RecordObject(original.gameObject, "Park Rushdown visual");
                original.name = RushdownVisual;
                original.gameObject.SetActive(false);
                outlineToCopy = original.GetComponent<SpriteOutline>();
            }
            var oldCopy = opponent.transform.Find(MonkVisual);
            if (oldCopy) Undo.DestroyObjectImmediate(oldCopy.gameObject);

            var copy = Object.Instantiate(playerVisual.gameObject, opponent.transform);
            copy.name = MonkVisual;
            copy.transform.localPosition = playerVisual.localPosition;
            copy.transform.localRotation = playerVisual.localRotation;
            copy.transform.localScale = playerVisual.localScale;
            copy.SetActive(true);
            Undo.RegisterCreatedObjectUndo(copy, "Monk visual copy");

            var pose = copy.GetComponent<FighterPoseView>();
            if (pose) SetRef(pose, "fighter", opponent);
            if (outlineToCopy && !copy.GetComponent<SpriteOutline>()
                && UnityEditorInternal.ComponentUtility.CopyComponent(outlineToCopy))
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(copy);
            log.Add($"'{MonkVisual}' copied from Player/Visual (poses, Animator + clips, Hitbox, CarryPoint, ThrowPoint); " +
                    $"original visual parked as '{RushdownVisual}' (off)");

            // 3. Fighter settings: the Monk's.
            int copied = CopyFighterSettings(player, opponent);
            SetRef(opponent, "fighterAnimator", copy.GetComponent<FighterAnimator>());
            log.Add($"{copied} Fighter settings copied from the Player; Fighter Animator → {MonkVisual}");

            // 4. Item hands.
            WireItemHolder(opponent, copy.transform);
            log.Add("Item Holder → Visual_Monk's CarryPoint / ThrowPoint");

            // 5. Archetype: the Player's (the Monk's attack data, which the clips are mapped to).
            var playerLoadout = player.GetComponent<ArchetypeLoadout>();
            var monkArchetype = playerLoadout ? Ref(playerLoadout, "fallback") as FighterArchetype : null;
            if (!monkArchetype) monkArchetype = FindAsset<FighterArchetype>("Archetype_1_NamPi");
            SetLoadout(opponent, monkArchetype, ignoreRosterPick: true, suffix: CpuSuffix);
            log.Add($"Opponent loads '{(monkArchetype ? monkArchetype.name : "none")}', Ignore Roster Pick ON, name suffix '{CpuSuffix}'");

            Finish(opponent, group, "CPU is now a copy of the Monk", log);
        }

        // ═════════════ Use Rushdown ═════════════

        [MenuItem(Menu + "Use Rushdown", priority = 10091)]
        public static void UseRushdown()
        {
            if (!FindFighters(out _, out var opponent)) return;
            var original = opponent.transform.Find(RushdownVisual) ?? opponent.transform.Find("Visual");
            if (!original) { Fail("The Opponent has no 'Visual_Rushdown' (or 'Visual') to switch back to."); return; }

            Undo.SetCurrentGroupName("CPU Fighter: Use Rushdown");
            int group = Undo.GetCurrentGroup();
            var log = new List<string>();

            // Remove the copy entirely: Fighter auto-finds an animator even on switched-off children.
            var copy = opponent.transform.Find(MonkVisual);
            if (copy) { Undo.DestroyObjectImmediate(copy.gameObject); log.Add($"'{MonkVisual}' removed"); }

            Undo.RecordObject(original.gameObject, "Restore Rushdown visual");
            original.name = "Visual";
            original.gameObject.SetActive(true);
            log.Add("Original visual back on, named 'Visual'");

            var snapshot = AssetDatabase.LoadAssetAtPath<Fighter>(SnapshotPath);
            if (snapshot)
            {
                int restored = CopyFighterSettings(snapshot, opponent);
                log.Add($"{restored} Fighter settings restored from {SnapshotPath}");
            }
            else log.Add("No saved settings found (Use Monk Copy was never run): Fighter settings left as they are");
            SetRef(opponent, "fighterAnimator", original.GetComponent<FighterAnimator>());

            WireItemHolder(opponent, original);
            log.Add("Item Holder → the original visual's CarryPoint / ThrowPoint (if it has them)");

            var rushdown = FindAsset<FighterArchetype>("Archetype_2_Rushdown");
            SetLoadout(opponent, rushdown, ignoreRosterPick: false, suffix: "");
            log.Add($"Opponent loads '{(rushdown ? rushdown.name : "none")}', Roster pick respected again, no name suffix");

            Finish(opponent, group, "CPU is Rushdown again", log);
        }

        [MenuItem(Menu + "Use Monk Copy", true)]
        [MenuItem(Menu + "Use Rushdown", true)]
        static bool NotPlaying() => !EditorApplication.isPlayingOrWillChangePlaymode;

        // ═════════════ Helpers ═════════════

        static bool FindFighters(out Fighter player, out Fighter opponent)
        {
            var all = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            player = all.FirstOrDefault(f => f.CompareTag("Player")) ?? all.FirstOrDefault(f => f.name == "Player");
            var p = player;
            opponent = all.FirstOrDefault(f => f != p && f.GetComponent<BehaviourTreeAI>())
                       ?? all.FirstOrDefault(f => f != p && f.name == "Opponent");
            if (player && opponent) return true;
            Fail("Open FightScene first: it needs the Player (tagged Player) and the Opponent (with the behaviour-tree AI).");
            return false;
        }

        /// <summary>Copy every character setting from one Fighter to another (skips wiring, identity, debug, scene links).</summary>
        static int CopyFighterSettings(Fighter from, Fighter to)
        {
            var src = new SerializedObject(from);
            var dst = new SerializedObject(to);
            Undo.RecordObject(to, "Copy Fighter settings");
            int n = 0;
            var prop = src.GetIterator();
            bool enter = true;
            while (prop.NextVisible(enter))
            {
                enter = false; // top-level fields only; whole arrays / structs are copied as one
                if (NotCopied.Contains(prop.name)) continue;
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue &&
                    !EditorUtility.IsPersistent(prop.objectReferenceValue)) continue; // a scene object: not character data
                var current = dst.FindProperty(prop.propertyPath);
                if (current != null && current.propertyType == SerializedPropertyType.ObjectReference &&
                    current.objectReferenceValue && !EditorUtility.IsPersistent(current.objectReferenceValue))
                    continue; // the target points at something in its own scene: keep that link
                dst.CopyFromSerializedProperty(prop);
                n++;
            }
            dst.ApplyModifiedProperties();
            return n;
        }

        /// <summary>Saves the Opponent's Fighter settings into a prefab (no scene links), for Use Rushdown.</summary>
        static void SaveSnapshot(Fighter opponent)
        {
            Folder(SnapshotFolder);
            var temp = new GameObject("Opponent_Original (saved Fighter settings)");
            try
            {
                var f = temp.AddComponent<Fighter>(); // brings its required SpriteRenderer + Health along
                CopyFighterSettings(opponent, f);
                PrefabUtility.SaveAsPrefabAsset(temp, SnapshotPath);
            }
            finally { Object.DestroyImmediate(temp); }
        }

        static void WireItemHolder(Fighter fighter, Transform visual)
        {
            var holder = fighter.GetComponent<ItemHolder>();
            if (!holder) return;
            var so = new SerializedObject(holder);
            Undo.RecordObject(holder, "Wire item hands");
            so.FindProperty("carryPoint").objectReferenceValue = FindDeep(visual, "CarryPoint");
            var tp = so.FindProperty("throwPoint");
            if (tp != null) tp.objectReferenceValue = FindDeep(visual, "ThrowPoint");
            so.ApplyModifiedProperties();
        }

        static void SetLoadout(Fighter fighter, FighterArchetype archetype, bool ignoreRosterPick, string suffix)
        {
            var loadout = fighter.GetComponent<ArchetypeLoadout>();
            if (!loadout) return;
            var so = new SerializedObject(loadout);
            Undo.RecordObject(loadout, "CPU archetype");
            if (archetype) so.FindProperty("fallback").objectReferenceValue = archetype;
            var ig = so.FindProperty("ignoreRosterPick");
            if (ig != null) ig.boolValue = ignoreRosterPick;
            var sx = so.FindProperty("nameSuffix");
            if (sx != null) sx.stringValue = suffix;
            so.ApplyModifiedProperties();
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            Undo.RecordObject(target, "Set " + field);
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        static Object Ref(Object target, string field) => new SerializedObject(target).FindProperty(field)?.objectReferenceValue;

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static T FindAsset<T>(string name) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<T>(p);
            }
            return null;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void Finish(Fighter opponent, int group, string headline, List<string> log)
        {
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(opponent.gameObject.scene);
            Selection.activeObject = opponent.gameObject;
            Debug.Log($"[CPU Fighter] {headline}:\n  • " + string.Join("\n  • ", log) +
                      "\nSave the scene (Ctrl+S). Ctrl+Z undoes it.");
        }

        static void Fail(string message) => EditorUtility.DisplayDialog("CPU Fighter", message, "OK");
    }
}
#endif
