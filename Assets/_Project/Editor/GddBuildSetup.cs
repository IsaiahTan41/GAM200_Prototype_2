#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using KungFuCrashout.AI;
using KungFuCrashout.Controls;
using KungFuCrashout.Core;
using KungFuCrashout.DebugTools;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;
using KungFuCrashout.UI;
using Object = UnityEngine.Object;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → GDD Build → …   (Prototype 2 port of the Prototype 3 GDD build, 10 Nov 2026)
    /// Brings the GDD systems into this project. Run the steps one by one (or "Run Everything"). Every step is safe to
    /// re-run: assets you've tuned are kept, built UI is rebuilt. Nothing runs by itself. Commit to git first: steps 2 and 3
    /// save FightScene / StartScreen.
    ///
    ///   1. Create GDD Data Assets — Rushdown move set, the Restaurant items (Plate = light, Chair = medium, Pot = heavy)
    ///      as prefabs made from the crate, and the six roster archetypes (Nam-pi + Rushdown playable, four locked)
    ///      Nam-pi  = the Monk (Shoto all-rounder, the Player's current Monk attacks and clips)
    ///      Rushdown = the fists fighter (the Opponent's Fighter_* art), faster and closer-range
    ///   2. Upgrade Fight Scene    — behaviour-tree AI on the Opponent, archetype loadouts, MatchManager (best of 3) instead
    ///      of RoundReset, StageItemDirector, the match HUD (timer = pause, names, pips, damage bars, callouts, popups,
    ///      letterbox, hints, Match over), the pause menu's AI / difficulty page, touch ripples, debug overlays off,
    ///      the Opponent's throw wind-up and CarryPoint
    ///   3. Build Roster + Start   — new Roster scene (playtest lock: Monk 1P vs fists CPU); Start screen rebuilt
    ///      (START → Roster, Leaderboard, Quit); build order
    /// Prototype 3's "Set Project Identity" step and its first-open auto-run are left out on purpose.
    /// Place in: Assets/_Project/Editor/GddBuildSetup.cs
    /// </summary>
    public static class GddBuildSetup
    {
        const string Root = "Assets/_Project";
        const string DataDir = Root + "/Data/GDD";
        const string PrefabDir = Root + "/Prefabs/GDD";
        const string MaterialDir = Root + "/Materials/GDD";
        const string ScenesDir = Root + "/Scenes";
        const string FightPath = ScenesDir + "/FightScene.unity";
        const string StartPath = ScenesDir + "/StartScreen.unity";
        const string RosterPath = ScenesDir + "/Roster.unity";
        const string MonkIdlePath = Root + "/Art/Characters/Monk_Idle.PNG";        // the Shoto (Nam-pi)
        const string FistsStancePath = Root + "/Art/Characters/Fighter_Stance.png"; // the Rushdown
        const string Menu = "Window/KungFu Crashout/GDD Build/";
        const float F = 1f / 60f;

        // ═════════════ Menu ═════════════

        [MenuItem(Menu + "Run Everything (1–3)", priority = 10020)]
        public static void RunAll()
        {
            if (!Ready()) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RunAllSteps();
        }

        public static void RunAllSteps()
        {
            CreateDataAssets();
            UpgradeFightScene();
            BuildMenus();
            EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("GDD Build",
                "Done. Start → Roster → Fight → Match over is ready.\n\nPress Play on StartScreen, or build to the phone. " +
                "The Console lists everything that changed.", "OK");
        }

        [MenuItem(Menu + "1. Create GDD Data Assets", priority = 10031)]
        public static void CreateDataAssetsMenu() { if (Ready()) CreateDataAssets(); }

        [MenuItem(Menu + "2. Upgrade Fight Scene", priority = 10032)]
        public static void UpgradeFightSceneMenu()
        {
            if (!Ready() || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateDataAssets();
            UpgradeFightScene();
        }

        [MenuItem(Menu + "3. Build Roster + Start Screens", priority = 10033)]
        public static void BuildMenusMenu()
        {
            if (!Ready() || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateDataAssets();
            BuildMenus();
        }

        [MenuItem(Menu + "Toggle Debug Overlays (open scene)", priority = 10050)]
        public static void ToggleDebug()
        {
            var fighters = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (fighters.Length == 0) { EditorUtility.DisplayDialog("GDD Build", "Open FightScene first.", "OK"); return; }
            bool on = !new SerializedObject(fighters[0]).FindProperty("showHitboxesInGame").boolValue;
            SetDebugOverlays(on);
            EditorSceneManager.MarkSceneDirty(fighters[0].gameObject.scene);
            Debug.Log($"[GDD Build] Debug overlays {(on ? "ON" : "OFF")}: hitboxes, state logs, gesture arbiter overlay " +
                      "(touch recording always OFF here), Tap Anywhere overlay, AI thinking label, Defense Debug HUD. Save the scene.");
        }

        static bool Ready()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("GDD Build", "Stop Play mode first.", "OK"); return false; }
            if (!File.Exists(FightPath) || !File.Exists(StartPath))
            {
                EditorUtility.DisplayDialog("GDD Build", $"Expected {FightPath} and {StartPath} (copied from Prototype 2).", "OK");
                return false;
            }
            return true;
        }

        // ═════════════ 1. Data assets ═════════════

        static AttackData light1, light2, light3, heavy, sideSp, upSp, downSp;
        static AttackData rd1, rd2, rd3, rdHeavy, rdSide, rdUp, rdDown;
        static Carryable plate, chair, pot;
        static FighterArchetype namPi, rushdown;
        static FighterArchetype[] roster;
        static Material outlinedText, highlightMat;

        static void CreateDataAssets()
        {
            Folder(DataDir); Folder(PrefabDir); Folder(MaterialDir);

            // Prototype 2's attack assets (Assets/_Project/Data/Attacks). Missing ones are reported, not recreated.
            light1 = FindAsset<AttackData>("Light1_Jab");
            light2 = FindAsset<AttackData>("Light2_Strike");
            light3 = FindAsset<AttackData>("Light3_Finisher");
            heavy = FindAsset<AttackData>("Heavy_PalmStrike");
            sideSp = FindAsset<AttackData>("Special_ElbowSlide");
            upSp = FindAsset<AttackData>("Special_Uppercut");
            downSp = FindAsset<AttackData>("Special_LegSweep");
            if (!light1 || !light2 || !light3 || !heavy || !sideSp || !upSp || !downSp)
                Debug.LogWarning("[GDD Build] Some of Prototype 2's attack assets are missing from Data/Attacks; " +
                                 "the Rushdown move set uses defaults for those.");

            // ── Rushdown: faster, closer, more aggressive versions (GDD: "gets close and keeps up pressure") ──
            rd1 = Variant(light1, "RD_Light1_QuickJab", a =>
            {
                a.damage = 3f; a.startup = 4 * F; a.active = 4 * F; a.recovery = 7 * F;
                a.hitboxOffset = new Vector2(0.65f, 0.2f); a.hitboxSize = new Vector2(0.8f, 0.6f);
                a.knockback = 0.25f; a.hitstun = 0.28f; a.lunge = 0.25f;
            });
            rd2 = Variant(light2, "RD_Light2_Hook", a =>
            {
                a.damage = 4f; a.startup = 4 * F; a.active = 5 * F; a.recovery = 8 * F;
                a.hitboxOffset = new Vector2(0.7f, 0.05f); a.hitboxSize = new Vector2(0.85f, 0.7f);
                a.knockback = 0.3f; a.hitstun = 0.3f; a.lunge = 0.3f;
            });
            rd3 = Variant(light3, "RD_Light3_Knee", a =>
            {
                a.damage = 8f; a.startup = 6 * F; a.active = 6 * F; a.recovery = 16 * F;
                a.hitboxOffset = new Vector2(0.7f, 0f); a.hitboxSize = new Vector2(1.0f, 0.9f);
                a.knockback = 1.4f; a.hitstun = 0.45f; a.lunge = 0.45f;
            });
            rdHeavy = Variant(heavy, "RD_Heavy_TigerPalm", a =>
            {
                a.damage = 12f; a.startup = 12 * F; a.active = 6 * F; a.recovery = 20 * F;
                a.hitboxOffset = new Vector2(0.8f, 0.1f); a.hitboxSize = new Vector2(1.1f, 0.9f);
                a.knockback = 2.2f; a.hitstun = 0.55f; a.lunge = 0.9f;
            });
            rdSide = Variant(sideSp, "RD_Special_TigerRush", a =>
            {
                a.damage = 7f; a.startup = 4 * F; a.active = 14 * F; a.recovery = 16 * F;
                a.activeTravel = 3.0f; a.knockback = 1.6f;
            });
            rdUp = Variant(upSp, "RD_Special_RisingKnee", a =>
            {
                a.damage = 7f; a.startup = 4 * F; a.active = 5 * F; a.recovery = 22 * F;
                a.hitboxOffset = new Vector2(0.45f, 0.4f); a.hitboxSize = new Vector2(0.8f, 1.3f);
                a.lunge = 0.35f;
            });
            rdDown = Variant(downSp, "RD_Special_LowSpin", a =>
            {
                a.damage = 6f; a.startup = 6 * F; a.active = 5 * F; a.recovery = 16 * F;
                a.hitboxSize = new Vector2(3.0f, 0.6f);
            });

            // ── Materials ──
            outlinedText = MakeOutlinedTextMaterial();
            GddUi.Outlined = outlinedText;
            highlightMat = GetOrCreateMaterial("ItemHighlight", "GUI/Text Shader");

            // ── Restaurant items ──
            var crate = FindCratePrefab();
            if (crate)
            {
                var crateData = new SerializedObject(crate).FindProperty("hitData").objectReferenceValue as ThrowableData;

                var plateData = Throwable("Item_Plate_Hit", crateData, t =>
                {
                    t.damage = 8f; t.weight = HitWeight.Light; t.hitstun = 0.3f;
                    t.launchSpeed = 8f; t.minLaunchAngle = 30f; t.maxLaunchAngle = 55f;
                    t.downedTime = 0.5f; t.groundBounce = 0.3f;
                    t.impactHitstop = 0.1f; t.impactShake = 0.15f; t.slowMotionTime = 0.08f;
                });
                var chairData = Throwable("Item_Chair_Hit", crateData, t => { });
                var potData = Throwable("Item_Pot_Hit", crateData, t =>
                {
                    t.damage = 16f; t.hitstun = 0.45f;
                    t.launchSpeed = 13f; t.downedTime = 1.0f;
                    t.wallSplat = true; t.wallSplatTime = 0.35f;
                    t.impactHitstop = 0.16f; t.impactShake = 0.32f; t.slowMotionTime = 0.2f;
                });

                plate = ItemPrefab(crate, "Item_Plate", "Plate", new Color(0.92f, 0.95f, 1f), new Vector2(0.75f, 0.35f),
                                   plateData, 18f, 2, true, ItemDurability.Weight.Light, breakOnHit: true, blocks: 0);
                chair = ItemPrefab(crate, "Item_Chair", "Chair", new Color(0.62f, 0.4f, 0.22f), new Vector2(0.95f, 1.05f),
                                   chairData, 14f, 3, true, ItemDurability.Weight.Medium, breakOnHit: false, blocks: 0);
                pot = ItemPrefab(crate, "Item_Pot", "Pot", new Color(0.3f, 0.32f, 0.36f), new Vector2(1.15f, 0.95f),
                                 potData, 10f, 3, false, ItemDurability.Weight.Heavy, breakOnHit: false, blocks: 2);
            }
            else Debug.LogWarning("[GDD Build] No crate item prefab (a prefab with a Carryable) found: the Restaurant items weren't made.");

            // ── Roster ──
            // Nam-pi = the Monk (the copy of Monk_Idle with the current import settings, not Monk_Sprites/Monk_Idle).
            // Rushdown = the fists fighter (the Opponent's Fighter_* art).
            var monkIdle = AssetDatabase.LoadAssetAtPath<Sprite>(MonkIdlePath) ?? FindAsset<Sprite>("Monk_Idle");
            var draftFighter = AssetDatabase.LoadAssetAtPath<Sprite>(FistsStancePath) ?? FindAsset<Sprite>("Fighter_Stance");
            namPi = Archetype("Archetype_1_NamPi", a =>
            {
                a.displayName = "Nam-pi"; a.archetypeLabel = "Shoto · all-rounder"; a.signatureMove = "Elbow Slide";
                a.description = "The Monk. A bit of everything, no big weakness. The baseline the cast is measured against.";
                a.playable = true; a.colour = new Color(0.85f, 0.25f, 0.2f); a.preview = monkIdle;
                // Move set copied from the Player in FightScene (the Monk attacks with their clips) by Upgrade Fight Scene.
                a.lightCombo = new AttackData[0];
                a.dashDistance = 0f; a.dashDuration = 0f; a.dashCooldown = 0f;
            });
            rushdown = Archetype("Archetype_2_Rushdown", a =>
            {
                a.displayName = "Rushdown"; a.archetypeLabel = "Rushdown · pressure"; a.signatureMove = "Tiger Rush";
                a.description = "The fists fighter. Fast and aggressive: gets close and keeps up pressure and combos.";
                a.playable = true; a.colour = new Color(0.95f, 0.55f, 0.12f); a.preview = draftFighter ? draftFighter : monkIdle;
                a.lightCombo = new[] { rd1, rd2, rd3 }; a.heavyAttack = rdHeavy;
                a.sideSpecial = rdSide; a.upSpecial = rdUp; a.downSpecial = rdDown;
                a.dashDistance = 3.0f; a.dashDuration = 0.15f; a.dashCooldown = 0.08f;
            });
            roster = new[]
            {
                namPi, rushdown,
                Locked("Archetype_3_Zoner", "Zoner", "Zoner · keep-away", "Projectiles and long reach", new Color(0.3f, 0.55f, 0.9f)),
                Locked("Archetype_4_Grappler", "Grappler", "Grappler · command throws", "Bait and punish", new Color(0.55f, 0.35f, 0.25f)),
                Locked("Archetype_5_Footsies", "Footsies", "Footsies · mid-range pokes", "Controls the space", new Color(0.35f, 0.7f, 0.45f)),
                Locked("Archetype_6_Trapper", "Trapper", "Trapper · traps and puppets", "Mind games", new Color(0.6f, 0.4f, 0.8f)),
            };

            AssetDatabase.SaveAssets();
            Debug.Log($"[GDD Build] Data assets in {DataDir} and {PrefabDir}: Rushdown move set, Restaurant items " +
                      $"(Plate / Chair / Pot{(crate ? "" : " — NOT made, no crate prefab")}), six archetypes (Nam-pi + Rushdown playable).");
        }

        static T Variant<T>(T source, string name, System.Action<T> tweak) where T : ScriptableObject
        {
            string path = $"{DataDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing; // keep your tuning
            var asset = source ? Object.Instantiate(source) : ScriptableObject.CreateInstance<T>();
            tweak(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static ThrowableData Throwable(string name, ThrowableData source, System.Action<ThrowableData> tweak)
        {
            string path = $"{DataDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ThrowableData>(path);
            if (existing) return existing;
            ThrowableData asset;
            if (source) asset = Object.Instantiate(source);
            else { asset = ScriptableObject.CreateInstance<ThrowableData>(); asset.ApplyThrowDefaults(); }
            tweak(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static FighterArchetype Archetype(string name, System.Action<FighterArchetype> init)
        {
            string path = $"{DataDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<FighterArchetype>(path);
            if (existing) return existing;
            var a = ScriptableObject.CreateInstance<FighterArchetype>();
            init(a);
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static FighterArchetype Locked(string asset, string displayName, string label, string signature, Color colour) =>
            Archetype(asset, a =>
            {
                a.displayName = displayName; a.archetypeLabel = label; a.signatureMove = signature;
                a.description = "Coming in full production (one archetype at a time).";
                a.playable = false; a.colour = colour;
                a.lightCombo = new AttackData[0];
            });

        static Carryable ItemPrefab(Carryable crate, string fileName, string displayName, Color colour, Vector2 scaleMul,
                                    ThrowableData hit, float throwSpeed, int maxHolds, bool breakOnShield,
                                    ItemDurability.Weight weight, bool breakOnHit, int blocks)
        {
            string path = $"{PrefabDir}/{fileName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<Carryable>(path);
            if (existing) return existing; // keep your tuning

            string cratePath = AssetDatabase.GetAssetPath(crate);
            var rootGo = PrefabUtility.LoadPrefabContents(cratePath);
            try
            {
                rootGo.name = fileName;
                var sr = rootGo.GetComponent<SpriteRenderer>();
                if (sr) sr.color = colour;
                var s = rootGo.transform.localScale;
                rootGo.transform.localScale = new Vector3(s.x * scaleMul.x, s.y * scaleMul.y, s.z);

                var carry = rootGo.GetComponent<Carryable>();
                var so = new SerializedObject(carry);
                so.FindProperty("hitData").objectReferenceValue = hit;
                so.FindProperty("throwSpeed").floatValue = throwSpeed;
                so.FindProperty("maxHolds").intValue = maxHolds;
                so.FindProperty("breakOnShield").boolValue = breakOnShield;
                var breakCue = so.FindProperty("breakCue").objectReferenceValue;
                so.ApplyModifiedPropertiesWithoutUndo();

                var hl = rootGo.GetComponent<ItemHighlight>();
                if (!hl) hl = rootGo.AddComponent<ItemHighlight>();
                var hso = new SerializedObject(hl);
                hso.FindProperty("silhouetteMaterial").objectReferenceValue = highlightMat;
                hso.ApplyModifiedPropertiesWithoutUndo();

                var dur = rootGo.GetComponent<ItemDurability>();
                if (!dur) dur = rootGo.AddComponent<ItemDurability>();
                var dso = new SerializedObject(dur);
                dso.FindProperty("weight").enumValueIndex = (int)weight;
                dso.FindProperty("breakOnCleanHit").boolValue = breakOnHit;
                dso.FindProperty("blocksBeforeBreaking").intValue = blocks;
                dso.FindProperty("breakCue").objectReferenceValue = breakCue;
                dso.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(rootGo, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(rootGo); }
            Debug.Log($"[GDD Build] {displayName}: {weight}, throw {throwSpeed} m/s, {hit.damage} damage → {path}");
            return AssetDatabase.LoadAssetAtPath<Carryable>(path);
        }

        static Carryable FindCratePrefab()
        {
            Carryable first = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith(PrefabDir)) continue;
                var c = AssetDatabase.LoadAssetAtPath<Carryable>(p);
                if (!c) continue;
                if (Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains("crate")) return c;
                if (!first) first = c;
            }
            return first;
        }

        static Material MakeOutlinedTextMaterial()
        {
            string path = $"{MaterialDir}/TMP_Outlined.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var font = TMP_Settings.defaultFontAsset;
            if (!font || !font.material) return null;
            var mat = new Material(font.material) { name = "TMP_Outlined" };
            mat.EnableKeyword("OUTLINE_ON");
            if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth)) mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            if (mat.HasProperty(ShaderUtilities.ID_OutlineColor)) mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
            if (mat.HasProperty(ShaderUtilities.ID_FaceDilate)) mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Material GetOrCreateMaterial(string name, string shader)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var sh = Shader.Find(shader);
            if (!sh) return null;
            var mat = new Material(sh) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ═════════════ 2. Fight scene ═════════════

        static void UpgradeFightScene()
        {
            var scene = EditorSceneManager.OpenScene(FightPath, OpenSceneMode.Single);
            var log = new List<string>();

            var all = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var player = all.FirstOrDefault(f => f.CompareTag("Player")) ??
                         all.FirstOrDefault(f => f.name.ToLowerInvariant().Contains("player")) ??
                         all.FirstOrDefault(f => !f.name.ToLowerInvariant().Contains("enemy") && !f.name.ToLowerInvariant().Contains("opponent"));
            var opponent = all.FirstOrDefault(f => f != player);
            if (!player || !opponent)
            {
                EditorUtility.DisplayDialog("GDD Build", "FightScene needs two Fighters (Player and Opponent).", "OK");
                return;
            }
            if (!player.CompareTag("Player")) { player.tag = "Player"; log.Add($"{player.name} tagged Player"); }

            // ── Retire the prototype stand-ins ──
            foreach (var rr in Object.FindObjectsByType<RoundReset>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { rr.enabled = false; log.Add($"RoundReset on '{rr.name}' off (MatchManager runs rounds)"); }
            foreach (var d in Object.FindObjectsByType<TrainingDummy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var dso = new SerializedObject(d);
                dso.FindProperty("practiceMode").boolValue = false;
                dso.FindProperty("behaviourMode").enumValueIndex = (int)TrainingDummy.BehaviourMode.Dummy;
                dso.ApplyModifiedPropertiesWithoutUndo();
                d.enabled = false;
                log.Add($"TrainingDummy on '{d.name}' off (kept for the pause menu's Practice Dummy option)");
            }
            // Prototype 2's rule-based AIController (looked up by name: it may not be in every copy of the project).
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb && mb.GetType().Name == "AIController") { mb.enabled = false; log.Add($"AIController on '{mb.name}' off"); }
            SetDebugOverlays(false);
            log.Add("Debug overlays off (hitboxes, gesture overlay, state logs, Defense Debug HUD) — Toggle Debug Overlays brings them back");

            // ── Fighters: loadouts, AI, item hands ──
            FillNamPiFromPlayer(player, log);
            AddLoadout(player, ArchetypeLoadout.Side.Player, namPi);
            AddLoadout(opponent, ArchetypeLoadout.Side.Cpu, rushdown);

            var playerHolder = player.GetComponent<ItemHolder>();
            var oppHolder = opponent.GetComponent<ItemHolder>();
            if (!oppHolder)
            {
                oppHolder = opponent.gameObject.AddComponent<ItemHolder>();
                log.Add("ItemHolder added to the Opponent (the AI fetches and throws items)");
            }
            if (playerHolder)
            {
                // Same throw as the Player: the wind-up (Throw Windup + Throw Attack = Data/Attacks/Throw_Windup), so the
                // AI's throws telegraph exactly like yours.
                var pso = new SerializedObject(playerHolder);
                var oso = new SerializedObject(oppHolder);
                var src = pso.FindProperty("throwAttack").objectReferenceValue;
                if (!oso.FindProperty("throwAttack").objectReferenceValue && src)
                    oso.FindProperty("throwAttack").objectReferenceValue = src;
                var windup = oso.FindProperty("throwWindup");
                var srcWindup = pso.FindProperty("throwWindup");
                if (windup != null && srcWindup != null) windup.boolValue = srcWindup.boolValue;
                oso.ApplyModifiedPropertiesWithoutUndo();
                log.Add($"Opponent throw = the Player's ({(src ? src.name : "none")}, wind-up {(srcWindup != null && srcWindup.boolValue ? "on" : "off")})");
            }
            EnsureOpponentCarryPoint(player, opponent, log);

            var brain = opponent.GetComponent<BehaviourTreeAI>();
            if (!brain) brain = opponent.gameObject.AddComponent<BehaviourTreeAI>();
            log.Add($"BehaviourTreeAI on '{opponent.name}' (difficulty from the Roster / pause menu)");

            foreach (var bar in Object.FindObjectsByType<FighterStaminaBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(bar);
                so.FindProperty("normalColour").colorValue = Color.white; // UI Standards fix: cyan read as "blocking"
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ── Stage items ──
            var stage = Object.FindFirstObjectByType<StageBounds>();
            foreach (var c in Object.FindObjectsByType<Carryable>(FindObjectsSortMode.None))
            { c.gameObject.SetActive(false); log.Add($"Hand-placed item '{c.name}' hidden (the director lays out the Restaurant set)"); }

            var directorGo = FindOrCreate("Stage Items");
            var director = directorGo.GetComponent<StageItemDirector>();
            if (!director) director = directorGo.AddComponent<StageItemDirector>();
            var dirSo = new SerializedObject(director);
            GddUi.Set(dirSo, "stage", stage);
            GddUi.SetArray(dirSo, "fighters", new Object[] { player, opponent });
            if (plate && chair && pot)
            {
                var placements = dirSo.FindProperty("roundStartItems");
                var set = new (Carryable prefab, float x)[] { (plate, 0.14f), (chair, 0.42f), (pot, 0.58f), (plate, 0.86f) };
                placements.arraySize = set.Length;
                for (int i = 0; i < set.Length; i++)
                {
                    var e = placements.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("prefab").objectReferenceValue = set[i].prefab;
                    e.FindPropertyRelative("stageX").floatValue = set[i].x;
                }
                GddUi.SetArray(dirSo, "dropPool", new Object[] { plate, plate, chair, pot });
            }
            dirSo.ApplyModifiedPropertiesWithoutUndo();
            log.Add("StageItemDirector: 2 plates, a chair and a pot every round; heavy-hit shockwave drops; always ≥ 1 item");

            // ── Match ──
            var matchGo = FindOrCreate("Match");
            var match = matchGo.GetComponent<MatchManager>();
            if (!match) match = matchGo.AddComponent<MatchManager>();
            var pSpawn = FindOrCreate("Player Spawn", matchGo.transform);
            var oSpawn = FindOrCreate("Opponent Spawn", matchGo.transform);
            if (pSpawn.transform.position == Vector3.zero) pSpawn.transform.position = player.transform.position;
            if (oSpawn.transform.position == Vector3.zero) oSpawn.transform.position = opponent.transform.position;
            var options = Object.FindFirstObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            var mso = new SerializedObject(match);
            GddUi.Set(mso, "player", player);
            GddUi.Set(mso, "opponent", opponent);
            GddUi.Set(mso, "playerSpawn", pSpawn.transform);
            GddUi.Set(mso, "opponentSpawn", oSpawn.transform);
            GddUi.Set(mso, "items", director);
            GddUi.Set(mso, "pauseMenu", options);
            mso.FindProperty("rosterScene").stringValue = Path.GetFileNameWithoutExtension(RosterPath);
            mso.FindProperty("startScene").stringValue = Path.GetFileNameWithoutExtension(StartPath);
            mso.ApplyModifiedPropertiesWithoutUndo();
            log.Add("MatchManager: best of 3, 90 s rounds, KO slow-mo, stats saved; spawns under 'Match'");

            // ── HUD ──
            BuildHud(match, options, brain, opponent, log);

            // ── Touch ripples ──
            var ripples = FindOrCreate("Touch Ripples");
            if (!ripples.GetComponent<TouchRipple>()) ripples.AddComponent<TouchRipple>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GDD Build] FightScene upgraded and saved:\n  • " + string.Join("\n  • ", log));
        }

        /// <summary>Nam-pi = exactly the Player's Prototype 2 set-up (Monk attacks, clips, specials, dash), unless already filled.</summary>
        static void FillNamPiFromPlayer(Fighter player, List<string> log)
        {
            if (!namPi || (namPi.lightCombo != null && namPi.lightCombo.Length > 0 && namPi.lightCombo[0])) return;
            var so = new SerializedObject(player);
            var combo = so.FindProperty("lightCombo");
            var list = new List<AttackData>();
            for (int i = 0; i < combo.arraySize; i++) list.Add(combo.GetArrayElementAtIndex(i).objectReferenceValue as AttackData);
            namPi.lightCombo = list.ToArray();
            namPi.heavyAttack = so.FindProperty("heavyAttack").objectReferenceValue as AttackData;
            namPi.sideSpecial = so.FindProperty("sideSpecial").objectReferenceValue as AttackData;
            namPi.upSpecial = so.FindProperty("upSpecial").objectReferenceValue as AttackData;
            namPi.downSpecial = so.FindProperty("downSpecial").objectReferenceValue as AttackData;
            namPi.dashDistance = so.FindProperty("dashDistance").floatValue;
            namPi.dashDuration = so.FindProperty("dashDuration").floatValue;
            namPi.dashCooldown = so.FindProperty("dashCooldown").floatValue;
            EditorUtility.SetDirty(namPi);
            AssetDatabase.SaveAssets();
            log.Add($"Nam-pi archetype = {player.name}'s Prototype 2 move set ({list.Count}-hit combo, heavy, 3 specials, dash)");
        }

        /// <summary>
        /// The Opponent holds items at its own CarryPoint (Visual/CarryPoint, so it flips with the art), placed like the
        /// Player's. Skipped if it already has one anywhere under it.
        /// </summary>
        static void EnsureOpponentCarryPoint(Fighter player, Fighter opponent, List<string> log)
        {
            if (FindDeep(opponent.transform, "CarryPoint")) return;
            var playerPoint = FindDeep(player.transform, "CarryPoint");
            var parent = opponent.transform.Find("Visual") ?? opponent.transform;
            var go = new GameObject("CarryPoint");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = playerPoint ? playerPoint.localPosition : new Vector3(0.45f, 0.9f, 0f);
            log.Add($"CarryPoint added under '{opponent.name}/{parent.name}' at {go.transform.localPosition} (move it to fit the fists art)");
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static void AddLoadout(Fighter f, ArchetypeLoadout.Side side, FighterArchetype fallback)
        {
            var l = f.GetComponent<ArchetypeLoadout>();
            if (!l) l = f.gameObject.AddComponent<ArchetypeLoadout>();
            var so = new SerializedObject(l);
            so.FindProperty("side").enumValueIndex = (int)side;
            so.FindProperty("fallback").objectReferenceValue = fallback;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetDebugOverlays(bool on)
        {
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(f);
                so.FindProperty("showHitboxesInGame").boolValue = on;
                so.FindProperty("logStateChanges").boolValue = on;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var t in Object.FindObjectsByType<TapAnywhereInputHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(t);
                so.FindProperty("showDebugOverlay").boolValue = on;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var g in Object.FindObjectsByType<GestureArbiterInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // The gesture overlay follows the toggle; touch recording is always switched off here (turn it on by hand).
                var so = new SerializedObject(g);
                so.FindProperty("showOverlay").boolValue = on;
                so.FindProperty("logGestures").boolValue = false;
                so.FindProperty("recordTouches").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var b in Object.FindObjectsByType<BehaviourTreeAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(b);
                so.FindProperty("showDebugLabel").boolValue = on;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var h in Object.FindObjectsByType<DefenseDebugHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                h.enabled = on;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb.GetType().Name == "DeviceCheck") mb.enabled = on;
        }

        // ───────────── HUD ─────────────

        static void BuildHud(MatchManager match, OptionsMenu options, BehaviourTreeAI brain, Fighter opponent, List<string> log)
        {
            Slider leftBar = null, rightBar = null;
            foreach (var s in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = s.name.ToLowerInvariant();
                if (!n.Contains("health")) continue;
                if (n.Contains("enemy") || n.Contains("opponent")) rightBar = s; else leftBar = s;
            }

            Canvas canvas = null;
            if (leftBar)
            {
                var parentCanvas = leftBar.GetComponentInParent<Canvas>();
                if (parentCanvas) canvas = parentCanvas.rootCanvas;
            }
            if (!canvas) canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).FirstOrDefault(c => c.isRootCanvas);
            if (!canvas)
            {
                var cgo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = cgo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var sc = cgo.GetComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1920f, 1080f);
                sc.matchWidthOrHeight = 1f;
            }
            var safe = canvas.transform.Find("SafeArea") as RectTransform;
            if (!safe)
            {
                safe = GddUi.Stretch(GddUi.New("SafeArea", canvas.transform));
                safe.gameObject.AddComponent<SafeAreaFitter>();
                safe.SetAsFirstSibling();
            }
            EnsureEventSystem();

            // Rebuild our layer from scratch each run.
            var old = safe.Find("GDD HUD");
            if (old) Object.DestroyImmediate(old.gameObject);
            var oldBars = canvas.transform.Find("GDD Letterbox");
            if (oldBars) Object.DestroyImmediate(oldBars.gameObject);
            var hud = GddUi.New("GDD HUD", safe);
            GddUi.Stretch(hud);

            // Timer = pause button (the only touch target in the HUD band).
            var timerBtn = GddUi.MakeButton("Timer (Pause)", hud.transform, "", 20f, new Color(0f, 0f, 0f, 0.45f), Color.white);
            GddUi.Place(timerBtn.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(200f, 130f));
            Object.DestroyImmediate(timerBtn.transform.Find("Label").gameObject);
            var timer = GddUi.Text("Time", timerBtn.transform, "90", 76f, TextAlignmentOptions.Center, bold: true, outlined: true);
            GddUi.Place(timer.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(200f, 90f));
            var pauseCap = GddUi.Text("Pause hint", timerBtn.transform, "II  PAUSE", 22f, TextAlignmentOptions.Center);
            GddUi.Place(pauseCap.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(200f, 30f));
            pauseCap.color = new Color(1f, 1f, 1f, 0.6f);

            // Names (outer end) and round pips (inner end), just under the bars.
            var leftName = GddUi.Text("P1 Name", hud.transform, "NAM-PI", 34f, TextAlignmentOptions.Left, bold: true, outlined: true);
            GddUi.Place(leftName.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(56f, -100f), new Vector2(700f, 46f));
            var rightName = GddUi.Text("CPU Name", hud.transform, "RUSHDOWN", 34f, TextAlignmentOptions.Right, bold: true, outlined: true);
            GddUi.Place(rightName.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, -100f), new Vector2(700f, 46f));
            var leftPips = Pips("P1 Pips", hud.transform, new Vector2(0.42f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -104f));
            var rightPips = Pips("CPU Pips", hud.transform, new Vector2(0.58f, 1f), new Vector2(0f, 1f), new Vector2(8f, -104f));

            // Red damage bars behind the health fills.
            var leftDamage = DamageBar(leftBar);
            var rightDamage = DamageBar(rightBar);

            // Callout banner, popups, combo counters, telegraph.
            var banner = GddUi.New("Callout", hud.transform);
            GddUi.Place(banner, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(4000f, 200f));
            GddUi.Img(banner, new Color(0f, 0f, 0f, 0.45f), sliced: false);
            var callout = GddUi.Text("Text", banner.transform, "ROUND 1", 140f, TextAlignmentOptions.Center, bold: true, outlined: true);
            GddUi.Place(callout.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 200f));

            var leftPopup = GddUi.Text("P1 Popup", hud.transform, "PARRY!", 64f, TextAlignmentOptions.Center, bold: true, outlined: true);
            GddUi.Place(leftPopup.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 90f));
            var rightPopup = GddUi.Text("CPU Popup", hud.transform, "PARRY!", 64f, TextAlignmentOptions.Center, bold: true, outlined: true);
            GddUi.Place(rightPopup.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 90f));
            var leftCombo = GddUi.Text("P1 Combo", hud.transform, "3 HITS", 60f, TextAlignmentOptions.Left, bold: true, outlined: true);
            GddUi.Place(leftCombo.gameObject, new Vector2(0f, 0.72f), new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(500f, 80f));
            leftCombo.color = GddUi.Gold;
            var rightCombo = GddUi.Text("CPU Combo", hud.transform, "3 HITS", 60f, TextAlignmentOptions.Right, bold: true, outlined: true);
            GddUi.Place(rightCombo.gameObject, new Vector2(1f, 0.72f), new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(500f, 80f));
            rightCombo.color = new Color(1f, 0.45f, 0.35f);
            var telegraph = GddUi.Text("CPU Telegraph", hud.transform, "!", 120f, TextAlignmentOptions.Center, bold: true, outlined: true);
            GddUi.Place(telegraph.gameObject, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 140f));
            telegraph.color = new Color(1f, 0.25f, 0.2f);

            // Letterbox (full bleed, outside the safe area).
            var bars = GddUi.New("GDD Letterbox", canvas.transform);
            GddUi.Stretch(bars);
            bars.transform.SetSiblingIndex(safe.GetSiblingIndex()); // under the HUD
            var top = GddUi.New("Top", bars.transform);
            var topRt = (RectTransform)top.transform;
            topRt.anchorMin = new Vector2(0f, 1f); topRt.anchorMax = new Vector2(1f, 1f); topRt.pivot = new Vector2(0.5f, 1f);
            topRt.anchoredPosition = Vector2.zero; topRt.sizeDelta = new Vector2(0f, 0f);
            GddUi.Img(top, Color.black, sliced: false);
            var bottom = GddUi.New("Bottom", bars.transform);
            var botRt = (RectTransform)bottom.transform;
            botRt.anchorMin = new Vector2(0f, 0f); botRt.anchorMax = new Vector2(1f, 0f); botRt.pivot = new Vector2(0.5f, 0f);
            botRt.anchoredPosition = Vector2.zero; botRt.sizeDelta = new Vector2(0f, 0f);
            GddUi.Img(bottom, Color.black, sliced: false);

            // First-match gesture hints.
            var hints = GddUi.New("Gesture Hints", hud.transform);
            GddUi.Place(hints, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1640f, 150f));
            GddUi.Img(hints, new Color(0f, 0f, 0f, 0.5f));
            var hintsGroup = hints.AddComponent<CanvasGroup>();
            hintsGroup.blocksRaycasts = false;
            hintsGroup.interactable = false;
            var hintText = GddUi.Text("Text", hints.transform,
                "<b>TAP</b> attack   ·   <b>HOLD</b> heavy   ·   <b>SWIPE</b> dash / jump   ·   <b>TWO FINGERS</b> block (just before a hit = parry)\n" +
                "<b>SWIPE + TAP</b> (other finger) special   ·   <b>TAP</b> an item to grab it   ·   <b>FLICK</b> from it to throw", 32f);
            GddUi.Stretch(hintText.gameObject, 10f);

            // Match over.
            var over = GddUi.New("Match Over", hud.transform);
            GddUi.Stretch(over);
            GddUi.Img(over, GddUi.Dim, sliced: false, raycast: true);
            var win = GddUi.New("Window", over.transform);
            GddUi.Place(win, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 760f));
            GddUi.Img(win, GddUi.Window);
            var result = GddUi.Text("Result", win.transform, "YOU WIN", 150f, TextAlignmentOptions.Center, bold: true, outlined: true);
            GddUi.Place(result.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1200f, 180f));
            var p1Label = GddUi.Text("1P", win.transform, "1P", 36f, TextAlignmentOptions.Right, bold: true);
            GddUi.Place(p1Label.gameObject, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-200f, -270f), new Vector2(120f, 50f));
            var resLeft = Pips("1P Pips", win.transform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -276f));
            var cpuLabel = GddUi.Text("CPU", win.transform, "CPU", 36f, TextAlignmentOptions.Left, bold: true);
            GddUi.Place(cpuLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(200f, -270f), new Vector2(120f, 50f));
            var resRight = Pips("CPU Pips", win.transform, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(60f, -276f));
            var record = GddUi.Text("Record", win.transform, "Record 0W · 0L", 34f);
            GddUi.Place(record.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(1200f, 50f));
            record.color = new Color(1f, 1f, 1f, 0.75f);
            var rematch = GddUi.Primary("Rematch", win.transform, "REMATCH", 56f);
            GddUi.Place(rematch.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-50f, 50f), new Vector2(460f, 150f));
            var change = GddUi.Secondary("Change Fighter", win.transform, "CHANGE FIGHTER", 40f);
            GddUi.Place(change.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-540f, 50f), new Vector2(440f, 130f));
            var toStart = GddUi.Secondary("Start", win.transform, "START", 40f);
            GddUi.Place(toStart.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 50f), new Vector2(300f, 130f));

            // Wire MatchHUD.
            var mh = hud.AddComponent<MatchHUD>();
            var so = new SerializedObject(mh);
            GddUi.Set(so, "match", match);
            GddUi.Set(so, "left.healthBar", leftBar);
            GddUi.Set(so, "left.damageFill", leftDamage);
            GddUi.Set(so, "left.nameText", leftName);
            GddUi.SetArray(so, "left.pips", leftPips);
            GddUi.Set(so, "left.popup", leftPopup);
            GddUi.Set(so, "left.comboText", leftCombo);
            GddUi.Set(so, "right.healthBar", rightBar);
            GddUi.Set(so, "right.damageFill", rightDamage);
            GddUi.Set(so, "right.nameText", rightName);
            GddUi.SetArray(so, "right.pips", rightPips);
            GddUi.Set(so, "right.popup", rightPopup);
            GddUi.Set(so, "right.comboText", rightCombo);
            GddUi.Set(so, "timerText", timer);
            GddUi.Set(so, "calloutText", callout);
            GddUi.Set(so, "calloutRoot", banner);
            GddUi.Set(so, "telegraphText", telegraph);
            GddUi.Set(so, "letterboxTop", topRt);
            GddUi.Set(so, "letterboxBottom", botRt);
            GddUi.Set(so, "hints", hintsGroup);
            GddUi.Set(so, "matchOverPanel", over);
            GddUi.Set(so, "resultText", result);
            GddUi.Set(so, "recordText", record);
            GddUi.SetArray(so, "resultPipsLeft", resLeft);
            GddUi.SetArray(so, "resultPipsRight", resRight);
            GddUi.Set(so, "rematchButton", rematch);
            GddUi.Set(so, "changeFighterButton", change);
            GddUi.Set(so, "startButton", toStart);
            so.ApplyModifiedPropertiesWithoutUndo();
            over.SetActive(false);
            log.Add("Match HUD built under SafeArea/GDD HUD (timer = pause, names, pips, damage bars, callouts, popups, letterbox, hints, Match over)");

            // ── Pause menu: the timer opens it; old corner buttons retired; GDD page ──
            if (options)
            {
                var oso = new SerializedObject(options);
                var oldBtn = oso.FindProperty("optionsButton").objectReferenceValue as Button;
                if (oldBtn && oldBtn != timerBtn) { oldBtn.gameObject.SetActive(false); log.Add($"'{oldBtn.name}' hidden (the timer opens the pause menu)"); }
                oso.FindProperty("optionsButton").objectReferenceValue = timerBtn;
                oso.ApplyModifiedPropertiesWithoutUndo();
                BuildAiPage(options, brain, opponent.GetComponent<TrainingDummy>(), log);
                options.transform.SetAsLastSibling(); // over the HUD
            }
            else log.Add("NO OptionsMenu in FightScene: the timer won't pause (add the Options Menu back, then run this again)");

            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.name.Replace(" ", "").ToLowerInvariant() == "backbutton")
                { b.gameObject.SetActive(false); log.Add($"'{b.name}' hidden (Quit to Start is in the pause menu)"); }
        }

        static Image[] Pips(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            var row = GddUi.New(name, parent);
            GddUi.Place(row, anchor, pivot, pos, new Vector2(80f, 34f));
            var pips = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                var p = GddUi.New($"Pip {i + 1}", row.transform);
                // Pip 1 sits at the inner end (nearest the timer) on both sides.
                bool rightAnchored = pivot.x > 0.5f;
                float x = rightAnchored ? -i * 44f : i * 44f;
                GddUi.Place(p, new Vector2(rightAnchored ? 1f : 0f, 0.5f), new Vector2(rightAnchored ? 1f : 0f, 0.5f),
                            new Vector2(x, 0f), new Vector2(32f, 32f));
                pips[i] = GddUi.Img(p, new Color(1f, 1f, 1f, 0.22f));
                p.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            }
            return pips;
        }

        static Image DamageBar(Slider bar)
        {
            if (!bar || !bar.fillRect) return null;
            var area = bar.fillRect.parent;
            var old = area.Find("Damage Fill");
            if (old) Object.DestroyImmediate(old.gameObject);
            var go = GddUi.New("Damage Fill", area);
            go.transform.SetAsFirstSibling(); // behind the health fill
            var img = go.AddComponent<Image>();
            var fillImg = bar.fillRect.GetComponent<Image>();
            if (fillImg) { img.sprite = fillImg.sprite; img.type = fillImg.type; }
            img.color = new Color(0.85f, 0.15f, 0.12f);
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = bar.fillRect.anchorMin;
            rt.anchorMax = bar.fillRect.anchorMax;
            rt.offsetMin = bar.fillRect.offsetMin;
            rt.offsetMax = bar.fillRect.offsetMax;
            return img;
        }

        static void BuildAiPage(OptionsMenu options, BehaviourTreeAI brain, TrainingDummy dummy, List<string> log)
        {
            var oso = new SerializedObject(options);
            var page = oso.FindProperty("gamePage").objectReferenceValue as GameObject;
            if (!page) { log.Add("Options menu has no Game Page: AI settings not added"); return; }

            // Hide Prototype 2's State Machine / Practice Dummy row (its toggle now lives in our panel).
            foreach (Transform child in page.transform)
                if (child.name != "GDD AI Settings") child.gameObject.SetActive(false);
            var old = page.transform.Find("GDD AI Settings");
            if (old) Object.DestroyImmediate(old.gameObject);

            var panel = GddUi.New("GDD AI Settings", page.transform);
            GddUi.Stretch(panel);

            var aiToggle = GddUi.MakeToggle("AI Toggle", panel.transform, "Opponent uses the behaviour-tree AI");
            GddUi.Place(aiToggle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(900f, 80f));
            var aiLabel = GddUi.Text("AI Label", panel.transform, "", 30f);
            GddUi.Place(aiLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -98f), new Vector2(940f, 44f));
            aiLabel.color = new Color(1f, 1f, 1f, 0.75f);

            var diffs = new Button[3];
            string[] names = { "EASY", "NORMAL", "HARD" };
            for (int i = 0; i < 3; i++)
            {
                diffs[i] = GddUi.Secondary(names[i], panel.transform, names[i], 38f);
                GddUi.Place(diffs[i].gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((i - 1) * 270f, -150f), new Vector2(250f, 84f));
            }

            var thinking = GddUi.MakeToggle("Thinking Toggle", panel.transform, "Show the AI's thinking (behaviour-tree branch)", 32f);
            GddUi.Place(thinking.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(900f, 70f));

            var resume = GddUi.Primary("Resume", panel.transform, "RESUME", 46f);
            GddUi.Place(resume.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(360f, 100f));
            var quit = GddUi.Secondary("Quit", panel.transform, "QUIT TO START", 36f);
            GddUi.Place(quit.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(360f, 100f));

            var comp = panel.AddComponent<AISettingsPanel>();
            var so = new SerializedObject(comp);
            GddUi.Set(so, "brain", brain);
            GddUi.Set(so, "dummy", dummy);
            GddUi.Set(so, "aiToggle", aiToggle);
            GddUi.Set(so, "aiLabel", aiLabel);
            GddUi.SetArray(so, "difficultyButtons", diffs);
            GddUi.Set(so, "thinkingToggle", thinking);
            GddUi.Set(so, "resumeButton", resume);
            GddUi.Set(so, "quitButton", quit);
            GddUi.Set(so, "menu", options);
            so.FindProperty("startScene").stringValue = Path.GetFileNameWithoutExtension(StartPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            log.Add("Pause menu GAME page: AI on/off (Practice Dummy), Easy/Normal/Hard, show AI thinking, Resume, Quit to Start");
        }

        // ═════════════ 3. Roster + Start ═════════════

        static void BuildMenus()
        {
            BuildRosterScene();
            BuildStartScreen();

            var list = EditorBuildSettings.scenes.Where(s => s.path != StartPath && s.path != RosterPath && s.path != FightPath).ToList();
            foreach (var s in list) s.enabled = false;
            list.Insert(0, new EditorBuildSettingsScene(FightPath, true));
            list.Insert(0, new EditorBuildSettingsScene(RosterPath, true));
            list.Insert(0, new EditorBuildSettingsScene(StartPath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[GDD Build] Build order: 0 StartScreen, 1 Roster, 2 FightScene.");
        }

        static void BuildRosterScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.05f, 0.05f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            EnsureEventSystem();

            var canvas = MakeCanvas();
            var bg = GddUi.New("Background", canvas.transform);
            GddUi.Stretch(bg);
            GddUi.Img(bg, new Color(0.07f, 0.05f, 0.05f), sliced: false);
            var safe = GddUi.Stretch(GddUi.New("SafeArea", canvas.transform));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var ui = safe.gameObject;

            var back = GddUi.Secondary("Back", ui.transform, "BACK", 40f);
            GddUi.Place(back.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(200f, 110f));
            var chip = GddUi.New("Stage Chip", ui.transform);
            GddUi.Place(chip, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(400f, 90f));
            GddUi.Img(chip, new Color(1f, 1f, 1f, 0.08f));
            var stageChip = GddUi.Text("Stage", chip.transform, "Stage: Restaurant", 34f, TextAlignmentOptions.Center, bold: true);
            GddUi.Stretch(stageChip.gameObject);
            var vs = GddUi.Text("VS", ui.transform, "VS", 96f, TextAlignmentOptions.Center, bold: true);
            GddUi.Place(vs.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(300f, 130f));
            vs.color = GddUi.Title;

            var p1 = PreviewBlock("1P Preview", ui.transform, new Vector2(0f, 0.5f), new Vector2(270f, -10f), "1P", GddUi.Gold, null);
            var cpuBtnGo = GddUi.New("CPU Preview", ui.transform);
            GddUi.Place(cpuBtnGo, new Vector2(1f, 0.5f), new Vector2(-270f, -10f), new Vector2(440f, 760f));
            var cpuBtn = cpuBtnGo.AddComponent<Button>();
            cpuBtn.targetGraphic = GddUi.Img(cpuBtnGo, new Color(1f, 1f, 1f, 0.04f), true, true);
            var cpu = PreviewBlock("Content", cpuBtnGo.transform, new Vector2(0.5f, 0.5f), Vector2.zero, "CPU", Color.white, "tap or swipe to change");

            // 3 × 2 grid.
            var tiles = new RosterMenu.Tile[6];
            const float tile = 200f, gap = 26f;
            for (int i = 0; i < 6; i++)
            {
                int col = i % 3, row = i / 3;
                var go = GddUi.New($"Tile {i + 1}", ui.transform);
                GddUi.Place(go, new Vector2(0.5f, 0.5f), new Vector2((col - 1) * (tile + gap), 150f - row * (tile + gap)), new Vector2(tile, tile));
                var img = GddUi.Img(go, roster != null && roster[i] ? roster[i].colour : Color.grey, true, true);
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var frame = go.AddComponent<Outline>();
                frame.enabled = false;
                var nameT = GddUi.Text("Name", go.transform, roster != null && roster[i] ? roster[i].displayName.ToUpperInvariant() : "-", 30f,
                                       TextAlignmentOptions.Center, bold: true, outlined: true);
                GddUi.Place(nameT.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(tile - 10f, 44f));
                var lockGo = GddUi.New("Locked", go.transform);
                GddUi.Stretch(lockGo);
                GddUi.Img(lockGo, new Color(0f, 0f, 0f, 0.45f));
                var lockT = GddUi.Text("Label", lockGo.transform, "LOCKED", 28f, TextAlignmentOptions.Center, bold: true);
                GddUi.Place(lockT.gameObject, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(tile, 40f));
                lockT.color = new Color(1f, 1f, 1f, 0.8f);
                var pTag = Tag("1P", go.transform, new Vector2(0f, 1f), GddUi.Gold, Color.black);
                var cTag = Tag("CPU", go.transform, new Vector2(1f, 1f), Color.white, Color.black);
                tiles[i] = new RosterMenu.Tile { button = btn, background = img, frame = frame, nameText = nameT, lockBadge = lockGo, playerTag = pTag, cpuTag = cTag };
            }

            var chips = new Button[3];
            string[] names = { "EASY", "NORMAL", "HARD" };
            for (int i = 0; i < 3; i++)
            {
                chips[i] = GddUi.Secondary(names[i], ui.transform, names[i], 36f);
                GddUi.Place(chips[i].gameObject, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 230f, -265f), new Vector2(210f, 90f));
                var o = chips[i].gameObject.AddComponent<Outline>();
                o.effectDistance = new Vector2(6f, -6f);
                o.enabled = false;
            }
            var diffLabel = GddUi.Text("Difficulty Label", ui.transform, "CPU DIFFICULTY", 26f);
            GddUi.Place(diffLabel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(600f, 36f));
            diffLabel.color = new Color(1f, 1f, 1f, 0.6f);

            var ready = GddUi.Primary("Ready", ui.transform, "READY", 64f);
            GddUi.Place(ready.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(440f, 150f));
            var toast = GddUi.Text("Toast", ui.transform, "Coming soon", 40f, TextAlignmentOptions.Center, bold: true);
            GddUi.Place(toast.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(900f, 60f));
            toast.color = GddUi.Gold;
            var record = GddUi.Text("Record", ui.transform, "", 28f);
            GddUi.Place(record.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(700f, 40f));
            record.color = new Color(1f, 1f, 1f, 0.6f);

            var menu = ui.AddComponent<RosterMenu>();
            var so = new SerializedObject(menu);
            GddUi.SetArray(so, "roster", roster.Cast<Object>().ToArray());
            var tp = so.FindProperty("tiles");
            tp.arraySize = tiles.Length;
            for (int i = 0; i < tiles.Length; i++)
            {
                var e = tp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("button").objectReferenceValue = tiles[i].button;
                e.FindPropertyRelative("background").objectReferenceValue = tiles[i].background;
                e.FindPropertyRelative("frame").objectReferenceValue = tiles[i].frame;
                e.FindPropertyRelative("nameText").objectReferenceValue = tiles[i].nameText;
                e.FindPropertyRelative("lockBadge").objectReferenceValue = tiles[i].lockBadge;
                e.FindPropertyRelative("playerTag").objectReferenceValue = tiles[i].playerTag;
                e.FindPropertyRelative("cpuTag").objectReferenceValue = tiles[i].cpuTag;
            }
            SetPreview(so, "playerPreview", p1);
            SetPreview(so, "cpuPreview", cpu);
            GddUi.Set(so, "cpuCycleButton", cpuBtn);
            GddUi.SetArray(so, "difficultyChips", chips);
            GddUi.Set(so, "readyButton", ready);
            GddUi.Set(so, "backButton", back);
            GddUi.Set(so, "toastText", toast);
            GddUi.Set(so, "stageText", stageChip);
            GddUi.Set(so, "recordText", record);
            so.FindProperty("fightScene").stringValue = Path.GetFileNameWithoutExtension(FightPath);
            so.FindProperty("backScene").stringValue = Path.GetFileNameWithoutExtension(StartPath);
            // Playtest lock: the Monk (Nam-pi, Shoto) for 1P vs the fists fighter (Rushdown) as the CPU, until each has
            // art for both sides. Untick Lock Matchup on the Roster object to open the picks.
            so.FindProperty("lockMatchup").boolValue = true;
            GddUi.Set(so, "lockedPlayer", namPi);
            GddUi.Set(so, "lockedCpu", rushdown);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, RosterPath);
            Debug.Log($"[GDD Build] Roster scene saved: {RosterPath} (playtest lock on: Nam-pi / Monk 1P vs Rushdown / fists CPU)");
        }

        static GameObject Tag(string text, Transform parent, Vector2 corner, Color fill, Color textColour)
        {
            var go = GddUi.New(text + " Tag", parent);
            GddUi.Place(go, corner, corner, new Vector2(corner.x > 0.5f ? -6f : 6f, -6f), new Vector2(84f, 44f));
            GddUi.Img(go, fill);
            var t = GddUi.Text("Label", go.transform, text, 28f, TextAlignmentOptions.Center, bold: true);
            GddUi.Stretch(t.gameObject);
            t.color = textColour;
            go.SetActive(false);
            return go;
        }

        struct PreviewRefs { public Image pose; public TMP_Text name, label, signature; }

        static PreviewRefs PreviewBlock(string name, Transform parent, Vector2 anchor, Vector2 pos, string tag, Color tagColour, string caption)
        {
            var go = GddUi.New(name, parent);
            GddUi.Place(go, anchor, pos, new Vector2(440f, 760f));
            var tagT = GddUi.Text("Tag", go.transform, tag, 44f, TextAlignmentOptions.Center, bold: true);
            GddUi.Place(tagT.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(300f, 60f));
            tagT.color = tagColour;
            var poseGo = GddUi.New("Pose", go.transform);
            GddUi.Place(poseGo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(400f, 420f));
            var pose = poseGo.AddComponent<Image>();
            pose.preserveAspect = true;
            pose.raycastTarget = false;
            var n = GddUi.Text("Name", go.transform, "NAM-PI", 60f, TextAlignmentOptions.Center, bold: true);
            GddUi.Place(n.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -500f), new Vector2(440f, 76f));
            var l = GddUi.Text("Archetype", go.transform, "Shoto · all-rounder", 32f);
            GddUi.Place(l.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -575f), new Vector2(440f, 44f));
            var s = GddUi.Text("Signature", go.transform, "Signature: Elbow Slide", 28f);
            GddUi.Place(s.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -620f), new Vector2(440f, 40f));
            s.color = new Color(1f, 1f, 1f, 0.7f);
            if (!string.IsNullOrEmpty(caption))
            {
                var c = GddUi.Text("Caption", go.transform, caption, 24f);
                GddUi.Place(c.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -670f), new Vector2(440f, 36f));
                c.color = new Color(1f, 1f, 1f, 0.45f);
            }
            return new PreviewRefs { pose = pose, name = n, label = l, signature = s };
        }

        static void SetPreview(SerializedObject so, string path, PreviewRefs p)
        {
            GddUi.Set(so, path + ".pose", p.pose);
            GddUi.Set(so, path + ".nameText", p.name);
            GddUi.Set(so, path + ".archetypeText", p.label);
            GddUi.Set(so, path + ".signatureText", p.signature);
        }

        static void BuildStartScreen()
        {
            var scene = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Single);
            EnsureEventSystem();
            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.isRootCanvas)
                         ?? MakeCanvas();

            // Retire Prototype 2's centred menu (kept, hidden, in case you want it back).
            foreach (var sm in Object.FindObjectsByType<StartMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sm.gameObject.name != "GDD Start Menu") sm.gameObject.SetActive(false);
            var old = canvas.transform.Find("GDD Start Menu");
            if (old) Object.DestroyImmediate(old.gameObject);

            var root = GddUi.New("GDD Start Menu", canvas.transform);
            GddUi.Stretch(root);
            GddUi.Img(root, new Color(0.07f, 0.05f, 0.05f), sliced: false);
            var safe = GddUi.Stretch(GddUi.New("SafeArea", root.transform));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Left half: title. Right half (thumb side): the button stack, START on top and largest.
            var title = GddUi.Text("Title", safe, "KUNGFU\nCRASHOUT", 130f, TextAlignmentOptions.Left, bold: true);
            GddUi.Place(title.gameObject, new Vector2(0.05f, 0.62f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1000f, 340f));
            title.color = GddUi.Title;
            title.lineSpacing = -20f;
            var sub = GddUi.Text("Subtitle", safe, "The environment is the weapon   ·   Team 11 Je-gnomes   ·   GDD build", 30f, TextAlignmentOptions.Left);
            GddUi.Place(sub.gameObject, new Vector2(0.05f, 0.38f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1100f, 50f));
            sub.color = new Color(1f, 1f, 1f, 0.6f);

            float x = -60f;
            var start = GddUi.Primary("Start", safe, "START", 64f);
            GddUi.Place(start.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, 250f), new Vector2(580f, 160f));
            var online = GddUi.MakeButton("Online", safe, "ONLINE 1v1  <size=60%>coming soon", 40f, GddUi.Disabled, new Color(1f, 1f, 1f, 0.55f));
            GddUi.Place(online.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, 80f), new Vector2(580f, 120f));
            online.interactable = false;
            var board = GddUi.Secondary("Leaderboard", safe, "LEADERBOARD", 44f);
            GddUi.Place(board.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, -60f), new Vector2(580f, 120f));
            var quit = GddUi.Secondary("Quit", safe, "QUIT", 44f);
            GddUi.Place(quit.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, -250f), new Vector2(580f, 120f));

            var sm2 = root.AddComponent<StartMenu>();
            var so = new SerializedObject(sm2);
            GddUi.Set(so, "startButton", start);
            GddUi.Set(so, "quitButton", quit);
            so.FindProperty("fightScene").stringValue = Path.GetFileNameWithoutExtension(RosterPath); // START → Roster
            so.ApplyModifiedPropertiesWithoutUndo();

            // Leaderboard popup ("This phone" tab; Online greyed).
            var panel = GddUi.New("Leaderboard Popup", root.transform);
            GddUi.Stretch(panel);
            GddUi.Img(panel, GddUi.Dim, sliced: false, raycast: true);
            var win = GddUi.New("Window", panel.transform);
            GddUi.Place(win, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 720f));
            GddUi.Img(win, GddUi.Window);
            var head = GddUi.Text("Title", win.transform, "LEADERBOARD", 56f, TextAlignmentOptions.Left, bold: true);
            GddUi.Place(head.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -40f), new Vector2(600f, 80f));
            var tabPhone = GddUi.Text("Tab This Phone", win.transform, "<u>THIS PHONE</u>", 32f, TextAlignmentOptions.Left, bold: true);
            GddUi.Place(tabPhone.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -130f), new Vector2(300f, 50f));
            tabPhone.color = GddUi.Gold;
            var tabOnline = GddUi.Text("Tab Online", win.transform, "ONLINE (with multiplayer)", 32f, TextAlignmentOptions.Left);
            GddUi.Place(tabOnline.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(380f, -130f), new Vector2(520f, 50f));
            tabOnline.color = new Color(1f, 1f, 1f, 0.35f);
            var stats = GddUi.Text("Stats", win.transform, "", 40f, TextAlignmentOptions.TopLeft);
            GddUi.Place(stats.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -210f), new Vector2(880f, 380f));
            var close = GddUi.Secondary("Close", win.transform, "X", 44f);
            GddUi.Place(close.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(100f, 100f));
            var reset = GddUi.Secondary("Reset", win.transform, "RESET", 30f);
            GddUi.Place(reset.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(220f, 90f));

            var rp = root.AddComponent<RecordPanel>();
            var rso = new SerializedObject(rp);
            GddUi.Set(rso, "openButton", board);
            GddUi.Set(rso, "panel", panel);
            GddUi.Set(rso, "closeButton", close);
            GddUi.Set(rso, "statsText", stats);
            GddUi.Set(rso, "resetButton", reset);
            rso.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GDD Build] StartScreen rebuilt (title left, START / ONLINE (soon) / LEADERBOARD / QUIT on the right). " +
                      "Prototype 2's menu is kept, hidden.");
        }

        static Canvas MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 1f;
            return canvas;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include)) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ═════════════ Helpers ═════════════

        static GameObject FindOrCreate(string name, Transform parent = null)
        {
            var existing = parent ? parent.Find(name) : null;
            if (existing) return existing.gameObject;
            if (!parent)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root.name == name) return root;
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        static T FindAsset<T>(string name) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                var a = AssetDatabase.LoadAssetAtPath<T>(p);
                if (a) return a;
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
    }
}
#endif
