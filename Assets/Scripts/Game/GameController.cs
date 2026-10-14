using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using FlickCurling.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace FlickCurling.Game
{
    /// <summary>
    /// Presentation and flow (prototype G.state machine): menu, last stone draw, ends, shots, AI turns, judging
    /// animations, camera, input and drawing. Rules and physics live in FlickCurling.Core; text comes from the
    /// "UI" string table through <see cref="Loc"/>.
    /// </summary>
    public sealed partial class GameController : MonoBehaviour
    {
        [SerializeField] Material unlitMaterial;   // sprites
        [SerializeField] Material shapeMaterial;   // vertex-colour shape meshes
        [SerializeField] Camera mainCam;
        [SerializeField] Camera miniCam;
        [SerializeField] TMP_FontAsset bodyFont;
        [SerializeField] TMP_FontAsset displayFont;

        enum St { Menu, Coin, Aim, Ai, AiPull, Moving, Restoring, Settle, Measure, EndCard, Over, LsdShow }
        enum Mode { Pvp, Easy, Normal, Hard, Watch }

        const float W = (float)Sheet.W, L = (float)Sheet.L, HOG = (float)Sheet.Hog, BACK = (float)Sheet.Back, R = (float)Sheet.R, SR = (float)Sheet.SR;
        const float TEEX = (float)Sheet.TeeX, TEEY = (float)Sheet.TeeY, LY = (float)Sheet.LaunchY;
        const float VIEW_TOP = BACK + 10;

        // ---- game state ----
        St state = St.Menu;
        Mode mode = Mode.Pvp;
        string mapKey = "base";
        int lvB = 2, lvW = 2;
        readonly AiLevel?[] ai = new AiLevel?[2];
        Match m;
        Ai brain;
        int token;
        int speed = 1;
        bool hint = true;
        bool noAuto;
        double? laneTo;
        float acc;
        float settleT;
        System.Random fx = new System.Random();

        sealed class Drag { public Vector2 Start, Cur; }
        sealed class AiPull { public double Ang, Pw, Lx; public float T; public string Intent; }
        sealed class Fade { public float X, Y, A; public Team Team; }
        sealed class Float { public float X, Y, T; public string Text; }
        sealed class Measure { public float T, Dur; public ScoreResult Sc; public Team PrevHammer; }
        sealed class Restore { public float T; public Dictionary<int, Vector2> From; }
        sealed class LsdShow { public float X, Y, T; public double? D; }

        Drag drag;
        AiPull aiPull;
        readonly List<Fade> fades = new List<Fade>();
        readonly List<Float> floats = new List<Float>();
        Measure measure;
        Restore restore;
        LsdShow lsdShow;
        Func<CardSpec> cardFactory;   // rebuilds the visible card after a language change
        // your-turn pulse: the launch stone and the turn line grow and shrink while a human is to aim
        float turnPulse = 1;
        Tween pulseTween;

        // ---- delayed callbacks (prototype setTimeout), real time; dropped when this object is destroyed ----
        void After(float seconds, Action a) => AfterAsync(seconds, a).Forget();

        async UniTaskVoid AfterAsync(float seconds, Action a)
        {
            bool cancelled = await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime, PlayerLoopTiming.Update, destroyCancellationToken)
                .SuppressCancellationThrow();
            if (!cancelled) a();
        }

        // ---- view ----
        Hud hud;
        Sfx sfx;
        ShapeBatch under, aim, over, mini;
        SpriteRenderer board;
        Sprite stoneB, stoneW, disc;
        readonly List<SpriteRenderer> stonePool = new List<SpriteRenderer>(), shadowPool = new List<SpriteRenderer>();
        int stoneUsed;
        readonly Dictionary<string, Sprite> boardSprites = new Dictionary<string, Sprite>();
        Field shownField;

        bool Watching => mode == Mode.Watch;
        float Speed => Watching ? speed : 1;
        bool IsAI(Team t) => ai[(int)t].HasValue;
        string NameOf(Team t) => Loc.Team(t) + (IsAI(t) ? Loc.T("team.ai") : "");
        // the HUD shows the side as a stone icon rather than its name
        string SideOf(Team t, int scalePct = 115) => Ui.Icon(t, scalePct) + (IsAI(t) ? Loc.T("team.ai") : "");
        static string EndLabel(int n) => n <= Sheet.Ends ? Loc.T("hud.end", ("n", n)) : Loc.T("hud.extra");
        string Versus(int b, int w) => Loc.T("score.versus", ("b", Loc.Team(Team.B)), ("bv", b), ("wv", w), ("w", Loc.Team(Team.W)));

        // =====================================================================
        // boot
        // =====================================================================

        void Awake()
        {
            Application.targetFrameRate = 60;
            Loc.Init();
            if (!mainCam) mainCam = Camera.main;
            int overlayLayer = LayerOr("Overlay", 1), miniLayer = LayerOr("Minimap", 4);
            mainCam.orthographic = true;
            mainCam.backgroundColor = Palette.Table;
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.cullingMask = ~(1 << miniLayer) & ~(1 << 5);
            if (miniCam)
            {
                miniCam.orthographic = true;
                miniCam.backgroundColor = Palette.Rail;
                miniCam.clearFlags = CameraClearFlags.SolidColor;
                miniCam.cullingMask = ~(1 << overlayLayer) & ~(1 << 5);
                miniCam.depth = mainCam.depth + 1;
                miniCam.enabled = false;
            }

            var sb = BoardPainter.PaintStone(Team.B); var sw = BoardPainter.PaintStone(Team.W); var dt = BoardPainter.PaintDisc();
            stoneB = Sprite.Create(sb, new Rect(0, 0, sb.width, sb.height), new Vector2(.5f, .5f), sb.width / (2 * SR));
            stoneW = Sprite.Create(sw, new Rect(0, 0, sw.width, sw.height), new Vector2(.5f, .5f), sw.width / (2 * SR));
            disc = Sprite.Create(dt, new Rect(0, 0, dt.width, dt.height), new Vector2(.5f, .5f), dt.width / 2f);
            Ui.Init(disc, stoneB, stoneW, bodyFont, displayFont);

            // the table under the sheet, seen beside it on wide screens, and the sheet's drop shadow on it
            var tt = BoardPainter.PaintTable();
            var table = new GameObject("Table").AddComponent<SpriteRenderer>();
            table.transform.SetParent(transform, false);
            table.sprite = Sprite.Create(tt, new Rect(0, 0, tt.width, tt.height), new Vector2(.5f, .5f), tt.width / 60f, 0, SpriteMeshType.FullRect);
            table.drawMode = SpriteDrawMode.Tiled;
            table.size = new Vector2(1500, 1800);
            table.sharedMaterial = unlitMaterial;
            table.sortingOrder = -2;
            table.transform.localPosition = new Vector3(W / 2, L / 2, 3);
            var st = BoardPainter.PaintShadow();
            const float blur = 3;
            var shadow = new GameObject("SheetShadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(transform, false);
            shadow.sprite = Sprite.Create(st, new Rect(0, 0, st.width, st.height), new Vector2(.5f, .5f), 24 / blur, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
            shadow.drawMode = SpriteDrawMode.Sliced;
            shadow.size = new Vector2(W + 8 + 2 * blur, L + 2 * blur);
            shadow.color = new Color(0, 0, 0, 0.6f);
            shadow.sharedMaterial = unlitMaterial;
            shadow.sortingOrder = -1;
            shadow.transform.localPosition = new Vector3(W / 2 + 1, L / 2 - 2, 2);

            var bgo = new GameObject("Board");
            bgo.transform.SetParent(transform, false);
            board = bgo.AddComponent<SpriteRenderer>();
            board.sharedMaterial = unlitMaterial;
            board.sortingOrder = 0;

            under = new ShapeBatch("ShapesUnder", shapeMaterial, 5, 0);
            aim = new ShapeBatch("ShapesAim", shapeMaterial, 8, overlayLayer);
            over = new ShapeBatch("ShapesOver", shapeMaterial, 20, overlayLayer);
            mini = new ShapeBatch("ShapesMinimap", shapeMaterial, 20, miniLayer);
            foreach (var b in new[] { under, aim, over, mini }) b.Go.transform.SetParent(transform, false);

            sfx = new Sfx(gameObject);
            hud = new Hud(transform);
            hud.OnStart = (md, mp, b, w) => StartMatch((Mode)md, mp == 4 ? Maps.Keys[fx.Next(Maps.Keys.Length)] : Maps.Keys[mp], b, w);
            hud.OnMenu = ToMenu;
            hud.OnSound = () => { sfx.On = !sfx.On; RenderSettings(); };
            hud.OnSpeed = () => { speed = speed == 1 ? 2 : speed == 2 ? 4 : 1; RenderSettings(); };
            hud.OnLanguage = Loc.Select;
            Loc.Changed += OnLocaleChanged;
            RenderSettings();
            Resize(true);
            ToMenu();
        }

        void OnDestroy() => Loc.Changed -= OnLocaleChanged;

        // a human to aim and not yet pulling: loop the pulse; otherwise stop it at scale 1
        void UpdateTurnPulse()
        {
            bool want = state == St.Aim && !IsAI(m.Cur) && drag == null;
            if (want && pulseTween == null)
                pulseTween = DOTween.To(() => turnPulse, v => turnPulse = v, 1.08f, 0.6f)
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(gameObject);
            else if (!want && pulseTween != null)
            {
                pulseTween.Kill();
                pulseTween = null;
                turnPulse = 1;
            }
        }

        void OnLocaleChanged()
        {
            hud.Relocalize();
            RenderSettings();
            RenderHud();
            if (hud.CardVisible && cardFactory != null) hud.ReplaceCard(cardFactory(), null);
        }

        void RenderSettings()
        {
            hud.SoundText = Loc.T(sfx.On ? "settings.soundOn" : "settings.soundOff");
            hud.SpeedText = Loc.T("settings.speed", ("n", speed));
        }

        static int LayerOr(string name, int fallback) { int l = LayerMask.NameToLayer(name); return l >= 0 ? l : fallback; }

        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void ShowField(Field f)
        {
            if (shownField == f) return;
            shownField = f;
            if (!boardSprites.TryGetValue(f.Key, out var sp))
            {
                var tex = BoardPainter.PaintBoard(f);
                sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, BoardPainter.PPU);
                boardSprites[f.Key] = sp;
            }
            board.sprite = sp;
            board.transform.localPosition = new Vector3(BoardPainter.X0, 0, 1);
        }

        // =====================================================================
        // flow
        // =====================================================================

        void StartMatch(Mode md, string key, int b, int w)
        {
            token++;
            mode = md; mapKey = key; lvB = b; lvW = w;
            ai[0] = ai[1] = null;
            if (md == Mode.Watch) { ai[0] = (AiLevel)b; ai[1] = (AiLevel)w; }
            else if (md != Mode.Pvp) ai[1] = (AiLevel)(md - Mode.Easy);
            hud.SpeedVisible = md == Mode.Watch;
            hud.HudVisible = true;
            int seed = fx.Next();
            m = new Match(Maps.Build(key), PhysicsParams.Default, seed) { Listener = new LiveEvents(this) };
            brain = new Ai(seed ^ 0x5bd1e995);
            m.StartMatch();
            fades.Clear(); floats.Clear();
            ShowField(m.Field);
            hud.MenuVisible = false;
            userMoved = false;
            state = St.Coin;
            RenderHud();
            Resize(false);
            SetCam(AimView(1));
            camTarget = null;
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("lsd.eyebrow", ("map", Loc.Map(mapKey)), ("mode", ModeName())),
                Title = Loc.T("lsd.title"),
                Head = Loc.T("lsd.head"),
                Body = Loc.T("lsd.body"),
                Buttons = { new CardButton(Loc.T("lsd.throwFirst", ("name", NameOf(Team.B))), LsdShot) }
            });
        }

        string ModeName()
        {
            if (mode == Mode.Pvp) return Loc.T("mode.pvp");
            if (mode == Mode.Watch)
                return Loc.T("mode.watchVs", ("b", Loc.Team(Team.B)), ("lb", Loc.Level((AiLevel)lvB)), ("w", Loc.Team(Team.W)), ("lw", Loc.Level((AiLevel)lvW)));
            return Loc.T("mode.vsAi", ("level", Loc.Level((AiLevel)(mode - Mode.Easy))));
        }

        // ---- last stone draw ----

        void LsdShot()
        {
            m.BeginLsdShot();
            fades.Clear();
            laneTo = null;
            state = IsAI(m.Cur) ? St.Ai : St.Aim;
            userMoved = false;
            CamTo(AimView(zoom), 3.5f);
            RenderHud();
            if (state == St.Ai)
            {
                int tok = token;
                var lvl = ai[(int)m.Cur].Value;
                After((900 + (float)fx.NextDouble() * 500) / 1000f / Speed, () =>
                {
                    if (tok != token || state != St.Ai) return;
                    var d = brain.DecideLsd(lvl, m.Lsd.TargetX, m.Lsd.TargetY, m.P);
                    state = St.AiPull;
                    aiPull = new AiPull { Ang = d.Ang, Pw = d.Pw, Lx = d.Lx, T = 0, Intent = d.Intent };
                });
            }
        }

        void LsdAfter()
        {
            double d = m.RecordLsd(out bool shortOfHog);
            bool ok = !double.IsInfinity(d);
            if (!ok) hud.Toast(Loc.T("hud.noRecord"), Loc.T(shortOfHog ? "lsd.shortOfHog" : "lsd.offSheet"), 1800);
            var s = m.ShotStone;
            lsdShow = ok ? new LsdShow { X = (float)s.X, Y = (float)s.Y, D = d, T = 0 } : new LsdShow { T = 0.6f };
            state = St.LsdShow;
            CamTo(HouseView(zoom), 4);
            RenderHud();
        }

        static string FmtDist(double? d) => d == null ? "—" : double.IsInfinity(d.Value) ? Loc.T("hud.noRecord") : d.Value.ToString("0.0");

        void LsdNext()
        {
            lsdShow = null;
            if (m.NextLsd()) { LsdShot(); return; }
            double? dB = m.Lsd.Dist[0], dW = m.Lsd.Dist[1];
            var win = m.ResolveLsd(out bool coin);
            state = St.Coin;
            RenderHud();
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("lsd.resultEyebrow"), Title = Loc.T("lsd.hasHammer", ("name", NameOf(win))),
                Head = Loc.T("score.versus", ("b", Loc.Team(Team.B)), ("bv", FmtDist(dB)), ("wv", FmtDist(dW)), ("w", Loc.Team(Team.W))),
                Body = (coin ? Loc.T("lsd.coin") : Loc.T("lsd.closer", ("name", NameOf(win)))) + " " + Loc.T("end.firstThrow", ("name", NameOf(Sheet.Other(win)))),
                Buttons = { new CardButton(Loc.T("end.start", ("n", 1)), () => { m.FinishLsd(); StartEnd(); }) }
            });
        }

        // ---- ends and shots ----

        void StartEnd()
        {
            m.StartEnd();
            hud.Toast(Loc.T("bonus.title"), Loc.T("bonus.body", ("guard", Loc.Team(Sheet.Other(m.Hammer))), ("hammer", Loc.Team(m.Hammer))), 2200, true);
            fades.Clear(); measure = null;
            PrepareShot();
        }

        void PrepareShot()
        {
            RenderHud();
            if (m.ShotsDone) { FinishEnd(); return; }
            m.BeginShot();
            laneTo = null;
            state = IsAI(m.Cur) ? St.Ai : St.Aim;
            userMoved = false;
            CamTo(AimView(zoom), 3.5f);
            RenderHud();
            if (state == St.Ai) RunAI();
        }

        void RunAI() => RunAIAsync().Forget();

        // the search runs on a worker thread; its result comes back on the main thread, then the AI "thinks" a moment
        async UniTaskVoid RunAIAsync()
        {
            var team = m.Cur;
            var input = AiInput.From(m, team, ai[(int)team].Value);
            var b = brain;
            int tok = token;
            float t0 = Time.unscaledTime;
            var ct = destroyCancellationToken;
            AiDecision d;
            try { d = await UniTask.RunOnThreadPool(() => b.Decide(input), cancellationToken: ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Debug.LogException(e); return; }
            if (tok != token || state != St.Ai) return;
            m.SetLane(team, d.Lx); laneTo = d.Lx;   // slide the stone over while the AI "thinks"
            float wait = Mathf.Max(0, (1000 + (float)fx.NextDouble() * 700) / 1000f / Speed - (Time.unscaledTime - t0));
            if (await UniTask.Delay(TimeSpan.FromSeconds(wait), DelayType.Realtime, PlayerLoopTiming.Update, ct).SuppressCancellationThrow()) return;
            if (tok != token || state != St.Ai) return;
            m.LaunchX = d.Lx; laneTo = null;
            state = St.AiPull;
            aiPull = new AiPull { Ang = d.Ang, Pw = d.Pw, Lx = d.Lx, T = 0, Intent = d.Intent };
        }

        /// <summary>"left lane takeout" and the like, shown while the AI pulls.</summary>
        static string IntentLabel(AiPull a)
        {
            string intent = string.IsNullOrEmpty(a.Intent) ? "" : Loc.T("intent." + a.Intent);
            if (a.Intent == "lsd" || a.Lx == 50) return intent;
            string lane = Loc.T(a.Lx < 50 ? "ai.laneLeft" : "ai.laneRight");
            return intent == "" ? lane : Loc.T("ai.withLane", ("lane", lane), ("intent", intent));
        }

        void Release(double ang, double pw)
        {
            if (laneTo.HasValue) { m.LaunchX = laneTo.Value; laneTo = null; }
            m.Release(ang, pw);
            state = St.Moving; hint = false; drag = null; aiPull = null;
            sfx.Blip(0.4f + (float)pw * 0.4f, 1800, 3, 0.05f);
        }

        void AfterShot()
        {
            if (m.InLsd) { LsdAfter(); return; }
            var res = m.ResolveShot();
            if (res.Foul)
            {
                // cushion without a ticket: every stone goes back and only the shot stone is removed
                state = St.Restoring;
                restore = new Restore { T = 0, From = m.Stones.ToDictionary(s => s.Id, s => new Vector2((float)s.X, (float)s.Y)) };
                hud.Toast(Loc.T("foul.title"), Loc.T("foul.body"), 3400);
                return;
            }
            var notes = new List<string>();
            foreach (var r in res.Removed) fades.Add(new Fade { X = (float)r.Stone.X, Y = (float)r.Stone.Y, Team = r.Stone.Team, A = 1 });
            if (res.ShotWhy == OutWhy.Hog) notes.Add(Loc.T("out.hog"));
            else if (res.ShotWhy == OutWhy.Back) notes.Add(Loc.T("out.back"));
            var knocked = new List<string>();
            foreach (var t in new[] { Team.B, Team.W })
                if (res.Knocked[(int)t] > 0) knocked.Add(Loc.T("out.knocked", ("team", Loc.Team(t)), ("n", res.Knocked[(int)t])));
            if (knocked.Count > 0) notes.Add(string.Join(", ", knocked));
            if (notes.Count > 0) hud.Toast(notes[0], string.Join(" · ", notes.Skip(1)));
            state = St.Settle; settleT = 0.5f;
        }

        void FinishRestore()
        {
            m.ApplyRestore();
            restore = null; state = St.Settle; settleT = 0.3f;
        }

        void FinishEnd()
        {
            var eo = m.FinishEnd();
            measure = new Measure { T = 0, Sc = eo.Score, PrevHammer = eo.PrevHammer, Dur = 0.5f + eo.Score.List.Count * 0.3f + 0.7f };
            state = St.Measure;
            userMoved = false;
            CamTo(HouseView(zoom), 4);
            RenderHud();
        }

        void ShowEndCard()
        {
            var sc = measure.Sc;
            Action next;
            var step = m.NextStep();
            switch (step)
            {
                case AfterEnd.NextEnd:
                case AfterEnd.ExtraEnd: next = () => { m.End++; StartEnd(); }; break;
                default: next = GameOver; break;
            }
            int end = m.End;
            state = St.EndCard;
            ShowCard(() =>
            {
                string nextLabel = step == AfterEnd.NextEnd ? Loc.T("end.start", ("n", end + 1))
                    : step == AfterEnd.ExtraEnd ? Loc.T("end.startExtra") : Loc.T("end.final");
                string score = Versus(m.Total(Team.B), m.Total(Team.W));
                return new CardSpec
                {
                    Soft = true,
                    Eyebrow = Loc.T("end.result", ("end", EndLabel(end))),
                    Title = sc.Team.HasValue ? Loc.T("end.scored", ("name", NameOf(sc.Team.Value))) : Loc.T("end.blank"),
                    Head = sc.Team.HasValue ? Loc.T("end.points", ("n", sc.Points)) : "0 : 0",
                    Body = Loc.T(sc.Team.HasValue ? "end.bodyScored" : "end.bodyBlank", ("name", NameOf(m.Hammer)), ("score", score)),
                    Buttons = { new CardButton(nextLabel, next) }
                };
            });
        }

        void GameOver()
        {
            state = St.Over; noAuto = true;
            var win = m.Winner;
            RenderHud();
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("over.eyebrow"), Title = win.HasValue ? Loc.T("over.win", ("name", NameOf(win.Value))) : Loc.T("over.draw"),
                Head = Versus(m.Total(Team.B), m.Total(Team.W)),
                Body = win.HasValue ? "" : Loc.T("over.drawBody"),
                Buttons =
                {
                    new CardButton(Loc.T(Watching ? "over.watchAgain" : "over.again"), () => StartMatch(mode, mapKey, lvB, lvW)),
                    new CardButton(Loc.T("over.menu"), ToMenu)
                }
            });
        }

        void ToMenu()
        {
            token++;
            state = St.Menu; drag = null; aiPull = null; measure = null; restore = null; lsdShow = null; laneTo = null;
            cardFactory = null;
            m = new Match(Maps.Build("base"));
            m.Stones = DemoStones();
            fades.Clear(); floats.Clear();
            hud.HideCard(); hud.MenuVisible = true; hud.SpeedVisible = false;
            ai[0] = ai[1] = null; mode = Mode.Pvp;
            hud.HudVisible = false;   // the menu card has the screen to itself
            ShowField(m.Field);
            zoom = 1; cx = W / 2; cy = L / 2; camTarget = null;
            RenderHud();
        }

        static List<Stone> DemoStones()
        {
            var pts = new (Team t, double x, double y)[] { (Team.B, 50, 166), (Team.W, 58, 178), (Team.B, 41, 183), (Team.W, 50, 130), (Team.B, 72, 150), (Team.W, 30, 196) };
            return pts.Select((p, i) => new Stone { Id = -10 - i, Team = p.t, X = p.x, Y = p.y }).ToList();
        }

        /// <summary>Shows a card built by <paramref name="build"/>; the builder runs again if the language changes while it is up.</summary>
        void ShowCard(Func<CardSpec> build)
        {
            cardFactory = build;
            var c = build();
            bool auto = Watching && c.Buttons.Count > 0 && !noAuto;
            noAuto = false;
            int seq = hud.ShowCard(c, null);
            if (auto)
            {
                int tok = token;
                var first = c.Buttons[0];
                After(2.6f / Speed, () => { if (tok == token && seq == hud.CardSeq && hud.CardVisible) { hud.HideCard(); first.OnClick(); } });
            }
        }

        // ---- HUD text ----

        void RenderHud()
        {
            bool hammerKnown = state != St.Menu && !(m.InLsd && m.Lsd != null && m.Lsd.Idx < m.Lsd.Order.Length);
            string hammer = Loc.T("hud.hammer");
            string Hm(Team t) => hammerKnown && m.Hammer == t ? $"<color=#d9a54c><size=10> {hammer}</size></color>" : "";
            string B(int v) => $"<color=#d9a54c><size=19><b>{v}</b></size></color>";
            int tB = m.Total(Team.B), tW = m.Total(Team.W);
            string end = $"<color=#b39f84><size=12>{(state == St.Menu ? Loc.T("hud.idle") : EndLabel(m.End))} </size><size=9>▼</size></color>";
            string iconB = Ui.Icon(Team.B, 140), iconW = Ui.Icon(Team.W, 140);
            if (hud.SideMode)
            {
                // narrow side column: the score, then who has the hammer, then the end
                string hm = hammerKnown ? $"\n{Ui.Icon(m.Hammer, 110)}<color=#d9a54c><size=11> {hammer}</size></color>" : "";
                hud.SetScorePill($"{iconB} {B(tB)}<color=#b39f84> : </color>{B(tW)} {iconW}{hm}\n{end}");
            }
            else hud.SetScorePill($"{iconB}{Hm(Team.B)} {B(tB)}<color=#b39f84> : </color>{B(tW)} {iconW}{Hm(Team.W)}  {end}");

            int ends = Math.Max(Sheet.Ends, Math.Max(m.End, m.Scores[0].Count));
            var rows = new List<string[]>();
            var head = new List<string> { "" };
            for (int i = 1; i <= ends; i++) head.Add(i <= Sheet.Ends ? i.ToString() : Loc.T("hud.extra"));
            head.Add(Loc.T("hud.total"));
            rows.Add(head.ToArray());
            foreach (var t in new[] { Team.B, Team.W })
            {
                var r = new List<string> { SideOf(t, 130) + Hm(t) };
                for (int i = 1; i <= ends; i++) r.Add(i - 1 < m.Scores[(int)t].Count ? m.Scores[(int)t][i - 1].ToString() : "");
                r.Add(m.Total(t).ToString());
                rows.Add(r.ToArray());
            }
            hud.SetScoreTable(rows, state != St.Menu ? m.End : -1);
        }

        /// <summary>
        /// The turn info printed on the sheet between the launch line and the min line: whose turn and the shot number,
        /// then each side's stones left (during the last stone draw: the draw and both distances). Null in the menu.
        /// </summary>
        (string title, string head, string sub)? TurnInfo()
        {
            if (state == St.Menu) return null;
            var t = m.Cur;
            string b = Ui.Icon(Team.B, 130), w = Ui.Icon(Team.W, 130);
            if (m.InLsd)
                return (Loc.T("hud.lsd"), Loc.T("hud.turn", ("name", SideOf(t))),
                        $"{b} {FmtDist(m.Lsd.Dist[0])}      {w} {FmtDist(m.Lsd.Dist[1])}");
            string Pips(Team team)
            {
                int left = Mathf.Clamp(Sheet.Per - m.ThrownBy(team), 0, Sheet.Per);
                // a little smaller than the side icon, so the pips do not read as more side markers
                return "<size=80%>" + new string('●', left) + new string('○', Sheet.Per - left) + "</size>";
            }
            string head = m.Shot < Sheet.Shots
                ? $"{Loc.T("hud.turn", ("name", SideOf(t)))} ({m.Shot + 1} / {Sheet.Shots})"
                : Loc.T("hud.endOver");
            return (null, head, $"{b} {Pips(Team.B)}      {w} {Pips(Team.W)}");
        }

        // =====================================================================
        // live physics events
        // =====================================================================

        sealed class LiveEvents : IMatchEvents
        {
            readonly GameController g;
            public LiveEvents(GameController g) { this.g = g; }
            public void OnStoneHit(Stone a, Stone b, double j) => g.sfx.Blip((float)j / 70, 2600, 4, 0.05f);
            public void OnPostHit(Stone s, double vn) => g.sfx.Blip((float)vn / 70, 1500, 5, 0.07f);
            public void OnWallHit(Stone s, double vn, Seg seg) => g.sfx.Blip((float)vn / 90, 420, 2, 0.09f);
            public void OnTicket(Stone s, bool used)
            {
                g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y - 6, Text = Loc.T(used ? "float.ticketUsed" : "float.noTicket") });
            }
            public void OnCushion(Stone s, int n)
            {
                g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y, Text = Loc.T("float.cushion", ("n", n)) });
                g.sfx.Chime(n);
            }
            public void OnDeadRail(Stone s) => g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y, Text = Loc.T("float.deadRail") });
        }

        // =====================================================================
        // loop
        // =====================================================================

        void Update()
        {
            float rdt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            float dt = rdt * Speed;   // game clock (physics, animations); the camera keeps real time

            UpdateTurnPulse();
            HandleInput();

            if (state == St.Moving)
            {
                acc += dt;
                while (acc >= Sheet.DT) { m.Step(); acc -= (float)Sheet.DT; }
                if (!m.Moving) { acc = 0; AfterShot(); }
            }
            else acc = 0;
            if (state == St.Restoring) { restore.T += dt / 0.9f; if (restore.T >= 1) FinishRestore(); }
            if (state == St.Settle) { settleT -= dt; if (settleT <= 0) { m.AdvanceShot(); PrepareShot(); } }
            if (laneTo.HasValue)
            {
                double d = laneTo.Value - m.LaunchX, stp = 110 * dt;
                if (Math.Abs(d) <= stp) { m.LaunchX = laneTo.Value; laneTo = null; } else m.LaunchX += Math.Sign(d) * stp;
            }
            if (state == St.AiPull) { aiPull.T += dt / 0.6f; if (aiPull.T >= 1.15f) Release(aiPull.Ang, aiPull.Pw); }
            if (state == St.Measure) { measure.T += dt; if (measure.T >= measure.Dur) ShowEndCard(); }
            if (state == St.LsdShow) { lsdShow.T += dt; if (lsdShow.T >= 1.8f) LsdNext(); }
            foreach (var f in fades) f.A -= dt / 0.5f;
            fades.RemoveAll(f => f.A <= 0);
            foreach (var f in floats) f.T += dt / 1.2f;
            floats.RemoveAll(f => f.T >= 1);

            Resize(false);
            UpdateCam(rdt * Mathf.Sqrt(Speed));
            ApplyCamera();
            hud.Tick(px, cw, ch);
            Draw(Time.unscaledTime * 1000);
        }
    }
}
