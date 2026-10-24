using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Controls;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.DebugTools
{
    /// <summary>
    /// PROTOTYPE text + bar indicators for testing the defence system. Draws with OnGUI, so there's no
    /// Canvas to set up, and it shows on the phone too.
    ///
    ///   Over each fighter:  health bar (green), stamina bar (yellow; red while exhausted) and a state
    ///                       label: BLOCKING / PARRY WINDOW / BLOCKSTUN / GUARD BROKEN / EXHAUSTED / HITSTUN.
    ///                       A red "!" while a TrainingDummy is winding up an attack, or (grey) why it
    ///                       isn't attacking: "come closer: 2.4 / 1.6", "Light Combo is empty", etc.
    ///   Pop-up text:        "BLOCKED  0 dmg  -10 stamina", "-5", "PARRY!", "GUARD BREAK!", "NO STAMINA".
    ///   Item hits (Step 3d): LAUNCHED in the air; on landing a big "STAGGERED!" and a flashing VULNERABLE
    ///                       label with a countdown bar and the damage multiplier; INVINCIBLE while getting up;
    ///                       "BOUNCE!", "WALL BOUNCE!", "WALL SPLAT!" pop-ups; hits on a downed fighter show
    ///                       the boosted damage.
    ///   Bottom of screen:   how to block in the active control scheme (from ControlSchemeSwitcher).
    ///   Top-left panel:     for the first fighter: hits blocked, damage negated, guard breaks, hits taken.
    ///
    /// Setup: put on any object (e.g. the Main Camera). Fighters = Player, Opponent. Switcher optional.
    /// Remove when the real HUD (M7) has these indicators.
    /// Place in: Assets/_Project/Scripts/DebugTools/DefenseDebugHUD.cs
    /// </summary>
    public class DefenseDebugHUD : MonoBehaviour
    {
        [SerializeField] Fighter[] fighters = new Fighter[2];
        [Tooltip("Empty = Main Camera.")]
        [SerializeField] Camera cam;
        [Tooltip("Optional: shows how to block in the active scheme at the bottom of the screen.")]
        [SerializeField] ControlSchemeSwitcher schemeSwitcher;
        [Tooltip("Optional: the Tap Anywhere handler, so the hint matches its latch setting and shows BLOCK LATCHED.")]
        [SerializeField] TapAnywhereInputHandler tapAnywhere;

        [Header("Show")]
        [SerializeField] bool showBars = true;
        [SerializeField] bool showStateText = true;
        [SerializeField] bool showPopups = true;
        [SerializeField] bool showBlockHint = true;
        [SerializeField] bool showStatsPanel = true;

        [Header("Look")]
        [Tooltip("Text and bar size as a fraction of the screen height.")]
        [SerializeField, Range(0.01f, 0.06f)] float textScale = 0.028f;
        [Tooltip("Gap above the fighter's head (world units).")]
        [SerializeField] float headOffset = 0.35f;
        [Tooltip("How long pop-up text stays (seconds).")]
        [SerializeField, Min(0.1f)] float popupTime = 0.9f;

        struct Popup { public Fighter fighter; public string text; public Color colour; public float born; public int slot; public bool big; }

        class Stats { public int blocked, guardBreaks, hitsTaken, parries; public float negated; }

        readonly List<Popup> popups = new List<Popup>();
        readonly Dictionary<Fighter, Stats> stats = new Dictionary<Fighter, Stats>();
        readonly Dictionary<Fighter, TrainingDummy> dummies = new Dictionary<Fighter, TrainingDummy>();
        readonly Dictionary<Fighter, SpriteRenderer> sprites = new Dictionary<Fighter, SpriteRenderer>();

        // Event handlers kept so they can be unsubscribed.
        readonly List<System.Action> unsubscribe = new List<System.Action>();

        GUIStyle label, big, popBig;

        static readonly Color Green = new Color(0.3f, 0.9f, 0.35f);
        static readonly Color Yellow = new Color(1f, 0.85f, 0.2f);
        static readonly Color Red = new Color(1f, 0.25f, 0.2f);
        static readonly Color Orange = new Color(1f, 0.55f, 0.1f);
        static readonly Color Blue = new Color(0.45f, 0.65f, 1f);
        static readonly Color Grey = new Color(0.7f, 0.7f, 0.7f);
        static readonly Color Back = new Color(0f, 0f, 0f, 0.6f);

        void OnEnable()
        {
            foreach (var f in fighters)
            {
                if (!f || stats.ContainsKey(f)) continue;
                var fighter = f;
                stats[fighter] = new Stats();
                dummies[fighter] = fighter.GetComponent<TrainingDummy>();
                sprites[fighter] = fighter.GetComponent<SpriteRenderer>();

                System.Action<Fighter, AttackData> onBlocked = (a, atk) =>
                {
                    var s = stats[fighter];
                    s.blocked++;
                    s.negated += atk.damage - fighter.LastBlockDamageTaken;
                    string cost = fighter.Stamina ? $"  -{fighter.LastBlockStaminaCost:0} stamina" : "";
                    string timing = !fighter.IsAutoBlocking && fighter.LastBlockFramesEarly > 0
                        ? $"  (parry missed: {fighter.LastBlockFramesEarly}f early)" : "";
                    Pop(fighter, $"BLOCKED  {fighter.LastBlockDamageTaken:0.#} dmg{cost}{timing}", Blue);
                };
                System.Action<Fighter, AttackData> onHit = (a, atk) =>
                {
                    stats[fighter].hitsTaken++;
                    // GotHit fires before the state changes, so IsDowned here = hit while lying down.
                    if (fighter.IsDowned)
                        Pop(fighter, $"-{fighter.LastDamageTaken:0.#}  DOWNED x{fighter.LastDamageTaken / Mathf.Max(0.01f, atk.damage):0.##}", Red, big: true);
                    else if (fighter.LastDamageTaken <= 0f)
                        Pop(fighter, "HIT - no damage (not staggered)", Grey);
                    else
                        Pop(fighter, $"-{fighter.LastDamageTaken:0.#}", Red);
                };
                System.Action<float> onDowned = t => Pop(fighter, "STAGGERED!", Orange, big: true);
                System.Action onBounce = () => Pop(fighter, "BOUNCE!", Yellow);
                System.Action<int> onWallBounce = side => Pop(fighter, "WALL BOUNCE!", Yellow, big: true);
                System.Action<int> onSplat = side => Pop(fighter, "WALL SPLAT!", Orange, big: true);
                System.Action onGotUp = () => Pop(fighter, "got up", Grey);
                System.Action<Fighter, AttackData> onParried = (a, atk) =>
                {
                    stats[fighter].parries++;
                    Pop(fighter, $"PERFECT BLOCK!  frame {fighter.LastParryFrame}/{fighter.LastParryWindowFrames}" +
                                 (fighter.Stamina ? "  0 stamina" : ""), Yellow, big: true);
                };
                System.Action onBroken = () =>
                {
                    stats[fighter].guardBreaks++;
                    Pop(fighter, "GUARD BREAK!", Orange);
                };
                System.Action onRefused = () => Pop(fighter, "NO STAMINA", Grey);

                fighter.Blocked += onBlocked;
                fighter.GotHit += onHit;
                fighter.Parried += onParried;
                fighter.GuardBroken += onBroken;
                fighter.BlockRefused += onRefused;
                fighter.Downed += onDowned;
                fighter.GroundBounced += onBounce;
                fighter.WallBounced += onWallBounce;
                fighter.WallSplatted += onSplat;
                fighter.GotUp += onGotUp;

                var holder = fighter.GetComponent<KungFuCrashout.Props.ItemHolder>();
                System.Action<KungFuCrashout.Props.Carryable> onCaught = item => Pop(fighter, "CAUGHT!", Yellow, big: true);
                if (holder) holder.Caught += onCaught;

                unsubscribe.Add(() =>
                {
                    if (holder) holder.Caught -= onCaught;
                    fighter.Downed -= onDowned;
                    fighter.GroundBounced -= onBounce;
                    fighter.WallBounced -= onWallBounce;
                    fighter.WallSplatted -= onSplat;
                    fighter.GotUp -= onGotUp;
                    fighter.Blocked -= onBlocked;
                    fighter.GotHit -= onHit;
                    fighter.Parried -= onParried;
                    fighter.GuardBroken -= onBroken;
                    fighter.BlockRefused -= onRefused;
                });
            }
        }

        void OnDisable()
        {
            foreach (var u in unsubscribe) u();
            unsubscribe.Clear();
            stats.Clear();
            popups.Clear();
        }

        void Pop(Fighter f, string text, Color colour, bool big = false)
        {
            if (!showPopups) return;
            // Stack pop-ups that arrive together (e.g. BLOCKED + GUARD BREAK) instead of overlapping.
            int slot = 0;
            foreach (var p in popups)
                if (p.fighter == f && Time.unscaledTime - p.born < 0.25f) slot = Mathf.Max(slot, p.slot + 1);
            popups.Add(new Popup { fighter = f, text = text, colour = colour, born = Time.unscaledTime, slot = slot, big = big });
        }

        // ───────────── Drawing ─────────────

        void OnGUI()
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;

            float unit = Screen.height * textScale;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                big = new GUIStyle(label);
                popBig = new GUIStyle(label);
            }
            label.fontSize = Mathf.RoundToInt(unit * 0.8f);
            big.fontSize = Mathf.RoundToInt(unit * 2f);
            popBig.fontSize = Mathf.RoundToInt(unit * 1.4f);

            foreach (var f in fighters)
            {
                if (!f) continue;
                Vector2 head = HeadOnScreen(f);
                if (showBars) DrawBars(f, head, unit);
                if (showStateText) DrawState(f, head, unit);
            }

            if (showPopups) DrawPopups(unit);
            if (showBlockHint) DrawBlockHint(unit);
            if (showStatsPanel) DrawStats(unit);
        }

        /// <summary>Top-centre of the fighter in GUI coordinates (y down).</summary>
        Vector2 HeadOnScreen(Fighter f)
        {
            float top = sprites.TryGetValue(f, out var sr) && sr ? sr.bounds.max.y : f.transform.position.y + 1f;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(f.X, top + headOffset, f.transform.position.z));
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        void DrawBars(Fighter f, Vector2 head, float unit)
        {
            float w = unit * 5f, h = unit * 0.4f;
            float x = head.x - w * 0.5f;

            // Health
            float hpY = head.y - h * 2.6f;
            Bar(new Rect(x, hpY, w, h), f.Health ? f.Health.Normalized : 0f, Green);

            // Stamina (if the fighter has it)
            var st = f.Stamina;
            if (!st) return;
            float stY = hpY + h * 1.3f;
            bool flash = st.IsExhausted && Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.5f;
            Bar(new Rect(x, stY, w, h), st.Normalized, st.IsExhausted ? (flash ? Red : Orange) : Yellow);
        }

        void DrawState(Fighter f, Vector2 head, float unit)
        {
            string text = null;
            Color c = Color.white;

            switch (f.State)
            {
                case FighterState.Blocking:
                    if (f.IsParryActive) { text = $"PARRY WINDOW  {f.BlockFrame}/{f.ParryWindowFrames}f"; c = Yellow; }
                    else if (f.InBlockstun) { text = "BLOCKSTUN"; c = Blue; }
                    else if (f.IsAutoBlocking) { text = "AUTO-BLOCK"; c = Blue; }
                    else { text = "BLOCKING"; c = Blue; }
                    break;
                case FighterState.Hitstun:
                    if (f.IsGuardBroken) { text = "GUARD BROKEN"; c = Orange; }
                    else if (f.IsParryStunned) { text = "PARRIED - STUNNED"; c = Yellow; }
                    else { text = "HITSTUN"; c = Red; }
                    break;
                case FighterState.KO:
                    text = "KO"; c = Grey; break;
                case FighterState.Downed:
                    DrawVulnerable(f, head, unit);
                    break;
            }
            if (text == null && f.IsWallSplatted) { text = "WALL SPLAT"; c = Orange; }
            else if (f.State == FighterState.Hitstun && f.IsLaunched && !f.IsGuardBroken) { text = "LAUNCHED"; c = Red; }
            if (text == null && f.IsInvulnerable) { text = "INVINCIBLE (getting up)"; c = Grey; }
            if (text == null && !f.IsDowned && f.Stamina && f.Stamina.IsExhausted) { text = "EXHAUSTED - can't block"; c = Orange; }

            if (text != null) Text(new Rect(head.x - unit * 8f, head.y - unit * 3.9f, unit * 16f, unit), text, c, label);

            // Dummy wind-up warning, or why it isn't attacking.
            if (dummies.TryGetValue(f, out var d) && d && d.AttackWhenClose)
            {
                if (d.IsWindingUp)
                    Text(new Rect(head.x - unit * 2f, head.y - unit * 6.6f, unit * 4f, unit * 2.4f), "!", Red, big);
                else if (d.Status != null)
                    Text(new Rect(head.x - unit * 12f, head.y - unit * 5f, unit * 24f, unit), d.Status, Grey, label);
            }
        }

        /// <summary>Big flashing VULNERABLE, a countdown bar for the punish window and the damage bonus.</summary>
        void DrawVulnerable(Fighter f, Vector2 head, float unit)
        {
            bool flash = Mathf.Repeat(Time.unscaledTime * 6f, 1f) < 0.5f;
            Text(new Rect(head.x - unit * 10f, head.y - unit * 5.4f, unit * 20f, unit * 1.6f),
                 "VULNERABLE", flash ? Red : Color.white, popBig);

            float w = unit * 6f, h = unit * 0.5f;
            float t = f.DownedDuration > 0f ? f.DownedTimeLeft / f.DownedDuration : 0f;
            Bar(new Rect(head.x - w * 0.5f, head.y - unit * 3.7f, w, h), t, Orange);

            var data = f.LastDownedData;
            if (data && data.downedDamageMultiplier > 1f)
                Text(new Rect(head.x - unit * 8f, head.y - unit * 3.1f, unit * 16f, unit),
                     $"x{data.downedDamageMultiplier:0.##} damage - punish!", Yellow, label);
        }

        void DrawPopups(float unit)
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                float age = Time.unscaledTime - p.born;
                if (age >= popupTime || !p.fighter) { popups.RemoveAt(i); continue; }

                float t = age / popupTime;
                Vector2 head = HeadOnScreen(p.fighter);
                float y = head.y - unit * (5.2f + p.slot * 1.1f) - t * unit * 2f; // rises
                var c = p.colour; c.a = 1f - t * t;                                // fades
                Text(new Rect(head.x - unit * 10f, y, unit * 20f, p.big ? unit * 1.6f : unit), p.text, c, p.big ? popBig : label);
            }
        }

        void DrawBlockHint(float unit)
        {
            string hint = !schemeSwitcher ? null : schemeSwitcher.Current switch
            {
                ControlSchemeSwitcher.Scheme.SplitZones => "Block: hold the LEFT side of the screen (press just before a hit = parry)",
                ControlSchemeSwitcher.Scheme.TapAnywhere => TapAnywhereHint(),
                _ => null
            };
            if (hint == null) return;

            var r = new Rect(0f, Screen.height - unit * 1.6f, Screen.width, unit * 1.2f);
            Fill(r, new Color(0f, 0f, 0f, 0.35f));
            Text(r, hint, Color.white, label);
        }

        // string TapAnywhereHint()
        // {
        //     if (!tapAnywhere || !tapAnywhere.LatchDownSwipeBlock)
        //         return "Block: swipe DOWN and hold (or double press and hold)";
        //     return tapAnywhere.IsBlockLatched
        //         ? "BLOCK LATCHED: tap, hold, dash or jump to drop it"
        //         : "Block: swipe DOWN (stays on until you attack, dash or jump)";
        // }

        string TapAnywhereHint()
        {
            return "Block: swipe DOWN and hold (or double press and hold)";
        }

        void DrawStats(float unit)
        {
            var f = fighters.Length > 0 ? fighters[0] : null;
            if (!f || !stats.TryGetValue(f, out var s)) return;

            string stamina = f.Stamina ? $"Stamina {f.Stamina.Current:0}/{f.Stamina.Max:0}" : "Stamina: none (blocking is free)";
            string text =
                $"{f.DisplayName}   {stamina}\n" +
                $"Blocked {s.blocked}   Damage negated {s.negated:0.#}   Parries {s.parries}\n" +
                $"Guard breaks {s.guardBreaks}   Hits taken {s.hitsTaken}";

            var style = new GUIStyle(label) { alignment = TextAnchor.UpperLeft };
            var r = new Rect(unit * 0.5f, unit * 2.2f, unit * 22f, unit * 3.4f); // below the scheme label
            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            Text(new Rect(r.x + unit * 0.3f, r.y + unit * 0.2f, r.width, r.height), text, Color.white, style);
        }

        // ───────────── Helpers ─────────────

        static void Bar(Rect r, float fill, Color colour)
        {
            Fill(r, Back);
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), colour);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Text(Rect r, string text, Color c, GUIStyle style)
        {
            // 1-pixel shadow so it reads over any background.
            var shadow = new Color(0f, 0f, 0f, c.a * 0.8f);
            style.normal.textColor = shadow;
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }
    }
}