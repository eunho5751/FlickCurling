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
            if (hud.SideMode != wasSide && m != null) RenderHud();   // the turn card is laid out differently in a column
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
        // input: pull anywhere on the sheet to shoot, drag the stone sideways for a lane, drag elsewhere to scroll,
        // pinch / ctrl+wheel to zoom
        // =====================================================================

        enum GK { None, Pinch, Mini, LaneQ, Lane, Shoot, Pan }

        readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        GK gk;
        bool mouseWasDown;
        Vector2 gStart, gLast, gMid;
        float gD0, gZ0;
        double gLane;

        Vector2 Css(Vector2 screen) => new Vector2(screen.x / px, (Screen.height - screen.y) / px);

        void HandleInput()
        {
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

        // which lane mark (if any) is under a point; the hit area never overlaps the neighbouring lane
        double? LaneHit(Vector2 p)
        {
            if (Mathf.Abs(p.y - Y(LY)) > Mathf.Max(22, (SR + 4) * s)) return null;
            float r = Mathf.Min(Mathf.Max(22, (SR + 4) * s), (float)(Sheet.Lanes[1] - Sheet.Lanes[0]) * s / 2);
            foreach (var lx in Sheet.Lanes) if (Mathf.Abs(p.x - X((float)lx)) <= r) return lx;
            return null;
        }

        void SetLane(double lx)
        {
            if (m.Lane[(int)m.Cur] == lx && (laneTo ?? m.LaunchX) == lx) return;
            m.SetLane(m.Cur, lx); laneTo = lx; hint = false;
            sfx.Blip(0.15f, 900, 2, 0.03f);
        }

        // touches on the sheet (rails included) aim on your turn; touches on the table around it always scroll
        bool OnSheet(Vector2 p)
        {
            var w = World(p);
            return w.x >= -4 && w.x <= W + 4 && w.y >= 0 && w.y <= L;
        }

        // pull is measured in css px from where the finger went down, so power precision does not depend on zoom
        (double ang, float pw, float len, float ux, float uy) DragShot(Drag d)
        {
            float ux = d.Cur.x - d.Start.x, uy = d.Cur.y - d.Start.y, len = Mathf.Sqrt(ux * ux + uy * uy);
            return (Math.Atan2(-ux, uy), Mathf.Min(len, maxPx) / maxPx, len, ux, uy);
        }

        void Down(int id, Vector2 p, Vector2 screen)
        {
            if (Hud.IsOverUi(screen)) return;
            pointers[id] = p;
            hud.ClosePopovers();
            if (pointers.Count == 2)
            {
                drag = null;
                var ab = pointers.Values.ToArray();
                gk = GK.Pinch;
                gD0 = Vector2.Distance(ab[0], ab[1]); if (gD0 == 0) gD0 = 1;
                gZ0 = zoom; gMid = (ab[0] + ab[1]) / 2;
                return;
            }
            if (pointers.Count > 2) return;
            var mh = MiniHit(p);
            if (mh.HasValue) { MiniJump(mh.Value, p); gk = GK.Mini; return; }
            if (state == St.Aim && !m.InLsd)
            {
                var li = LaneHit(p);
                if (li.HasValue) { gk = GK.LaneQ; gStart = p; gLane = li.Value; return; }
            }
            if (state == St.Aim && OnSheet(p)) { gk = GK.Shoot; drag = new Drag { Start = p, Cur = p }; return; }
            gk = GK.Pan; gLast = p;
        }

        void Move(int id, Vector2 p)
        {
            if (!pointers.ContainsKey(id)) return;
            pointers[id] = p;
            switch (gk)
            {
                case GK.LaneQ:
                {
                    float dx = p.x - gStart.x, dy = p.y - gStart.y;
                    if (Mathf.Sqrt(dx * dx + dy * dy) < 8) return;
                    // sideways = move the stone to another lane; anything else = a normal pull from this point
                    if (Mathf.Abs(dx) > Mathf.Abs(dy)) gk = GK.Lane;
                    else { gk = GK.Shoot; drag = new Drag { Start = gStart, Cur = p }; return; }
                    goto case GK.Lane;
                }
                case GK.Lane:
                    if (state == St.Aim) SetLane(Sheet.NearestLane(cx + (p.x - cw / 2) / s));
                    return;
                case GK.Shoot:
                    if (drag != null) drag.Cur = p;
                    return;
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
            if (!pointers.Remove(id)) return;
            if (gk == GK.LaneQ || gk == GK.Lane)
            {
                if (gk == GK.LaneQ && state == St.Aim) SetLane(gLane);   // a tap on another lane mark
                gk = GK.None; return;
            }
            if (gk == GK.Shoot)
            {
                var d = drag;
                drag = null; gk = GK.None;
                if (d == null) return;
                d.Cur = p;
                var sh = DragShot(d);
                // a tiny pull, or dragging back to where you started, cancels
                if (isUp && state == St.Aim && sh.pw >= Sheet.CancelPw) Release(sh.ang, sh.pw);
                return;
            }
            if (pointers.Count == 0) gk = GK.None;
            else if (gk == GK.Pinch) { gk = GK.Pan; gLast = pointers.Values.First(); }
        }

        // =====================================================================
        // aim ray: from the launch point to the first stone, post or wall
        // =====================================================================

        enum HitKind { None, Stone, Post, Wall }

        (Vector2 p, HitKind kind) AimRay(double ang)
        {
            double dx = Math.Sin(ang), dy = Math.Cos(ang);
            double x = m.LaunchX, y = Sheet.LaunchY;
            var live = m.Stones.Where(t => t.Live).ToList();
            for (int i = 0; i < 1200; i++)
            {
                x += dx * 0.35; y += dy * 0.35;
                var here = new Vector2((float)x, (float)y);
                if (y > Sheet.L + 4 || y < 0) return (here, HitKind.None);
                foreach (var st in live) if (Sheet.Dist(st.X, st.Y, x, y) < 2 * Sheet.SR) return (here, HitKind.Stone);
                foreach (var p in m.Field.Posts) if (Sheet.Dist(p.X, p.Y, x, y) < p.R + Sheet.SR) return (here, HitKind.Post);
                foreach (var w in m.Field.Walls)
                {
                    double ddx = w.X2 - w.X1, ddy = w.Y2 - w.Y1;
                    double t = Sheet.Clamp(((x - w.X1) * ddx + (y - w.Y1) * ddy) / (ddx * ddx + ddy * ddy), 0, 1);
                    if (Sheet.Dist(x, y, w.X1 + t * ddx, w.Y1 + t * ddy) < Sheet.SR) return (here, HitKind.Wall);
                }
            }
            return (new Vector2((float)x, (float)y), HitKind.None);
        }

        // =====================================================================
        // drawing
        // =====================================================================

        void BeginStones() => stoneUsed = 0;

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
            s1.sprite = team == Team.B ? stoneB : stoneW;
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

            bool playing = state == St.Aim || state == St.Ai || state == St.AiPull || state == St.Moving || state == St.Settle;
            // live scoring glow
            if (playing || state == St.Menu)
            {
                var sc = Rules.Scoring(m.Stones);
                foreach (var id in sc.Ids)
                {
                    var st = m.Stones.First(t => t.Id == id);
                    under.Ring(new Vector2((float)st.X, (float)st.Y), SR + 1.4f, U(3), GlowCol);
                }
            }
            // the draw target: a ring with a cross, under the stones
            if (m.InLsd && m.Lsd != null)
            {
                var T = new Vector2((float)m.Lsd.TargetX, (float)m.Lsd.TargetY);
                float rr = U(Mathf.Max(7, SR * s * 0.9f)), pulse = (Mathf.Sin(time / 260) + 1) / 2;
                under.Ring(T, rr, U(5), Palette.Rgba(32, 20, 11, .75f));
                var gc = Palette.Rgba(255, 217, 138, 0.75f + 0.25f * pulse);
                under.Ring(T, rr, U(2.5f), gc);
                under.Line(T + new Vector2(-rr * 1.6f, 0), T + new Vector2(-rr * 0.5f, 0), U(2), gc);
                under.Line(T + new Vector2(rr * 0.5f, 0), T + new Vector2(rr * 1.6f, 0), U(2), gc);
                under.Line(T + new Vector2(0, -rr * 1.6f), T + new Vector2(0, -rr * 0.5f), U(2), gc);
                under.Line(T + new Vector2(0, rr * 0.5f), T + new Vector2(0, rr * 1.6f), U(2), gc);
                under.Disc(T, U(2), Palette.Gold);
            }
            foreach (var f in fades) DrawStone(f.X, f.Y, f.Team, f.A);
            // stones
            if (state == St.Restoring)
            {
                float t = Mathf.Min(1, restore.T), e = t * t * (3 - 2 * t);
                foreach (var snap in m.Snapshot)
                {
                    var to = new Vector2((float)snap.X, (float)snap.Y);
                    var from = restore.From.TryGetValue(snap.Id, out var fp) ? fp : to;
                    var p = Vector2.LerpUnclamped(from, to, e);
                    DrawStone(p.x, p.y, snap.Team);
                }
                var shot = m.ShotStone;
                if (shot != null) DrawStone((float)shot.X, (float)shot.Y, shot.Team, 1 - e);
            }
            else
            {
                foreach (var st in m.Stones) if (st.Live) DrawStone((float)st.X, (float)st.Y, st.Team);
            }
            // pending stone at launch
            if (state == St.Aim || state == St.Ai || state == St.AiPull)
            {
                var lp = new Vector2((float)m.LaunchX, LY);
                float lx = X(lp.x), ly = Y(lp.y);
                if (state == St.Aim && drag == null)
                {
                    float pulse = (Mathf.Sin(time / 300) + 1) / 2;
                    over.Ring(lp, SR + 2.5f + pulse * 1.5f, U(2), Palette.Rgba(217, 165, 76, 0.35f + pulse * 0.45f));
                }
                if (drag != null)
                {
                    var sh = DragShot(drag);
                    // where the finger went down, and a faint line to the finger
                    var faint = Palette.Rgba(239, 226, 200, .45f);
                    aim.Ring(World(drag.Start), U(10), U(1.5f), faint);
                    aim.Dashed(World(drag.Start), World(drag.Cur), U(1.5f), U(4), U(4), faint);
                    if (sh.pw < Sheet.CancelPw) hud.Label(Loc.T("hint.cancel"), drag.Start.x, drag.Start.y - 24, 13, Palette.Text, 0, false, true);
                    else DrawAim(sh.ang, sh.pw);
                }
                else if (state == St.AiPull)
                {
                    DrawAim(aiPull.Ang, (float)aiPull.Pw * Mathf.Min(1, aiPull.T));
                }
                DrawStone(lp.x, lp.y, m.Cur);
                string note = state == St.Aim && hint && drag == null ? Loc.T(!m.InLsd ? "hint.lanes" : "hint.pull")
                    : state == St.Ai ? Loc.T("ai.thinking", ("name", NameOf(m.Cur)))
                    : state == St.AiPull && IntentLabel(aiPull) != "" ? Loc.T("ai.pulling", ("name", NameOf(m.Cur)), ("intent", IntentLabel(aiPull))) : "";
                if (note != "") hud.Label(note, lx, ly - SR * s - 16, 13, Palette.Text, 0, false, true);
            }
            // last stone draw: distance from the target
            if (state == St.LsdShow && lsdShow != null && lsdShow.D.HasValue)
            {
                float p = Mathf.Clamp01(lsdShow.T / 0.5f);
                var T = new Vector2((float)m.Lsd.TargetX, (float)m.Lsd.TargetY);
                var S = new Vector2(lsdShow.X, lsdShow.Y);
                over.Line(T, T + (S - T) * p, U(2.5f), Gold95);
                if (p >= 1) hud.Label(lsdShow.D.Value.ToString("0.0"), X(S.x), Y(S.y) - SR * s - 14, 16, Palette.Gold, 0, true, true);
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
                        over.Ring(op, SR + 1.5f, U(3), Gold95);
                        var badge = op + new Vector2(SR, SR);
                        over.Disc(badge, U(8), Palette.Brass);
                        hud.Label(rank.ToString(), X(badge.x), Y(badge.y) + 0.5f, 11, Palette.BrassInk, 0, true);
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
            DrawMinimap();
            under.Commit(); aim.Commit(); over.Commit(); mini.Commit();
            EndStones(); hud.EndLabels();
        }

        void DrawAim(double ang, float pw)
        {
            var lp = new Vector2((float)m.LaunchX, LY);
            float lx = X(lp.x), ly = Y(lp.y);
            // power gauge: dark backing ring so it reads on wood and on the dark table alike
            float gr = SR + 4;
            aim.Ring(lp, gr, U(8), Palette.Rgba(32, 20, 11, .6f));
            aim.Ring(lp, gr, U(4), Palette.Rgba(239, 226, 200, .18f));
            // one continuous ramp blue -> violet -> red (never through yellow, which vanishes on the wood)
            var col = Palette.Hsl((220 + 145 * pw) % 360, 0.65f, (58 - 6 * pw) / 100f);
            aim.Arc(lp, gr, U(4.5f), Mathf.PI / 2, Mathf.PI / 2 - pw * Mathf.PI * 2, col);
            // direction arrow
            var dir = new Vector2((float)Math.Sin(ang), (float)Math.Cos(ang));
            float len = 18 + pw * 70, r0 = SR * s + 6;
            var a = lp + dir * U(r0); var b = lp + dir * U(r0 + len);
            aim.Line(a, b, U(3), col);
            var back = -dir * U(9);
            aim.Tri(b, b + Rot(back, 0.45f), b + Rot(back, -0.45f), col);
            // aim line to first contact
            var hit = AimRay(ang);
            aim.Dashed(lp, hit.p, U(1.5f), U(5), U(5), Palette.Rgba(40, 24, 12, .6f));
            bool noTicket = !m.InLsd && state == St.Aim && m.Tickets[(int)m.Cur] <= 0;
            if (hit.kind == HitKind.Wall && noTicket)
            {
                aim.Ring(hit.p, SR, U(2), Palette.Hex("#c0503a"));
                bool leftSide = hit.p.x < W / 2;
                hud.Label(Loc.T("float.noTicket"), X(hit.p.x) + (leftSide ? 1 : -1) * (SR * s + 6), Y(hit.p.y), 12, Palette.Hex("#ff9c84"), leftSide ? -1 : 1, false, true);
            }
            else if (hit.kind != HitKind.None) aim.Ring(hit.p, SR, U(1.2f), Palette.Rgba(40, 24, 12, .55f));
            float tl = r0 + len + 18;
            hud.Label($"{Mathf.RoundToInt(pw * 100)}%", lx + dir.x * tl, ly - dir.y * tl, 15, Palette.Hex("#ffe2a6"), 0, true, true);
        }

        static Vector2 Rot(Vector2 v, float a) => new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));

        void DrawBoardLabels()
        {
            var f = m.Field;
            hud.Label(Loc.T("board.out"), X(W / 2), Y((L + BACK) / 2), 12, Palette.Rgba(239, 226, 200, .55f));
            var ink = Palette.Rgba(52, 32, 16, .9f);
            hud.Label(Loc.T("board.minLine"), X(2.5f), Y(HOG) + 9, 10, ink, -1);
            hud.Label(Loc.T("board.backLine"), X(2.5f), Y(BACK) + 9, 10, ink, -1);
            foreach (var w in f.Walls)
            {
                if (w.Dead || !w.IsSide) continue;
                float bot = Mathf.Max(0, (float)Math.Min(w.Y1, w.Y2)), top = Mathf.Min(L, (float)Math.Max(w.Y1, w.Y2));
                if (top <= bot) continue;
                bool left = w.X1 == 0;
                hud.Label(Loc.T("board.lively"), X(left ? -2 : W + 2), Y((bot + top) / 2), 10, Palette.BrassInk, 0, false, false, left ? 90 : -90);
            }
            foreach (var z in f.Zones)
            {
                float zx = (float)(z.X1 + z.X2) / 2, zy = (float)(z.Y1 + z.Y2) / 2;
                if (z.Kind == ZoneKind.Sand) hud.Label(Loc.T("board.sand"), X(zx), Y(zy), 11, Palette.Rgba(70, 46, 18, .8f));
                else hud.Label(Loc.T("board.ice"), X(zx), Y(zy), 11, Palette.Rgba(40, 70, 100, .75f), 0, false, false, 90);
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
            if (drag != null || state == St.AiPull)
            {
                double ang = drag != null ? DragShot(drag).ang : aiPull.Ang;
                var hit = AimRay(ang);
                mini.Dashed(new Vector2((float)m.LaunchX, LY), hit.p, one, 2 * one, 2 * one, Palette.Rgba(40, 24, 12, .8f));
            }
            // current view
            float x0 = cx - cw / (2 * s), x1 = cx + cw / (2 * s), y0 = cy - ch / (2 * s), y1 = cy + ch / (2 * s);
            mini.RectStroke(x0, y0, x1, y1, 1.5f * one, Palette.Gold);
        }
    }
}
