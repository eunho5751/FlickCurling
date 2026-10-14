using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlickCurling.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace FlickCurling.Game
{
    /// <summary>
    /// Presentation and flow (prototype G.state machine): menu, last stone draw, ends, shots, AI turns, judging
    /// animations, camera, input and drawing. Rules and physics live in FlickCurling.Core.
    /// </summary>
    public sealed partial class GameController : MonoBehaviour
    {
        [SerializeField] Material unlitMaterial;   // sprites
        [SerializeField] Material shapeMaterial;   // vertex-colour shape meshes
        [SerializeField] Camera mainCam;
        [SerializeField] Camera miniCam;

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
        sealed class AiPull { public double Ang, Pw; public float T; public string Label; }
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
        Task<AiDecision> aiTask;
        int aiToken;
        float aiT0;

        // ---- scheduled callbacks (prototype setTimeout), real time ----
        readonly List<(float at, Action a)> timers = new List<(float, Action)>();
        void After(float seconds, Action a) => timers.Add((Time.unscaledTime + seconds, a));

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
        string NameOf(Team t) => Sheet.Name(t) + (IsAI(t) ? "(AI)" : "");
        static string EndLabel(int n) => n <= Sheet.Ends ? $"{n}엔드" : "연장";
        static readonly string[] LvName = { "쉬움", "보통", "어려움" };

        // =====================================================================
        // boot
        // =====================================================================

        void Awake()
        {
            Application.targetFrameRate = 60;
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
            Ui.Init(disc, stoneB, stoneW);

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

            hud = new Hud(transform);
            hud.OnStart = (md, mp, b, w) => { sfx.Blip(0, 0); StartMatch((Mode)md, mp == 4 ? Maps.Keys[fx.Next(Maps.Keys.Length)] : Maps.Keys[mp], b, w); };
            hud.OnMenu = ToMenu;
            hud.OnSound = () => { sfx.On = !sfx.On; hud.SoundText = sfx.On ? "소리 켬" : "소리 끔"; };
            hud.OnSpeed = () => { speed = speed == 1 ? 2 : speed == 2 ? 4 : 1; hud.SpeedText = $"{speed}배속"; };
            sfx = new Sfx(gameObject);
            Resize(true);
            ToMenu();
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
            string modeName = md == Mode.Pvp ? "2인 대전"
                : md == Mode.Watch ? $"AI 관전 · 흑 {LvName[b]} vs 백 {LvName[w]}"
                : "AI " + LvName[md - Mode.Easy];
            hud.MenuVisible = false;
            userMoved = false;
            state = St.Coin;
            RenderHud();
            Resize(false);
            SetCam(AimView(1));
            camTarget = null;
            ShowCard(new CardSpec
            {
                Eyebrow = $"{m.Field.Name} 맵 · {modeName}",
                Title = "라스트 스톤 드로",
                Head = "표적에 가까운 쪽이 해머",
                Body = "하우스 안에 매번 다른 자리에 표적이 생깁니다. 각자 알 하나를 표적에 최대한 가깝게 굴리고, 더 가까운 쪽이 1엔드의 마지막 샷(해머)을 가집니다. 최소 라인에 못 미치거나 백 라인을 넘으면 기록이 없습니다.",
                Buttons = { new CardButton($"{NameOf(Team.B)}부터 굴리기", LsdShot) }
            });
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
                    aiPull = new AiPull { Ang = d.Ang, Pw = d.Pw, T = 0, Label = d.Label };
                });
            }
        }

        void LsdAfter()
        {
            double d = m.RecordLsd(out bool shortOfHog);
            bool ok = !double.IsInfinity(d);
            if (!ok) hud.Toast("기록 없음", shortOfHog ? "최소 라인에 못 미쳤습니다." : "판 밖으로 나갔습니다.", 1800);
            var s = m.ShotStone;
            lsdShow = ok ? new LsdShow { X = (float)s.X, Y = (float)s.Y, D = d, T = 0 } : new LsdShow { T = 0.6f };
            state = St.LsdShow;
            CamTo(HouseView(zoom), 4);
            RenderHud();
        }

        static string FmtDist(double? d) => d == null ? "—" : double.IsInfinity(d.Value) ? "기록 없음" : d.Value.ToString("0.0");

        void LsdNext()
        {
            lsdShow = null;
            if (m.NextLsd()) { LsdShot(); return; }
            double? dB = m.Lsd.Dist[0], dW = m.Lsd.Dist[1];
            var win = m.ResolveLsd(out bool coin);
            string how = coin ? "두 기록이 같아 동전 던지기로 정했습니다." : $"표적에 더 가까운 {NameOf(win)}이 1엔드의 마지막 샷을 가집니다.";
            state = St.Coin;
            RenderHud();
            ShowCard(new CardSpec
            {
                Eyebrow = "라스트 스톤 드로 결과", Title = $"{NameOf(win)} 해머",
                Head = $"흑 {FmtDist(dB)} : {FmtDist(dW)} 백",
                Body = $"{how} {NameOf(Sheet.Other(win))}이 먼저 쏩니다.",
                Buttons = { new CardButton("1엔드 시작", () => { m.FinishLsd(); StartEnd(); }) }
            });
        }

        // ---- ends and shots ----

        void StartEnd()
        {
            m.StartEnd();
            hud.Toast("선공 보너스 가드", $"{Sheet.Name(Sheet.Other(m.Hammer))} 가드 1개 먼저 · {Sheet.Name(m.Hammer)} 마지막 샷", 2200, true);
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

        void RunAI()
        {
            var team = m.Cur;
            var input = AiInput.From(m, team, ai[(int)team].Value);
            var b = brain;
            aiToken = token;
            aiT0 = Time.unscaledTime;
            aiTask = Task.Run(() => b.Decide(input));
        }

        void PollAI()
        {
            if (aiTask == null || !aiTask.IsCompleted) return;
            var t = aiTask; aiTask = null;
            if (aiToken != token || state != St.Ai) return;
            if (t.IsFaulted) { Debug.LogException(t.Exception); return; }
            var d = t.Result;
            var team = m.Cur;
            int tok = token;
            m.SetLane(team, d.Lx); laneTo = d.Lx;   // slide the stone over while the AI "thinks"
            float wait = Mathf.Max(0, (1000 + (float)fx.NextDouble() * 700) / 1000f / Speed - (Time.unscaledTime - aiT0));
            After(wait, () =>
            {
                if (tok != token || state != St.Ai) return;
                m.LaunchX = d.Lx; laneTo = null;
                state = St.AiPull;
                aiPull = new AiPull { Ang = d.Ang, Pw = d.Pw, T = 0, Label = d.Label };
            });
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
                hud.Toast("쿠션권 없음", "이번 엔드의 쿠션권을 이미 썼습니다. 알을 되돌리고 방금 쏜 알을 제거합니다.", 3400);
                return;
            }
            var notes = new List<string>();
            foreach (var r in res.Removed) fades.Add(new Fade { X = (float)r.Stone.X, Y = (float)r.Stone.Y, Team = r.Stone.Team, A = 1 });
            if (res.ShotWhy == OutWhy.Hog) notes.Add("최소 라인을 넘지 못해 무효");
            else if (res.ShotWhy == OutWhy.Back) notes.Add("백 라인을 넘어가 무효");
            var knocked = new List<string>();
            foreach (var t in new[] { Team.B, Team.W }) if (res.Knocked[(int)t] > 0) knocked.Add($"{Sheet.Name(t)} {res.Knocked[(int)t]}개 아웃");
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
            int tB = m.Total(Team.B), tW = m.Total(Team.W);
            string nextLabel; Action next;
            switch (m.NextStep())
            {
                case AfterEnd.NextEnd: nextLabel = $"{m.End + 1}엔드 시작"; next = () => { m.End++; StartEnd(); }; break;
                case AfterEnd.ExtraEnd: nextLabel = "연장 엔드 시작"; next = () => { m.End++; StartEnd(); }; break;
                default: nextLabel = "최종 결과 보기"; next = GameOver; break;
            }
            string body = sc.Team.HasValue
                ? $"다음 엔드 해머: {NameOf(m.Hammer)} (점수를 내지 못한 쪽). 합계 흑 {tB} : {tW} 백"
                : $"블랭크 엔드라 해머가 {NameOf(m.Hammer)}에게 넘어갑니다. 합계 흑 {tB} : {tW} 백";
            state = St.EndCard;
            ShowCard(new CardSpec
            {
                Soft = true, Eyebrow = $"{EndLabel(m.End)} 결과", Title = sc.Team.HasValue ? $"{NameOf(sc.Team.Value)} 득점" : "블랭크 엔드",
                Head = sc.Team.HasValue ? $"{sc.Points}점" : "0 : 0", Body = body,
                Buttons = { new CardButton(nextLabel, next) }
            });
        }

        void GameOver()
        {
            state = St.Over; noAuto = true;
            int tB = m.Total(Team.B), tW = m.Total(Team.W);
            var win = m.Winner;
            RenderHud();
            ShowCard(new CardSpec
            {
                Eyebrow = "경기 종료", Title = win.HasValue ? $"{NameOf(win.Value)} 승리" : "무승부",
                Head = $"흑 {tB} : {tW} 백",
                Body = win.HasValue ? "" : "연장 엔드까지 동점으로 끝났습니다.",
                Buttons =
                {
                    new CardButton(Watching ? "다시 관전" : "다시 하기", () => StartMatch(mode, mapKey, lvB, lvW)),
                    new CardButton("메뉴로", ToMenu)
                }
            });
        }

        void ToMenu()
        {
            token++;
            state = St.Menu; drag = null; aiPull = null; measure = null; restore = null; lsdShow = null; laneTo = null;
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

        void ShowCard(CardSpec c)
        {
            bool auto = Watching && c.Buttons.Count > 0 && !noAuto;
            noAuto = false;
            int seq = hud.ShowCard(c, () => sfx.Blip(0, 0));
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
            string Hm(Team t) => hammerKnown && m.Hammer == t ? "<color=#d9a54c><size=10> 해머</size></color>" : "";
            string B(int v) => $"<color=#d9a54c><size=19><b>{v}</b></size></color>";
            int tB = m.Total(Team.B), tW = m.Total(Team.W);
            hud.SetScorePill($"흑{Hm(Team.B)} {B(tB)}<color=#b39f84> : </color>{B(tW)} 백{Hm(Team.W)}" +
                             $"<color=#b39f84><size=12>  {(state == St.Menu ? "대기" : EndLabel(m.End))} </size><size=9>▼</size></color>");

            int ends = Math.Max(Sheet.Ends, Math.Max(m.End, m.Scores[0].Count));
            var rows = new List<string[]>();
            var head = new List<string> { "" };
            for (int i = 1; i <= ends; i++) head.Add(i <= Sheet.Ends ? i.ToString() : "연장");
            head.Add("합");
            rows.Add(head.ToArray());
            foreach (var t in new[] { Team.B, Team.W })
            {
                var r = new List<string> { NameOf(t) + Hm(t) };
                for (int i = 1; i <= ends; i++) r.Add(i - 1 < m.Scores[(int)t].Count ? m.Scores[(int)t][i - 1].ToString() : "");
                r.Add(m.Total(t).ToString());
                rows.Add(r.ToArray());
            }
            hud.SetScoreTable(rows, state != St.Menu ? m.End : -1);
            RenderTurn();
        }

        void RenderTurn()
        {
            if (state == St.Menu) { hud.SetTurn(null, "<size=16><color=#efe2c8><b>대기 중</b></color></size>", "", ""); return; }
            var t = m.Cur;
            if (m.InLsd)
            {
                hud.SetTurn(t, $"<size=16><color=#efe2c8><b>{NameOf(t)} 차례</b></color></size>  라스트 스톤 드로",
                    $"흑 {FmtDist(m.Lsd.Dist[0])}    백 {FmtDist(m.Lsd.Dist[1])}", "");
                return;
            }
            string Pips(Team team)
            {
                int left = Mathf.Clamp(Sheet.Per - m.ThrownBy(team), 0, Sheet.Per);
                return "<size=10>" + new string('●', left) + new string('○', Sheet.Per - left) + "</size>";
            }
            string who = m.Shot < Sheet.Shots ? $"{NameOf(t)} 차례" : "엔드 종료";
            hud.SetTurn(t, $"<size=16><color=#efe2c8><b>{who}</b></color></size>  샷 {Math.Min(m.Shot + 1, Sheet.Shots)}/{Sheet.Shots}",
                $"흑 {Pips(Team.B)}    백 {Pips(Team.W)}",
                $"쿠션권 흑 {m.Tickets[0]} · 백 {m.Tickets[1]}");
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
                g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y - 6, Text = used ? "쿠션권 사용" : "쿠션권 없음" });
                if (used) g.RenderTurn();
            }
            public void OnCushion(Stone s, int n)
            {
                g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y, Text = $"쿠션 {n}" });
                g.sfx.Chime(n);
            }
            public void OnDeadRail(Stone s) => g.floats.Add(new Float { X = (float)s.X, Y = (float)s.Y, Text = "죽은 레일" });
        }

        // =====================================================================
        // loop
        // =====================================================================

        void Update()
        {
            float rdt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            float dt = rdt * Speed;   // game clock (physics, animations); the camera keeps real time

            for (int i = 0; i < timers.Count; i++)
                if (Time.unscaledTime >= timers[i].at) { var a = timers[i].a; timers.RemoveAt(i--); a(); }
            PollAI();
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
            if (state == St.Moving || state == St.Settle) RenderTurn();

            Resize(false);
            UpdateCam(rdt * Mathf.Sqrt(Speed));
            ApplyCamera();
            hud.Tick(px, cw, ch);
            Draw(Time.unscaledTime * 1000);
        }
    }
}
