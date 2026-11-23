using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using KungFuCrashout.AI;
using KungFuCrashout.Controls;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;
using Object = UnityEngine.Object;

namespace KungFuCrashout.UI
{
    public enum TuneApply { Live, NextRound }
    public enum TuneDisplay { Number, Percent01, Percent100, OnOff }

    /// <summary>One field on one object that a tuning value writes to.</summary>
    public sealed class TuneTarget
    {
        public readonly Object owner;   // the component or data asset that holds it
        public readonly object obj;     // the object the field is on (the owner, or a nested settings class)
        public readonly FieldInfo field;
        /// <summary>For a Vector2 field: 0 = x, 1 = y. -1 = a plain number / bool field.</summary>
        public readonly int component;

        public TuneTarget(Object owner, object obj, FieldInfo field, int component = -1)
        {
            this.owner = owner;
            this.obj = obj;
            this.field = field;
            this.component = component;
        }

        public bool IsAsset => owner is ScriptableObject;
        public string Key => $"{owner.GetInstanceID()}/{RuntimeHelpers.GetHashCode(obj)}/{field.Name}/{component}";

        public float Read()
        {
            var v = field.GetValue(obj);
            if (v is bool b) return b ? 1f : 0f;
            if (v is int i) return i;
            if (v is Vector2 v2) return v2[Mathf.Clamp(component, 0, 1)];
            return Convert.ToSingle(v, CultureInfo.InvariantCulture);
        }

        public void Write(float v)
        {
            var t = field.FieldType;
            if (t == typeof(Vector2))
            {
                var v2 = (Vector2)field.GetValue(obj);
                v2[Mathf.Clamp(component, 0, 1)] = v;
                field.SetValue(obj, v2);
                return;
            }
            object value = t == typeof(bool) ? v >= 0.5f
                         : t == typeof(int) ? Mathf.RoundToInt(v)
                         : (object)v;
            field.SetValue(obj, value);
        }
    }

    /// <summary>One tunable value: what the TUNING tab shows, and where it writes.</summary>
    public sealed class TuneDef
    {
        public string id, section, group, label, unit;
        public float min, max, step;
        public TuneApply apply;
        public TuneDisplay display;
        /// <summary>Re-applied every half second, for objects that appear later (spawned items).</summary>
        public bool sweep;
        /// <summary>
        /// A testing toggle (TESTING section): works on the tab like any value, but is never exported to JSON, never imported,
        /// and not counted as a tuning change, so it stays out of the tuning journal / tracker.
        /// </summary>
        public bool testOnly;
        public Func<IEnumerable<TuneTarget>> targets;
    }

    /// <summary>
    /// M2D02 TUNING tab, step 1: the list of tunable values and how each is read and written (no code changes to the
    /// systems themselves: fields are written by name). Values the tester changes are remembered on the device
    /// (PlayerPrefs "kfc.tune.&lt;id&gt;"), like the other pause-menu options, and applied again on every load.
    ///
    /// Data assets (moves, item hit data) are shared with the project: in the Editor their original values are put
    /// back when Play mode ends, so tuning in Play mode never changes the project's assets.
    ///
    /// Place in: Assets/_Project/Scripts/UI/TuningRegistry.cs
    /// </summary>
    public static class TuningRegistry
    {
        public static readonly string[] Sections = { "Rules", "Moves", "Items", "Stage", "Input", "CPU", "Testing" };

        const string Prefix = "kfc.tune.";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static List<TuneDef> defs;
        static Dictionary<string, TuneDef> byId;

        public static IReadOnlyList<TuneDef> Defs => defs ??= Build();

        public static TuneDef Find(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, TuneDef>();
                foreach (var d in Defs) byId[d.id] = d;
            }
            return id != null && byId.TryGetValue(id, out var def) ? def : null;
        }

        // ───────────── Values ─────────────

        // Asset values before the first change this session (put back when Play mode ends; also the reset value).
        static readonly Dictionary<string, (TuneTarget target, float value)> assetOriginals =
            new Dictionary<string, (TuneTarget, float)>();
        // Scene values as loaded (the reset value for scene objects). Refilled on every scene load.
        static readonly Dictionary<string, float> sceneDefaults = new Dictionary<string, float>();

        public static string Key(TuneDef d) => Prefix + d.id;
        public static bool IsChanged(TuneDef d) => PlayerPrefs.HasKey(Key(d));

        static TuneTarget First(TuneDef d)
        {
            foreach (var t in d.targets()) return t;
            return null;
        }

        /// <summary>The value in the game now (NaN = nothing in this scene uses it).</summary>
        public static float Current(TuneDef d)
        {
            var t = First(d);
            return t != null ? t.Read() : float.NaN;
        }

        /// <summary>The value before any tuning (the project's own value).</summary>
        public static float Default(TuneDef d)
        {
            var t = First(d);
            if (t == null) return float.NaN;
            if (!Application.isPlaying) return t.Read(); // in the Editor the project's value is what's there now
            if (t.IsAsset && assetOriginals.TryGetValue(t.Key, out var o)) return o.value;
            return sceneDefaults.TryGetValue(d.id, out var v) ? v : t.Read();
        }

        public static void Set(TuneDef d, float value, bool save = true)
        {
            foreach (var t in d.targets())
            {
                if (t.IsAsset && !assetOriginals.ContainsKey(t.Key)) assetOriginals[t.Key] = (t, t.Read());
                t.Write(value);
            }
            if (save) PlayerPrefs.SetFloat(Key(d), value);
        }

        public static void Reset(TuneDef d)
        {
            PlayerPrefs.DeleteKey(Key(d));
            float v = Default(d);
            if (float.IsNaN(v)) return;
            foreach (var t in d.targets()) t.Write(v);
        }

        /// <summary>Forgets every saved tab value on this device (the game keeps running with its current values).</summary>
        public static void ClearSaved()
        {
            foreach (var d in Defs) PlayerPrefs.DeleteKey(Key(d));
            PlayerPrefs.Save();
        }

        public static bool AnySaved()
        {
            foreach (var d in Defs) if (IsChanged(d)) return true;
            return false;
        }

        /// <summary>Name of the data asset the value lives on (moves, launch, item hit data); empty for scene values.</summary>
        public static string AssetName(TuneDef d)
        {
            var t = First(d);
            return t != null && t.IsAsset ? t.owner.name : "";
        }

        /// <summary>The fighter whose move set the Moves values belong to.</summary>
        public static string PlayerName()
        {
            var p = Player();
            return p ? p.name : "";
        }

        public static void ResetAll()
        {
            foreach (var d in Defs) Reset(d);
            PlayerPrefs.Save();
        }

        /// <summary>A scene has loaded: note its own values (the reset values), then apply the saved tuning.</summary>
        public static void OnSceneLoaded()
        {
            sceneDefaults.Clear();
            foreach (var d in Defs)
            {
                var t = First(d);
                if (t != null && !t.IsAsset) sceneDefaults[d.id] = t.Read();
            }
            ApplySaved();
        }

        public static void ApplySaved()
        {
            foreach (var d in Defs)
                if (IsChanged(d)) Set(d, PlayerPrefs.GetFloat(Key(d)), save: false);
        }

        /// <summary>Re-applies the values marked Sweep (spawned items pick up the tuned values).</summary>
        public static void Sweep()
        {
            foreach (var d in Defs)
                if (d.sweep && IsChanged(d)) Set(d, PlayerPrefs.GetFloat(Key(d)), save: false);
        }

        public static string Format(TuneDef d, float v)
        {
            if (float.IsNaN(v)) return "—";
            switch (d.display)
            {
                case TuneDisplay.OnOff: return v >= 0.5f ? "ON" : "OFF";
                case TuneDisplay.Percent01: return Mathf.RoundToInt(v * 100f) + "%";
                case TuneDisplay.Percent100: return Mathf.RoundToInt(v) + "%";
            }
            int decimals = 0;
            while (decimals < 3 && Mathf.Abs(d.step * Mathf.Pow(10f, decimals) - Mathf.Round(d.step * Mathf.Pow(10f, decimals))) > 0.001f)
                decimals++;
            string s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(d.unit) ? s : s + " " + d.unit;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void HookPlayMode()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) RestoreAssets();
                if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode) assetOriginals.Clear();
            };
        }

        /// <summary>Puts the project's data assets back as they were before Play mode.</summary>
        static void RestoreAssets()
        {
            foreach (var o in assetOriginals.Values)
                if (o.target.owner) o.target.Write(o.value);
            if (assetOriginals.Count > 0) Debug.Log($"[Tuning] Restored {assetOriginals.Count} data-asset value(s) to their project values.");
            assetOriginals.Clear();
        }
#endif

        // ───────────── Where each value lives ─────────────

        static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Flags);
                if (f != null) return f;
            }
            return null;
        }

        static T[] All<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

        /// <summary>A field on every component of type T in the scene.</summary>
        static IEnumerable<TuneTarget> On<T>(string field) where T : Component
        {
            var f = Field(typeof(T), field);
            if (f == null) yield break;
            foreach (var c in All<T>()) yield return new TuneTarget(c, c, f);
        }

        /// <summary>A field on every fighter's component, the player's first (so the row shows the player's value).</summary>
        static IEnumerable<TuneTarget> OnFighters<T>(string field) where T : Component
        {
            var f = Field(typeof(T), field);
            if (f == null) yield break;
            var player = Player();
            var first = player ? player.GetComponent<T>() : null;
            if (first) yield return new TuneTarget(first, first, f);
            foreach (var c in All<T>()) if (c != first) yield return new TuneTarget(c, c, f);
        }

        /// <summary>The field on the human player's fighter only.</summary>
        static IEnumerable<TuneTarget> OnPlayer<T>(string field) where T : Component
        {
            var f = Field(typeof(T), field);
            var player = Player();
            var c = player ? player.GetComponent<T>() : null;
            if (f != null && c) yield return new TuneTarget(c, c, f);
        }

        /// <summary>The field on every fighter except the human player (the CPU).</summary>
        static IEnumerable<TuneTarget> OnCpu<T>(string field) where T : Component
        {
            var f = Field(typeof(T), field);
            if (f == null) yield break;
            var player = Player();
            foreach (var c in All<T>())
                if (!player || c.gameObject != player.gameObject) yield return new TuneTarget(c, c, f);
        }

        static Fighter Player()
        {
            Fighter any = null, notAi = null;
            foreach (var f in All<Fighter>())
            {
                if (f.name.ToLowerInvariant().Contains("player")) return f;
                if (!notAi && !f.GetComponent<BehaviourTreeAI>()) notAi = f;
                if (!any) any = f;
            }
            return notAi ? notAi : any;
        }

        static AttackData MoveOf(Fighter f, string slot)
        {
            object Get(string name) => Field(typeof(Fighter), name)?.GetValue(f);
            AttackData At(string name, int i) => Get(name) is AttackData[] a && i < a.Length ? a[i] : null;

            switch (slot)
            {
                case "light1": return At("lightCombo", 0);
                case "light2": return At("lightCombo", 1);
                case "light3": return At("lightCombo", 2);
                case "heavy": return Get("heavyAttack") as AttackData;
                case "side": return Get("sideSpecial") as AttackData;
                case "up": return Get("upSpecial") as AttackData;
                case "down": return Get("downSpecial") as AttackData;
                case "air1": return At("airCombo", 0) ?? Get("airAttack") as AttackData;
                case "air2": return At("airCombo", 1);
                case "air3": return At("airCombo", 2);
            }
            return null;
        }

        /// <summary>The player's move asset (shared with the opponent when both use the same move set).</summary>
        static IEnumerable<TuneTarget> Move(string slot, string field)
        {
            var p = Player();
            var a = p ? MoveOf(p, slot) : null;
            var f = Field(typeof(AttackData), field);
            if (a && f != null) yield return new TuneTarget(a, a, f);
        }

        /// <summary>A Vector2 component on the player's move asset (0 = x, 1 = y).</summary>
        static IEnumerable<TuneTarget> MoveVector(string slot, string field, int component)
        {
            var p = Player();
            var a = p ? MoveOf(p, slot) : null;
            var f = Field(typeof(AttackData), field);
            if (a && f != null) yield return new TuneTarget(a, a, f, component);
        }

        /// <summary>The launch data a move points to (Launch On Hit, e.g. the Uppercut's Uppercut_Launch).</summary>
        static IEnumerable<TuneTarget> Launcher(string slot, string field)
        {
            var p = Player();
            var a = p ? MoveOf(p, slot) : null;
            var launch = a ? a.launchOnHit : null;
            var f = Field(typeof(ThrowableData), field);
            if (launch && f != null) yield return new TuneTarget(launch, launch, f);
        }

        static IEnumerable<Carryable> ItemsOf(ItemDurability.Weight weight)
        {
            foreach (var c in All<Carryable>())
            {
                var d = c.GetComponent<ItemDurability>();
                if (d && d.ItemWeight == weight) yield return c;
            }
        }

        /// <summary>The hit data of every item in a weight class (one shared asset after Apply Light-Heavy Item Base).</summary>
        static IEnumerable<TuneTarget> ItemHit(ItemDurability.Weight weight, string field)
        {
            var f = Field(typeof(ThrowableData), field);
            if (f == null) yield break;
            var seen = new HashSet<ThrowableData>();
            foreach (var c in ItemsOf(weight))
                if (c.HitData && seen.Add(c.HitData)) yield return new TuneTarget(c.HitData, c.HitData, f);
        }

        static IEnumerable<TuneTarget> ItemProp(ItemDurability.Weight weight, string field)
        {
            var f = Field(typeof(Carryable), field);
            if (f == null) yield break;
            foreach (var c in ItemsOf(weight)) yield return new TuneTarget(c, c, f);
        }

        static IEnumerable<TuneTarget> Gesture(string field)
        {
            var settingsField = Field(typeof(GestureArbiterInput), "settings");
            if (settingsField == null) yield break;
            foreach (var g in All<GestureArbiterInput>())
            {
                var s = settingsField.GetValue(g);
                var f = s != null ? Field(s.GetType(), field) : null;
                if (f != null) yield return new TuneTarget(g, s, f);
            }
        }

        static IEnumerable<TuneTarget> Cpu(string tier, string field)
        {
            var profileField = Field(typeof(BehaviourTreeAI), tier);
            if (profileField == null) yield break;
            foreach (var ai in All<BehaviourTreeAI>())
            {
                var p = profileField.GetValue(ai);
                var f = p != null ? Field(p.GetType(), field) : null;
                if (f != null) yield return new TuneTarget(ai, p, f);
            }
        }

        // ───────────── The list (plan section 5, Tier 1) ─────────────

        static List<TuneDef> Build()
        {
            var list = new List<TuneDef>();

            void Add(string id, string section, string group, string label, string unit,
                     float min, float max, float step, TuneApply apply, Func<IEnumerable<TuneTarget>> targets,
                     TuneDisplay display = TuneDisplay.Number, bool sweep = false, bool testOnly = false) =>
                list.Add(new TuneDef
                {
                    id = id, section = section, group = group, label = label, unit = unit,
                    min = min, max = max, step = step, apply = apply, targets = targets, display = display, sweep = sweep,
                    testOnly = testOnly,
                });

            const TuneApply Live = TuneApply.Live, Next = TuneApply.NextRound;

            // Rules & fighter
            Add("rules.roundTime", "Rules", "Round", "Round time", "s", 30, 180, 5, Next, () => On<MatchManager>("roundTime"));
            Add("fighters.maxHealth", "Rules", "Health (both fighters)", "Max health", "", 50, 300, 10, Next, () => OnFighters<Health>("maxHealth"));
            Add("fighters.redRecoverDelay", "Rules", "Health (both fighters)", "Red health: wait before recovering", "s", 0, 15, 0.5f, Live, () => OnFighters<Health>("recoverDelay"));
            Add("fighters.redRecoverRate", "Rules", "Health (both fighters)", "Red health: recovery per second", "/s", 0, 30, 1, Live, () => OnFighters<Health>("recoverRate"));
            Add("fighters.staminaMax", "Rules", "Stamina (both fighters)", "Stamina max", "", 20, 300, 10, Next, () => OnFighters<Stamina>("maxStamina"));
            Add("fighters.staminaRegen", "Rules", "Stamina (both fighters)", "Stamina refill per second", "/s", 0, 100, 5, Live, () => OnFighters<Stamina>("regenPerSecond"));
            Add("rules.parryWindowFrames", "Rules", "Defence and combos", "Parry window (melee)", "f", 1, 30, 1, Live, () => OnFighters<Fighter>("parryWindowFrames"));
            Add("rules.thrownParryWindowFrames", "Rules", "Defence and combos", "Parry / catch window (thrown items)", "f", 1, 30, 1, Live, () => OnFighters<Fighter>("thrownParryWindowFrames"));
            Add("rules.maxJuggleHits", "Rules", "Defence and combos", "Juggle limit", "hits", 1, 10, 1, Live, () => OnFighters<Fighter>("maxJuggleHits"));
            Add("rules.stunnedBounceSpeed", "Rules", "Defence and combos", "Bounce on juggled hit", "", 0, 12, 0.25f, Live, () => OnFighters<Fighter>("stunnedBounceSpeed"));

            // Moves (the player's move set)
            var moves = new (string slot, string name)[]
            {
                ("light1", "Light 1"), ("light2", "Light 2"), ("light3", "Light 3 (finisher)"), ("heavy", "Heavy"),
                ("side", "Side special"), ("up", "Up special"), ("down", "Down special"),
                ("air1", "Air 1"), ("air2", "Air 2"), ("air3", "Air 3"),
            };
            foreach (var (slot, name) in moves)
            {
                Add($"moves.{slot}.damage", "Moves", name, "Damage", "", 0, 40, 0.5f, Live, () => Move(slot, "damage"));
                Add($"moves.{slot}.hitstun", "Moves", name, "Hitstun", "s", 0, 1.5f, 0.02f, Live, () => Move(slot, "hitstun"));
                Add($"moves.{slot}.knockback", "Moves", name, "Knockback", "", 0, 6, 0.05f, Live, () => Move(slot, "knockback"));

                if (slot == "up")
                {
                    // Launcher → jump → air combo (knock-up analysis, 19 Nov)
                    Add("moves.up.recovery", "Moves", name, "Recovery", "s", 0, 1, 0.02f, Live, () => Move("up", "recovery"));
                    Add("moves.up.launchDirectionX", "Moves", name, "Launch direction X (0 = straight up)", "", 0, 0.5f, 0.05f, Live, () => MoveVector("up", "launchDirection", 0));
                    const string launcher = "Up special launch";
                    Add("moves.up.launch.speed", "Moves", launcher, "Launch speed", "", 0, 20, 0.5f, Live, () => Launcher("up", "launchSpeed"));
                    Add("moves.up.launch.gravity", "Moves", launcher, "Launch gravity", "", 5, 40, 0.5f, Live, () => Launcher("up", "launchGravity"));
                    Add("moves.up.launch.minAngle", "Moves", launcher, "Min launch angle", "deg", 0, 80, 1, Live, () => Launcher("up", "minLaunchAngle"));
                    Add("moves.up.launch.maxAngle", "Moves", launcher, "Max launch angle", "deg", 10, 89, 1, Live, () => Launcher("up", "maxLaunchAngle"));
                }
            }

            // Items (by ItemDurability weight)
            var classes = new (ItemDurability.Weight weight, string key, string name)[]
            {
                (ItemDurability.Weight.Light, "light", "Light items"),
                (ItemDurability.Weight.Heavy, "heavy", "Heavy items"),
            };
            foreach (var (weight, key, name) in classes)
            {
                Add($"items.{key}.damage", "Items", name, "Damage on hit", "", 0, 40, 0.5f, Live, () => ItemHit(weight, "damage"));
                Add($"items.{key}.downedTime", "Items", name, "Knockdown (downed) time", "s", 0, 5, 0.05f, Live, () => ItemHit(weight, "downedTime"));
                Add($"items.{key}.hitstun", "Items", name, "Hitstun", "s", 0, 1.5f, 0.02f, Live, () => ItemHit(weight, "hitstun"));
                Add($"items.{key}.launchSpeed", "Items", name, "Launch speed", "", 0, 20, 0.5f, Live, () => ItemHit(weight, "launchSpeed"));
                Add($"items.{key}.throwSpeed", "Items", name, "Throw speed", "", 4, 40, 1, Live, () => ItemProp(weight, "throwSpeed"), sweep: true);
                Add($"items.{key}.canMoveWhileHeld", "Items", name, "Can move while held", "", 0, 1, 1, Live, () => ItemProp(weight, "canMoveWhileHeld"), TuneDisplay.OnOff, sweep: true);
            }

            // Stage items
            Add("stage_items.wakeChance", "Stage", "Waking items", "Wake chance per light hit", "", 0, 100, 5, Live, () => On<StageItemDirector>("wakeChance"), TuneDisplay.Percent100);
            Add("stage_items.heavyWakeChance", "Stage", "Waking items", "Wake chance per heavy / special", "", 0, 100, 5, Live, () => On<StageItemDirector>("heavyWakeChance"), TuneDisplay.Percent100);
            Add("stage_items.shockwaveRadius", "Stage", "Waking items", "Shockwave radius", "", 0.5f, 10, 0.25f, Live, () => On<StageItemDirector>("shockwaveRadius"));
            Add("stage_items.shockwaveCooldown", "Stage", "Waking items", "Shockwave cooldown", "s", 0, 6, 0.05f, Live, () => On<StageItemDirector>("shockwaveCooldown"));
            Add("stage_items.emptyDelay", "Stage", "Empty stage", "Delay before an item drops in", "s", 0, 10, 0.25f, Live, () => On<StageItemDirector>("emptyDelay"));

            // Input
            Add("input.heavyHoldTime", "Input", "Fighting", "Hold time before a heavy", "s", 0.15f, 0.6f, 0.01f, Live, () => Gesture("holdTime"));
            Add("input.pairWindow", "Input", "Fighting", "Two-finger pair window (block)", "s", 0.05f, 0.3f, 0.01f, Live, () => Gesture("pairWindow"));
            Add("input.pickUpRange", "Input", "Items", "Pick-up range", "", 0.2f, 4, 0.1f, Live, () => OnFighters<ItemHolder>("pickUpRange"));
            Add("input.itemTapRadius", "Input", "Items", "Item tap radius", "", 0.1f, 1.5f, 0.05f, Live, () => On<ItemTapHandler>("tapRadius"));
            Add("input.throwSwipeThreshold", "Input", "Items", "Throw swipe distance (× screen)", "", 0.02f, 0.3f, 0.01f, Live, () => On<ItemTapHandler>("throwSwipeThreshold"));

            // CPU difficulty
            var tiers = new (string field, string name)[] { ("easy", "Easy CPU"), ("normal", "Normal CPU"), ("hard", "Hard CPU") };
            foreach (var (tier, name) in tiers)
            {
                Add($"ai.{tier}.reactionTime", "CPU", name, "Reaction time", "s", 0, 1.5f, 0.01f, Live, () => Cpu(tier, "reactionTime"));
                Add($"ai.{tier}.attackCooldown", "CPU", name, "Attack cooldown", "s", 0.1f, 4, 0.05f, Live, () => Cpu(tier, "attackCooldown"));
                Add($"ai.{tier}.blockChance", "CPU", name, "Block chance", "", 0, 1, 0.05f, Live, () => Cpu(tier, "blockChance"), TuneDisplay.Percent01);
                Add($"ai.{tier}.parryChance", "CPU", name, "Parry chance", "", 0, 1, 0.05f, Live, () => Cpu(tier, "parryChance"), TuneDisplay.Percent01);
                Add($"ai.{tier}.comboBreakChance", "CPU", name, "Combo-break chance", "", 0, 1, 0.05f, Live, () => Cpu(tier, "comboBreakChance"), TuneDisplay.Percent01);
                Add($"ai.{tier}.punishChance", "CPU", name, "Punish chance", "", 0, 1, 0.05f, Live, () => Cpu(tier, "punishChance"), TuneDisplay.Percent01);
            }

            // Testing toggles: on the tab for A/B tests, never exported / imported / logged (TuneDef.testOnly)
            const string toggles = "Testing toggles (not exported or logged)";
            Add("test.playerCanParryMelee", "Testing", toggles, "Player can parry melee", "", 0, 1, 1, Live, () => OnPlayer<Fighter>("canParryMelee"), TuneDisplay.OnOff, testOnly: true);
            Add("test.cpuCanParryMelee", "Testing", toggles, "CPU can parry melee", "", 0, 1, 1, Live, () => OnCpu<Fighter>("canParryMelee"), TuneDisplay.OnOff, testOnly: true);
            Add("test.heldGlowShowsThrowArea", "Testing", toggles, "Held glow shows throw area", "", 0, 1, 1, Live, () => On<ItemTapHandler>("heldGlowShowsThrowArea"), TuneDisplay.OnOff, testOnly: true);

            return list;
        }
    }
}
