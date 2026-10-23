using System.Collections;
using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// Runs the tutorial (Docs/Tutorial.md) over the game scene: one line of explanation at a time in a box (a tap goes
    /// on, once what it shows has finished), with the match showing it — shots thrown for both sides, a stone short of
    /// the min line and one past the out line, an end measured — then the controls, each with the rest of the screen
    /// dimmed and a hand acting it out while the aim or the power follows, and last the player's own end against the
    /// AI. Its layer sits in the HUD under the toasts, cards and the settings gear, which stay usable (the settings
    /// popup is where it is skipped).
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        GameController game;
        RectTransform layer, bubble, hand, arrowH, arrowV;
        Image[] dims, edges;
        TextMeshProUGUI text, hint;
        bool hintUp, tapped, bubbleTop;
        int spot = -1;   // the part of the screen shown (GameController.TutorialArea), the rest dimmed; -1 none

        const float BubbleW = 420, Margin = 14, BottomRoom = 24;   // css px
        const float HandW = 46, HandH = 76, ArrowLong = 96, ArrowShort = 27;
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

            dims = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                dims[i] = Ui.Rect(layer, "Dim" + i).gameObject.AddComponent<Image>();
                dims[i].color = new Color(0, 0, 0, 0.62f); dims[i].raycastTarget = false;
            }
            edges = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                edges[i] = Ui.Rect(layer, "Edge" + i).gameObject.AddComponent<Image>();
                edges[i].color = Palette.Gold; edges[i].raycastTarget = false;
            }

            var box = Ui.Panel(layer, "Bubble", Palette.Panel, Palette.Line, 12);
            box.raycastTarget = false;
            bubble = box.rectTransform;
            text = Ui.Label(bubble, "", 16, Palette.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            hint = Ui.Label(bubble, Loc.T("tutorial.tap"), 11, Palette.Brass, false, TextAnchor.LowerRight, "Hint");
            hint.raycastTarget = false;

            hand = Sprite(layer, "Hand", "Tutorial/hand", new Vector2(0.25f, 1));   // the fingertip is the point
            arrowH = Sprite(layer, "ArrowH", "Tutorial/arrow_h", new Vector2(0.5f, 0.5f));
            arrowV = Sprite(layer, "ArrowV", "Tutorial/arrow_v", new Vector2(0.5f, 0.5f));
            ShowHand(false);
            StartCoroutine(Run());
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

        IEnumerator Run()
        {
            yield return Say("tutorial.welcome");
            yield return Say("tutorial.about");
            // the rules, each shown by the match
            yield return Say("tutorial.turns", Turns());
            yield return Say("tutorial.hog", Throw(Team.B, 44, 0, 0.36));
            yield return Say("tutorial.back", Throw(Team.W, 37, 0, 0.86));
            yield return Say("tutorial.score", Score());
            // the controls, on the player's stone (their own input still off)
            game.TutorialReadyToAim();
            yield return Say("tutorial.controls");
            yield return Say("tutorial.aim", AimDemo(), area: 0, top: true);
            yield return Say("tutorial.power", PowerDemo(), area: 1, top: true);
            yield return Say("tutorial.spot", SpotDemo(), area: 2);
            yield return Say("tutorial.play");
            // the player's own end: the layer goes, the match takes the input
            layer.gameObject.SetActive(false);
            game.TutorialPlay();
        }

        // a line in the box (with the screen dimmed round one part, if given) while what it shows plays; a tap goes on
        // once that has finished
        IEnumerator Say(string key, IEnumerator show = null, int area = -1, bool top = false)
        {
            text.text = Loc.T(key);
            bubbleTop = top;
            hintUp = false; tapped = false;
            spot = area;
            if (show != null) yield return show;
            hintUp = true;
            while (!tapped) yield return null;
            spot = -1;
            ShowHand(false);
        }

        IEnumerator Turns()
        {
            yield return Throw(Team.B, 50, 0, 0.70);
            yield return new WaitForSeconds(0.3f);
            yield return Throw(Team.W, 60, 0, 0.66);
        }

        IEnumerator Throw(Team team, double lane, double ang, double pw)
        {
            bool done = false;
            game.TutorialThrow(team, lane, ang, pw, () => done = true);
            while (!done) yield return null;
        }

        // one more of the player's stones nearer the centre than the AI's, then the end measured: two points
        IEnumerator Score()
        {
            game.TutorialPlace(Team.B, 43, 165);
            yield return new WaitForSeconds(0.4f);
            bool done = false;
            game.TutorialMeasure(() => done = true);
            while (!done) yield return null;
        }

        // left of the stone: a finger sliding sideways turns the aim with it
        IEnumerator AimDemo()
        {
            var a = game.TutorialArea(spot);
            var at = new Vector2(a.x + a.width * 0.5f, a.y + a.height * 0.55f);
            ShowHand(true, arrowH);
            const float Swing = 60, Turns = 2, Dur = 3.2f;
            for (float t = 0; t < Dur; t += Time.deltaTime)
            {
                float dx = Mathf.Sin(t / Dur * Turns * 2 * Mathf.PI) * Swing;
                PlaceHand(at + new Vector2(dx, 0), arrowH, new Vector2(0, ArrowShort));   // the arrow over the fingertip
                game.TutorialSetAim(dx / Swing * 0.3);
                yield return null;
            }
            game.TutorialSetAim(0);
            ShowHand(false);
        }

        // right of the stone: a finger pulling down (or up) from where it touched fills the power
        IEnumerator PowerDemo()
        {
            var a = game.TutorialArea(spot);
            var at = new Vector2(a.x + a.width * 0.45f, a.y + a.height * 0.25f);
            ShowHand(true, arrowV);
            float full = game.TutorialPowerPx * 0.85f;
            for (int k = 0; k < 2; k++)
            {
                for (float t = 0; t < 1.3f; t += Time.deltaTime)
                {
                    float p = Mathf.SmoothStep(0, 1, t / 1.3f);
                    PlaceHand(at + new Vector2(0, p * full), arrowV, new Vector2(HandW + 18, -HandH * 0.5f));
                    game.TutorialSetPower(p * full / game.TutorialPowerPx);
                    yield return null;
                }
                yield return new WaitForSeconds(0.35f);
                game.TutorialSetPower(-1);
                PlaceHand(at, arrowV, new Vector2(HandW + 18, -HandH * 0.5f));
                yield return new WaitForSeconds(0.45f);
            }
            ShowHand(false);
        }

        // above the min line: a touch turns the aim to that spot at once, and a drag takes it along
        IEnumerator SpotDemo()
        {
            var from = game.TutorialPoint(30, (float)Sheet.TeeY - 8);
            var to = game.TutorialPoint(72, (float)Sheet.TeeY + 12);
            ShowHand(true);
            PlaceHand(from, null, Vector2.zero);
            yield return new WaitForSeconds(0.5f);
            // the touch: the hand presses, the aim jumps there
            for (float t = 0; t < 0.25f; t += Time.deltaTime) { hand.localScale = Vector3.one * (1 - 0.15f * t / 0.25f); yield return null; }
            game.TutorialAimAt(from);
            yield return new WaitForSeconds(0.6f);
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                var p = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t / 1.6f));
                PlaceHand(p, null, Vector2.zero);
                game.TutorialAimAt(p);
                yield return null;
            }
            yield return new WaitForSeconds(0.4f);
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
            hand.pivot = new Vector2(0.25f, 1);
            hand.anchoredPosition = new Vector2(p.x, -p.y);
            if (!arrow) return;
            bool wide = arrow == arrowH;
            arrow.sizeDelta = wide ? new Vector2(ArrowLong, ArrowShort) : new Vector2(ArrowShort, ArrowLong);
            arrow.anchoredPosition = new Vector2(p.x + arrowOffset.x, -(p.y - arrowOffset.y));
        }

        void Update()
        {
            if (!game || !layer.gameObject.activeSelf) return;
            var view = game.TutorialView;
            // the box: across the bottom (or the top, under the score row, when the bottom is being shown)
            float w = Mathf.Min(BubbleW, view.x - 2 * Margin);
            float th = text.GetPreferredValues(text.text, w - 28, 0).y;
            float h = th + 46;
            float y = bubbleTop ? game.TutorialTopRoom + 6 : view.y - h - BottomRoom;
            Ui.TopLeft(bubble, (view.x - w) / 2, y, w, h);
            Ui.TopLeft(text.rectTransform, 14, 12, w - 28, th + 4);
            Ui.TopLeft(hint.rectTransform, 14, h - 24, w - 28, 16);
            hint.gameObject.SetActive(hintUp);
            if (hintUp) hint.alpha = 0.55f + 0.45f * Mathf.Cos(Time.time * 4);

            // the dim round the part being shown (four pieces round a hole), with a gold edge on the hole
            bool on = spot >= 0;
            foreach (var d in dims) d.gameObject.SetActive(on);
            foreach (var e in edges) e.gameObject.SetActive(on);
            if (!on) return;
            var r = game.TutorialArea(spot);
            Ui.TopLeft(dims[0].rectTransform, 0, 0, view.x, r.yMin);                         // above
            Ui.TopLeft(dims[1].rectTransform, 0, r.yMax, view.x, view.y - r.yMax);           // below
            Ui.TopLeft(dims[2].rectTransform, 0, r.yMin, r.xMin, r.height);                  // left
            Ui.TopLeft(dims[3].rectTransform, r.xMax, r.yMin, view.x - r.xMax, r.height);    // right
            Ui.TopLeft(edges[0].rectTransform, r.xMin, r.yMin, r.width, Edge);
            Ui.TopLeft(edges[1].rectTransform, r.xMin, r.yMax - Edge, r.width, Edge);
            Ui.TopLeft(edges[2].rectTransform, r.xMin, r.yMin, Edge, r.height);
            Ui.TopLeft(edges[3].rectTransform, r.xMax - Edge, r.yMin, Edge, r.height);
        }
    }
}
