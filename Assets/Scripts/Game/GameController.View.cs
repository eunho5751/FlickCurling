using System;
using System.Collections.Generic;
using System.Linq;
using FlickCurling.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace FlickCurling.Game
{
    public sealed partial class GameController
    {
        // =====================================================================
        // camera (prototype g / cam). Sizes are css px: px = screen pixels per css px.
        // zoom 1 = the whole sheet in view; s = sFit * zoom css px per sheet unit.
        // =====================================================================

        struct View { public float Zoom, Cx, Cy, Speed; }

        float px = 1, cw = 400, ch = 800, s = 1, sFit = 1, maxPx = 120, bottomPx = 72, topPx = 80;
        float zoom = 1, cx = W / 2, cy = L / 2;
        View? camTarget;
        bool userMoved;
        int lastW, lastH;

        float X(float x) => cw / 2 + (x - cx) * s;
        float Y(float y) => ch / 2 - (y - cy) * s;
        Vector2 World(Vector2 p) => new Vector2(cx + (p.x - cw / 2) / s, cy - (p.y - ch / 2) / s);
        float U(float cssPx) => cssPx / s;
        float MaxZoom => Mathf.Max(1, 8 / sFit);

        // safe-area insets (notch, punch-hole, home bar), css px
        float insL, insR, insT, insB;
        Rect lastSafe;
        bool lastInGame;
        const float SideMin = 120;   // free room per side (css px) needed to move the HUD beside the sheet
#if UNITY_EDITOR
        /// <summary>Editor checks: a device's safe area (screen pixels) for a Game view set to that device's resolution.</summary>
        public static Rect? SafeAreaOverride;

        // statics survive entering play mode when domain reload is off: start every play session without the override
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSafeAreaOverride() => SafeAreaOverride = null;
#endif

        void Resize(bool force)
        {
            int sw = Screen.width, sh = Screen.height;
            var safe = Screen.safeArea;
#if UNITY_EDITOR
            // only while it matches the Game view it was set for; a leftover from another resolution is ignored
            if (SafeAreaOverride.HasValue && SafeAreaOverride.Value.xMax <= sw && SafeAreaOverride.Value.yMax <= sh) safe = SafeAreaOverride.Value;
#endif
            // never trust a safe area outside the screen (the editor's Device Simulator can report another device's)
            float sx0 = Mathf.Clamp(safe.xMin, 0, sw), sy0 = Mathf.Clamp(safe.yMin, 0, sh), sx1 = Mathf.Clamp(safe.xMax, sx0, sw), sy1 = Mathf.Clamp(safe.yMax, sy0, sh);
            safe = sx1 - sx0 < sw * 0.5f || sy1 - sy0 < sh * 0.5f ? new Rect(0, 0, sw, sh) : Rect.MinMaxRect(sx0, sy0, sx1, sy1);
            bool inGame = state != St.Menu;
            float top = hud.HudBottom;
            if (!force && sw == lastW && sh == lastH && safe == lastSafe && inGame == lastInGame && Mathf.Abs(top - topPx) <= 4) return;
            lastW = sw; lastH = sh; lastSafe = safe; lastInGame = inGame;
            // css px: at least a phone-sized logical screen (about 412 x 732)
            px = Mathf.Clamp(Mathf.Min(sw / 412f, sh / 732f), 1, 4);
            cw = sw / px; ch = sh / px;
            insL = safe.xMin / px; insR = (sw - safe.xMax) / px; insB = safe.yMin / px; insT = (sh - safe.yMax) / px;
            hud.SetInsets(insL, insT, insR, insB);
            // the sheet is centred, so keep the same margin on both sides
            float side = Mathf.Max(insL, insR), safeW = cw - 2 * side;
            // pull-from-anywhere needs no room under the launch point; a modest margin keeps the stone in easy thumb reach
            maxPx = Mathf.Clamp(Mathf.Min(cw, ch) * 0.3f, 90, 150);
            bottomPx = 72 + insB;
            float viewH = VIEW_TOP - LY, wFit = (safeW - 20) / (W + 8);
            // wide screens (tablets, foldables, 16:9 phones): with the top row gone, is there room beside the sheet for the HUD?
            float sSide = Mathf.Max(0.4f, Mathf.Min(wFit, (ch - (insT + 18) - bottomPx) / viewH));
            float freeSide = (safeW - (W + 8) * sSide) / 2;
            bool wasSide = hud.SideMode;
            hud.SetSideLayout(inGame && freeSide >= SideMin, freeSide - 20);
            if (hud.SideMode != wasSide && m != null) RenderHud();   // the score pill is laid out differently in a column
            topPx = hud.HudBottom;
            sFit = Mathf.Max(0.4f, Mathf.Min(wFit, (ch - topPx - bottomPx) / viewH));
            if (force || state == St.Menu) { zoom = 1; cx = W / 2; cy = L / 2; camTarget = null; }
            var v = Clamp(new View { Zoom = zoom, Cx = cx, Cy = cy });
            zoom = v.Zoom; cx = v.Cx; cy = v.Cy;
            s = sFit * zoom;
        }

        View Clamp(View c)
        {
            c.Zoom = Mathf.Clamp(c.Zoom, 1, MaxZoom);
            float s2 = sFit * c.Zoom, halfW = cw / (2 * s2), halfH = ch / (2 * s2);
            float xMin = -6, xMax = W + 6, yMin = LY - bottomPx / s2, yMax = VIEW_TOP + topPx / s2;
            c.Cx = xMax - xMin <= 2 * halfW ? W / 2 : Mathf.Clamp(c.Cx, xMin + halfW, xMax - halfW);
            // when the whole sheet fits with height to spare (tall phones), the spare goes below the launch point: more thumb room
            c.Cy = yMax - yMin <= 2 * halfH + 1e-6f ? yMax - halfH : Mathf.Clamp(c.Cy, yMin + halfH, yMax - halfH);
            return c;
        }

        void ClampCam() { var v = Clamp(new View { Zoom = zoom, Cx = cx, Cy = cy }); zoom = v.Zoom; cx = v.Cx; cy = v.Cy; s = sFit * zoom; }
        View AimView(float z) => Clamp(new View { Zoom = z, Cx = W / 2, Cy = -1e9f });
        View HouseView(float z) => Clamp(new View { Zoom = z, Cx = W / 2, Cy = TEEY });
        void CamTo(View v, float speedK = 5) { v.Speed = speedK; camTarget = v; }
        void SetCam(View v) { zoom = v.Zoom; cx = v.Cx; cy = v.Cy; s = sFit * zoom; }
        void Manual() { userMoved = true; camTarget = null; }

        // the playing area (launch stone to just past the back line, rails included) is all on screen
        bool WholeVisible() => X(-4) >= -1 && X(W + 4) <= cw + 1 && Y(VIEW_TOP) >= -1 && Y(LY - SR) <= ch + 1;

        void ZoomAt(float z, float ppx, float ppy)
        {
            float wx = cx + (ppx - cw / 2) / s, wy = cy - (ppy - ch / 2) / s;
            zoom = Mathf.Clamp(z, 1, MaxZoom);
            float s2 = sFit * zoom;
            cx = wx - (ppx - cw / 2) / s2; cy = wy + (ppy - ch / 2) / s2;
            ClampCam();
        }

        void UpdateCam(float dt)
        {
            // follow the shot while it travels, unless the player moved the camera themselves
            if (state == St.Moving && !userMoved)
            {
                var focus = m.Stones.FirstOrDefault(t => t.Id == m.ShotId && t.Live && t.IsMoving)
                            ?? m.Stones.Where(t => t.Live && t.IsMoving).OrderByDescending(t => Sheet.Hypot(t.Vx, t.Vy)).FirstOrDefault();
                if (focus != null)
                {
                    float fX = (float)focus.X, fY = (float)focus.Y, sx = X(fX), sy = Y(fY);
                    float ncx = cx, ncy = cy;
                    if (sy < ch * 0.38f) ncy = fY - (ch * 0.12f) / s;
                    else if (sy > ch * 0.78f) ncy = fY + (ch * 0.28f) / s;
                    if (sx < cw * 0.25f) ncx = fX + (cw * 0.25f) / s;
                    else if (sx > cw * 0.75f) ncx = fX - (cw * 0.25f) / s;
                    var v = Clamp(new View { Zoom = zoom, Cx = ncx, Cy = ncy });
                    v.Speed = 7;
                    camTarget = v;
                }
            }
            if (camTarget.HasValue)
            {
                var t = camTarget.Value;
                float k = 1 - Mathf.Exp(-dt * t.Speed);
                zoom += (t.Zoom - zoom) * k; cx += (t.Cx - cx) * k; cy += (t.Cy - cy) * k;
                if (Mathf.Abs(t.Zoom - zoom) < 0.002f && Mathf.Abs(t.Cx - cx) < 0.05f && Mathf.Abs(t.Cy - cy) < 0.05f)
                {
                    zoom = t.Zoom; cx = t.Cx; cy = t.Cy; camTarget = null;
                }
            }
            ClampCam();
        }

        void ApplyCamera()
        {
            mainCam.orthographicSize = ch / (2 * s);
            mainCam.transform.position = new Vector3(cx, cy, -10);
        }

        // ---- minimap (shown whenever the sheet does not fit the view) ----

        struct Mini { public float X, Y, W, H, Ms; }

        Mini? MiniRect()
        {
            if (state == St.Menu || WholeVisible()) return null;
            float h = Mathf.Clamp(ch * 0.3f, 90, 170), w = h * (W + 8) / L;
            return new Mini { X = cw - w - 10 - insR, Y = Mathf.Max(10 + insT, hud.RightColumnBottom), W = w, H = h, Ms = h / L };   // under the HUD on the right
        }

        Mini? MiniHit(Vector2 p)
        {
            var mm = MiniRect();
            if (!mm.HasValue) return null;
            var r = mm.Value;
            return p.x >= r.X && p.x <= r.X + r.W && p.y >= r.Y && p.y <= r.Y + r.H ? mm : null;
        }

        void MiniJump(Mini r, Vector2 p)
        {
            cx = (p.x - r.X) / r.Ms - 4; cy = L - (p.y - r.Y) / r.Ms;
            Manual(); ClampCam();
        }

        // =====================================================================
        // input on your turn, below the min line, split at the launch stone: left of it, drag sideways to aim;
        // right of it, drag up or down from the touch point for power and let go to shoot (leaving that area cancels). The two work at the same
        // time with two fingers. Above the min line: drag to scroll, pinch / ctrl+wheel to zoom.
        // =====================================================================

        enum GK { None, Pinch, Mini, Pan }

        // shot controls, outside the scroll / pinch gesture: on your turn a touch above the min line aims at that spot
        // and follows it as it drags. Below it two hands: a drag left of the stone steers, a pull right of it sets the power (up or down from the
        // touch point). The pull is called off if it crosses to the aim side, leaves the screen, or another touch lands on
        // the power side.
        void LoadSettings() => sensLevel = GameSettings.SensLevel;

        // aim sensitivity, levels 1..5 in settings (shared with the main menu's, GameSettings); level 3 is the original speed
        static readonly float[] SensMul = { 0.5f, 0.75f, 1f, 1.5f, 2f };
        int sensLevel = GameSettings.DefaultSens;
        float Sens => SensMul[sensLevel];

        sealed class Finger { public bool Power; public Vector2 Start, Last; public bool Out, Moved; }
        readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        double aimAng;      // radians from straight up the sheet
        float power = -1;   // 0..1 while a power finger is down and inside its area, else -1
        int powerId = int.MinValue;
        Vector2 powerStart;
        const float AimPerPx = 0.0035f, AimMax = 1.35f;
        // tap to aim
        bool Charging => power >= 0;
        bool HumanAim => state == St.Aim && !IsAI(m.Cur);

        void ResetShotInput() { fingers.Clear(); power = -1; powerId = int.MinValue; spotId = int.MinValue; }
        int spotId = int.MinValue;   // the finger aiming at a spot above the min line (it follows as the finger drags)

        // below the min line the two hands split at the launch stone: aim on its left, power on its right
        bool RightOfStone(Vector2 p) => p.x >= X((float)m.LaunchX);
        bool OnScreen(Vector2 p) => p.y >= 0 && p.y <= ch && p.x >= 0 && p.x <= cw;

        readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        GK gk;
        bool mouseWasDown;
        Vector2 gStart, gLast, gMid;
        float gD0, gZ0;

        Vector2 Css(Vector2 screen) => new Vector2(screen.x / px, (Screen.height - screen.y) / px);

        void HandleInput()
        {
            // nor while the screen fades, while an online match is being taken from the opponent's device, or while the
            // network popup is up
            if (ScreenFader.Busy || awaitingState || Account.NetworkGuard.Blocking) { mouseWasDown = false; ResetShotInput(); return; }
            // the tutorial shows the controls itself until the player's own end
            if (TutorialBlocksInput) { mouseWasDown = false; return; }
            bool touched = false;
            foreach (var t in ETouch.activeTouches)
            {
                touched = true;
                var sp = t.screenPosition;
                var p = Css(sp);
                switch (t.phase)
                {
                    case TouchPhase.Began: Down(t.touchId, p, sp); break;
                    case TouchPhase.Moved: case TouchPhase.Stationary: Move(t.touchId, p); break;
                    case TouchPhase.Ended: Up(t.touchId, p, true); break;
                    case TouchPhase.Canceled: Up(t.touchId, p, false); break;
                }
            }
            // with several mice attached (or a simulated one), follow whichever is pressed
            var mouse = Mouse.current;
            foreach (var dev in InputSystem.devices)
                if (dev is Mouse mm && mm.leftButton.isPressed) { mouse = mm; break; }
            if (touched || mouse == null) return;
            var msp = mouse.position.ReadValue();
            var mp = Css(msp);
            // edges from the button state, so a press and release inside one input update is not lost
            bool pressed = mouse.leftButton.isPressed;
            if (pressed && !mouseWasDown) Down(-1, mp, msp);
            else if (pressed) Move(-1, mp);
            else if (mouseWasDown) Up(-1, mp, true);
            mouseWasDown = pressed;
            var sc = mouse.scroll.ReadValue();
            if (sc != Vector2.zero && !Hud.IsOverUi(msp)) Wheel(sc, mp);
        }

        void Wheel(Vector2 sc, Vector2 p)
        {
            // browser-like deltas: about 100 css px per notch, positive = scrolling toward the launch end
            float k = Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y)) >= 30 ? 100f / 120 : 100f;
            float dx = sc.x * k, dy = -sc.y * k;
            var kb = Keyboard.current;
            bool ctrl = kb != null && (kb.ctrlKey.isPressed || kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed);
            if (ctrl) ZoomAt(zoom * Mathf.Exp(-dy * 0.004f), p.x, p.y);
            else { cx += dx / s; cy -= dy / s; ClampCam(); }
            Manual();
        }

        // touches on the sheet (rails included) aim on your turn; touches on the table around it always scroll
        bool OnSheet(Vector2 p)
        {
            var w = World(p);
            return w.x >= -4 && w.x <= W + 4 && w.y >= 0 && w.y <= L;
        }

        void Down(int id, Vector2 p, Vector2 screen)
        {
            if (Hud.IsOverUi(screen)) return;
            hud.ClosePopovers();
            if (PressSlot(p)) return;   // (item mode: a hand, never an aim)
            if (HumanAim && !MiniHit(p).HasValue)
            {
                // above the min line a touch aims straight at the touched spot, and dragging moves the aim with it (no
                // power), also while the other hand is pulling
                if (p.y < Y(HOG))
                {
                    if (OnSheet(p)) AimAt(World(p));
                    spotId = id;
                    return;
                }
                // below it, two hands: left of the stone a drag steers, right of it a pull sets the power. A second
                // touch on the power side during a pull calls the shot off; a second one on the aim side does nothing
                bool right = RightOfStone(p);
                var pull = fingers.Values.FirstOrDefault(f => f.Power && !f.Out);
                if (right && pull != null) { pull.Out = true; power = -1; return; }
                if (fingers.Values.All(f => f.Power != right) && pointers.Count == 0)
                {
                    fingers[id] = new Finger { Power = right, Start = p, Last = p };
                    if (right) { powerId = id; powerStart = p; power = 0; }
                }
                return;
            }
            pointers[id] = p;
            if (pointers.Count == 2)
            {
                var ab = pointers.Values.ToArray();
                gk = GK.Pinch;
                gD0 = Vector2.Distance(ab[0], ab[1]); if (gD0 == 0) gD0 = 1;
                gZ0 = zoom; gMid = (ab[0] + ab[1]) / 2;
                return;
            }
            if (pointers.Count > 2) return;
            var mh = MiniHit(p);
            if (mh.HasValue) { MiniJump(mh.Value, p); gk = GK.Mini; return; }
            gk = GK.Pan; gLast = p;
        }

        void Move(int id, Vector2 p)
        {
            if (id == spotId)
            {
                if (HumanAim && OnSheet(p)) AimAt(World(p));
                return;
            }
            if (fingers.TryGetValue(id, out var fg))
            {
                if (!HumanAim) return;
                if (!fg.Power)
                {
                    // the aim hand: sideways movement turns the aim
                    aimAng = Mathf.Clamp((float)aimAng + (p.x - fg.Last.x) * AimPerPx * Sens, -AimMax, AimMax);
                }
                else if (!fg.Out)
                {
                    // the power hand: past the min line still pulls; across to the aim side or off the screen calls the
                    // shot off
                    if (!OnScreen(p) || !RightOfStone(p)) { fg.Out = true; power = -1; }
                    else power = Mathf.Min(Mathf.Abs(p.y - fg.Start.y), maxPx) / maxPx;   // up or down from the touch point
                }
                if ((p - fg.Start).sqrMagnitude >= 9) fg.Moved = true;   // more than a tap
                fg.Last = p;
                return;
            }
            if (!pointers.ContainsKey(id)) return;
            pointers[id] = p;
            switch (gk)
            {
                case GK.Mini:
                {
                    var mm = MiniRect();
                    if (mm.HasValue) { var r = mm.Value; MiniJump(r, new Vector2(Mathf.Clamp(p.x, r.X, r.X + r.W), Mathf.Clamp(p.y, r.Y, r.Y + r.H))); }
                    return;
                }
                case GK.Pan:
                    cx -= (p.x - gLast.x) / s; cy += (p.y - gLast.y) / s;
                    gLast = p; Manual(); ClampCam();
                    return;
                case GK.Pinch:
                    if (pointers.Count == 2)
                    {
                        var ab = pointers.Values.ToArray();
                        var mid = (ab[0] + ab[1]) / 2;
                        cx -= (mid.x - gMid.x) / s; cy += (mid.y - gMid.y) / s;
                        gMid = mid;
                        ZoomAt(gZ0 * Vector2.Distance(ab[0], ab[1]) / gD0, mid.x, mid.y);
                        Manual();
                    }
                    return;
            }
        }

        void Up(int id, Vector2 p, bool isUp)
        {
            if (id == spotId) { spotId = int.MinValue; return; }
            if (fingers.TryGetValue(id, out var fg))
            {
                fingers.Remove(id);
                if (!fg.Power) return;
                float pw = power;
                power = -1; powerId = int.MinValue;
                // releasing always shoots at the current power; only a finger that left the area, or a pure tap, does not
                if (isUp && HumanAim && !fg.Out && fg.Moved && pw > 0) Release(aimAng, pw);
                return;
            }
            if (!pointers.Remove(id)) return;
            if (pointers.Count == 0) gk = GK.None;
            else if (gk == GK.Pinch) { gk = GK.Pan; gLast = pointers.Values.First(); }
        }

        // turn the aim straight at a spot on the sheet (one ahead of the launch line)
        void AimAt(Vector2 w)
        {
            double dx = w.x - m.LaunchX, dy = w.y - Sheet.LaunchY;
            if (dy < Sheet.SR) return;
            aimAng = Math.Max(-AimMax, Math.Min(AimMax, Math.Atan2(dx, dy)));
        }

        // =====================================================================
        // aim ray: from the launch point to the first stone, post or wall
        // =====================================================================

        enum HitKind { None, Stone, Post, Wall }

        // with the hit marker off, the line runs on through stones and posts to a wall or the far end of the sheet
        (Vector2 p, HitKind kind) AimRay(double ang, bool throughObjects = false)
        {
            double dx = Math.Sin(ang), dy = Math.Cos(ang);
            double x = m.LaunchX, y = Sheet.LaunchY, rad = m.ArmedRad;   // (item mode: the stone may be a giant or mini one)
            var live = m.Stones.Where(t => t.Live).ToList();
            for (int i = 0; i < 1200; i++)
            {
                x += dx * 0.35; y += dy * 0.35;
                var here = new Vector2((float)x, (float)y);
                if (y > Sheet.L + 4 || y < 0) return (here, HitKind.None);
                if (!throughObjects)
                {
                    foreach (var st in live) if (Sheet.Dist(st.X, st.Y, x, y) < st.Rad + rad) return (here, HitKind.Stone);
                    foreach (var p in m.ActiveField.Posts) if (Sheet.Dist(p.X, p.Y, x, y) < p.R + rad) return (here, HitKind.Post);
                }
                foreach (var w in m.ActiveField.Walls)
                {
                    double ddx = w.X2 - w.X1, ddy = w.Y2 - w.Y1;
                    double t = Sheet.Clamp(((x - w.X1) * ddx + (y - w.Y1) * ddy) / (ddx * ddx + ddy * ddy), 0, 1);
                    if (Sheet.Dist(x, y, w.X1 + t * ddx, w.Y1 + t * ddy) < rad) return (here, HitKind.Wall);
                }
            }
            return (new Vector2((float)x, (float)y), HitKind.None);
        }

        // =====================================================================
        // drawing
        // =====================================================================

        void BeginStones() => stoneUsed = 0;

        // the turn order shot: where an earlier draw stopped (its stone is off the sheet by now), faint, with its record
        // in the middle, for the one throwing next to beat
        void DrawLsdMarks()
        {
            if (!m.InLsd || m.Lsd == null || LsdDuel) return;   // (thrown together: DrawDuel shows both)
            foreach (var t in m.Lsd.Order)
            {
                double? d = m.Lsd.Dist[(int)t];
                if (t == m.Cur || !d.HasValue || double.IsInfinity(d.Value)) continue;
                float x = (float)m.Lsd.RestX[(int)t], y = (float)m.Lsd.RestY[(int)t];
                DrawStone(x, y, t, LsdMarkAlpha);
                hud.Label(LsdMarkText(t, d.Value), X(x), Y(y), 13, Ui.Shown(t) == Team.B ? Palette.Rgba(255, 255, 255, .95f) : Palette.Rgba(40, 24, 12, .95f), 0, true);
            }
        }
        const float LsdMarkAlpha = 0.4f;
        readonly string[] lsdMarkText = new string[2];
        readonly double[] lsdMarkD = { -1, -1 };

        string LsdMarkText(Team t, double d)
        {
            int i = (int)t;
            if (lsdMarkD[i] != d) { lsdMarkD[i] = d; lsdMarkText[i] = d.ToString("0.0"); }
            return lsdMarkText[i];
        }

        void DrawStone(float x, float y, Team team, float alpha = 1, float scale = 1)
        {
            if (stoneUsed >= stonePool.Count)
            {
                var sh = new GameObject("Shadow").AddComponent<SpriteRenderer>();
                sh.transform.SetParent(transform, false);
                sh.sharedMaterial = unlitMaterial; sh.sprite = disc; sh.sortingOrder = 9;
                var st = new GameObject("Stone").AddComponent<SpriteRenderer>();
                st.transform.SetParent(transform, false);
                st.sharedMaterial = unlitMaterial; st.sortingOrder = 10;
                shadowPool.Add(sh); stonePool.Add(st);
            }
            int i = stoneUsed++;
            var s1 = stonePool[i]; var s0 = shadowPool[i];
            float r = SR * scale, z = -i * 0.001f;
            s1.gameObject.SetActive(true); s0.gameObject.SetActive(true);
            s1.sprite = Ui.Shown(team) == Team.B ? stoneB : stoneW;
            s1.color = new Color(1, 1, 1, alpha);
            s1.transform.localPosition = new Vector3(x, y, z);
            s1.transform.localScale = Vector3.one * scale;
            s0.color = Palette.Rgba(45, 22, 6, .38f * alpha);
            s0.transform.localPosition = new Vector3(x + r * 0.16f, y - r * 0.26f, z);
            s0.transform.localScale = Vector3.one * (r * 1.02f);
        }

        void EndStones()
        {
            for (int i = stoneUsed; i < stonePool.Count; i++)
            {
                if (stonePool[i].gameObject.activeSelf) stonePool[i].gameObject.SetActive(false);
                if (shadowPool[i].gameObject.activeSelf) shadowPool[i].gameObject.SetActive(false);
            }
        }

        static readonly Color GlowCol = Palette.Rgba(255, 214, 120, .55f), Gold95 = Palette.Rgba(255, 214, 120, .95f);

        void Draw(float time)
        {
            under.Clear(); aim.Clear(); over.Clear(); mini.Clear();
            BeginStones(); hud.BeginLabels();
            DrawBoardLabels();
            DrawTurnInfo();
            DrawBoxes();
            DrawSlots(time);

            bool playing = state == St.Aim || state == St.Ai || state == St.AiPull || state == St.Remote || state == St.Moving || state == St.Settle;
            // live scoring glow
            if (showScoringGlow && (playing || state == St.Menu))
            {
                var sc = Rules.Scoring(m.Stones);
                foreach (var id in sc.Ids)
                {
                    var st = m.Stones.First(t => t.Id == id);
                    under.Ring(new Vector2((float)st.X, (float)st.Y), (float)st.Rad + 1.4f, U(3), GlowCol);
                }
            }
            foreach (var f in fades) DrawStone(f.X, f.Y, f.Team, f.A, f.Size);
            // stones
            if (state == St.Restoring)
            {
                float t = Mathf.Min(1, restore.T), e = t * t * (3 - 2 * t);
                foreach (var snap in m.Snapshot)
                {
                    var to = new Vector2((float)snap.X, (float)snap.Y);
                    var from = restore.From.TryGetValue(snap.Id, out var fp) ? fp : to;
                    var p = Vector2.LerpUnclamped(from, to, e);
                    DrawStone(p.x, p.y, snap.Team, 1, (float)snap.Size);
                }
                var shot = m.ShotStone;
                if (shot != null) DrawStone((float)shot.X, (float)shot.Y, shot.Team, 1 - e, (float)shot.Size);
            }
            else
            {
                DrawLsdMarks();
                foreach (var st in m.Stones)
                {
                    if (!st.Live) continue;
                    DrawStone((float)st.X, (float)st.Y, st.Team, 1, (float)st.Size);
                    if (st.Bomb) DrawBombMark((float)st.X, (float)st.Y, (float)st.Rad, time);
                    if (st.Magnet) DrawMagnetMark((float)st.X, (float)st.Y, (float)st.Rad, st.IsMoving, time);
                }
            }
            // pending stone at launch
            if (state == St.Aim || state == St.Ai || state == St.AiPull || state == St.Remote)
            {
                var lp = new Vector2((float)m.LaunchX, LY);
                float lx = X(lp.x), ly = Y(lp.y);
                if (state == St.Aim && !Charging)
                {
                    float pulse = (Mathf.Sin(time / 300) + 1) / 2;
                    over.Ring(lp, SR + 2.5f + pulse * 1.5f, U(2), Palette.Rgba(217, 165, 76, 0.35f + pulse * 0.45f));
                }
                if (HumanAim)
                {
                    // the aim line is always up on your turn; the gauge fills while power is pulled
                    DrawAim(aimAng, Charging ? power : 0);
                    // the split between the aim side and the power side, faintly, below the min line
                    aim.Dashed(new Vector2(lp.x, HOG), new Vector2(lp.x, lp.y + SR + 3), U(1.5f), U(4), U(6), Palette.Rgba(52, 32, 16, Charging ? .15f : .3f));
                    // a small ring where the pull began
                    if (Charging) aim.Ring(World(powerStart), U(10), U(1.5f), Palette.Rgba(239, 226, 200, .45f));
                }
                else if (state == St.AiPull)
                {
                    DrawAim(aiPull.Ang, (float)aiPull.Pw * Mathf.Min(1, aiPull.T));
                }
                // (item mode: the item put on it shows: a giant or mini stone, a bomb)
                float armed = (float)(m.ArmedRad / Sheet.SR);
                DrawStone(lp.x, lp.y, m.Cur, 1, turnPulse * armed);
                if (m.Armed == ItemKind.Bomb) DrawBombMark(lp.x, lp.y, SR * turnPulse, time);
                if (m.Armed == ItemKind.Magnet) DrawMagnetMark(lp.x, lp.y, SR * turnPulse, false, time);
                // online, the opponent's turn is noted over the stone (the AI's turns show nothing)
                if (state == St.Remote) hud.Label(LT("online.opponentAiming"), lx, ly - SR * s - 16, 13, Palette.Text, 0, false, true);
            }
            DrawDuel();
            // last stone draw: distance from the target
            if (state == St.LsdShow && lsdShow != null && lsdShow.D.HasValue)
            {
                float p = Mathf.Clamp01(lsdShow.T / 0.5f);
                var T = new Vector2((float)m.Lsd.TargetX, (float)m.Lsd.TargetY);
                var S = new Vector2(lsdShow.X, lsdShow.Y);
                over.Line(T, T + (S - T) * p, U(2.5f), Gold95);
                if (p >= 1) hud.Label(lsdShow.DText, X(S.x), Y(S.y) - SR * s - 14, 16, Palette.Gold, 0, true, true);
            }
            // end measurement
            if ((state == St.Measure || state == St.EndCard) && measure != null)
            {
                float t = state == St.EndCard ? 99 : measure.T;
                int rank = 0;
                var tee = new Vector2(TEEX, TEEY);
                for (int i = 0; i < measure.Sc.List.Count; i++)
                {
                    var o = measure.Sc.List[i];
                    float p = Mathf.Clamp01((t - 0.35f - i * 0.3f) / 0.25f);
                    if (p <= 0) continue;
                    bool scorer = measure.Sc.Ids.Contains(o.Stone.Id);
                    var op = new Vector2((float)o.Stone.X, (float)o.Stone.Y);
                    over.Line(tee, tee + (op - tee) * p, U(scorer ? 2.5f : 1.5f), scorer ? Gold95 : Palette.Rgba(40, 24, 12, .55f));
                    if (p >= 1 && scorer)
                    {
                        rank++;
                        float orad = (float)o.Stone.Rad;
                        over.Ring(op, orad + 1.5f, U(3), Gold95);
                        var badge = op + new Vector2(orad, orad);
                        over.Disc(badge, U(8), Palette.Brass);
                        hud.Label(Num(rank), X(badge.x), Y(badge.y) + 0.5f, 11, Palette.BrassInk, 0, true);
                    }
                }
                over.Disc(tee, U(2.5f), Palette.BrassInk);
            }
            // floating texts
            foreach (var f in floats)
            {
                float fxp = Mathf.Clamp(X(f.X), X(10), X(90)), fyp = Y(f.Y + 8 + f.T * 10);
                hud.Label(f.Text, fxp, fyp, 16, Palette.WithA(Palette.Gold, 1 - f.T), 0, true, true);
            }
            DrawItemFx();
            DrawFuse(time);
            hud.SetToastAnchor(Y(HOG) - 1.2f * s - 6);   // toasts stack up from just above the fuse
            DrawMinimap();
            under.Commit(); aim.Commit(); over.Commit(); mini.Commit();
            EndStones(); EndItemSprites(); hud.EndLabels();
        }

        // a ring gauge: a dark backing and a light track, filled clockwise from the top in one continuous ramp
        // blue -> violet -> red (never through yellow, which vanishes on the wood); radius in sheet units, width in
        // screen points
        void DrawRingGauge(Vector2 c, float radius, float width, float pw)
        {
            aim.Ring(c, radius, U(width * 16 / 9f), Palette.Rgba(32, 20, 11, .6f));
            aim.Ring(c, radius, U(width * 8 / 9f), Palette.Rgba(239, 226, 200, .18f));
            if (pw <= 0) return;
            var col = Palette.Hsl((220 + 145 * pw) % 360, 0.65f, (58 - 6 * pw) / 100f);
            aim.Arc(c, radius, U(width), Mathf.PI / 2, Mathf.PI / 2 - pw * Mathf.PI * 2, col);
        }

        void DrawAim(double ang, float pw)
        {
            var lp = new Vector2((float)m.LaunchX, LY);
            float lx = X(lp.x), ly = Y(lp.y);
            var dir = new Vector2((float)Math.Sin(ang), (float)Math.Cos(ang));
            float r0 = SR * s + 6;
            // power gauge round the stone (radius and width from GameConfig)
            DrawRingGauge(lp, launchRingRadius, launchRingWidth, pw);
            // aim line to first contact (or, with the hit marker off, on to a wall or the far end)
            var hit = AimRay(ang, !showHitMarker);
            // the dashes run between the two rings, stopping at their outer edges
            // (the ring where the line ends grows and shrinks with the stone: a giant or mini one in item mode)
            float hitR = hitRingRadius * (float)(m.ArmedRad / Sheet.SR);
            float from = launchRingRadius + U(launchRingWidth * 8 / 9f), to = Vector2.Distance(lp, hit.p) - (hit.kind != HitKind.None ? hitR + U(hitRingWidth * 8 / 9f) : 0);
            if (to > from) aim.Dashed(lp + dir * from, lp + dir * to, U(1.5f), U(5), U(5), Palette.Rgba(40, 24, 12, .6f));
            // where the line ends is a second power gauge, so the power reads where the player is looking
            if (hit.kind != HitKind.None) DrawRingGauge(hit.p, hitR, hitRingWidth, pw);
            bool noTicket = !m.InLsd && !Sheet.UnlimitedTickets && state == St.Aim && m.Tickets[(int)m.Cur] <= 0;
            if (hit.kind == HitKind.Wall && noTicket)
            {
                aim.Ring(hit.p, hitR + U(hitRingWidth + 2), U(2), Palette.Hex("#c0503a"));
                bool leftSide = hit.p.x < W / 2;
                hud.Label(LT("float.noTicket"), X(hit.p.x) + (leftSide ? 1 : -1) * (hitR * s + 10), Y(hit.p.y), 12, Palette.Hex("#ff9c84"), leftSide ? -1 : 1, false, true);
            }
            if (state == St.Aim) DrawProtractor(ang);   // (item mode: this player's own aim only)
            float tl = r0 + 26;
            if (pw > 0 && showPowerPercent) hud.Label(Percent[Mathf.Clamp(Mathf.RoundToInt(pw * 100), 0, 100)], lx + dir.x * tl, ly - dir.y * tl, 15, Palette.Hex("#ffe2a6"), 0, true, true);
        }

        // the shot clock as a fuse laid over the min line, burning from the left end toward the right; the burnt part
        // leaves the line showing again
        void DrawFuse(float time)
        {
            if (!(HumanAim || state == St.Remote) || clock < 0) { fuse.Burn(Vector3.zero, false, 0); return; }
            const float x0 = 0.5f, x1 = W - 0.5f, y = HOG;
            float left = Mathf.Clamp01(clock / ShotTime);
            var tip = new Vector2(Mathf.Lerp(x1, x0, left), y);
            var end = new Vector2(x1, y);
            // still to burn: a twisted cord; in the last third of the time it flashes between red and its own colour
            float red = left <= 1 / 3f ? (1 - Mathf.Cos(time / 120f)) / 2 : 0;
            Color Cord(Color c, Color r) => Color.Lerp(c, r, red);
            under.Line(tip, end, 2.0f, Cord(Palette.Rgba(122, 88, 48, 1), Palette.Rgba(206, 52, 36, 1)));
            for (float x = Mathf.Ceil(tip.x / 2f) * 2f; x < x1 - 0.9f; x += 2f)
                under.Line(new Vector2(x, y - .9f), new Vector2(x + .9f, y + .9f), .5f, Cord(Palette.Rgba(78, 52, 24, .9f), Palette.Rgba(122, 22, 14, .9f)));
            under.Line(tip + new Vector2(0, .5f), end + new Vector2(0, .5f), .35f, Cord(Palette.Rgba(196, 160, 104, .7f), Palette.Rgba(255, 150, 126, .7f)));
            // lit once the turn toast has gone; the glow flickers
            bool burning = clockWait <= 0 && !hud.CardVisible;
            if (burning)
            {
                float fl = 0.8f + 0.2f * Mathf.Sin(time / 45f) * Mathf.Sin(time / 17f);
                aim.Disc(tip, 2.6f * fl, Palette.Rgba(255, 130, 30, .45f));
                aim.Disc(tip, 1.1f, Palette.Rgba(255, 232, 160, 1));
            }
            fuse.Burn(new Vector3(tip.x, tip.y, -1), burning, 1 - left);
        }

        // printed on the sheet, sized in sheet units like the other markings; it fades while a shot is being aimed so the
        // aim arrow reads over it
        void DrawTurnInfo()
        {
            var info = CachedTurnInfo();
            if (!info.HasValue) return;
            float a = Charging || state == St.AiPull ? 0.3f : 0.82f;
            var ink = Palette.Rgba(52, 32, 16, a);
            var (names, records, roles, pips) = info.Value;
            // each side in its half: its stones left just above the min line at its own edge; below the min line (in item
            // mode under its hands) the stone and the name (a long name wraps under its first letter), centred in the
            // half, first or last under the stone, and (online) the record under the name, centred on it; sizes and gaps in
            // sheet units (times s)
            int nameSize = Mathf.RoundToInt(5.4f * 1.3f * 0.8f * s), small = Mathf.RoundToInt(3.4f * s), pipSize = Mathf.RoundToInt(5.1f * s);
            // a name wraps past the width of a stone and six letters (measured once per size)
            if (nameCapSize != nameSize)
            {
                nameCapSize = nameSize;
                nameCap = hud.Measure(Ui.Icon(Team.B, 115) + "\u00A0" + NameCapSample, nameSize, true);
                iconW = hud.Measure(Ui.Icon(Team.B, 115), nameSize, true);
            }
            float nameW = Mathf.Min(nameCap, (W / 2 - 4) * s);
            float top = Y(m.Items ? NameYItems : NameY) - nameSize * 0.7f;
            for (int i = 0; i < 2; i++)
            {
                float cx = X(i == 0 ? W / 4 : W * 3 / 4);
                float h = hud.LabelWrapped(names[i], cx, top, nameSize, ink, nameW, out float w, true);
                float roleY = top + nameSize * 1.6f + small * 0.55f;   // (under the stone icon, clear of it)
                if (roles != null)
                {
                    hud.Label(roles[i], cx - w / 2 + iconW / 2, roleY, small, ink, 0, true);
                    hud.Label(pips[i], i == 0 ? X(PipInset) : X(W - PipInset), Y(PipY), pipSize, ink, i == 0 ? -1 : 1, true);
                }
                // the record under the name (the line beside first / last, or lower if the name wraps), centred on the
                // name's letters (right of the stone icon)
                if (records != null)
                    hud.Label(records[i], cx + iconW / 2, Mathf.Max(roleY, top + h + RecordGap * s + small * 0.5f), small, ink);
            }
        }
        const float NameY = 52, NameYItems = 43;   // sheet y of the name's first line (in item mode, under the hands)
        const float PipY = HOG + 5, PipInset = 2.5f;   // the stones left: just above the min line (clear of the fuse), at the edge
        const float RecordGap = 2.2f;              // between the name (or first / last) and the line under it
        float iconW;
        const string NameCapSample = "테스트ㅇㅇㅇ";   // the widest a name line gets
        int nameCapSize = -1;
        float nameCap;

        // strings made once and kept: the cards' and labels' fixed words (per language), numbers, percents
        static readonly string[] Percent = Enumerable.Range(0, 101).Select(i => i + "%").ToArray();
        static readonly string[] Nums = Enumerable.Range(0, 32).Select(i => i.ToString()).ToArray();
        static string Num(int n) => n >= 0 && n < Nums.Length ? Nums[n] : n.ToString();
        readonly Dictionary<string, string> locCache = new Dictionary<string, string>();
        int locVersion, locCacheVersion = -1;

        /// <summary>A fixed string of the "UI" table, looked up once per language.</summary>
        string LT(string key)
        {
            if (locCacheVersion != locVersion) { locCache.Clear(); locCacheVersion = locVersion; }
            if (!locCache.TryGetValue(key, out var v)) locCache[key] = v = Loc.T(key);
            return v;
        }

        void DrawBoardLabels()
        {
            var f = m.Field;

            foreach (var z in f.Zones)
            {
                float zx = (float)(z.X1 + z.X2) / 2, zy = (float)(z.Y1 + z.Y2) / 2;
                if (z.Kind == ZoneKind.Sand) hud.Label(LT("board.sand"), X(zx), Y(zy), 11, Palette.Rgba(70, 46, 18, .8f));
                else hud.Label(LT("board.ice"), X(zx), Y(zy), 11, Palette.Rgba(40, 70, 100, .75f), 0, false, false, 90);
            }
        }

        void DrawMinimap()
        {
            var mm = MiniRect();
            if (!mm.HasValue)
            {
                if (miniCam) miniCam.enabled = false;
                hud.MiniFrame(null);
                return;
            }
            var r = mm.Value;
            hud.MiniFrame(new Rect(r.X, r.Y, r.W, r.H));
            if (miniCam)
            {
                miniCam.enabled = true;
                miniCam.orthographicSize = L / 2;
                miniCam.transform.position = new Vector3(W / 2, L / 2, -10);
                miniCam.rect = new Rect(r.X * px / Screen.width, 1 - (r.Y + r.H) * px / Screen.height, r.W * px / Screen.width, r.H * px / Screen.height);
            }
            float one = 1 / r.Ms;   // one css px at minimap scale
            if (HumanAim || state == St.AiPull)
            {
                double ang = HumanAim ? aimAng : aiPull.Ang;
                var hit = AimRay(ang, !showHitMarker);
                mini.Dashed(new Vector2((float)m.LaunchX, LY), hit.p, one, 2 * one, 2 * one, Palette.Rgba(40, 24, 12, .8f));
            }
            // current view
            float x0 = cx - cw / (2 * s), x1 = cx + cw / (2 * s), y0 = cy - ch / (2 * s), y1 = cy + ch / (2 * s);
            mini.RectStroke(x0, y0, x1, y1, 1.5f * one, Palette.Gold);
        }
    }
}
