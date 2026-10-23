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

        enum St { Menu, Coin, Aim, Ai, AiPull, Remote, Moving, Restoring, Settle, Measure, EndCard, Over, LsdShow }   // Remote: online, the opponent's turn
        enum Mode { Pvp, Easy, Normal, Hard, Online }

        const float W = (float)Sheet.W, L = (float)Sheet.L, HOG = (float)Sheet.Hog, BACK = (float)Sheet.Back, R = (float)Sheet.R;
        static float SR => (float)Sheet.SR;
        const float TEEX = (float)Sheet.TeeX, TEEY = (float)Sheet.TeeY, LY = (float)Sheet.LaunchY;
        const float VIEW_TOP = BACK + 10;

        // ---- game state ----
        St state = St.Menu;
        Mode mode = Mode.Pvp;
        string mapKey = "base";
        readonly AiLevel?[] ai = new AiLevel?[2];
        Match m;
        Ai brain;
        int token;
        double? laneTo;
        float acc;
        float settleT;
        System.Random fx = new System.Random();

        sealed class AiPull { public double Ang, Pw, Lx; public float T; public string Intent; }
        sealed class Fade { public float X, Y, A; public Team Team; }
        sealed class Float { public float X, Y, T; public string Text; }
        sealed class Measure { public float T, Dur; public ScoreResult Sc; public Team PrevHammer; }
        sealed class Restore { public float T; public Dictionary<int, Vector2> From; }
        sealed class LsdShow { public float X, Y, T; public double? D; public string DText; }

        AiPull aiPull;
        readonly List<Fade> fades = new List<Fade>();
        readonly List<Float> floats = new List<Float>();
        Measure measure;
        Restore restore;
        LsdShow lsdShow;
        Func<CardSpec> cardFactory;   // rebuilds the visible card after a language change
        // your-turn pulse: the launch stone and the turn line grow and shrink while a human is to aim
        float turnPulse = 1;
        bool showPowerPercent = true, showHitMarker = true;   // from GameConfig
        float launchRingRadius = 8, launchRingWidth = 4.5f, hitRingRadius = 4, hitRingWidth = 3;
        // the shot clock: a human has this long to throw once the turn toast has gone, or the stone is lost
        const float ClockWait = 1.45f;
        float ShotTime = 15;   // from GameConfig
        float clock = -1, clockWait;
        FuseFx fuse;
        Tween pulseTween;

        // ---- delayed callbacks (prototype setTimeout), real time; dropped when this object is destroyed ----
        void After(float seconds, Action a) => AfterAsync(seconds, a).Forget();

        async UniTaskVoid AfterAsync(float seconds, Action a)
        {
            bool cancelled = await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.DeltaTime, PlayerLoopTiming.Update, destroyCancellationToken)
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

        bool IsAI(Team t) => ai[(int)t].HasValue;
        // the HUD shows the side as a stone icon rather than its name
        string SideOf(Team t, int scalePct = 115) => Ui.Icon(t, scalePct) + (IsAI(t) ? Loc.T("team.ai") : "");
        static string EndLabel(int n) => n <= Sheet.Ends ? Loc.T("hud.end", ("n", n)) : Loc.T("hud.extra");
        // the side shown on the left (as black) and the one on the right: online, this device's player is on the left
        static Team Left => Ui.Shown(Team.B);
        static Team Right => Ui.Shown(Team.W);
        // the two sides' scores, each with its stone icon, the left side's first
        string Versus(int iconPct = 130) =>
            Loc.T("score.versus", ("b", Ui.Icon(Left, iconPct)), ("bv", m.Total(Left)), ("wv", m.Total(Right)), ("w", Ui.Icon(Right, iconPct)));
        const float CardCountdown = 3;

        // =====================================================================
        // boot
        // =====================================================================

        void Awake()
        {
            // the tunable rules first: the stone sprites and the board are sized from them
            var config = GameConfig.Load();
            config.Apply();
            showPowerPercent = config.showPowerPercent;
            showHitMarker = config.showHitMarker;
            ShotTime = config.turnSeconds;
            catchUpSpeed = config.catchUpSpeed;
            launchRingRadius = config.launchRingRadius; launchRingWidth = config.launchRingWidth;
            hitRingRadius = config.hitRingRadius; hitRingWidth = config.hitRingWidth;
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
            fuse = new FuseFx(transform, unlitMaterial, overlayLayer);

            sfx = new Sfx(gameObject);
            hud = new Hud(transform);
            // the settings gear opens the shared settings popup (Resources/SettingsPopup), with a way back to the menu
            settingsPrefab = Resources.Load<SettingsPanel>(SettingsPanel.ResourcePath);
            if (settingsPrefab) hud.SettingsIcon = settingsPrefab.Icon;
            hud.OnSettings = OpenSettings;
            GameSettings.Changed += OnSettingsChanged;
            Account.NetworkGuard.Resumed += OnNetworkBack;
            LoadSettings();
            Loc.Changed += OnLocaleChanged;
            Resize(true);
            // the match picked in the main menu, or (the scene opened straight in the editor) two players on the basic map
            if (MatchLaunch.TakeTutorial()) StartTutorial();
            else if (MatchLaunch.TakeOnline()) StartOnline();
            else if (MatchLaunch.Take(out bool vsAi, out var level, out string map))
                StartMatch(vsAi ? Mode.Easy + (int)level : Mode.Pvp, map);
            else StartMatch(Mode.Pvp, "base");
        }

        void OnDestroy() { Loc.Changed -= OnLocaleChanged; GameSettings.Changed -= OnSettingsChanged; Account.NetworkGuard.Resumed -= OnNetworkBack; OnlineDestroy(); Ui.Flip = false; }

        SettingsPanel settingsPrefab, settings;
        bool SettingsOpen => settings && settings.IsOpen;

        // the popup on its own canvas above the HUD, laid out like the main menu (1080 x 1920 reference)
        void OpenSettings()
        {
            if (!settingsPrefab) return;
            if (!settings)
            {
                var go = new GameObject("SettingsCanvas", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = hud.Canvas.sortingOrder + 10;
                var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                settings = Instantiate(settingsPrefab, go.transform);
                settings.ToMenu += RequestMenu;
                if (InTutorial) settings.SetMenuKey("tutorial.skip");   // the tutorial's way out: skipping it
            }
            ResetShotInput();   // a pull under way is let go without a shot
            settings.Open(true);
        }

        // the aim sensitivity can change from the settings popup
        void OnSettingsChanged() => sensLevel = GameSettings.SensLevel;

        // a human to aim and not yet pulling: loop the pulse; otherwise stop it at scale 1
        void UpdateTurnPulse()
        {
            bool want = HumanAim && !Charging;
            if (want && pulseTween == null)
                pulseTween = DOTween.To(() => turnPulse, v => turnPulse = v, 1.08f, 0.6f)
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            else if (!want && pulseTween != null)
            {
                pulseTween.Kill();
                pulseTween = null;
                turnPulse = 1;
            }
        }

        void OnLocaleChanged()
        {
            locVersion++;
            hud.Relocalize();
            RenderHud();
            if (hud.CardVisible && cardFactory != null) hud.ReplaceCard(cardFactory(), null);
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

        void StartMatch(Mode md, string key)
        {
            token++;
            hud.HideCard();   // a card from before (its countdown) must not press into the new match
            mode = md; mapKey = key;
            ai[0] = ai[1] = null;
            if (md != Mode.Pvp && md != Mode.Online) ai[1] = (AiLevel)(md - Mode.Easy);
            hud.HudVisible = true;
            // online: both devices start the same match (the seed decides the draw's bumpers), each showing its player as black
            int seed = md == Mode.Online ? Online.OnlineLobby.Seed : fx.Next();
            Ui.Flip = md == Mode.Online && Online.OnlineLobby.MyTeam == Team.W;
            OnlineReset();
            m = new Match(Maps.Build(key), PhysicsParams.Default, seed) { Listener = new LiveEvents(this) };
            brain = new Ai(seed ^ 0x5bd1e995);
            m.StartMatch();
            fades.Clear(); floats.Clear();
            ShowField(m.Field);
            userMoved = false;
            state = St.Coin;
            RenderHud();
            Resize(false);
            SetCam(AimView(1));
            camTarget = null;
            // online the records panel comes first (or, coming back to a match, the card waits for where it is)
            if (mode == Mode.Online) OnlineFirstCard(LsdStartCard);
            else LsdStartCard();
        }

        void LsdStartCard() => ShowCard(() => new CardSpec
        {
            Eyebrow = Loc.T("lsd.eyebrow", ("map", Loc.Map(mapKey)), ("mode", ModeName())),
            Title = Loc.T("lsd.title"),
            Head = Loc.T("lsd.head"),
            Countdown = CardCountdown,
            Buttons = { SyncButton(CardLsdStart, Loc.T("lsd.start"), LsdShot) }
        });

        string ModeName()
        {
            if (mode == Mode.Online) return Loc.T("mainmenu.online");
            if (mode == Mode.Pvp) return Loc.T("mode.pvp");
            return Loc.T("mode.vsAi", ("level", Loc.Level((AiLevel)(mode - Mode.Easy))));
        }

        // ---- last stone draw ----

        void LsdShot()
        {
            m.BeginLsdShot();
            fades.Clear();
            laneTo = null;
            state = IsAI(m.Cur) ? St.Ai : IsRemote(m.Cur) ? St.Remote : St.Aim;
            aimAng = 0; ResetShotInput();   // every throw starts aimed straight up the sheet
            userMoved = false;
            CamTo(AimView(zoom), 3.5f);
            RenderHud();
            hud.TurnToast(Loc.T("hud.turn", ("name", SideOf(m.Cur, 130))));
            sfx.TurnStart();
            StartClock();
            OnlineTurnStarted();
            if (state == St.Ai)
            {
                int tok = token;
                var lvl = ai[(int)m.Cur].Value;
                After((900 + (float)fx.NextDouble() * 500) / 1000f, () =>
                {
                    if (tok != token || state != St.Ai) return;
                    var d = brain.DecideLsd(lvl, m.Lsd.TargetX, m.Lsd.TargetY, m.P, m.ActiveField);
                    state = St.AiPull;
                    aiPull = new AiPull { Ang = d.Ang, Pw = d.Pw, Lx = d.Lx, T = 0, Intent = d.Intent };
                });
            }
        }

        void LsdAfter()
        {
            double d = m.RecordLsd(out bool shortOfHog);
            bool ok = !double.IsInfinity(d);
            if (!ok) sfx.Void();
            if (!ok) hud.Toast(Loc.T("hud.noRecord"), Loc.T(shortOfHog ? "lsd.shortOfHog" : "lsd.offSheet"), 1800);
            var s = m.ShotStone;
            lsdShow = ok ? new LsdShow { X = (float)s.X, Y = (float)s.Y, D = d, DText = d.ToString("0.0"), T = 0 } : new LsdShow { T = 0.6f };
            state = St.LsdShow;
            CamTo(HouseView(zoom), 4);
            RenderHud();
        }


        void LsdNext()
        {
            lsdShow = null;
            if (m.NextLsd()) { LsdShot(); return; }
            double? dB = m.Lsd.Dist[0], dW = m.Lsd.Dist[1];
            var win = m.ResolveLsd(out bool coin);
            LsdResultCard(win);
        }

        void LsdResultCard(Team win)
        {
            state = St.Coin;
            RenderHud();
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("lsd.resultEyebrow"),
                Title = Loc.T("lsd.hasHammer", ("first", SideOf(Sheet.Other(win), 110)), ("name", SideOf(win, 110))),
                Countdown = CardCountdown,
                Buttons = { SyncButton(CardLsdResult, Loc.T("end.start", ("n", 1)), () => { m.FinishLsd(); StartEnd(); }) }
            });
        }

        // ---- ends and shots ----

        void StartEnd()
        {
            m.StartEnd();
            fades.Clear(); measure = null;
            PrepareShot();
        }

        void PrepareShot()
        {
            RenderHud();
            if (m.ShotsDone) { FinishEnd(); return; }
            m.BeginShot();
            laneTo = null;
            state = IsAI(m.Cur) ? St.Ai : IsRemote(m.Cur) ? St.Remote : St.Aim;
            aimAng = 0; ResetShotInput();   // every throw starts aimed straight up the sheet
            userMoved = false;
            CamTo(AimView(zoom), 3.5f);
            RenderHud();
            // the first thrower's opening turn of each end also notes the bonus guard it starts with
            hud.TurnToast(Loc.T("hud.turn", ("name", SideOf(m.Cur, 130))), m.Shot == 0 ? Loc.T("bonus.title") : null);
            sfx.TurnStart();
            StartClock();
            OnlineTurnStarted();
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
            float t0 = Time.time;
            var ct = destroyCancellationToken;
            AiDecision d;
            try { d = await UniTask.RunOnThreadPool(() => b.Decide(input), cancellationToken: ct); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Debug.LogException(e); return; }
            if (tok != token || state != St.Ai) return;
            m.SetLane(team, d.Lx); laneTo = d.Lx;   // slide the stone over while the AI "thinks"
            float wait = Mathf.Max(0, (1000 + (float)fx.NextDouble() * 700) / 1000f - (Time.time - t0));
            if (await UniTask.Delay(TimeSpan.FromSeconds(wait), DelayType.DeltaTime, PlayerLoopTiming.Update, ct).SuppressCancellationThrow()) return;
            if (tok != token || state != St.Ai) return;
            m.LaunchX = d.Lx; laneTo = null;
            state = St.AiPull;
            aiPull = new AiPull { Ang = d.Ang, Pw = d.Pw, Lx = d.Lx, T = 0, Intent = d.Intent };
        }

        void Release(double ang, double pw)
        {
            // online, this device's own shot: the input goes to the opponent first (from then on the shot stands), then
            // this device runs it and sends the record both play
            bool mine = mode == Mode.Online && state == St.Aim;
            if (laneTo.HasValue) { m.LaunchX = laneTo.Value; laneTo = null; }
            if (mine) OnlineCommit(ang, pw);
            m.Release(ang, pw);
            state = St.Moving; ResetShotInput(); aiPull = null; clock = -1;
            sfx.Blip(0.4f + (float)pw * 0.4f, 1800, 3, 0.05f);
            if (mine) OnlineRecordShot();
        }

        void AfterShot()
        {
            OnlineShotDone();
            if (m.InLsd) { LsdAfter(); return; }
            var res = m.ResolveShot();
            if (res.Foul)
            {
                // cushion without a ticket: every stone goes back and only the shot stone is removed
                state = St.Restoring;
                restore = new Restore { T = 0, From = m.Stones.ToDictionary(s => s.Id, s => new Vector2((float)s.X, (float)s.Y)) };
                hud.Toast(Loc.T("foul.lost", ("team", Ui.Icon(m.Cur))), null, 3400, 14);
                sfx.Void();
                return;
            }
            foreach (var r in res.Removed) fades.Add(new Fade { X = (float)r.Stone.X, Y = (float)r.Stone.Y, Team = r.Stone.Team, A = 1 });
            // the shot stone's own fate as the title, then one line per colour knocked out
            string head = res.ShotWhy == OutWhy.Hog ? Loc.T("out.hog") : res.ShotWhy == OutWhy.Back ? Loc.T("out.back")
                : res.ShotWhy == OutWhy.Near ? Loc.T("out.near") : null;
            var knocked = new List<string>();
            foreach (var t in new[] { Team.B, Team.W })
                if (res.Knocked[(int)t] > 0) knocked.Add(Loc.T("out.knocked", ("team", Ui.Icon(t)), ("n", res.Knocked[(int)t])));
            if (head != null) { hud.Toast(head, string.Join("\n", knocked)); sfx.Void(); }
            else if (knocked.Count > 0) hud.Toast(string.Join("\n", knocked));
            state = St.Settle; settleT = 0.9f;
        }

        // ---- shot clock ----

        void StartClock()
        {
            if (InTutorial) { clock = -1; return; }   // no shot clock while learning
            clock = state == St.Aim || state == St.Remote ? ShotTime : -1;
            clockWait = ClockWait + (!m.InLsd && m.Shot == 0 ? 0.45f : 0);   // the end's first turn toast stays up longer
            if (mode == Mode.Online && clock >= 0) deadline = Now + clockWait + ShotTime;
        }

        // real time, held while the turn toast is up, a card covers the sheet or the settings popup is open
        void TickClock(float rdt)
        {
            // online the fuse runs on the wall clock (time in the background counts), on both devices
            if (mode == Mode.Online) { TickOnlineClock(); return; }
            // online the clock never waits for the settings popup: the opponent's does not
            if (clock < 0 || !HumanAim || hud.CardVisible || (SettingsOpen && mode != Mode.Online)) return;
            if (clockWait > 0) { clockWait -= rdt; return; }
            float before = clock;
            clock -= rdt;
            // a third of the time left: the fuse starts flashing red and a warning beeps
            if (before > ShotTime / 3 && clock <= ShotTime / 3 && clock > 0) sfx.Warn();
            if (clock <= 0) TimeOut();
        }

        // time is up: a pull under way is dropped and the stone is lost (in the draw: no record)
        void TimeOut()
        {
            if (mode == Mode.Online) OnlineSend(Online.OnlineMsg.Void);
            ApplyTimeOut();
        }

        // the turn's stone is lost (online also when the opponent's time ran out)
        void ApplyTimeOut()
        {
            clock = -1;
            ResetShotInput();
            m.Forfeit();
            OnlineShotDone();
            sfx.Void();
            if (m.InLsd)
            {
                hud.Toast(Loc.T("timeout.title"), Loc.T("timeout.lsd"), 1800);
                lsdShow = new LsdShow { T = 0.6f };
                state = St.LsdShow;
                RenderHud();
                return;
            }
            hud.Toast(Loc.T("timeout.title"), Loc.T("timeout.body", ("name", SideOf(m.Cur))), 1800);
            state = St.Settle; settleT = 1.0f;
        }

        void FinishRestore()
        {
            m.ApplyRestore();
            restore = null; state = St.Settle; settleT = 0.7f;
        }

        void FinishEnd()
        {
            var eo = m.FinishEnd();
            sfx.EndOver();
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
            var step = InTutorial ? AfterEnd.GameOver : m.NextStep();   // the tutorial's one end
            switch (step)
            {
                case AfterEnd.NextEnd:
                case AfterEnd.ExtraEnd: next = () => { m.End++; StartEnd(); }; break;
                default: next = InTutorial ? TutorialEndCard : GameOver; OnlineReportResult(); break;
            }
            int end = m.End;
            state = St.EndCard;
            ShowCard(() =>
            {
                string nextLabel = step == AfterEnd.NextEnd ? Loc.T("end.start", ("n", end + 1))
                    : step == AfterEnd.ExtraEnd ? Loc.T("end.startExtra") : Loc.T("end.final");
                return new CardSpec
                {
                    Soft = true,
                    Eyebrow = Loc.T("end.result", ("end", EndLabel(end))),
                    // one line: who scored (or blank) and the running score
                    Title = sc.Team.HasValue
                        ? Loc.T("end.scored", ("name", SideOf(sc.Team.Value, 110)), ("n", sc.Points), ("score", Versus(110)))
                        : Loc.T("end.blank", ("score", Versus(110))),
                    // NextStep has already handed the hammer on; nothing to say after the last end
                    Body = step == AfterEnd.GameOver ? "" : Loc.T("end.nextHammer", ("name", SideOf(m.Hammer))),
                    Countdown = CardCountdown,
                    // the last end's card is not shared: each player goes on to the result alone
                    // (online, it goes on once the server has counted the match: the result card shows the points)
                    Buttons = { step != AfterEnd.GameOver ? SyncButton(CardEnd + end, nextLabel, next)
                        : mode == Mode.Online ? ResultButton(nextLabel, next) : new CardButton(nextLabel, next) }
                };
            });
        }

        void GameOver()
        {
            state = St.Over;
            var win = m.Winner;
            RenderHud();
            if (mode == Mode.Online) { OnlineGameOver(); return; }
            // against the AI the player's own result; two players on one device, who won
            bool vsAi = IsAI(Team.B) != IsAI(Team.W);
            ShowCard(() => new CardSpec
            {
                Eyebrow = Loc.T("over.eyebrow"),
                Title = vsAi ? MyResult(win, IsAI(Team.B) ? Team.W : Team.B)
                    : win.HasValue
                    ? Loc.T("over.win", ("name", SideOf(win.Value, 110)), ("score", Versus(110)))
                    : Loc.T("over.draw", ("score", Versus(110))),
                Buttons =
                {
                    new CardButton(Loc.T("over.again"), () => StartMatch(mode, mapKey)),
                    new CardButton(Loc.T("over.menu"), ToMenu)
                }
            });
        }

        // a player's own result on the final card: won, lost or drawn, with the score
        string MyResult(Team? win, Team me) =>
            Loc.T(!win.HasValue ? "over.myDraw" : win.Value == me ? "over.myWin" : "over.myLoss", ("score", Versus(110)));

        // back to the main menu scene, through the screen fade; whatever is under way here stops
        void ToMenu()
        {
            token++;
            OnlineLeave();
            ResetShotInput(); aiPull = null; clock = -1;
            hud.ClearToasts(); hud.HideCard();
            ScreenFader.LoadScene(MainMenuScene);
        }
        const string MainMenuScene = "MainMenu";

        /// <summary>Shows a card built by <paramref name="build"/>; the builder runs again if the language changes while it is up.</summary>
        void ShowCard(Func<CardSpec> build)
        {
            // a card waits for a screen fade to finish, then a moment more (coming from the main menu, the turn order card
            // appears half a second after the game has faded in, its countdown untouched)
            if (ScreenFader.Busy) { ScreenFader.WhenClear(() => { if (this) After(CardAfterFade, () => ShowCard(build)); }); return; }
            cardFactory = build;
            var c = build();
            hud.ShowCard(c, null);
            sfx.Pop();
        }
        const float CardAfterFade = 0.5f;

        // ---- HUD text ----

        void RenderHud()
        {
            bool hammerKnown = state != St.Menu && !(m.InLsd && m.Lsd != null && m.Lsd.Idx < m.Lsd.Order.Length);
            string hammer = Loc.T("hud.hammer");
            string Hm(Team t) => hammerKnown && m.Hammer == t ? $"<color=#d9a54c><size=10> {hammer}</size></color>" : "";
            string B(int v) => $"<color=#d9a54c><size=19><b>{v}</b></size></color>";
            // "B" here is the side on the left (black on screen), "W" the one on the right
            int tB = m.Total(Left), tW = m.Total(Right);
            string end = $"<color=#b39f84><size=12>{(state == St.Menu ? Loc.T("hud.idle") : EndLabel(m.End))} </size><size=9>▼</size></color>";
            string iconB = Ui.Icon(Left, 140), iconW = Ui.Icon(Right, 140);
            if (hud.SideMode)
            {
                // narrow side column: the score, then who has the hammer, then the end
                string hm = hammerKnown ? $"\n{Ui.Icon(m.Hammer, 110)}<color=#d9a54c><size=11> {hammer}</size></color>" : "";
                hud.SetScorePill($"{iconB} {B(tB)}<color=#b39f84> : </color>{B(tW)} {iconW}{hm}\n{end}");
            }
            else hud.SetScorePill($"{iconB}{Hm(Left)} {B(tB)}<color=#b39f84> : </color>{B(tW)} {iconW}{Hm(Right)}  {end}");

            int ends = Math.Max(Sheet.Ends, Math.Max(m.End, m.Scores[0].Count));
            var rows = new List<string[]>();
            var head = new List<string> { "" };
            for (int i = 1; i <= ends; i++) head.Add(i <= Sheet.Ends ? i.ToString() : Loc.T("hud.extra"));
            head.Add(Loc.T("hud.total"));
            rows.Add(head.ToArray());
            foreach (var t in new[] { Left, Right })
            {
                var r = new List<string> { Ui.Icon(t, 130) };   // just the stone: no tags between it and the scores
                for (int i = 1; i <= ends; i++) r.Add(i - 1 < m.Scores[(int)t].Count ? m.Scores[(int)t][i - 1].ToString() : "");
                r.Add(m.Total(t).ToString());
                rows.Add(r.ToArray());
            }
            hud.SetScoreTable(rows, state != St.Menu ? m.End : -1);
        }

        /// <summary>
        /// The turn info printed on the sheet between the launch line and the min line, black's in the left half and
        /// white's in the right (on a device the player is always black, the left): the stone and the name, the record
        /// (online only), then first or last and the stones left (not during the turn order shot, which decides them).
        /// Null in the menu.
        /// </summary>
        (string[] names, string[] records, string[] roles, string[] pips)? TurnInfo()
        {
            if (state == St.Menu) return null;
            // the stone, then the name with a hanging indent: a long name wraps under its own first letter
            string Name(Team t) => $"{Ui.Icon(t, 115)}\u00A0<indent={NameIndentEm().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}em>{SideName(t)}</indent>";
            var names = new[] { Name(Left), Name(Right) };
            var records = SideRecords();
            if (m.InLsd) return (names, records, null, null);
            string Pips(Team team)
            {
                int left = Mathf.Clamp(Sheet.Per - m.ThrownBy(team), 0, Sheet.Per);
                // the row empties from the right: the stone being thrown now is the rightmost one still to go, in the
                // house red, which stands out on the wood
                bool throwing = team == m.Cur && left > 0 && (state == St.Aim || state == St.Ai || state == St.AiPull || state == St.Remote);
                string now = throwing ? $"<color=#a3341f{(Charging || state == St.AiPull ? "4d" : "")}>●</color>" : "";
                int rest = throwing ? left - 1 : left;
                // a little smaller than the side icon, so the pips do not read as more side markers
                return "<size=80%>" + new string('●', rest) + now + new string('○', Sheet.Per - left) + "</size>";
            }
            // each side's stones left in its own half, under "선공" / "후공"
            string Role(Team team) => Loc.T(m.Hammer == team ? "hud.hammer" : "hud.first");
            return (names, records, new[] { Role(Left), Role(Right) }, new[] { Pips(Left), Pips(Right) });
        }

        // how far the name starts after the stone, in em (measured once)
        float nameIndentEm = -1;
        float NameIndentEm()
        {
            if (nameIndentEm < 0)
                nameIndentEm = (hud.Measure(Ui.Icon(Team.B, 115) + "\u00A0가", 100, true) - hud.Measure("가", 100, true)) / 100f;
            return nameIndentEm;
        }

        // the turn info is rebuilt only when something it shows changes (it is drawn every frame)
        int turnInfoKey;
        (string[] names, string[] records, string[] roles, string[] pips)? turnInfo;

        (string[] names, string[] records, string[] roles, string[] pips)? CachedTurnInfo()
        {
            bool throwing = state == St.Aim || state == St.Ai || state == St.AiPull || state == St.Remote;
            bool dim = Charging || state == St.AiPull;
            int key = HashCode.Combine(HashCode.Combine(state == St.Menu, state == St.Over, m.InLsd, m.Cur, m.Hammer, m.Shot, m.End, mode),
                HashCode.Combine(throwing, dim, locVersion, Account.AccountService.PlayerName, m,
                    mode == Mode.Online && Online.OnlineLobby.MyStats != null, mode == Mode.Online && Online.OnlineLobby.OpponentStats != null));
            if (key != turnInfoKey || (turnInfo == null && state != St.Menu))
            {
                turnInfoKey = key;
                turnInfo = TurnInfo();
            }
            return turnInfo;
        }

        /// <summary>What a side is called on the sheet: two players are player 1 (black) and 2; against the AI the
        /// player goes by their nickname (or "player") and the AI by "AI"; online, the two nicknames.</summary>
        string SideName(Team t)
        {
            if (mode == Mode.Online) return OnlineSideName(t);
            if (mode == Mode.Pvp) return Loc.T(t == Team.B ? "hud.player1" : "hud.player2");
            if (t == Team.W) return Loc.T("hud.ai");
            string nick = Account.Nickname.Display(Account.AccountService.PlayerName);
            return string.IsNullOrEmpty(nick) ? Loc.T("hud.player") : nick;
        }

        /// <summary>Each side's record line (left, right), or null where records are not shown (offline).</summary>
        string[] SideRecords() => mode == Mode.Online ? OnlineRecords() : null;

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
            // the one wall text: a ticket spent, on the shot's first cushion (Core spends at most one per shot);
            // without a ticket the foul toast says it instead
            public void OnTicket(Stone s, bool used)
            {
                if (used) g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y - 6, Text = Loc.T("float.ticketMinus") });
            }
            public void OnCushion(Stone s, int n) => g.sfx.Chime(n);
            public void OnDeadRail(Stone s) { }
        }

        // =====================================================================
        // loop
        // =====================================================================

        void Update()
        {
            // the game's clock is the scaled one: Time.timeScale 0 (the network popup) stops the match where it is
            float rdt = Mathf.Min(0.05f, Time.deltaTime);
            float dt = rdt;   // game clock (physics, animations)

            UpdateTurnPulse();
            HandleInput();
            TickClock(rdt);

            OnlineUpdate();
            if (state == St.Moving && playback != null)
            {
                // online: the shot plays from its record (the same on both devices, in step)
                AdvancePlayback(dt);
                if (playback.Done) { playback = null; AfterShot(); }
            }
            else if (state == St.Moving)
            {
                acc += dt;
                while (acc >= Sheet.DT) { m.Step(); acc -= (float)Sheet.DT; }
                if (!m.Moving) { acc = 0; AfterShot(); }
            }
            else acc = 0;
            if (state == St.Restoring) { restore.T += dt / 0.9f; if (restore.T >= 1) FinishRestore(); }
            // the next turn waits until the last one's toasts have fully gone
            if (state == St.Settle)
            {
                settleT -= dt;
                if (settleT <= 0 && !hud.ToastsUp && OnlineNextTurnReady())
                {
                    // a shot the tutorial threw: back to it, not on to the next turn
                    if (tutorialSettled != null) { var a = tutorialSettled; tutorialSettled = null; state = St.Menu; a(); }
                    else { m.AdvanceShot(); PrepareShot(); }
                }
            }
            if (laneTo.HasValue)
            {
                double d = laneTo.Value - m.LaunchX, stp = 110 * dt;
                if (Math.Abs(d) <= stp) { m.LaunchX = laneTo.Value; laneTo = null; } else m.LaunchX += Math.Sign(d) * stp;
            }
            if (state == St.AiPull) { aiPull.T += dt / 0.6f; if (aiPull.T >= 1.15f) Release(aiPull.Ang, aiPull.Pw); }
            if (state == St.Measure)
            {
                measure.T += dt;
                if (measure.T >= measure.Dur)
                {
                    if (tutorialMeasured != null) { var a = tutorialMeasured; tutorialMeasured = null; state = St.EndCard; a(); }
                    else ShowEndCard();
                }
            }
            if (state == St.LsdShow) { lsdShow.T += dt; if (lsdShow.T >= 1.8f && !hud.ToastsUp && OnlineNextTurnReady()) LsdNext(); }
            foreach (var f in fades) f.A -= dt / 0.5f;
            fades.RemoveAll(f => f.A <= 0);
            foreach (var f in floats) f.T += dt / 1.2f;
            floats.RemoveAll(f => f.T >= 1);

            Resize(false);
            UpdateCam(rdt);
            ApplyCamera();
            hud.Tick(px, cw, ch);
            Draw(Time.time * 1000);
        }
    }
}
