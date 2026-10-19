using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FlickCurling.Core
{
    public enum ShotEventKind : byte { StoneHit, PostHit, WallHit, Ticket, Cushion, DeadRail, Flew }

    /// <summary>Something that happened during a recorded shot, at a step of the simulation (1/180 s each).</summary>
    public struct ShotEvent
    {
        public int Step;
        public ShotEventKind Kind;
        public int A, B;          // stone ids (B: the other stone of a hit)
        public double Value;      // impulse / speed into the wall or post / cushion number / 1 = ticket used
        public Seg Wall;          // the wall of a WallHit
    }

    /// <summary>One stone's path: its position every few steps from the sample it first moved in, in 0.01 units.</summary>
    public sealed class StoneTrack
    {
        public int StoneId;
        public int FirstSample;               // before this sample the stone sat where the shot found it
        public List<short> X = new List<short>(), Y = new List<short>();

        public const double Unit = 0.01;
        public static short Pack(double v) => (short)Math.Round(Sheet.Clamp(v, -300, 320) / Unit);
        public static double Unpack(short v) => v * Unit;
        public int LastSample => FirstSample + X.Count - 1;
    }

    /// <summary>
    /// A shot as one device simulated it, for every device to play back the same: each moving stone's path (sampled
    /// every <see cref="SampleEvery"/> steps), the events along the way, and the match exactly as the stones stopped
    /// (<see cref="After"/>), from which play goes on. Nobody but the recording device runs the physics, so devices whose
    /// floating point differs still show the same shot.
    /// </summary>
    public sealed class ShotRecord
    {
        public int StepCount;
        public int SampleEvery = 6;
        public List<StoneTrack> Tracks = new List<StoneTrack>();
        public List<ShotEvent> Events = new List<ShotEvent>();
        public MatchState After;

        /// <summary>How long it plays, in seconds.</summary>
        public double Duration => StepCount * Sheet.DT;

        // ---- recording ----

        /// <summary>
        /// Runs the released shot on <paramref name="m"/> to rest and records it. The match's own listener hears nothing
        /// now; playback tells it the events at their time.
        /// </summary>
        public static ShotRecord Record(Match m, int sampleEvery = 6)
        {
            var rec = new ShotRecord { SampleEvery = sampleEvery };
            var catcher = new Catcher(rec);
            var listener = m.Listener;
            m.Listener = catcher;
            var start = m.Stones.ToDictionary(s => s.Id, s => (s.X, s.Y));
            var tracks = new Dictionary<int, StoneTrack>();
            var live = m.Stones.ToDictionary(s => s.Id, s => s.Live);
            try
            {
                int step = 0;
                Sample(0);
                while (step < Sim.MaxSteps && m.Moving)
                {
                    catcher.Step = step + 1;
                    m.Step();
                    step++;
                    foreach (var s in m.Stones)
                        if (live.TryGetValue(s.Id, out bool was) && was && !s.Live)
                        {
                            live[s.Id] = false;
                            if (s.Flew) rec.Events.Add(new ShotEvent { Step = step, Kind = ShotEventKind.Flew, A = s.Id });
                        }
                    if (step % sampleEvery == 0) Sample(step / sampleEvery);
                }
                // the moment it stopped, on the next sample mark, so playback ends exactly there
                int last = (step + sampleEvery - 1) / sampleEvery;
                if (step % sampleEvery != 0) Sample(last);
                rec.StepCount = step;

                void Sample(int k)
                {
                    foreach (var s in m.Stones)
                    {
                        var (x0, y0) = start.TryGetValue(s.Id, out var p) ? p : (s.X, s.Y);
                        bool moved = tracks.ContainsKey(s.Id) || s.X != x0 || s.Y != y0 || s.IsMoving;
                        if (!moved) continue;
                        if (!tracks.TryGetValue(s.Id, out var t))
                        {
                            t = new StoneTrack { StoneId = s.Id, FirstSample = k };
                            tracks[s.Id] = t;
                            rec.Tracks.Add(t);
                        }
                        t.X.Add(StoneTrack.Pack(s.X)); t.Y.Add(StoneTrack.Pack(s.Y));
                    }
                }
            }
            finally { m.Listener = listener; }
            rec.After = m.Capture();
            return rec;
        }

        sealed class Catcher : IMatchEvents
        {
            readonly ShotRecord rec;
            public int Step;
            public Catcher(ShotRecord rec) { this.rec = rec; }
            void Add(ShotEventKind k, int a, int b = 0, double v = 0, Seg wall = default) =>
                rec.Events.Add(new ShotEvent { Step = Step, Kind = k, A = a, B = b, Value = v, Wall = wall });
            public void OnStoneHit(Stone a, Stone b, double j) => Add(ShotEventKind.StoneHit, a.Id, b.Id, j);
            public void OnPostHit(Stone s, double vn) => Add(ShotEventKind.PostHit, s.Id, 0, vn);
            public void OnWallHit(Stone s, double vn, Seg g) => Add(ShotEventKind.WallHit, s.Id, 0, vn, g);
            public void OnTicket(Stone s, bool used) => Add(ShotEventKind.Ticket, s.Id, 0, used ? 1 : 0);
            public void OnCushion(Stone s, int n) => Add(ShotEventKind.Cushion, s.Id, 0, n);
            public void OnDeadRail(Stone s) => Add(ShotEventKind.DeadRail, s.Id);
        }

        // ---- bytes ----

        const int Version = 1;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version);
                w.Write(StepCount); w.Write(SampleEvery);
                w.Write(Tracks.Count);
                foreach (var t in Tracks)
                {
                    w.Write(t.StoneId); w.Write(t.FirstSample); w.Write(t.X.Count);
                    for (int i = 0; i < t.X.Count; i++) { w.Write(t.X[i]); w.Write(t.Y[i]); }
                }
                w.Write(Events.Count);
                foreach (var e in Events)
                {
                    w.Write(e.Step); w.Write((byte)e.Kind); w.Write(e.A); w.Write(e.B); w.Write(e.Value);
                    w.Write(e.Wall.X1); w.Write(e.Wall.Y1); w.Write(e.Wall.X2); w.Write(e.Wall.Y2); w.Write(e.Wall.Dead);
                }
                After.Write(w);
                return ms.ToArray();
            }
        }

        public static ShotRecord FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                int v = r.ReadInt32();
                if (v != Version) throw new InvalidDataException("shot record version " + v);
                var rec = new ShotRecord { StepCount = r.ReadInt32(), SampleEvery = r.ReadInt32() };
                int nt = r.ReadInt32();
                for (int i = 0; i < nt; i++)
                {
                    var t = new StoneTrack { StoneId = r.ReadInt32(), FirstSample = r.ReadInt32() };
                    int n = r.ReadInt32();
                    for (int k = 0; k < n; k++) { t.X.Add(r.ReadInt16()); t.Y.Add(r.ReadInt16()); }
                    rec.Tracks.Add(t);
                }
                int ne = r.ReadInt32();
                for (int i = 0; i < ne; i++)
                    rec.Events.Add(new ShotEvent
                    {
                        Step = r.ReadInt32(), Kind = (ShotEventKind)r.ReadByte(), A = r.ReadInt32(), B = r.ReadInt32(), Value = r.ReadDouble(),
                        Wall = new Seg(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadBoolean()),
                    });
                rec.After = MatchState.Read(r);
                return rec;
            }
        }
    }

    /// <summary>
    /// Plays a <see cref="ShotRecord"/> on a match that is where the shot started (its stone released): moves the stones
    /// along their paths, tells the match's listener the events at their step, and at the end puts the match exactly
    /// where the recording device's stopped.
    /// </summary>
    public sealed class ShotPlayback
    {
        readonly Match m;
        readonly ShotRecord rec;
        readonly Dictionary<int, (double x, double y)> start;
        int nextEvent;
        double step;

        public ShotPlayback(Match m, ShotRecord rec)
        {
            this.m = m; this.rec = rec;
            start = m.Stones.ToDictionary(s => s.Id, s => (s.X, s.Y));
        }

        public bool Done { get; private set; }

        /// <summary>Plays on by <paramref name="seconds"/>; at the end the match takes the recorded state.</summary>
        public void Advance(double seconds)
        {
            if (Done) return;
            step = Math.Min(rec.StepCount, step + seconds / Sheet.DT);
            var byId = m.Stones.ToDictionary(s => s.Id);
            double k = step / rec.SampleEvery;
            foreach (var t in rec.Tracks)
            {
                if (!byId.TryGetValue(t.StoneId, out var s)) continue;
                var (px, py) = Position(t, k);
                var (qx, qy) = Position(t, k + 0.25);
                s.X = px; s.Y = py;
                // a speed for whatever looks at it (moving stones, trails); the recorded state replaces it at the end
                double dt = 0.25 * rec.SampleEvery * Sheet.DT;
                s.Vx = (qx - px) / dt; s.Vy = (qy - py) / dt;
            }
            while (nextEvent < rec.Events.Count && rec.Events[nextEvent].Step <= step)
                Fire(rec.Events[nextEvent++], byId);
            if (step >= rec.StepCount)
            {
                while (nextEvent < rec.Events.Count) Fire(rec.Events[nextEvent++], byId);
                m.Restore(rec.After);
                Done = true;
            }
        }

        (double, double) Position(StoneTrack t, double k)
        {
            var (x0, y0) = start.TryGetValue(t.StoneId, out var p) ? p : (StoneTrack.Unpack(t.X[0]), StoneTrack.Unpack(t.Y[0]));
            (double, double) At(int i) =>
                i < t.FirstSample ? (x0, y0) : i > t.LastSample ? At(t.LastSample)
                : (StoneTrack.Unpack(t.X[i - t.FirstSample]), StoneTrack.Unpack(t.Y[i - t.FirstSample]));
            int a = (int)Math.Floor(k);
            double f = k - a;
            var (ax, ay) = At(a);
            var (bx, by) = At(a + 1);
            return (ax + (bx - ax) * f, ay + (by - ay) * f);
        }

        void Fire(ShotEvent e, Dictionary<int, Stone> byId)
        {
            var l = m.Listener;
            if (!byId.TryGetValue(e.A, out var a)) return;
            switch (e.Kind)
            {
                case ShotEventKind.StoneHit: if (byId.TryGetValue(e.B, out var b)) l?.OnStoneHit(a, b, e.Value); break;
                case ShotEventKind.PostHit: l?.OnPostHit(a, e.Value); break;
                case ShotEventKind.WallHit: l?.OnWallHit(a, e.Value, e.Wall); break;
                case ShotEventKind.Ticket: l?.OnTicket(a, e.Value > 0); break;
                case ShotEventKind.Cushion: l?.OnCushion(a, (int)e.Value); break;
                case ShotEventKind.DeadRail: l?.OnDeadRail(a); break;
                case ShotEventKind.Flew: a.Live = false; a.Flew = true; a.Vx = 0; a.Vy = 0; break;
            }
        }
    }
}
