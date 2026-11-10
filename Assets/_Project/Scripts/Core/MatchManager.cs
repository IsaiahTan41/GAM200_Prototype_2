using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;
using KungFuCrashout.Sound;
using KungFuCrashout.UI;

namespace KungFuCrashout.Core
{
    public enum MatchPhase { Intro, Fighting, RoundOver, MatchOver }

    /// <summary>
    /// GDD BUILD match flow (replaces Prototype 2's temporary RoundReset scene reload):
    /// best-of-3 rounds of 90 s in ONE scene: "ROUND 1" → "FIGHT!" → KO or time-out → next round → match over.
    ///
    ///   • Each round: both fighters back to full health at their start spots, stamina refilled, items wiped and the stage's
    ///     starting set laid out again (StageItemDirector) — the GDD's hectic "race for the items" opening every round
    ///   • KO: slow motion + punch-in (CinemachineFightRig) + "K.O." + letterbox (MatchHUD); time-out: more health wins
    ///   • Draws (equal health, double KO) never lock the match; Max Rounds caps endless draws
    ///   • Match over: YOU WIN / YOU LOSE / DRAW with Rematch, Change fighter and Start (MatchHUD); the result is saved to
    ///     this phone's record (PlayerStats)
    ///   • Pause: the Options popup is the pause menu. Losing focus (call, notification shade, home) opens it.
    /// Keeps every public member of the Prototype 2 "code ready" version, so HUDController still compiles.
    ///
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene (adds the "Match" object and wires everything).
    /// Place in: Assets/_Project/Scripts/Core/MatchManager.cs
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MatchManager : MonoBehaviour
    {
        [Header("Fighters")]
        [SerializeField] Fighter player;
        [SerializeField] Fighter opponent;
        [Tooltip("Where each round starts. Empty = wherever the fighters stand when the scene loads.")]
        [SerializeField] Transform playerSpawn;
        [SerializeField] Transform opponentSpawn;

        [Header("Rules")]
        [SerializeField, Min(10f)] float roundTime = 90f;
        [SerializeField, Min(1)] int winsNeeded = 2;
        [Tooltip("Safety cap so endless draws still end the match.")]
        [SerializeField, Min(1)] int maxRounds = 5;

        [Header("Pacing (seconds, real time)")]
        [SerializeField] float introDelay = 0.4f;
        [SerializeField] float roundCallTime = 1f;
        [SerializeField] float fightCallTime = 0.7f;
        [SerializeField] float roundOverTime = 2.4f;

        [Header("Knockout")]
        [Tooltip("Slow motion right after the knockout blow (time scale).")]
        [SerializeField, Range(0.05f, 1f)] float koSlowScale = 0.25f;
        [Tooltip("How long the KO slow motion lasts (real seconds). 0 = none.")]
        [SerializeField, Min(0f)] float koSlowTime = 0.9f;

        [Header("Stage")]
        [Tooltip("Lays out the stage items each round and keeps at least one in play.")]
        [SerializeField] StageItemDirector items;
        [Tooltip("The pause menu. Empty = found in the scene.")]
        [SerializeField] OptionsMenu pauseMenu;

        [Header("Flow")]
        [SerializeField] string rosterScene = "Roster";
        [SerializeField] string startScene = "StartScreen";
        [Tooltip("Save each finished match to this phone's record (PlayerStats).")]
        [SerializeField] bool recordStats = true;
        [Tooltip("Open the pause menu when the app loses focus (phone call, notification shade). Off in the editor by default, " +
                 "so clicking another window doesn't pause the fight.")]
        [SerializeField] bool pauseOnFocusLossInEditor = false;

        [Header("Audio (optional: cues play through AudioManager, clips on the Announcer)")]
        [SerializeField] SfxCue roundCue;
        [SerializeField] SfxCue fightCue;
        [SerializeField] SfxCue koCue;
        [SerializeField] SfxCue timeCue;
        [SerializeField] SfxCue winCue;
        [SerializeField] SfxCue loseCue;
        [SerializeField] AudioMixer mixer;
        [SerializeField] AudioSource announcer;
        [SerializeField] AudioSource music;
        [SerializeField] AudioClip[] roundClips = new AudioClip[3];
        [SerializeField] AudioClip finalRoundClip;
        [SerializeField] AudioClip fightClip;
        [SerializeField] AudioClip koClip;
        [SerializeField] AudioClip timeClip;
        [SerializeField] AudioClip winClip;
        [SerializeField] AudioClip loseClip;

        // ───────────── Events for the HUD ─────────────

        public event Action<MatchPhase> PhaseChanged;
        /// <summary>Big centre text, e.g. "ROUND 1", "FIGHT!", "K.O.". (text, seconds to show; 0 = stay up)</summary>
        public event Action<string, float> Callout;
        /// <summary>A round is about to start (round number). Fighters are reset and locked.</summary>
        public event Action<int> RoundStarting;
        /// <summary>Round winner, or null for a draw.</summary>
        public event Action<Fighter> RoundEnded;
        /// <summary>Round ended by a knockout (true) or by the timer (false): (winner or null, knockout).</summary>
        public event Action<Fighter, bool> RoundEndedBy;
        /// <summary>Match winner, or null for a drawn match.</summary>
        public event Action<Fighter> MatchEnded;
        /// <summary>Fired when the pause state changes.</summary>
        public event Action<bool> PauseChanged;

        // ───────────── State ─────────────

        /// <summary>True while the match is paused (the pause menu is open).</summary>
        public static bool Paused { get; private set; }

        public MatchPhase Phase { get; private set; } = MatchPhase.Intro;
        public int Round { get; private set; }
        public float TimeLeft { get; private set; }
        public float RoundTime => roundTime;
        public int PlayerWins { get; private set; }
        public int OpponentWins { get; private set; }
        public int WinsNeeded => winsNeeded;
        public Fighter Player => player;
        public Fighter Opponent => opponent;
        /// <summary>The match winner once it's over (null = draw or not over).</summary>
        public Fighter Winner { get; private set; }

        Vector3 playerStart, opponentStart;
        bool roundEnding;
        bool ownPause;

        void Awake()
        {
            playerStart = playerSpawn ? playerSpawn.position : player.transform.position;
            opponentStart = opponentSpawn ? opponentSpawn.position : opponent.transform.position;
            Paused = false;
            Time.timeScale = 1f;
            MatchSetup.Player = player;
            if (!pauseMenu) pauseMenu = FindFirstObjectByType<OptionsMenu>();
            if (!items) items = FindFirstObjectByType<StageItemDirector>();
        }

        void OnEnable()
        {
            player.Health.Died += OnSomeoneDied;
            opponent.Health.Died += OnSomeoneDied;
        }

        void OnDisable()
        {
            player.Health.Died -= OnSomeoneDied;
            opponent.Health.Died -= OnSomeoneDied;
        }

        void OnDestroy()
        {
            if (MatchSetup.Player == player) MatchSetup.Player = null;
            Paused = false;
        }

        void Start()
        {
            GameSettings.ApplyVolumes(mixer);
            StartCoroutine(RunRound());
        }

        void Update()
        {
            bool menuOpen = pauseMenu && pauseMenu.IsOpen;
            bool paused = ownPause || menuOpen;
            if (paused != Paused)
            {
                Paused = paused;
                PauseChanged?.Invoke(paused);
            }

            if (Phase != MatchPhase.Fighting || Paused) return;

            TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
            if (TimeLeft <= 0f) StartCoroutine(EndRound(timeOut: true));
        }

        // ───────────── Round flow ─────────────

        IEnumerator RunRound()
        {
            Round++;
            roundEnding = false;
            TimeLeft = roundTime;

            // Never start a round under an open pause menu (it owns the time scale while open).
            while (Paused) yield return null;
            Time.timeScale = 1f;

            LockFighters(true);
            ResetFighters();
            if (items) items.ResetForRound();
            SetPhase(MatchPhase.Intro);
            RoundStarting?.Invoke(Round);

            yield return new WaitForSecondsRealtime(introDelay);

            bool finalRound = PlayerWins == winsNeeded - 1 && OpponentWins == winsNeeded - 1;
            Announce(finalRound ? "FINAL ROUND" : $"ROUND {Round}", roundCallTime,
                     roundCue, finalRound && finalRoundClip ? finalRoundClip : RoundClip(Round));
            yield return new WaitForSecondsRealtime(roundCallTime);

            Announce("FIGHT!", fightCallTime, fightCue, fightClip);
            if (music && !music.isPlaying) music.Play();

            LockFighters(false);
            SetPhase(MatchPhase.Fighting);
        }

        void ResetFighters()
        {
            foreach (var f in new[] { player, opponent })
            {
                var holder = f.GetComponent<ItemHolder>();
                if (holder) holder.Drop();
            }
            player.ResetFighter(playerStart);
            opponent.ResetFighter(opponentStart);
        }

        void OnSomeoneDied()
        {
            if (Phase == MatchPhase.Fighting) StartCoroutine(EndRound(timeOut: false));
        }

        IEnumerator EndRound(bool timeOut)
        {
            if (roundEnding) yield break;
            roundEnding = true;

            // Let a simultaneous hit (double KO) land before judging.
            yield return null;

            SetPhase(MatchPhase.RoundOver);
            LockFighters(true);

            Fighter winner = DecideRoundWinner();
            if (winner == player) PlayerWins++;
            else if (winner == opponent) OpponentWins++;

            string call = timeOut ? "TIME" : "K.O.";
            if (winner == null) call = timeOut ? "TIME · DRAW" : "DOUBLE K.O.";
            Announce(call, roundOverTime * 0.6f, timeOut ? timeCue : koCue, timeOut ? timeClip : koClip);
            RoundEnded?.Invoke(winner);
            RoundEndedBy?.Invoke(winner, !timeOut);

            // Knockout: a slow-motion beat (the punch-in camera and the hit freeze play over it).
            if (!timeOut && koSlowTime > 0f && koSlowScale < 1f && !Paused)
            {
                Time.timeScale = koSlowScale;
                yield return new WaitForSecondsRealtime(koSlowTime);
                if (!Paused) Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, roundOverTime - koSlowTime));
            }
            else yield return new WaitForSecondsRealtime(roundOverTime);

            bool someoneWon = PlayerWins >= winsNeeded || OpponentWins >= winsNeeded;
            if (someoneWon || Round >= maxRounds) EndMatch();
            else StartCoroutine(RunRound());
        }

        Fighter DecideRoundWinner()
        {
            bool pDead = player.Health.IsDead, oDead = opponent.Health.IsDead;
            if (pDead && oDead) return null;
            if (oDead) return player;
            if (pDead) return opponent;

            // Time-out: more health left wins; equal = draw.
            float diff = player.Health.Normalized - opponent.Health.Normalized;
            if (Mathf.Abs(diff) < 0.0001f) return null;
            return diff > 0f ? player : opponent;
        }

        void EndMatch()
        {
            Winner = null;
            if (PlayerWins > OpponentWins) Winner = player;
            else if (OpponentWins > PlayerWins) Winner = opponent;

            if (recordStats)
            {
                var result = Winner == player ? PlayerStats.Result.Win
                           : Winner == opponent ? PlayerStats.Result.Loss
                           : PlayerStats.Result.Draw;
                PlayerStats.Record(result, player.DisplayName);
            }

            Time.timeScale = 1f;
            SetPhase(MatchPhase.MatchOver);
            string text = Winner == player ? "YOU WIN" : Winner == opponent ? "YOU LOSE" : "DRAW";
            Announce(text, 0f, Winner == player ? winCue : loseCue, Winner == player ? winClip : loseClip);
            MatchEnded?.Invoke(Winner);
        }

        // ───────────── Buttons (Match over panel, pause menu) ─────────────

        /// <summary>Same fighters, new match (reloads the fight scene).</summary>
        public void Rematch() => SceneLoader.Load(gameObject.scene.name);

        /// <summary>Kept from Prototype 2 (HUDController's Restart button): same as Rematch.</summary>
        public void Restart() => Rematch();

        /// <summary>Back to the Roster with both picks kept.</summary>
        public void ChangeFighter() => SceneLoader.Load(rosterScene);

        public void QuitToStart() => SceneLoader.Load(startScene);

        public void TogglePause() => SetPaused(!Paused);

        /// <summary>Open / close the pause menu (the Options popup). Without one, just stops time.</summary>
        public void SetPaused(bool paused)
        {
            if (pauseMenu)
            {
                if (paused) pauseMenu.Open(); else pauseMenu.Close();
                return;
            }
            if (ownPause == paused) return;
            ownPause = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (music) { if (paused) music.Pause(); else music.UnPause(); }
        }

        // Phone call, home button, notification shade: pause, and stay paused until the player resumes.
        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && Phase == MatchPhase.Fighting && FocusPauseAllowed) SetPaused(true);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && Phase == MatchPhase.Fighting && FocusPauseAllowed) SetPaused(true);
        }

        bool FocusPauseAllowed => !Application.isEditor || pauseOnFocusLossInEditor;

        // ───────────── Helpers ─────────────

        void LockFighters(bool locked)
        {
            player.SetControlsLocked(locked);
            opponent.SetControlsLocked(locked);
        }

        void SetPhase(MatchPhase next)
        {
            Phase = next;
            PhaseChanged?.Invoke(next);
        }

        void Announce(string text, float seconds, SfxCue cue, AudioClip clip)
        {
            Callout?.Invoke(text, seconds);
            AudioManager.Play(cue);
            if (announcer && clip) announcer.PlayOneShot(clip);
        }

        AudioClip RoundClip(int round)
        {
            if (roundClips == null || roundClips.Length == 0) return null;
            return roundClips[Mathf.Clamp(round - 1, 0, roundClips.Length - 1)];
        }
    }
}
