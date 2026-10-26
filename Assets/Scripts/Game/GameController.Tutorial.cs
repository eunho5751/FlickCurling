using System;
using FlickCurling.Core;
using UnityEngine;

namespace FlickCurling.Game
{
    // The tutorial (Docs/Tutorial.md): the game scene opened for it, run by a TutorialDirector. The match here only does
    // what the director asks (shots thrown for it, stones placed, an end measured, the aim and power moved), with the
    // player's input off, until the last step: a match against the easy AI as any starts, with the turn order shot (the
    // player's own first throws), then one end, the director first pointing out the bonus guard it starts with. No shot
    // clock, no extra end. The settings popup's way out is "skip the tutorial".
    public sealed partial class GameController
    {
        TutorialDirector tutorial;
        bool tutorialInput;          // the player's own end: input as in any match
        Action tutorialSettled;      // the director waits for the shot it had thrown to come to rest
        Action tutorialMeasured;     // ... or for an end's measuring to finish
        Action<Action> tutorialBeforeEnd;   // ... or to say something as the end starts (given how to go on)
        Action<Action> tutorialBeforeDuel;  // ... or as the turn order shot is to be thrown

        bool inTutorial;   // from the start of StartTutorial, before its director is up (the HUD is drawn first)
        bool InTutorial => inTutorial;
        bool TutorialBlocksInput => InTutorial && !tutorialInput;

        void StartTutorial()
        {
            inTutorial = true;
            token++;
            hud.HideCard();
            mode = Mode.Easy; mapKey = "base";
            ai[0] = null; ai[1] = AiLevel.Easy;   // the sheet's names read "you" and "the AI" from the start
            hud.HudVisible = true;
            Ui.Flip = false;
            OnlineReset();
            NewTutorialMatch(Team.W);
            Resize(false);
            SetCam(AimView(1));
            camTarget = null;
            var go = new GameObject("Tutorial");
            go.transform.SetParent(transform, false);
            tutorial = go.AddComponent<TutorialDirector>();
            tutorial.Begin(this, hud.AddLayer("Tutorial"));
        }

        // a fresh end on an empty sheet (no bonus guard), hammer to the given side; nothing is thrown until asked
        void NewTutorialMatch(Team hammer)
        {
            int seed = fx.Next();
            m = new Match(Maps.Build(mapKey), PhysicsParams.Default, seed) { Listener = new LiveEvents(this) };
            brain = new Ai(seed ^ 0x5bd1e995);
            m.Hammer = hammer;
            m.StartEnd();
            m.Stones.Clear();
            fades.Clear(); floats.Clear(); measure = null;
            ShowField(m.Field);
            userMoved = false;
            state = St.Menu;
            RenderHud();
        }

        // ---------------- what the director asks for ----------------

        /// <summary>A shot for the given side, shown as the AI's are (the stone slides to its lane, the aim and power
        /// rise, then it goes); <paramref name="settled"/> once everything has stopped and been judged.</summary>
        internal void TutorialThrow(Team team, double lane, double ang, double pw, Action settled)
        {
            m.Cur = team;
            m.Snapshot = m.Stones.ConvertAll(s => s.Clone());
            m.LaunchX = lane;
            ResetShotInput();
            tutorialSettled = settled;
            state = St.AiPull;
            aiPull = new AiPull { Ang = ang, Pw = pw, Lx = lane, T = 0 };
            CamTo(AimView(1), 3.5f);
        }

        /// <summary>A stone put straight on the sheet.</summary>
        internal void TutorialPlace(Team team, double x, double y)
        {
            m.Stones.Add(new Stone { Id = 1000 + m.Stones.Count, Team = team, X = x, Y = y, Placed = true });
        }

        /// <summary>The end measured as at its close (lines from the centre, the scoring stones, the points); then
        /// <paramref name="done"/> instead of the end's card.</summary>
        internal void TutorialMeasure(Action done)
        {
            tutorialMeasured = done;
            FinishEnd();
        }

        /// <summary>The player's stone at the launch line, ready to aim, the aim straight up and no power: the
        /// director moves them (the player's own input stays off).</summary>
        internal void TutorialReadyToAim()
        {
            NewTutorialMatch(Team.W);
            m.Cur = Team.B; m.LaunchX = 50;
            aimAng = 0; ResetShotInput();
            state = St.Aim;
            CamTo(AimView(1), 3.5f);
        }

        internal void TutorialPop() => sfx.Pop();
        internal void TutorialSetAim(double ang) => aimAng = Math.Max(-AimMax, Math.Min(AimMax, ang));
        internal void TutorialSetPower(float p) => power = p;   // (below 0: no gauge)
        internal void TutorialAimAt(Vector2 css) { if (OnSheet(css)) AimAt(World(css)); }

        /// <summary>The parts of the screen the controls use, in css px (top left origin): left of the stone below the min
        /// line (aim), right of it (power), and above the min line (aim at a spot).</summary>
        internal Rect TutorialArea(int which)
        {
            const float Inset = 4;   // css px off the screen's edges
            float hog = Mathf.Clamp(Y(HOG), 0, ch), stoneX = Mathf.Clamp(X((float)m.LaunchX), 0, cw);
            float top = hog + Inset, bottom = ch - Inset;
            // the two sides below the min line meet at the stone's centre (the frame's lines leave the stone out:
            // TutorialStoneClear)
            return which switch
            {
                0 => Rect.MinMaxRect(Inset, top, stoneX, bottom),
                1 => Rect.MinMaxRect(stoneX, top, cw - Inset, bottom),
                3 => TutorialGuardArea(),
                4 => TutorialTargetArea(),
                _ => Rect.MinMaxRect(Inset, topPx, cw - Inset, hog - Inset),
            };
        }

        /// <summary>The launch stone with its power ring and a little room (centre and radius, css px): a frame's lines
        /// are cut where they would cross it.</summary>
        internal (Vector2 c, float r) TutorialStoneClear =>
            (new Vector2(X((float)m.LaunchX), Y(LY)), Mathf.Max(SR * s, launchRingRadius * s + launchRingWidth) + 6);

        internal Vector2 TutorialStone() => new Vector2(X((float)m.LaunchX), Y(LY));
        internal Vector2 TutorialPoint(float x, float y) => new Vector2(X(x), Y(y));
        internal float TutorialPowerPx => maxPx;   // a full pull, in css px
        internal Vector2 TutorialView => new Vector2(cw, ch);
        internal float TutorialTopRoom => topPx;   // the score row's height at the top
        // where the box goes: where the toasts stand (just above the min line), over any toast up now
        internal float TutorialBoxBottom => hud.ToastY - hud.ToastStackHeight;

        /// <summary>The player's own end against the easy AI: the player has the last stone (the AI starts, with the
        /// bonus guard).</summary>
        internal void TutorialPlay(Action<Action> beforeDuel, Action<Action> beforeEnd)
        {
            tutorialInput = true;
            tutorialBeforeDuel = beforeDuel;
            tutorialBeforeEnd = beforeEnd;
            int seed = fx.Next();
            m = new Match(Maps.Build(mapKey), PhysicsParams.Default, seed) { Listener = new LiveEvents(this) };
            brain = new Ai(seed ^ 0x5bd1e995);
            m.StartMatch();   // the turn order shot first, as in any match (its card, then the draw thrown together)
            DuelReset();
            fades.Clear(); floats.Clear(); measure = null;
            ShowField(m.Field);
            userMoved = false;
            state = St.Coin;
            RenderHud();
            CamTo(AimView(1), 3.5f);
            LsdStartCard();
        }

        // the end has started (its bonus guard down): the director says its piece before the first throw
        bool TutorialHoldsEnd()
        {
            if (tutorialBeforeEnd == null) return false;
            var a = tutorialBeforeEnd; tutorialBeforeEnd = null;
            state = St.Coin;
            RenderHud();
            CamTo(HouseView(zoom), 4);
            a(PrepareShot);
            return true;
        }

        // the turn order shot is up, the target on the sheet: the director points it out before the player throws
        bool TutorialHoldsDuel(Action go)
        {
            if (tutorialBeforeDuel == null) return false;
            var a = tutorialBeforeDuel; tutorialBeforeDuel = null;
            state = St.Coin;
            RenderHud();
            a(go);
            return true;
        }

        /// <summary>Round the turn order shot's target (css px), for the director to frame.</summary>
        internal Rect TutorialTargetArea()
        {
            float x = X((float)m.Lsd.TargetX), y = Y((float)m.Lsd.TargetY), r = (float)Sheet.LsdTargetR * s + 12;
            return Rect.MinMaxRect(x - r, y - r, x + r, y + r);
        }

        /// <summary>Round the bonus guard (css px), for the director to frame.</summary>
        internal Rect TutorialGuardArea()
        {
            var g = m.Stones.Find(st => st.Placed);
            float x = X((float)(g?.X ?? Sheet.PlaceGuardX)), y = Y((float)(g?.Y ?? Sheet.PlaceGuardY)), r = SR * s + 12;
            return Rect.MinMaxRect(x - r, y - r, x + r, y + r);
        }

        // the end's card leads to the tutorial's last card
        void TutorialEndCard()
        {
            state = St.Over;
            var win = m.Winner;
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("tutorial.doneEyebrow"),
                Title = MyResult(win, Team.B),
                Body = Loc.T("tutorial.doneBody"),
                Buttons = { new CardButton(Loc.T("tutorial.start"), () => FinishTutorial()) }
            });
        }

        // settings, "skip the tutorial": asked once more
        void RequestSkipTutorial()
        {
            var under = hud.CardVisible ? cardFactory : null;
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("tutorial.eyebrow"),
                Title = Loc.T("tutorial.skipTitle"),
                Body = Loc.T("tutorial.skipBody"),
                Buttons =
                {
                    new CardButton(Loc.T("tutorial.skipStay"), () => { if (under != null) ShowCard(under); }),
                    new CardButton(Loc.T("tutorial.skipLeave"), () => FinishTutorial())
                }
            });
        }

        // done (or skipped): noted, then the main menu
        void FinishTutorial()
        {
            TutorialProgress.MarkDone();
            if (tutorial) Destroy(tutorial.gameObject);
            ToMenu();
        }
    }
}
