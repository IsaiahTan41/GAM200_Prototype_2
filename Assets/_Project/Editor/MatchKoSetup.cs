#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Match KO Announcer   (open FightScene first)
    ///   • Imports every clip in Audio/SFX/Announcer (VO_KO_01 … 04): stereo kept, levels as mastered by the sound team
    ///     (no normalising), Vorbis quality 0.7, compressed in memory (4 s takes), preloaded so the first one has no delay.
    ///   • Builds Audio/Cues/SFX_MatchKO: RANDOM take each match (never the same one twice in a row), no pitch / volume
    ///     variation (it's a voice-over), one voice, top priority (never cut by hits or whooshes).
    ///   • MatchManager → Match Ko Cue = it. It plays only on the knockout that ends the match, as the KO slow motion
    ///     starts, so the 4 s take runs through the slow motion and ends as the win / lose screen appears.
    /// Re-run after adding / renaming takes. Place in: Assets/_Project/Editor/MatchKoSetup.cs
    /// </summary>
    public static class MatchKoSetup
    {
        const string Title = "Match KO Announcer";
        const string Folder = "Assets/_Project/Audio/SFX/Announcer";
        const string CuePath = "Assets/_Project/Audio/Cues/SFX_MatchKO.asset";

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Match KO Announcer", priority = 10076)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) { EditorUtility.DisplayDialog(Title, $"Missing {Folder}.", "OK"); return; }

            var clips = ImportVoices("VO_KO");
            if (clips.Length == 0) { EditorUtility.DisplayDialog(Title, $"No VO_KO clips in {Folder}.", "OK"); return; }

            var cue = VoiceCue(CuePath, clips);
            cue.order = SfxCue.Order.Random;
            cue.avoidRepeatingLast = 1;    // random take, never the same one twice in a row
            EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var mm in Object.FindObjectsByType<MatchManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(mm);
                so.FindProperty("matchKoCue").objectReferenceValue = cue;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] {cue.name}: {string.Join(", ", clips.Select(c => c.name))} (random). " +
                      (n > 0 ? $"MatchManager → Match Ko Cue set. Save the scene (Ctrl+S)." : "Open FightScene and run again to assign it."));
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Win / Lose Announcer   (open FightScene first)
        /// VO_Wins_Player1 → SFX_Wins_Player1 on the VICTORY screen (the player won); VO_Wins_Player2 → SFX_Wins_Player2 on
        /// the DEFEAT screen (the player lost). Played by the end screen (End Screen → Victory / Defeat Voice) as it appears.
        /// Same import as the KO takes. A draw plays neither.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Win - Lose Announcer", priority = 10077)]
        public static void SetUpWins()
        {
            const string title = "Win / Lose Announcer";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var p1 = ImportVoices("VO_Wins_Player1");
            var p2 = ImportVoices("VO_Wins_Player2");
            if (p1.Length == 0 && p2.Length == 0) { EditorUtility.DisplayDialog(title, $"No VO_Wins_Player1 / 2 clips in {Folder}.", "OK"); return; }
            var win = p1.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_Wins_Player1.asset", p1) : null;
            var lose = p2.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_Wins_Player2.asset", p2) : null;
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var es in Object.FindObjectsByType<KungFuCrashout.UI.EndScreenConcept>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(es);
                if (win) so.FindProperty("victoryVoice").objectReferenceValue = win;
                if (lose) so.FindProperty("defeatVoice").objectReferenceValue = lose;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] Victory screen: {(win ? win.name : "none")}, defeat screen: {(lose ? lose.name : "none")}. " +
                      (n > 0 ? "End Screen voices set. Save the scene (Ctrl+S)." : "No End Screen in the open scene: open FightScene and run again."));
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Round Announcer   (open FightScene first)
        /// VO_Round1* / VO_Round2* / VO_Round3* → SFX_Round1 / 2 / 3 → MatchManager → Round Voices. Each plays with the
        /// ROUND callout at the start of its round; rounds after 3 (draws) reuse Round 3. Same import as the KO takes.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Round Announcer", priority = 10078)]
        public static void SetUpRounds()
        {
            const string title = "Round Announcer";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var cues = new SfxCue[3];
            for (int r = 1; r <= 3; r++)
            {
                var clips = ImportVoices($"VO_Round{r}");
                if (clips.Length > 0) cues[r - 1] = VoiceCue($"Assets/_Project/Audio/Cues/SFX_Round{r}.asset", clips);
            }
            if (cues.All(c => !c)) { EditorUtility.DisplayDialog(title, $"No VO_Round1 / 2 / 3 clips in {Folder}.", "OK"); return; }
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var mm in Object.FindObjectsByType<MatchManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(mm);
                var arr = so.FindProperty("roundVoices");
                arr.arraySize = 3;
                for (int i = 0; i < 3; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = cues[i];
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] Round voices: {string.Join(", ", cues.Select((c, i) => $"Round {i + 1} = {(c ? c.clips[0].name : "none")}"))}; " +
                      "rounds after 3 reuse Round 3. " +
                      (n > 0 ? "MatchManager → Round Voices set. Save the scene (Ctrl+S)." : "Open FightScene and run again to assign them."));
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Start Screen Voice + UI Tap Sound
        ///   • Start screen voice: VO_KungFuCrashout* → SFX_StartScreenVoice, played right after the startup logos
        ///     (StartScreen gets an AudioManager if it has none, and Start Screen Voice).
        ///   • UI tap: PressStart* → SFX_PressStart, played by every button / icon that changes scene (UI Tap Sound in
        ///     StartScreen, Roster, LevelSelect and FightScene).
        /// Same import as the KO takes. Opens each scene, sets it up and saves it.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Start Screen Voice + UI Tap Sound", priority = 10079)]
        public static void SetUpStartVoice()
        {
            const string title = "Start Screen Voice + UI Tap";
            const string scenes = "Assets/_Project/Scenes/";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var voiceClips = ImportVoices("VO_KungFuCrashout");
            var tapClips = ImportVoices("PressStart");
            var voiceCue = voiceClips.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_StartScreenVoice.asset", voiceClips) : null;
            var tapCue = tapClips.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_PressStart.asset", tapClips) : null;
            if (tapCue) { tapCue.maxVoices = 1; tapCue.cooldown = 0.1f; EditorUtility.SetDirty(tapCue); }
            AssetDatabase.SaveAssets();
            if (!voiceCue && !tapCue) { EditorUtility.DisplayDialog(title, $"No VO_KungFuCrashout / PressStart clips in {Folder}.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var done = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "StartScreen", "Roster", "LevelSelect", "FightScene" })
            {
                string path = scenes + name + ".unity";
                if (!System.IO.File.Exists(path)) continue;
                var active = EditorSceneManager.GetActiveScene();
                bool opened = active.path != path;
                var scene = opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : active;

                T Find<T>() where T : Component
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        var c = root.GetComponentInChildren<T>(true);
                        if (c) return c;
                    }
                    return null;
                }
                T Make<T>(string goName) where T : Component
                {
                    var go = new GameObject(goName);
                    EditorSceneManager.MoveGameObjectToScene(go, scene);
                    return go.AddComponent<T>();
                }

                if (tapCue)
                {
                    var tap = Find<UiTapSound>() ?? Make<UiTapSound>("UI Tap Sound");
                    var so = new SerializedObject(tap);
                    so.FindProperty("tap").objectReferenceValue = tapCue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (name == "StartScreen" && voiceCue)
                {
                    if (!Find<AudioManager>()) Make<AudioManager>("AudioManager");
                    var voice = Find<StartScreenVoice>() ?? Make<StartScreenVoice>("Start Screen Voice");
                    var so = new SerializedObject(voice);
                    so.FindProperty("voice").objectReferenceValue = voiceCue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                if (opened) EditorSceneManager.CloseScene(scene, true);
                done.Add(name);
            }
            Debug.Log($"[Audio] Start screen voice: {(voiceCue ? voiceClips[0].name : "none")}; UI tap: {(tapCue ? tapClips[0].name : "none")} " +
                      $"(every scene-changing button). Saved: {string.Join(", ", done)}.");
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Roster Character Voices
        ///   • VO_MonkWu* → SFX_Voice_MonkWu, VO_LordYuan* → SFX_Voice_LordYuan (same import as the announcer takes).
        ///   • Roster: each fighter card's Voice (matched by name: "Monk" / "Yuan"), Lord Yuan's portrait mirrored on the
        ///     P1 side, the yellow confirm-flash material, and an AudioManager if the scene has none. Saves Roster.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Roster Character Voices", priority = 10080)]
        public static void SetUpRosterVoices()
        {
            const string title = "Roster Character Voices";
            const string path = "Assets/_Project/Scenes/Roster.unity";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var monk = ImportVoices("VO_MonkWu");
            var yuan = ImportVoices("VO_LordYuan");
            var monkCue = monk.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_Voice_MonkWu.asset", monk) : null;
            var yuanCue = yuan.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_Voice_LordYuan.asset", yuan) : null;
            foreach (var c in new[] { monkCue, yuanCue }) if (c) { c.cooldown = 0.3f; EditorUtility.SetDirty(c); }

            const string matPath = "Assets/_Project/Art/UI/CharacterSelect/Mat_UI_ConfirmFlash.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var sh = Shader.Find("GUI/Text Shader");
            if (!mat && sh) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); }
            AssetDatabase.SaveAssets();
            if (!System.IO.File.Exists(path)) { EditorUtility.DisplayDialog(title, "No Roster scene at " + path, "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var active = EditorSceneManager.GetActiveScene();
            bool opened = active.path != path;
            var scene = opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : active;
            KungFuCrashout.UI.CharacterSelectMenu menu = null;
            bool hasAudio = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!menu) menu = root.GetComponentInChildren<KungFuCrashout.UI.CharacterSelectMenu>(true);
                hasAudio |= root.GetComponentInChildren<AudioManager>(true);
            }
            // Without an AudioListener nothing is heard in the scene (the Roster camera had none).
            bool hasListener = false;
            Camera cam = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                hasListener |= root.GetComponentInChildren<AudioListener>(true);
                if (!cam) cam = root.GetComponentInChildren<Camera>(true);
            }
            if (!hasListener && cam) cam.gameObject.AddComponent<AudioListener>();
            if (!hasAudio)
            {
                var go = new GameObject("AudioManager");
                EditorSceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<AudioManager>();
            }
            var set = new System.Collections.Generic.List<string>();
            if (menu)
            {
                var so = new SerializedObject(menu);
                if (mat) so.FindProperty("flashMaterial").objectReferenceValue = mat;
                var entries = so.FindProperty("entries");
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    string n = e.FindPropertyRelative("displayName").stringValue ?? "";
                    bool isMonk = n.Contains("Monk"), isYuan = n.Contains("Yuan");
                    if (isMonk && monkCue) { e.FindPropertyRelative("voice").objectReferenceValue = monkCue; set.Add(n); }
                    if (isYuan && yuanCue) { e.FindPropertyRelative("voice").objectReferenceValue = yuanCue; set.Add(n); }
                    if (isYuan) e.FindPropertyRelative("mirrorOnP1").boolValue = true; // faces left in the art → face the centre on P1
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            Debug.Log(menu ? $"[Audio] Roster voices set on: {string.Join(", ", set)}. Lord Yuan mirrored on the P1 side. " +
                             $"Flash material: {(mat ? "ok" : "GUI/Text Shader missing — tint fallback")}. " +
                             $"Audio Listener: {(hasListener ? "already there" : cam ? "added to " + cam.name : "NO CAMERA FOUND")}. Roster saved."
                           : "[Audio] Voice cues made, but the Roster scene has no Character Select Menu.");
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Carousel Swipe Sounds
        ///   • Swipe_* in Audio/SFX/UI → Audio/Cues/SFX_UI_Swipe: a RANDOM take each press (never the same twice in a row),
        ///     levels as mastered, no pitch / volume variation.
        ///   • Roster → Character Select Menu → Turn Sound, LevelSelect → Level Select Menu → Arrow Sound.
        ///     Adds an AudioManager to a scene that has none (LevelSelect had none). Saves both scenes.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Carousel Swipe Sounds", priority = 10081)]
        public static void SetUpSwipe()
        {
            const string title = "Carousel Swipe Sounds";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var clips = ImportVoices("Swipe", "Assets/_Project/Audio/SFX/UI");
            if (clips.Length == 0) { EditorUtility.DisplayDialog(title, "No Swipe_* clips in Assets/_Project/Audio/SFX/UI.", "OK"); return; }
            var cue = VoiceCue("Assets/_Project/Audio/Cues/SFX_UI_Swipe.asset", clips);
            cue.order = SfxCue.Order.Random;
            cue.avoidRepeatingLast = 1;
            cue.maxVoices = 2;
            cue.cooldown = 0.05f;
            cue.priority = 64;
            EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var done = new System.Collections.Generic.List<string>();
            foreach (var (name, menuType, field) in new[]
                     {
                         ("Roster", typeof(KungFuCrashout.UI.CharacterSelectMenu), "turnSound"),
                         ("LevelSelect", typeof(KungFuCrashout.UI.LevelSelectMenu), "arrowSound"),
                     })
            {
                string path = "Assets/_Project/Scenes/" + name + ".unity";
                if (!System.IO.File.Exists(path)) continue;
                var active = EditorSceneManager.GetActiveScene();
                bool opened = active.path != path;
                var scene = opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : active;
                Component menu = null;
                bool hasAudio = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!menu) menu = root.GetComponentInChildren(menuType, true);
                    hasAudio |= root.GetComponentInChildren<AudioManager>(true);
                }
                if (!hasAudio)
                {
                    var go = new GameObject("AudioManager");
                    EditorSceneManager.MoveGameObjectToScene(go, scene);
                    go.AddComponent<AudioManager>();
                }
                if (menu)
                {
                    var so = new SerializedObject(menu);
                    so.FindProperty(field).objectReferenceValue = cue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    done.Add(name);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log($"[Audio] Swipe sounds ({clips.Length} takes, random) on the carousel arrows in: {string.Join(", ", done)}. Scenes saved.");
        }

        /// <summary>Announcer clips in the folder whose name starts with Prefix: stereo, levels as mastered, Vorbis 0.7,
        /// compressed in memory, preloaded.</summary>
        static AudioClip[] ImportVoices(string prefix, string folder = Folder)
        {
            return AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith(prefix))
                .OrderBy(p => p).Select(path =>
                {
                    var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (ai)
                    {
                        ai.forceToMono = false;
                        var s = ai.defaultSampleSettings;
                        s.loadType = AudioClipLoadType.CompressedInMemory;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = 0.7f;
                        s.preloadAudioData = true;
                        ai.defaultSampleSettings = s;
                        ai.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }).Where(c => c).ToArray();
        }

        /// <summary>An announcer cue: no pitch / volume variation, one voice, top priority (never cut by other sounds).</summary>
        static SfxCue VoiceCue(string path, AudioClip[] clips)
        {
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            bool created = !cue;
            if (created) cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = clips;
            cue.order = SfxCue.Order.Shuffle;
            cue.volume = 1f;
            cue.volumeVariation = 0f;
            cue.pitch = 1f;
            cue.pitchVariation = 0f;
            cue.maxVoices = 1;
            cue.cooldown = 1f;
            cue.priority = 0;
            if (created) AssetDatabase.CreateAsset(cue, path);
            else EditorUtility.SetDirty(cue);
            return cue;
        }
    }
}
#endif
