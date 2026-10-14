using System;
using System.Collections.Generic;
using System.Linq;
using FlickCurling.Core;
using NUnit.Framework;

namespace FlickCurling.Tests
{
    public class PhysicsTests
    {
        static Stone Shot(double ang, double pw, double x = 50, int id = 1, Team t = Team.B) =>
            new Stone { Id = id, Team = t, X = x, Y = Sheet.LaunchY, Vx = Math.Sin(ang) * pw * 197, Vy = Math.Cos(ang) * pw * 197 };

        static Stone At(double x, double y, int id, Team t = Team.W) => new Stone { Id = id, Team = t, X = x, Y = y };

        [Test]
        public void SameInputGivesBitIdenticalResult()
        {
            var field = Maps.Build("bumper");
            List<Stone> Run()
            {
                var st = new List<Stone> { At(50, 130, 1), At(44, 170, 2, Team.B), At(58, 176, 3), Shot(0.03, 0.92, 35, 4), };
                Sim.Simulate(st, field, PhysicsParams.Default);
                var st2 = st.Where(s => s.Live).ToList();
                st2.Add(Shot(-0.12, 1, 65, 5));
                Sim.Simulate(st2, field, PhysicsParams.Default);
                return st2;
            }
            var a = Run(); var b = Run();
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].X, b[i].X, 0.0);
                Assert.AreEqual(a[i].Y, b[i].Y, 0.0);
                Assert.AreEqual(a[i].Live, b[i].Live);
            }
        }

        [Test]
        public void SeventyPercentStopsOnTheTee_FullPowerPassesBackLine()
        {
            var field = Maps.Build("base");
            var s = new List<Stone> { Shot(0, 0.7) };
            Sim.Simulate(s, field, PhysicsParams.Default);
            Assert.AreEqual(Sheet.TeeY, s[0].Y, 3.0);
            Assert.IsFalse(s[0].IsMoving);
            var f = new List<Stone> { Shot(0, 1) };
            Sim.Simulate(f, field, PhysicsParams.Default);
            Assert.AreEqual(OutWhy.Back, Rules.OutReason(f[0], f[0].Id));
        }

        [Test]
        public void HeadOnHitLeavesShooterNearlyInPlace()
        {
            var st = new List<Stone> { At(50, 80, 1), Shot(0, 0.65, 50, 2) };
            Sim.Simulate(st, Maps.Build("base"), PhysicsParams.Default);
            var target = st[0]; var shooter = st[1];
            Assert.Less(shooter.Y, 80, "shooter stays behind the struck stone's old spot");
            Assert.Greater(target.Y, 120, "struck stone carries the speed");
        }

        [Test]
        public void LivelyRailBouncesHarderThanDeadRail()
        {
            var field = Maps.Build("base");
            // straight into the left wall low (lively) and high (dead), same incoming speed
            var low = new Stone { Id = 1, X = 20, Y = 40, Vx = -60, Vy = 0 };
            var high = new Stone { Id = 2, X = 20, Y = 150, Vx = -60, Vy = 0 };
            var a = new List<Stone> { low }; var b = new List<Stone> { high };
            for (int i = 0; i < 180 && low.Vx <= 0; i++) Sim.Step(a, field, PhysicsParams.Default);
            for (int i = 0; i < 180 && high.Vx <= 0; i++) Sim.Step(b, field, PhysicsParams.Default);
            Assert.Greater(low.Vx, 0); Assert.Greater(high.Vx, 0);
            Assert.AreEqual(0.75 / 0.15, low.Vx / high.Vx, 0.05);
        }

        [Test]
        public void RailWindowsSplitAtLiveTop()
        {
            var f = Maps.Build("base");
            Assert.AreEqual(5, f.Walls.Length);
            Assert.IsTrue(f.Walls.Any(w => w.IsSide && w.X1 == 0 && !w.Dead && w.Y1 == 0 && w.Y2 == 80));
            Assert.IsTrue(f.Walls.Any(w => w.IsSide && w.X1 == 100 && w.Dead && w.Y1 == 80));
            Assert.IsTrue(Maps.Build("kink").Walls.Where(w => !w.IsSide && !w.IsBottom).All(w => w.Dead));
        }
    }

    public class RulesTests
    {
        static Stone At(double x, double y, int id, Team t) => new Stone { Id = id, Team = t, X = x, Y = y };

        [Test]
        public void MinLineOnlyAppliesToTheDeliveredStone()
        {
            var shortShot = At(50, 50, 7, Team.B);
            var knockedBack = At(50, 50, 3, Team.W);
            Assert.AreEqual(OutWhy.Hog, Rules.OutReason(shortShot, 7));
            Assert.AreEqual(OutWhy.None, Rules.OutReason(knockedBack, 7));
            // completely over: y - 4 must reach 64
            Assert.AreEqual(OutWhy.Hog, Rules.OutReason(At(50, 67.9, 7, Team.B), 7));
            Assert.AreEqual(OutWhy.None, Rules.OutReason(At(50, 68, 7, Team.B), 7));
        }

        [Test]
        public void BackLineRemovesAnyStoneFullyOver()
        {
            Assert.AreEqual(OutWhy.Back, Rules.OutReason(At(50, 204.1, 3, Team.W), 7));
            Assert.AreEqual(OutWhy.None, Rules.OutReason(At(50, 204, 3, Team.W), 7));
            var removed = Rules.Judge(new List<Stone> { At(50, 210, 1, Team.B), At(50, 172, 2, Team.W), At(50, 40, 3, Team.B) }, 3);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, removed.Select(r => r.Stone.Id));
        }

        [Test]
        public void ScoringCountsStonesInsideOpponentsBest()
        {
            var st = new List<Stone>
            {
                At(50, 172, 1, Team.B), At(50, 180, 2, Team.B), At(50, 190, 3, Team.W), At(50, 195, 4, Team.B),
                At(50, 140, 5, Team.B) // touches the house edge exactly? 32 away -> counts as candidate but beyond W's best
            };
            var sc = Rules.Scoring(st);
            Assert.AreEqual(Team.B, sc.Team);
            Assert.AreEqual(2, sc.Points);
            CollectionAssert.AreEqual(new[] { 1, 2 }, sc.Ids);
        }

        [Test]
        public void HouseEdgeAndBlankAndTie()
        {
            Assert.IsNull(Rules.Scoring(new List<Stone> { At(50, 172 + 32.01, 1, Team.B) }).Team, "not touching the house");
            Assert.AreEqual(Team.B, Rules.Scoring(new List<Stone> { At(50, 172 - 32, 1, Team.B) }).Team, "touching the outer ring counts");
            Assert.IsNull(Rules.Scoring(new List<Stone>()).Team);
            var tie = Rules.Scoring(new List<Stone> { At(40, 172, 1, Team.B), At(60, 172, 2, Team.W) });
            Assert.IsNull(tie.Team);
            Assert.IsTrue(tie.Tie);
        }
    }

    public class MatchTests
    {
        static Match NewEnd(Team hammer = Team.W, string map = "base")
        {
            var m = new Match(Maps.Build(map), PhysicsParams.Default, 1) { Hammer = hammer };
            m.StartEnd();
            m.BeginShot();
            return m;
        }

        [Test]
        public void EndStartsWithBonusGuardForTheSideWithoutHammer()
        {
            var m = NewEnd(Team.W);
            Assert.AreEqual(1, m.Stones.Count);
            Assert.AreEqual(Team.B, m.Stones[0].Team);
            Assert.AreEqual(50, m.Stones[0].X); Assert.AreEqual(130, m.Stones[0].Y);
            Assert.AreEqual(Team.B, m.Cur, "the side without hammer throws first");
            CollectionAssert.AreEqual(new[] { 2, 2 }, m.Tickets);
            Assert.AreEqual(0, m.ThrownBy(Team.B), "the bonus guard is not one of the five");
        }

        [Test]
        public void DirectCushionUsesOneTicketPerShot()
        {
            var m = NewEnd();
            m.Release(-Math.PI / 4, 0.75);   // straight at the left rail, low (lively): may bounce more than once
            m.RunToRest();
            Assert.AreEqual(1, m.Tickets[(int)Team.B]);
            Assert.AreEqual(2, m.Tickets[(int)Team.W]);
            Assert.IsFalse(m.ResolveShot().Foul);
        }

        [Test]
        public void WallAfterHittingAStoneIsNotACushion()
        {
            var m = NewEnd();
            m.Stones.Add(new Stone { Id = 99, Team = Team.W, X = 42, Y = 22 });
            m.Snapshot = m.Stones.Select(s => s.Clone()).ToList();
            m.Release(-Math.PI / 4, 0.75);
            m.RunToRest();
            Assert.IsTrue(m.ShotHitStone);
            Assert.AreEqual(2, m.Tickets[(int)Team.B]);
        }

        [Test]
        public void CushionWithoutTicketFoulsAndRewinds()
        {
            var m = NewEnd();
            m.Tickets[(int)Team.B] = 0;
            m.Release(-Math.PI / 4, 0.75);
            m.RunToRest();
            var o = m.ResolveShot();
            Assert.IsTrue(o.Foul);
            m.ApplyRestore();
            Assert.AreEqual(1, m.Stones.Count, "only the shot stone is gone");
            Assert.AreEqual(130, m.Stones[0].Y);
            Assert.IsTrue(m.Stones.All(s => !s.IsMoving));
        }

        [Test]
        public void ShortShotIsRemovedAndKnockedOutStonesCounted()
        {
            var m = NewEnd();
            m.Release(0, 0.3);
            m.RunToRest();
            var o = m.ResolveShot();
            Assert.AreEqual(OutWhy.Hog, o.ShotWhy);
            Assert.AreEqual(1, m.Stones.Count);

            var m2 = NewEnd();
            m2.Release(0, 1);   // full power through the guard: both leave over the back line
            m2.RunToRest();
            var o2 = m2.ResolveShot();
            Assert.AreEqual(1, o2.Knocked[(int)Team.B]);
        }

        [Test]
        public void HammerGoesToTheSideThatDidNotScore_BlankPassesIt()
        {
            var m = NewEnd(Team.W);
            m.Stones = new List<Stone> { new Stone { Id = 1, Team = Team.W, X = 50, Y = 172 } };
            var eo = m.FinishEnd();
            Assert.AreEqual(Team.W, eo.Score.Team);
            Assert.AreEqual(Team.B, m.Hammer);
            CollectionAssert.AreEqual(new[] { 0 }, m.Scores[0]);
            CollectionAssert.AreEqual(new[] { 1 }, m.Scores[1]);

            m.Stones = new List<Stone>();
            m.FinishEnd();
            Assert.AreEqual(Team.W, m.Hammer, "blank end passes the hammer (mixed doubles rule)");

            var m3 = NewEnd(Team.W);
            m3.Stones = new List<Stone> { new Stone { Id = 1, Team = Team.B, X = 50, Y = 172 } };
            m3.FinishEnd();
            Assert.AreEqual(Team.W, m3.Hammer, "steal: the side that was stolen from keeps hammer");
        }

        [Test]
        public void TiedAfterFourEndsPlaysOneExtraEnd()
        {
            var m = new Match(Maps.Build("base"));
            for (int e = 1; e <= 4; e++) { m.End = e; m.Stones = new List<Stone>(); m.FinishEnd(); }
            Assert.AreEqual(AfterEnd.ExtraEnd, m.NextStep());
            m.End = 5; m.FinishEnd();
            Assert.AreEqual(AfterEnd.GameOver, m.NextStep());
            Assert.IsNull(m.Winner);
        }

        [Test]
        public void LastStoneDrawCloserTakesHammer()
        {
            var m = new Match(Maps.Build("base"), null, 3);
            m.StartMatch();
            Assert.IsTrue(m.InLsd);
            Assert.LessOrEqual(Sheet.Dist(m.Lsd.TargetX, m.Lsd.TargetY, Sheet.TeeX, Sheet.TeeY), Sheet.R - Sheet.SR - 2 + 1e-9);
            m.Lsd.Dist[0] = 5; m.Lsd.Dist[1] = 3;
            Assert.AreEqual(Team.W, m.ResolveLsd(out bool coin));
            Assert.IsFalse(coin);
            Assert.AreEqual(Team.B, m.Cur);
            m.Lsd.Dist[0] = double.PositiveInfinity; m.Lsd.Dist[1] = double.PositiveInfinity;
            m.ResolveLsd(out coin);
            Assert.IsTrue(coin);
        }
    }

    public class AiTests
    {
        static AiInput Input(AiLevel lvl, int shot = 3)
        {
            var m = new Match(Maps.Build("base"), null, 1) { Hammer = Team.B };
            m.StartEnd();
            m.Stones.Add(new Stone { Id = 50, Team = Team.B, X = 47, Y = 175 });
            m.Shot = shot;
            m.BeginShot();
            return AiInput.From(m, m.Cur, lvl);
        }

        [Test]
        public void SameSeedSameDecision()
        {
            var a = new Ai(42).Decide(Input(AiLevel.Normal));
            var b = new Ai(42).Decide(Input(AiLevel.Normal));
            Assert.AreEqual(a.Ang, b.Ang, 0.0);
            Assert.AreEqual(a.Pw, b.Pw, 0.0);
            Assert.AreEqual(a.Lx, b.Lx);
        }

        [Test]
        public void EveryLevelReturnsAPlayableShot([Values] AiLevel lvl)
        {
            var d = new Ai(7).Decide(Input(lvl, 9));   // last shot of the end: win-probability evaluation
            Assert.That(d.Pw, Is.InRange(0.1, 1.0));
            Assert.Less(Math.Abs(d.Ang), 1.6);
            CollectionAssert.Contains(Sheet.Lanes, d.Lx);
        }
    }
}
