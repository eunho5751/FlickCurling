using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FlickCurling.Core
{
    /// <summary>
    /// Everything a match is at one moment (stones, end, shot, hammer, scores, tickets, the shot in flight's flags, the
    /// turn order shot, the random state), so it can be sent to another device or kept and put back exactly: online, the
    /// device that threw a shot sends the state the stones stopped in, and a player coming back gets the whole match.
    /// Doubles travel as their exact bits.
    /// </summary>
    public sealed class MatchState
    {
        public List<Stone> Stones = new List<Stone>();
        public List<Stone> Snapshot = new List<Stone>();
        public int NextId, End, Shot;
        public Team Hammer, Cur;
        public int[] ScoresB = new int[0], ScoresW = new int[0];
        public int[] Tickets = new int[2];
        public double[] Lane = new double[2];
        public double LaunchX;
        public int ShotId, Cushions;
        public bool ShotHitStone, WallTouched, Foul;
        public bool InLsd;
        public int LsdIdx;
        public double?[] LsdDist = new double?[2];
        public double[] LsdRestX = new double[2], LsdRestY = new double[2];
        public ulong RngState;
        // item mode
        public List<ItemBox> Boxes = new List<ItemBox>(), BoxSnapshot = new List<ItemBox>();
        public ItemKind[] Held = new ItemKind[2 * Sheet.ItemSlots], HeldSnapshot = new ItemKind[2 * Sheet.ItemSlots];   // B's hands, then W's
        public ItemKind Armed;
        public int NextBoxId;

        const int Version = 5;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                Write(w);
                return ms.ToArray();
            }
        }

        public static MatchState FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms)) return Read(r);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Version);
            WriteStones(w, Stones); WriteStones(w, Snapshot);
            w.Write(NextId); w.Write(End); w.Write(Shot);
            w.Write((byte)Hammer); w.Write((byte)Cur);
            WriteInts(w, ScoresB); WriteInts(w, ScoresW); WriteInts(w, Tickets);
            w.Write(Lane[0]); w.Write(Lane[1]); w.Write(LaunchX);
            w.Write(ShotId); w.Write(Cushions);
            w.Write(ShotHitStone); w.Write(WallTouched); w.Write(Foul);
            w.Write(InLsd); w.Write(LsdIdx);
            for (int i = 0; i < 2; i++) { w.Write(LsdDist[i].HasValue); w.Write(LsdDist[i] ?? 0); w.Write(LsdRestX[i]); w.Write(LsdRestY[i]); }
            w.Write(RngState);
            WriteBoxes(w, Boxes); WriteBoxes(w, BoxSnapshot);
            foreach (var k in Held) w.Write((byte)k);
            foreach (var k in HeldSnapshot) w.Write((byte)k);
            w.Write((byte)Armed); w.Write(NextBoxId);
        }

        public static MatchState Read(BinaryReader r)
        {
            int v = r.ReadInt32();
            if (v != Version) throw new InvalidDataException("match state version " + v);
            var s = new MatchState();
            s.Stones = ReadStones(r); s.Snapshot = ReadStones(r);
            s.NextId = r.ReadInt32(); s.End = r.ReadInt32(); s.Shot = r.ReadInt32();
            s.Hammer = (Team)r.ReadByte(); s.Cur = (Team)r.ReadByte();
            s.ScoresB = ReadInts(r); s.ScoresW = ReadInts(r); s.Tickets = ReadInts(r);
            s.Lane = new[] { r.ReadDouble(), r.ReadDouble() }; s.LaunchX = r.ReadDouble();
            s.ShotId = r.ReadInt32(); s.Cushions = r.ReadInt32();
            s.ShotHitStone = r.ReadBoolean(); s.WallTouched = r.ReadBoolean(); s.Foul = r.ReadBoolean();
            s.InLsd = r.ReadBoolean(); s.LsdIdx = r.ReadInt32();
            for (int i = 0; i < 2; i++)
            {
                bool has = r.ReadBoolean(); double d = r.ReadDouble(); s.LsdDist[i] = has ? d : (double?)null;
                s.LsdRestX[i] = r.ReadDouble(); s.LsdRestY[i] = r.ReadDouble();
            }
            s.RngState = r.ReadUInt64();
            s.Boxes = ReadBoxes(r); s.BoxSnapshot = ReadBoxes(r);
            for (int i = 0; i < s.Held.Length; i++) s.Held[i] = (ItemKind)r.ReadByte();
            for (int i = 0; i < s.HeldSnapshot.Length; i++) s.HeldSnapshot[i] = (ItemKind)r.ReadByte();
            s.Armed = (ItemKind)r.ReadByte(); s.NextBoxId = r.ReadInt32();
            return s;
        }

        static void WriteStones(BinaryWriter w, List<Stone> list)
        {
            w.Write(list.Count);
            foreach (var s in list)
            {
                w.Write(s.Id); w.Write((byte)s.Team);
                w.Write(s.X); w.Write(s.Y); w.Write(s.Vx); w.Write(s.Vy);
                w.Write(s.Live); w.Write(s.Flew); w.Write(s.Placed);
                w.Write(s.Size); w.Write(s.Bomb); w.Write(s.Magnet);
            }
        }

        static void WriteBoxes(BinaryWriter w, List<ItemBox> list)
        {
            w.Write(list.Count);
            foreach (var b in list) { w.Write(b.Id); w.Write((byte)b.Team); w.Write(b.X); w.Write(b.Y); }
        }

        static List<ItemBox> ReadBoxes(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<ItemBox>(n);
            for (int i = 0; i < n; i++) list.Add(new ItemBox { Id = r.ReadInt32(), Team = (Team)r.ReadByte(), X = r.ReadDouble(), Y = r.ReadDouble() });
            return list;
        }

        static List<Stone> ReadStones(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<Stone>(n);
            for (int i = 0; i < n; i++)
                list.Add(new Stone
                {
                    Id = r.ReadInt32(), Team = (Team)r.ReadByte(),
                    X = r.ReadDouble(), Y = r.ReadDouble(), Vx = r.ReadDouble(), Vy = r.ReadDouble(),
                    Live = r.ReadBoolean(), Flew = r.ReadBoolean(), Placed = r.ReadBoolean(),
                    Size = r.ReadDouble(), Bomb = r.ReadBoolean(), Magnet = r.ReadBoolean(),
                });
            return list;
        }

        static void WriteInts(BinaryWriter w, int[] a) { w.Write(a.Length); foreach (int x in a) w.Write(x); }

        static int[] ReadInts(BinaryReader r)
        {
            var a = new int[r.ReadInt32()];
            for (int i = 0; i < a.Length; i++) a[i] = r.ReadInt32();
            return a;
        }
    }

    public sealed partial class Match
    {
        /// <summary>The match as it is now (a deep copy).</summary>
        public MatchState Capture() => new MatchState
        {
            Stones = Stones.Select(s => s.Clone()).ToList(),
            Snapshot = Snapshot.Select(s => s.Clone()).ToList(),
            NextId = nextId, End = End, Shot = Shot, Hammer = Hammer, Cur = Cur,
            ScoresB = Scores[0].ToArray(), ScoresW = Scores[1].ToArray(),
            Tickets = (int[])Tickets.Clone(), Lane = (double[])Lane.Clone(), LaunchX = LaunchX,
            ShotId = ShotId, Cushions = Cushions, ShotHitStone = ShotHitStone, WallTouched = WallTouched, Foul = Foul,
            InLsd = InLsd,
            LsdIdx = Lsd?.Idx ?? 0,
            LsdDist = Lsd != null ? (double?[])Lsd.Dist.Clone() : new double?[2],
            LsdRestX = Lsd != null ? (double[])Lsd.RestX.Clone() : new double[2],
            LsdRestY = Lsd != null ? (double[])Lsd.RestY.Clone() : new double[2],
            RngState = rng.State,
            Boxes = Boxes.Select(b => b.Clone()).ToList(), BoxSnapshot = boxSnapshot.Select(b => b.Clone()).ToList(),
            Held = Held[0].Concat(Held[1]).ToArray(), HeldSnapshot = heldSnapshot[0].Concat(heldSnapshot[1]).ToArray(),
            Armed = Armed, NextBoxId = nextBoxId,
        };

        /// <summary>Puts the match back to <paramref name="s"/> exactly (the field stays this match's own).</summary>
        public void Restore(MatchState s)
        {
            Stones = s.Stones.Select(x => x.Clone()).ToList();
            Snapshot = s.Snapshot.Select(x => x.Clone()).ToList();
            nextId = s.NextId; End = s.End; Shot = s.Shot; Hammer = s.Hammer; Cur = s.Cur;
            Scores[0].Clear(); Scores[0].AddRange(s.ScoresB);
            Scores[1].Clear(); Scores[1].AddRange(s.ScoresW);
            Array.Copy(s.Tickets, Tickets, Tickets.Length);
            Array.Copy(s.Lane, Lane, Lane.Length);
            LaunchX = s.LaunchX;
            ShotId = s.ShotId; Cushions = s.Cushions; ShotHitStone = s.ShotHitStone; WallTouched = s.WallTouched; Foul = s.Foul;
            InLsd = s.InLsd;
            if (s.InLsd)
            {
                Lsd = NewLsd();
                Lsd.Idx = s.LsdIdx;
                Array.Copy(s.LsdDist, Lsd.Dist, 2);
                Array.Copy(s.LsdRestX, Lsd.RestX, 2); Array.Copy(s.LsdRestY, Lsd.RestY, 2);
            }
            else Lsd = null;
            rng.State = s.RngState;
            Boxes = s.Boxes.Select(b => b.Clone()).ToList();
            boxSnapshot = s.BoxSnapshot.Select(b => b.Clone()).ToList();
            for (int t = 0; t < 2; t++)
            {
                Array.Copy(s.Held, t * Sheet.ItemSlots, Held[t], 0, Sheet.ItemSlots);
                Array.Copy(s.HeldSnapshot, t * Sheet.ItemSlots, heldSnapshot[t], 0, Sheet.ItemSlots);
            }
            Armed = s.Armed; nextBoxId = s.NextBoxId;
        }
    }
}
