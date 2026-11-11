using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// Runs the tutorial (Docs/Tutorial.md) over the game scene: one line of explanation at a time in a box (a tap goes
    /// on, once what it shows has finished), with the match showing it — shots thrown for both sides, a stone short of
    /// the min line and one past the out line, an end measured — then the controls, each with its part of the screen
    /// framed (clear of the stone) and a hand acting it out while the aim or the power follows, and last the player's own end against the
    /// AI. Its layer sits in the HUD under the toasts, cards and the settings gear, which stay usable (the settings
    /// popup is where it is skipped).
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        GameController game;
        RectTransform layer, bubble, hand, arrowH, arrowV;
        Image[] edges, dims;
        TextMeshProUGUI text, hint;
        bool hintUp, tapped;
        int spot = -1;
        // the part of the screen shown (GameController.TutorialArea), framed; -1 none. The target's and the guard's have
        // the rest dimmed
        const int Guard = 3, Target = 4;

        const float BubbleW = 420, Margin = 14;   // css px
        const float HandW = 50, HandH = 72, ArrowLong = 96, ArrowShort = 27;
        const float Small = 0.8f, Lower = 28, LowerPower = 18;   // the controls' hand and arrows: their size, and how far below the area's middle
        const float Edge = 3;

        internal void Begin(GameController g, RectTransform hudLayer)
        {
            game = g; layer = hudLayer;
            Build();
            Run().Forget();
        }

        void Build()
        {
            dims = new Image[4];   // round the part shown
            for (int i = 0; i < dims.Length; i++)
            {
                dims[i] = Ui.Rect(layer, "Dim" + i).gameObject.AddComponent<Image>();
                dims[i].color = new Color(0, 0, 0, 0.55f); dims[i].raycastTarget = false;
            }
            edges = new Image[8];   // four sides, each in up to two pieces round the stone
            for (int i = 0; i < edges.Length; i++)
            {
                edges[i] = Ui.Rect(layer, "Edge" + i).gameObject.AddComponent<Image>();
                edges[i].color = Palette.Gold; edges[i].raycastTarget = false;
            }

            var box = Ui.Panel(layer, "Bubble", Palette.WithA(Palette.Panel, 0.78f), Palette.Line, 12);   // the sheet shows through
            box.raycastTarget = false;
            bubble = box.rectTransform;
            text = Ui.Label(bubble, "", 16, Palette.Text, false, TextAnchor.MiddleCenter);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            hint = Ui.Label(bubble, Loc.T("tutorial.tap"), 11, Palette.Brass, false, TextAnchor.LowerCenter, "Hint");   // (centred: clear of the stones left at both edges)
            hint.raycastTarget = false;

            hand = Sprite(layer, "Hand", "Tutorial/hand", new Vector2(0.38f, 1));   // the fingertip is the point
            arrowH = Sprite(layer, "ArrowH", "Tutorial/arrow_h", new Vector2(0.5f, 0.5f));
            arrowV = Sprite(layer, "ArrowV", "Tutorial/arrow_v", new Vector2(0.5f, 0.5f));
            ShowHand(false);
        }

        static RectTransform Sprite(RectTransform parent, string name, string path, Vector2 pivot)
        {
            var img = Ui.Rect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = Resources.Load<Sprite>(path);
            img.preserveAspect = true; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = pivot;
            return rt;
        }

        // ---------------- the steps ----------------

        // (UniTask: the steps wait on the game's scaled clock, so the network popup's pause stops them too, and stop when
        // this goes)
        CancellationToken Ct => destroyCancellationToken;
        UniTask Frame() => UniTask.Yield(PlayerLoopTiming.Update, Ct);
        UniTask Wait(float seconds) => UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.DeltaTime, PlayerLoopTiming.Update, Ct);

        async UniTaskVoid Run()
        {
            try
            {
                await Say("tutorial.welcome");
                await Say("tutorial.about");
                // the rules, each shown by the match
                await Say("tutorial.turns", Turns);
                await Say("tutorial.void", Void);
                await Say("tutorial.score", Score);
                // the controls, on the player's stone (their own input still off)
                game.TutorialReadyToAim();
                await Say("tutorial.controls");
                await Say("tutorial.aim", AimDemo, area: 0);
                await Say("tutorial.power", PowerDemo, area: 1);
                await Say("tutorial.spot", SpotDemo, area: 2);
                // on to a real start: the turn order (a coin, or the shot the player throws themselves). From here the tutorial
                // counts as done (a player who leaves now is not shown it again)
                TutorialProgress.MarkDone();
                await Say("tutorial.lsd");
                // the turn order by a coin (GameConfig.coinToss): told before it is tossed (the target shot is told on
                // the sheet, as it is thrown)
                if (GameConfig.Load().coinToss) await Say("tutorial.coin");
            }
            catch (OperationCanceledException) { return; }
            Play();
        }

        // the match as any starts (its card, the draws, the result), the input the player's; the layer comes back as
        // the draw is to be thrown, for the target, and as the end starts, for the bonus guard
        void Play()
        {
            layer.gameObject.SetActive(false);
            game.TutorialPlay(go => Note("tutorial.target", Target, 0.45f, go).Forget(), go => Note("tutorial.guard", Guard, 0.45f, go).Forget());
        }

        // something on the sheet pointed out (the target, the bonus guard), then play
        async UniTaskVoid Note(string key, int area, float after, Action go)
        {
            try
            {
                await Wait(after);   // the card before fading out first, and the camera on its way
                layer.gameObject.SetActive(true);
                await Say(key, area: area);
            }
            catch (OperationCanceledException) { return; }
            layer.gameObject.SetActive(false);
            go();
        }

        // a line in the box (with one part of the screen framed, if given) while what it shows plays; a tap goes on once
        // that has finished
        async UniTask Say(string key, Func<UniTask> show = null, int area = -1)
        {
            text.text = Loc.T(key);
            game.TutorialPop();   // a line coming up sounds like a card
            hintUp = false; tapped = false;
            spot = area;
            if (show != null) await show();
            hintUp = true;
            while (!tapped) await Frame();
            spot = -1;
            ShowHand(false);
        }

        async UniTask Turns()
        {
            await Throw(Team.B, 50, 0, 0.70);
            await Wait(0.3f);
            await Throw(Team.W, 60, 0, 0.66);
        }

        // a stone short of the min line, then one past the out line: both gone
        async UniTask Void()
        {
            await Throw(Team.B, 44, 0, 0.36);
            await Wait(0.3f);
            await Throw(Team.W, 37, 0, 0.86);
        }

        async UniTask Throw(Team team, double lane, double ang, double pw)
        {
            bool done = false;
            game.TutorialThrow(team, lane, ang, pw, () => done = true);
            while (!done) await Frame();
        }

        // one more of the player's stones nearer the centre than the AI's, then the end measured: two points
        async UniTask Score()
        {
            game.TutorialPlace(Team.B, 41, 163);
            await Wait(0.4f);
            bool done = false;
            game.TutorialMeasure(() => done = true);
            while (!done) await Frame();
        }

        // left of the stone: a finger sliding sideways turns the aim with it (the arrow still, in the middle of the area,
        // the hand along under it)
        async UniTask AimDemo()
        {
            // (where, worked out every frame from the area: it follows the screen)
            Vector2 Mid() { var a = game.TutorialArea(spot); return new Vector2(a.x + a.width * 0.5f, a.y + a.height * 0.5f - HandH * Small * 0.5f + Lower); }
            ShowHand(true, arrowH);
            const float Swing = (ArrowLong * 0.5f - 12) * Small, Turns = 2, Dur = 3.2f;
            for (float t = 0; t < Dur; t += Time.deltaTime)
            {
                float dx = Mathf.Sin(t / Dur * Turns * 2 * Mathf.PI) * Swing;
                PlaceArrow(arrowH, Mid() - new Vector2(0, ArrowShort * Small * 0.5f), ArrowLong * Small);   // just over the fingertip
                PlaceHand(Mid() + new Vector2(dx, 0), Small);
                game.TutorialSetAim(dx / Swing * 0.3);
                await Frame();
            }
            game.TutorialSetAim(0);
            ShowHand(false);
        }

        // right of the stone: a finger pulling down (or up) from where it touched fills the power (the arrow still, the
        // length of the pull, in the middle of the area; the hand beside it, from its top to its bottom)
        async UniTask PowerDemo()
        {
            float Full() => Mathf.Min(game.TutorialPowerPx * 0.85f, game.TutorialArea(spot).height * 0.6f);   // (kept on the screen)
            // the arrow's middle, and the fingertip's start: half the pull (as drawn, Small) up from there
            Vector2 Mid() { var a = game.TutorialArea(spot); return new Vector2(a.x + a.width * 0.5f, a.y + a.height * 0.5f + LowerPower); }
            float Long() => (Full() + 24) * Small;
            Vector2 Top() => Mid() + new Vector2(ArrowShort * Long() / ArrowLong * 0.5f + HandW * Small * 0.38f + 6, -Full() * Small * 0.5f);
            void Arrow() => PlaceArrow(arrowV, Mid(), Long());
            ShowHand(true, arrowV);
            for (int k = 0; k < 2; k++)
            {
                for (float t = 0; t < 1.3f; t += Time.deltaTime)
                {
                    float p = Mathf.SmoothStep(0, 1, t / 1.3f);
                    float full = Full();
                    Arrow();
                    PlaceHand(Top() + new Vector2(0, p * full * Small), Small);
                    game.TutorialSetPower(p * full / game.TutorialPowerPx);
                    await Frame();
                }
                await Wait(0.35f);
                game.TutorialSetPower(-1);
                Arrow();
                PlaceHand(Top(), Small);
                await Wait(0.45f);
            }
            ShowHand(false);
        }

        // above the min line: a touch turns the aim to that spot at once, and a drag takes it along
        async UniTask SpotDemo()
        {
            var from = game.TutorialPoint(30, (float)Sheet.TeeY - 8);
            var to = game.TutorialPoint(72, (float)Sheet.TeeY + 12);
            ShowHand(true);
            PlaceHand(from);
            await Wait(0.5f);
            // the touch: the hand presses, the aim jumps there
            for (float t = 0; t < 0.25f; t += Time.deltaTime) { hand.localScale = Vector3.one * (1 - 0.15f * t / 0.25f); await Frame(); }
            game.TutorialAimAt(from);
            await Wait(0.6f);
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                var p = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t / 1.6f));
                PlaceHand(p);
                game.TutorialAimAt(p);
                await Frame();
            }
            await Wait(0.4f);
            hand.localScale = Vector3.one;
            game.TutorialSetAim(0);
            ShowHand(false);
        }

        // ---------------- the layer ----------------

        void ShowHand(bool on, RectTransform arrow = null)
        {
            hand.gameObject.SetActive(on);
            arrowH.gameObject.SetActive(on && arrow == arrowH);
            arrowV.gameObject.SetActive(on && arrow == arrowV);
            hand.localScale = Vector3.one;
        }

        // the fingertip at p (css px, top left origin), the hand this much of its size
        void PlaceHand(Vector2 p, float size = 1)
        {
            Ui.TopLeft(hand, p.x, p.y, HandW * size, HandH * size);
            hand.pivot = new Vector2(0.38f, 1);
            hand.anchoredPosition = new Vector2(p.x, -p.y);
        }

        // an arrow centred on c, this long (its thickness kept in proportion)
        void PlaceArrow(RectTransform arrow, Vector2 c, float length)
        {
            float thick = ArrowShort * length / ArrowLong;
            arrow.sizeDelta = arrow == arrowH ? new Vector2(length, thick) : new Vector2(thick, length);
            arrow.anchoredPosition = new Vector2(c.x, -c.y);
        }

        // laid out after the steps have run this frame: a step that has just changed the line, the framed part or the hint
        // shows it at once, not with last frame's layout
        void LateUpdate()
        {
            if (!game || !layer.gameObject.activeSelf) return;
            // a tap anywhere but on UI (the settings gear, a card) goes on, when the line allows it; it clicks like any
            // tap on the background (UiClickSound)
            if (hintUp)
                foreach (var p in UiClickSound.NewPresses())
                    if (!UiClickSound.OverUi(p)) { tapped = true; break; }
            var view = game.TutorialView;
            // the box: where the toasts stand, just above the min line (over a toast that is up), clear of the stone
            float w = Mathf.Min(BubbleW, view.x - 2 * Margin);
            float th = text.GetPreferredValues(text.text, w - 28, 0).y;
            float h = th + 46;
            float y = Mathf.Max(game.TutorialTopRoom, game.TutorialBoxBottom - h);
            Ui.TopLeft(bubble, (view.x - w) / 2, y, w, h);
            Ui.TopLeft(text.rectTransform, 14, 12, w - 28, th + 4);
            Ui.TopLeft(hint.rectTransform, 14, h - 24, w - 28, 16);
            hint.gameObject.SetActive(hintUp);
            if (hintUp) hint.alpha = 0.55f + 0.45f * Mathf.Cos(Time.time * 4);

            // a gold frame round the part being shown, its lines cut where they would cross the stone; the target's and
            // the guard's with the rest of the screen dimmed
            used = 0;
            bool dim = spot == Target || spot == Guard;
            if (spot >= 0)
            {
                var r = game.TutorialArea(spot);
                if (dim)
                {
                    Ui.TopLeft(dims[0].rectTransform, 0, 0, view.x, r.yMin);
                    Ui.TopLeft(dims[1].rectTransform, 0, r.yMax, view.x, view.y - r.yMax);
                    Ui.TopLeft(dims[2].rectTransform, 0, r.yMin, r.xMin, r.height);
                    Ui.TopLeft(dims[3].rectTransform, r.xMax, r.yMin, view.x - r.xMax, r.height);
                }
                var (c, cr) = game.TutorialStoneClear;
                Line(r.yMin, r.xMin, r.xMax, true, c, cr);
                Line(r.yMax - Edge, r.xMin, r.xMax, true, c, cr);
                Line(r.xMin, r.yMin, r.yMax, false, c, cr);
                Line(r.xMax - Edge, r.yMin, r.yMax, false, c, cr);
            }
            for (int i = 0; i < edges.Length; i++) edges[i].gameObject.SetActive(i < used);
            foreach (var d in dims) d.gameObject.SetActive(dim);
        }

        int used;

        // one side of the frame from a to b (along x if horizontal, else along y) at the other coordinate at, less the
        // part inside the circle (c, cr)
        void Line(float at, float a, float b, bool horizontal, Vector2 c, float cr)
        {
            float mid = at + Edge / 2, d = horizontal ? mid - c.y : mid - c.x;
            if (Mathf.Abs(d) >= cr) { Piece(at, a, b, horizontal); return; }
            float half = Mathf.Sqrt(cr * cr - d * d), cc = horizontal ? c.x : c.y;
            Piece(at, a, Mathf.Min(b, cc - half), horizontal);
            Piece(at, Mathf.Max(a, cc + half), b, horizontal);
        }

        void Piece(float at, float a, float b, bool horizontal)
        {
            if (b - a < 1 || used >= edges.Length) return;
            var rt = edges[used++].rectTransform;
            if (horizontal) Ui.TopLeft(rt, a, at, b - a, Edge);
            else Ui.TopLeft(rt, at, a, Edge, b - a);
        }
    }
}
