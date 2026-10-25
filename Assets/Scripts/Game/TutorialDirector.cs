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
        Image[] edges;
        TextMeshProUGUI text, hint;
        bool hintUp, tapped;
        int spot = -1;
        const int Guard = 3;   // spot: round the bonus guard   // the part of the screen shown (GameController.TutorialArea), the rest dimmed; -1 none

        const float BubbleW = 420, Margin = 14;   // css px
        const float HandW = 50, HandH = 72, ArrowLong = 96, ArrowShort = 27;
        const float Edge = 3;

        internal void Begin(GameController g, RectTransform hudLayer)
        {
            game = g; layer = hudLayer;
            // taps anywhere go on (when the line allows it)
            var catcher = Ui.Rect(layer, "Tap").gameObject.AddComponent<Image>();
            Ui.Stretch(catcher.rectTransform);
            catcher.color = new Color(0, 0, 0, 0);
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (hintUp) tapped = true; });
            catcher.gameObject.AddComponent<NoClickSound>();

            edges = new Image[8];   // four sides, each in up to two pieces round the stone
            for (int i = 0; i < edges.Length; i++)
            {
                edges[i] = Ui.Rect(layer, "Edge" + i).gameObject.AddComponent<Image>();
                edges[i].color = Palette.Gold; edges[i].raycastTarget = false;
            }

            var box = Ui.Panel(layer, "Bubble", Palette.WithA(Palette.Panel, 0.78f), Palette.Line, 12);   // the sheet shows through
            box.raycastTarget = false;
            bubble = box.rectTransform;
            text = Ui.Label(bubble, "", 16, Palette.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            hint = Ui.Label(bubble, Loc.T("tutorial.tap"), 11, Palette.Brass, false, TextAnchor.LowerRight, "Hint");
            hint.raycastTarget = false;

            hand = Sprite(layer, "Hand", "Tutorial/hand", new Vector2(0.38f, 1));   // the fingertip is the point
            arrowH = Sprite(layer, "ArrowH", "Tutorial/arrow_h", new Vector2(0.5f, 0.5f));
            arrowV = Sprite(layer, "ArrowV", "Tutorial/arrow_v", new Vector2(0.5f, 0.5f));
            ShowHand(false);
            Run().Forget();
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
                // on to a real start: the turn order shot, which the player throws themselves
                await Say("tutorial.lsd");
            }
            catch (OperationCanceledException) { return; }
            // the match as any starts (its card, the draws, the result), the input the player's; the layer comes back
            // as the end starts, for the bonus guard
            layer.gameObject.SetActive(false);
            game.TutorialPlay(go => GuardNote(go).Forget());
        }

        // the end has started with the first thrower's bonus guard in front of the house: pointed out, then play
        async UniTaskVoid GuardNote(Action go)
        {
            try
            {
                await Wait(0.45f);   // the turn order result's card fading out first, and the camera on its way to the house
                layer.gameObject.SetActive(true);
                await Say("tutorial.guard", area: Guard);
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
            game.TutorialPlace(Team.B, 43, 165);
            await Wait(0.4f);
            bool done = false;
            game.TutorialMeasure(() => done = true);
            while (!done) await Frame();
        }

        // left of the stone: a finger sliding sideways turns the aim with it
        async UniTask AimDemo()
        {
            // (where, worked out every frame from the area: it follows the screen)
            Vector2 At() { var a = game.TutorialArea(spot); return new Vector2(a.x + a.width * 0.5f, a.y + a.height * 0.55f); }
            ShowHand(true, arrowH);
            const float Swing = 60, Turns = 2, Dur = 3.2f;
            for (float t = 0; t < Dur; t += Time.deltaTime)
            {
                float dx = Mathf.Sin(t / Dur * Turns * 2 * Mathf.PI) * Swing;
                PlaceHand(At() + new Vector2(dx, 0), arrowH, new Vector2(0, ArrowShort));   // the arrow over the fingertip
                game.TutorialSetAim(dx / Swing * 0.3);
                await Frame();
            }
            game.TutorialSetAim(0);
            ShowHand(false);
        }

        // right of the stone: a finger pulling down (or up) from where it touched fills the power
        async UniTask PowerDemo()
        {
            Vector2 At() { var a = game.TutorialArea(spot); return new Vector2(a.x + a.width * 0.6f, a.y + a.height * 0.25f); }
            float Full() => Mathf.Min(game.TutorialPowerPx * 0.85f, game.TutorialArea(spot).height * 0.6f);   // (kept on the screen)
            var arrowAt = new Vector2(-(ArrowShort + 14), -HandH * 0.5f);
            ShowHand(true, arrowV);
            for (int k = 0; k < 2; k++)
            {
                for (float t = 0; t < 1.3f; t += Time.deltaTime)
                {
                    float p = Mathf.SmoothStep(0, 1, t / 1.3f);
                    float full = Full();
                    PlaceHand(At() + new Vector2(0, p * full), arrowV, arrowAt);
                    game.TutorialSetPower(p * full / game.TutorialPowerPx);
                    await Frame();
                }
                await Wait(0.35f);
                game.TutorialSetPower(-1);
                PlaceHand(At(), arrowV, arrowAt);
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
            PlaceHand(from, null, Vector2.zero);
            await Wait(0.5f);
            // the touch: the hand presses, the aim jumps there
            for (float t = 0; t < 0.25f; t += Time.deltaTime) { hand.localScale = Vector3.one * (1 - 0.15f * t / 0.25f); await Frame(); }
            game.TutorialAimAt(from);
            await Wait(0.6f);
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                var p = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t / 1.6f));
                PlaceHand(p, null, Vector2.zero);
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

        // the fingertip at p (css px, top left origin); the arrow beside it
        void PlaceHand(Vector2 p, RectTransform arrow, Vector2 arrowOffset)
        {
            Ui.TopLeft(hand, p.x, p.y, HandW, HandH);
            hand.pivot = new Vector2(0.38f, 1);
            hand.anchoredPosition = new Vector2(p.x, -p.y);
            if (!arrow) return;
            bool wide = arrow == arrowH;
            arrow.sizeDelta = wide ? new Vector2(ArrowLong, ArrowShort) : new Vector2(ArrowShort, ArrowLong);
            arrow.anchoredPosition = new Vector2(p.x + arrowOffset.x, -(p.y - arrowOffset.y));
        }

        // laid out after the steps have run this frame: a step that has just changed the line, the framed part or the hint
        // shows it at once, not with last frame's layout
        void LateUpdate()
        {
            if (!game || !layer.gameObject.activeSelf) return;
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

            // a gold frame round the part being shown, its lines cut where they would cross the stone
            used = 0;
            if (spot >= 0)
            {
                var r = game.TutorialArea(spot);
                var (c, cr) = game.TutorialStoneClear;
                Line(r.yMin, r.xMin, r.xMax, true, c, cr);
                Line(r.yMax - Edge, r.xMin, r.xMax, true, c, cr);
                Line(r.xMin, r.yMin, r.yMax, false, c, cr);
                Line(r.xMax - Edge, r.yMin, r.yMax, false, c, cr);
            }
            for (int i = 0; i < edges.Length; i++) edges[i].gameObject.SetActive(i < used);
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
