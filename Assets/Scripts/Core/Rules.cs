using System;
using System.Collections.Generic;
using System.Linq;

namespace FlickCurling.Core
{
    public enum OutWhy { None, Hog, Back, Near }   // Near: off the sheet at the launch end

    public readonly struct Removal
    {
        public readonly Stone Stone;
        public readonly OutWhy Why;
        public Removal(Stone s, OutWhy w) { Stone = s; Why = w; }
    }

    public readonly struct Ranked
    {
        public readonly Stone Stone;
        public readonly double D;
        public Ranked(Stone s, double d) { Stone = s; D = d; }
    }

    public sealed class ScoreResult
    {
        public Team? Team;
        public int Points;
        public List<Ranked> List;   // stones touching the house, nearest first
        public List<int> Ids;       // counting stones
        public bool Tie;
    }

    public static class Rules
    {
        // The min-line rule applies only to the stone just delivered; a stone already in play that is knocked back stays.
        public static OutWhy OutReason(Stone s, int deliveredId)
        {
            if (s.Flew && s.Y < Sheet.Hog) return OutWhy.Near;   // went off the launch end
            if (s.Flew || s.Y - Sheet.SR > Sheet.Back) return OutWhy.Back;
            if (s.Id == deliveredId && s.Y - Sheet.SR < Sheet.Hog) return OutWhy.Hog;
            return OutWhy.None;
        }

        /// <summary>Judge after every stone has stopped. Pure: does not mutate.</summary>
        public static List<Removal> Judge(List<Stone> stones, int deliveredId)
        {
            var removed = new List<Removal>();
            foreach (var s in stones)
            {
                if (!s.Live && !s.Flew) continue;
                var why = OutReason(s, deliveredId);
                if (why != OutWhy.None) removed.Add(new Removal(s, why));
            }
            return removed;
        }

        public static ScoreResult Scoring(List<Stone> stones)
        {
            var list = stones.Where(s => s.Live).Select(s => new Ranked(s, Sheet.DistTee(s)))
                .Where(o => o.D <= Sheet.R + Sheet.SR).OrderBy(o => o.D).ToList();
            var res = new ScoreResult { List = list, Ids = new List<int>() };
            if (list.Count == 0) return res;
            var team = list[0].Stone.Team;
            int oi = list.FindIndex(o => o.Stone.Team != team);
            if (oi >= 0 && Math.Abs(list[oi].D - list[0].D) < 1e-6) { res.Tie = true; return res; }
            double lim = oi >= 0 ? list[oi].D : double.PositiveInfinity;
            foreach (var o in list) if (o.Stone.Team == team && o.D < lim) res.Ids.Add(o.Stone.Id);
            res.Team = team;
            res.Points = res.Ids.Count;
            return res;
        }
    }
}
