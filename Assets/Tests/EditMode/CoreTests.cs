using System;
using System.Collections.Generic;
using System.Linq;
using FlickCurling.Core;
using NUnit.Framework;

namespace FlickCurling.Tests
{
    // the rules and physics as they ship, whatever a GameConfig the editor applied last left in the statics
    [SetUpFixture]
    public class ShippedRules
    {
        [OneTimeSetUp]
        public void Defaults()
        {
            Sheet.Configure();
            PhysicsParams.Configure();
        }
    }

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
        public void UpperRailBouncesLikeLowerRail()
        {
            var field = Maps.Build("base");
            // straight into the left wall low and high: the whole rail is lively, so both come back at e = 0.75
            var low = new Stone { Id = 1, X = 20, Y = 40, Vx = -60, Vy = 0 };
            var high = new Stone { Id = 2, X = 20, Y = 150, Vx = -60, Vy = 0 };
            var a = new List<Stone> { low }; var b = new List<Stone> { high };
            for (int i = 0; i < 180 && low.Vx <= 0; i++) Sim.Step(a, field, PhysicsParams.Default);
            for (int i = 0; i < 180 && high.Vx <= 0; i++) Sim.Step(b, field, PhysicsParams.Default);
            Assert.Greater(low.Vx, 0); Assert.Greater(high.Vx, 0);
            Assert.AreEqual(low.Vx, high.Vx, 1e-9);
            Assert.Greater(high.Vx, 20, "a lively bounce (a dead rail would send it back at about 6)");
        }

        [Test]
        public void SideRailsAreLivelyUpToTheOutLine()
        {
            // lively all the way from the launch end to the back (out) line, dead beside the out area
            foreach (var key in Maps.Keys)
                foreach (var w in Maps.Build(key).Walls.Where(w => w.IsSide))
                {
                    double mid = (w.Y1 + w.Y2) / 2;
                    if (mid > 0) Assert.AreEqual(mid > Sheet.Back, w.Dead, key + " rail at " + mid);
                }
            // the prototype's dead upper rail is still available as an option
            var old = Maps.Build("base", 80);
            Assert.IsTrue(old.Walls.Any(w => w.IsSide && w.Dead && w.Y1 == 80));
        }

        [Test]
        public void DeadRailAndTurnOrderShotSpendNoTicket()
        {
            // a shot along the right rail into the out area: soaked up by the dead rail, no ticket, no foul
            var m = new Match(Maps.Build("base"), null, 1) { Hammer = Team.W };
            m.StartEnd();
            m.Stones.Clear();
            m.BeginShot();
            var s = m.Release(0, 1);
            s.X = Sheet.W - Sheet.SR - 0.5; s.Y = Sheet.Back + 6; s.Vx = 60; s.Vy = 20;
            m.RunToRest();
            Assert.AreEqual(Match_TicketCount(m), m.Tickets[(int)m.Cur]);
            Assert.IsFalse(m.Foul);
            Assert.AreEqual(0, m.Cushions);

            // the turn order shot banks off a lively rail without spending a ticket
            var d = new Match(Maps.Build("base"), null, 3);
            d.StartMatch();
            d.BeginLsdShot();
            int before = d.Tickets[(int)d.Cur];
            d.Release(0.9, 0.8);
            d.RunToRest();
            Assert.Greater(d.Cushions, 0);
            Assert.AreEqual(before, d.Tickets[(int)d.Cur]);
            Assert.IsFalse(d.Foul);
        }

        static int Match_TicketCount(Match m) => m.TicketCount;

        [Test]
        public void EveryShotStartsFromTheCentre()
        {
            CollectionAssert.AreEqual(new[] { 50.0 }, Sheet.Lanes);
            var m = new Match(Maps.Build("base"), null, 1) { Hammer = Team.W };
            m.StartEnd(); m.BeginShot();
            Assert.AreEqual(50, m.LaunchX);
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
        public void ASideWallIsAMirrorThatSlowsTheStone()
        {
            // a stone heading up and into the left wall, mid-sheet (lively rail), stepped until it comes off
            var map = Maps.Build("base");
            var s = new Stone { Id = 1, Team = Team.B, X = Sheet.SR + 1, Y = 60, Vx = -60, Vy = 80 };
            var stones = new List<Stone> { s };
            double inAng = Math.Atan2(-s.Vx, s.Vy);
            for (int i = 0; i < 20 && s.Vx < 0; i++) Sim.Step(stones, map, PhysicsParams.Default);
            Assert.Greater(s.Vx, 0, "off the wall");
            Assert.AreEqual(inAng, Math.Atan2(s.Vx, s.Vy), 0.02, "the angle out the angle in");
            Assert.AreEqual(100 * PhysicsParams.Default.WallKeep, Sheet.Hypot(s.Vx, s.Vy), 3, "its speed down to WallKeep (and a little slide)");
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
        public void TiedPlaysExtraEndsUntilOneSideWins()
        {
            var m = new Match(Maps.Build("base"));
            for (int e = 1; e <= 4; e++) { m.End = e; m.Stones = new List<Stone>(); m.FinishEnd(); }
            Assert.AreEqual(AfterEnd.ExtraEnd, m.NextStep());
            m.End = 5; m.FinishEnd();
            Assert.AreEqual(AfterEnd.ExtraEnd, m.NextStep(), "a blank extra end: another one");
            m.End = 6; m.FinishEnd();
            Assert.AreEqual(AfterEnd.ExtraEnd, m.NextStep());
            m.Scores[1][5] = 1;   // (the last extra end scored by white)
            Assert.AreEqual(AfterEnd.GameOver, m.NextStep());
            Assert.AreEqual(Team.W, m.Winner);
        }

        [Test]
        public void LastStoneDrawOnTheMapAsItIs()
        {
            foreach (var key in new[] { "base", "bumper" })
            {
                var m = new Match(Maps.Build(key), null, 7);
                m.StartMatch();
                Assert.AreSame(m.Field, m.ActiveField, key + ": no bumpers of its own");
                m.FinishLsd();
                Assert.AreSame(m.Field, m.ActiveField);
            }
        }

        [Test]
        public void ConfiguredRulesApply()
        {
            try
            {
                Sheet.Configure(ends: 2, shotsPerTeam: 3, stoneRadius: 5, tickets: 3);
                Assert.AreEqual(6, Sheet.Shots); Assert.AreEqual(3, Sheet.Per); Assert.AreEqual(2, Sheet.Ends); Assert.AreEqual(5, Sheet.SR);
                var m = new Match(Maps.Build("base"), null, 2);
                m.StartMatch();
                Assert.AreEqual(3, m.Tickets[0], "three cushion tickets");
                m.FinishLsd(); m.StartEnd();
                for (int i = 0; i < 6; i++) { Assert.IsFalse(m.ShotsDone); m.AdvanceShot(); }
                Assert.IsTrue(m.ShotsDone, "an end is 3 shots each");
                m.Scores[0].Add(1); m.Scores[1].Add(0); m.End = 2; m.Scores[0].Add(0); m.Scores[1].Add(0);
                Assert.AreEqual(AfterEnd.GameOver, m.NextStep(), "two ends");
            }
            finally { Sheet.Configure(); }
        }

        [Test]
        public void UnlimitedTicketsNeverSpendOrFoul()
        {
            try
            {
                Sheet.Configure(tickets: 0, unlimitedTickets: true);
                var m = new Match(Maps.Build("base"), null, 1) { Hammer = Team.W };
                m.StartEnd(); m.Stones.Clear(); m.BeginShot();
                m.Release(0.9, 0.9);   // off the side rail first
                m.RunToRest();
                Assert.IsFalse(m.Foul, "no foul with no tickets when unlimited");
                Assert.AreEqual(0, m.Tickets[(int)m.Cur]);
                Assert.Greater(m.Cushions, 0);
            }
            finally { Sheet.Configure(); }
        }

        [Test]
        public void LastStoneDrawCloserTakesHammer()
        {
            var m = new Match(Maps.Build("base"), null, 3);
            m.StartMatch();
            Assert.IsTrue(m.InLsd);
            m.Lsd.Dist[0] = 5; m.Lsd.Dist[1] = 3;
            Assert.AreEqual(Team.W, m.ResolveLsd(out bool coin));
            Assert.IsFalse(coin);
            Assert.AreEqual(Team.B, m.Cur);
            m.Lsd.Dist[0] = double.PositiveInfinity; m.Lsd.Dist[1] = double.PositiveInfinity;
            m.ResolveLsd(out coin);
            Assert.IsTrue(coin);
        }

        [Test]
        public void CoinTossGivesTheHammerTheSameOnEveryDevice()
        {
            int black = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var a = new Match(Maps.Build("base"), null, seed); a.StartMatch();
                var b = new Match(Maps.Build("item"), null, seed); b.StartMatch();
                var win = a.TossCoin();
                Assert.AreEqual(win, b.TossCoin(), "the seed decides it (online, both devices)");
                Assert.AreEqual(win, a.Hammer, "the side it shows goes second");
                Assert.AreEqual(Sheet.Other(win), a.Cur);
                Assert.AreEqual(a.Lsd.Order.Length, a.Lsd.Idx, "the draw counts as done");
                if (win == Team.B) black++;
            }
            Assert.That(black, Is.InRange(70, 130), "either side, about as often");
        }

        [Test]
        public void LastStoneDrawTarget()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var m = new Match(Maps.Build("base"), null, seed);
                m.StartMatch();
                Assert.That(m.Lsd.TargetX, Is.InRange(Sheet.LsdTargetXMin, Sheet.LsdTargetXMax), "across, in its range");
                Assert.That(m.Lsd.TargetY, Is.InRange(Sheet.LsdTargetYMin, Sheet.LsdTargetYMax), "up the sheet, in its range");
                // the same seed, the same place; and again after a restore
                var again = new Match(Maps.Build("base"), null, seed); again.StartMatch();
                Assert.AreEqual(m.Lsd.TargetX, again.Lsd.TargetX); Assert.AreEqual(m.Lsd.TargetY, again.Lsd.TargetY);
                var r = new Match(Maps.Build("base"), null, seed); r.Restore(m.Capture());
                Assert.AreEqual(m.Lsd.TargetX, r.Lsd.TargetX); Assert.AreEqual(m.Lsd.TargetY, r.Lsd.TargetY);
            }
        }

        [Test]
        public void LastStoneDrawThrownOnItsOwn()
        {
            var m = new Match(Maps.Build("base"), null, 5);
            m.StartMatch();
            m.BeginLsdShot();
            var before = m.Capture().ToBytes();
            var t = m.SimulateLsd(Team.W, 0, 0.7, 50);
            Assert.AreEqual(before, m.Capture().ToBytes(), "the match as it was");
            Assert.IsTrue(t.HasRecord, "a straight draw at 0.7 stops on the sheet");
            Assert.AreEqual(Sheet.Dist(t.X, t.Y, m.Lsd.TargetX, m.Lsd.TargetY), t.Dist, 1e-9);
            // read again from its record (as the other device does): the same
            var back = m.LsdFromRecord(Team.W, 0, 0.7, 50, ShotRecord.FromBytes(t.Record.ToBytes()));
            Assert.AreEqual(t.Dist, back.Dist);
            var none = m.SimulateLsd(Team.B, 0, 0.2, 50);
            Assert.IsFalse(none.HasRecord, "short of the min line: no record");
            Assert.IsTrue(none.ShortOfHog);
            m.SetLsd(Team.B, none.Dist, none.X, none.Y); m.SetLsd(Team.W, t.Dist, t.X, t.Y);
            Assert.AreEqual(Team.W, m.ResolveLsd(out bool coin));
            Assert.IsFalse(coin);
        }

        [Test]
        public void TimedOutShotIsLost()
        {
            // in an end: no stone goes down, but the shot still counts as thrown
            var m = new Match(Maps.Build("base"), null, 1) { Hammer = Team.W };
            m.StartEnd();
            m.BeginShot();
            var shooter = m.Cur;
            int stones = m.Stones.Count;
            m.Forfeit();
            Assert.IsNull(m.ShotStone);
            m.AdvanceShot();
            Assert.AreEqual(stones, m.Stones.Count);
            Assert.AreEqual(1, m.ThrownBy(shooter));
            Assert.AreEqual(Sheet.Other(shooter), m.Shooter);

            // in the last stone draw: no record, so the other drawer takes the hammer with any record
            var d = new Match(Maps.Build("base"), null, 3);
            d.StartMatch();
            d.BeginLsdShot();
            d.Forfeit();
            Assert.IsTrue(double.IsPositiveInfinity(d.Lsd.Dist[0].Value));
            Assert.IsTrue(d.NextLsd());
            d.BeginLsdShot();
            d.Lsd.Dist[1] = 40;
            Assert.AreEqual(Team.W, d.ResolveLsd(out bool coin));
            Assert.IsFalse(coin);
        }
    }

    public class ItemTests
    {
        // the rules as they ship (the editor may have applied a local GameConfig to these statics)
        [SetUp]
        public void Defaults()
        {
            Sheet.Configure();
            Sheet.ConfigureItems(2, 3.2, 0, Sheet.W, Sheet.Hog, Sheet.TeeY, 1.5, 0.6, 20, 80, 20, 8);
        }

        // how often each kind comes, over r spread evenly across [0, 1)
        static Dictionary<ItemKind, double> Shares(ItemKind[] hands)
        {
            const int n = 90000;
            var c = new Dictionary<ItemKind, double>();
            for (int i = 0; i < n; i++)
            {
                var k = Match.PickItem((i + 0.5) / n, hands);
                c[k] = (c.TryGetValue(k, out var v) ? v : 0) + 1.0 / n;
            }
            return c;
        }

        [Test]
        public void AnItemAlreadyHeldComesLessOften()
        {
            var empty = Shares(new[] { ItemKind.None, ItemKind.None });
            foreach (var k in new[] { ItemKind.Giant, ItemKind.Mini, ItemKind.Bomb, ItemKind.Magnet, ItemKind.Protractor })
                Assert.AreEqual(0.2, empty[k], 1e-3, "empty hands: all alike");
            Assert.IsFalse(empty.ContainsKey(ItemKind.None));

            // holding a bomb: it counts half (0.5 / 4.5), the others 1 / 4.5 each
            var held = Shares(new[] { ItemKind.Bomb, ItemKind.None });
            Assert.AreEqual(0.5 / 4.5, held[ItemKind.Bomb], 1e-3);
            Assert.AreEqual(1 / 4.5, held[ItemKind.Giant], 1e-3);

            Sheet.ConfigureItems(2, 3.2, 0, Sheet.W, Sheet.Hog, Sheet.TeeY, 1.5, 0.6, 20, 80, 20, 8, 25, 0);
            Assert.IsFalse(Shares(new[] { ItemKind.Bomb, ItemKind.None }).ContainsKey(ItemKind.Bomb), "0: never");
            Assert.AreEqual(ItemKind.Protractor, Match.PickItem(0.9999999999, new[] { ItemKind.Bomb, ItemKind.None }));
        }

        // an end in item mode, its turn begun, the sheet cleared of the guard and the boxes for the test to set
        static Match ItemEnd(int seed = 1)
        {
            var m = new Match(Maps.Build(Maps.ItemKey), PhysicsParams.Default, seed) { Hammer = Team.W };
            m.StartEnd();
            m.Stones.Clear(); m.Boxes.Clear();
            m.BeginShot();
            return m;
        }

        static ItemBox Box(Match m, Team t, double x, double y, int id = 50)
        {
            var b = new ItemBox { Id = id, Team = t, X = x, Y = y };
            m.Boxes.Add(b);
            return b;
        }

        [Test]
        public void BoxesArePutInTheirBandApartFromEverything()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var m = new Match(Maps.Build(Maps.ItemKey), PhysicsParams.Default, seed) { Hammer = Team.W };
                m.StartEnd();
                foreach (var t in new[] { Team.B, Team.W }) Assert.AreEqual(Sheet.ItemBoxes, m.Boxes.Count(b => b.Team == t));
                double r = Sheet.ItemBoxR;
                foreach (var b in m.Boxes)
                {
                    Assert.That(b.Y, Is.InRange(Sheet.Hog + r, Math.Max(Sheet.Hog + r, Sheet.ItemYMax)), "between the min line and the house's centre");
                    Assert.That(b.X, Is.InRange(r, Sheet.W - r));
                    bool corner = b.Y < Sheet.Hog + Sheet.ItemKeepOutH + r && (b.X < Sheet.ItemKeepOutW + r || b.X > Sheet.W - Sheet.ItemKeepOutW - r);
                    Assert.IsFalse(corner, "not where the stones left are printed");
                    foreach (var s in m.Stones) Assert.Greater(Sheet.Dist(s.X, s.Y, b.X, b.Y), s.Rad + r);
                    foreach (var o in m.Boxes) if (o != b) Assert.Greater(Sheet.Dist(o.X, o.Y, b.X, b.Y), 2 * r);
                }
                // the next end puts out a fresh set (none of these left)
                var before = m.Boxes.ToList();
                m.Boxes.RemoveAt(m.Boxes.Count - 1);
                m.StartEnd();
                Assert.AreEqual(2 * Sheet.ItemBoxes, m.Boxes.Count);
                Assert.IsFalse(m.Boxes.Any(before.Contains));
            }
            // the basic map has none
            var b0 = new Match(Maps.Build("base"), PhysicsParams.Default, 1) { Hammer = Team.W };
            b0.StartEnd();
            Assert.AreEqual(0, b0.Boxes.Count);
        }

        [Test]
        public void TakenAndBrokenBoxesComeBackAfterARound()
        {
            var m = new Match(Maps.Build(Maps.ItemKey), PhysicsParams.Default, 4) { Hammer = Team.W };
            m.StartEnd();
            var gone = m.Boxes.Where(b => b.Team == Team.B).ToList();
            m.Boxes.Remove(gone[0]);
            m.Boxes.RemoveAll(b => b.Team == Team.W);
            int left = m.Boxes.Count;
            m.AdvanceShot();
            Assert.AreEqual(left, m.Boxes.Count, "not before the round is over");
            m.AdvanceShot();
            foreach (var t in new[] { Team.B, Team.W }) Assert.AreEqual(Sheet.ItemBoxes, m.Boxes.Count(b => b.Team == t), "back after both have thrown");
            Assert.IsTrue(m.Boxes.Contains(gone[1]), "the one left stays");
        }

        [Test]
        public void OwnStoneTakesTheBoxOthersBreakIt()
        {
            var m = ItemEnd();
            var me = m.Cur;
            Box(m, me, 50, 100);
            m.Release(0, 0.7);
            m.RunToRest();
            Assert.AreEqual(0, m.Boxes.Count);
            Assert.AreNotEqual(ItemKind.None, m.Held[(int)me][0], "taken into the first hand");
            Assert.AreEqual(ItemKind.None, m.Held[(int)me][1]);

            var o = ItemEnd();
            Box(o, Sheet.Other(o.Cur), 50, 100);
            o.Release(0, 0.7);
            o.RunToRest();
            Assert.AreEqual(0, o.Boxes.Count, "broken");
            Assert.IsTrue(o.Held.All(h => h.All(k => k == ItemKind.None)));
        }

        [Test]
        public void FullHandsBreakTheBox()
        {
            var m = ItemEnd();
            var me = m.Cur;
            m.Held[(int)me][0] = ItemKind.Bomb; m.Held[(int)me][1] = ItemKind.Mini;
            Box(m, me, 50, 100);
            m.Release(0, 0.7);
            m.RunToRest();
            Assert.AreEqual(0, m.Boxes.Count, "gone, not put somewhere else (back after the round, as any taken)");
            CollectionAssert.AreEqual(new[] { ItemKind.Bomb, ItemKind.Mini }, m.Held[(int)me]);
        }

        [Test]
        public void AnItemGoesOnTheStoneAboutToBeThrown()
        {
            var m = ItemEnd();
            var me = m.Cur;
            m.Held[(int)me][1] = ItemKind.Giant;
            Assert.IsFalse(m.CanUseItem(Sheet.Other(me), 1), "only on its own turn");
            Assert.IsFalse(m.CanUseItem(me, 0), "an empty hand");
            Assert.AreEqual(ItemKind.Giant, m.UseItem(me, 1));
            Assert.AreEqual(ItemKind.None, m.Held[(int)me][1]);
            Assert.AreEqual(Sheet.SR * Sheet.GiantSize, m.ArmedRad, 1e-9);
            m.Held[(int)me][0] = ItemKind.Mini;
            Assert.IsFalse(m.CanUseItem(me, 0), "one a turn");
            var s = m.Release(0, 0.7);
            Assert.AreEqual(Sheet.GiantSize, s.Size);
            Assert.AreEqual(ItemKind.None, m.Armed);
            m.RunToRest();
            // it stays giant, and counts as one in the house
            Assert.AreEqual(Sheet.GiantSize, m.ShotStone.Size);
            var sc = Rules.Scoring(m.Stones);
            Assert.AreEqual(me, sc.Team);
            // a stone it rests against is a giant's radius plus a stone's away
            var t = new List<Stone> { new Stone { Id = 1, X = 50, Y = 100, Size = Sheet.GiantSize }, new Stone { Id = 2, X = 50, Y = 103 } };
            Sim.Step(t, m.Field, m.P);
            Assert.AreEqual(Sheet.SR * (Sheet.GiantSize + 1), Sheet.Dist(t[0].X, t[0].Y, t[1].X, t[1].Y), 1e-9);
        }

        [Test]
        public void ABombPushesTheStonesNearItAway()
        {
            double Pushed(bool bomb)
            {
                var m = ItemEnd();
                m.Stones.Add(new Stone { Id = 90, Team = Team.W, X = 50, Y = 120 });
                m.Stones.Add(new Stone { Id = 91, Team = Team.W, X = 62, Y = 122 });
                m.Snapshot = m.Stones.Select(s => s.Clone()).ToList();
                if (bomb) { m.Held[(int)m.Cur][0] = ItemKind.Bomb; m.UseItem(m.Cur, 0); }
                var shot = m.Release(0, 0.65);
                Assert.AreEqual(bomb, shot.Bomb);
                m.RunToRest();
                Assert.IsFalse(m.Stones.Any(s => s.Bomb), "gone off once");
                var side = m.Stones.FirstOrDefault(s => s.Id == 91);
                return side == null || !side.Live ? 999 : Sheet.Dist(side.X, side.Y, 62, 122);
            }
            Assert.AreEqual(0, Pushed(false), 1e-9, "without a bomb the stone beside is not touched");
            Assert.Greater(Pushed(true), 5, "the bomb throws it away");

            // a rail does not set it off: only a stone does
            var w = ItemEnd();
            w.Held[(int)w.Cur][0] = ItemKind.Bomb; w.UseItem(w.Cur, 0);
            w.Release(-Math.PI / 4, 0.75);
            w.RunToRest();
            Assert.IsTrue(w.ShotStone.Bomb, "still to go off while it moves");
            w.ResolveShot();
            Assert.IsFalse(w.Stones.Any(s => s.Bomb), "spent once the shot has stopped");
        }

        [Test]
        public void AMagnetDragsTheStonesNearItsWay()
        {
            double Moved(bool magnet)
            {
                var m = ItemEnd();
                m.Stones.Add(new Stone { Id = 90, Team = Team.W, X = 61, Y = 100 });
                if (magnet) { m.Held[(int)m.Cur][0] = ItemKind.Magnet; m.UseItem(m.Cur, 0); }
                var shot = m.Release(0, 0.7);
                Assert.AreEqual(magnet, shot.Magnet);
                m.RunToRest();
                var o = m.Stones.First(s => s.Id == 90);
                m.ResolveShot();
                Assert.IsFalse(m.Stones.Any(s => s.Magnet), "spent with the shot");
                return Sheet.Dist(o.X, o.Y, 61, 100);
            }
            Assert.AreEqual(0, Moved(false), 1e-9, "a plain stone passes it by");
            Assert.Greater(Moved(true), 3, "the magnet drags it along");
        }

        [Test]
        public void ItemsTravelWithTheStateAndTheRecord()
        {
            var m = ItemEnd(3);
            var me = m.Cur;
            Box(m, me, 50, 90, 7);
            Box(m, Sheet.Other(me), 30, 150, 8);
            m.Held[(int)Sheet.Other(me)][1] = ItemKind.Bomb;
            m.Held[(int)me][1] = ItemKind.Mini;
            m.UseItem(me, 1);
            var bytes = m.Capture().ToBytes();
            var back = new Match(Maps.Build(Maps.ItemKey), PhysicsParams.Default, 3);
            back.Restore(MatchState.FromBytes(bytes));
            CollectionAssert.AreEqual(bytes, back.Capture().ToBytes());
            Assert.AreEqual(ItemKind.Mini, back.Armed);

            // recorded on one match and played on another: the same boxes and hands along the way and at the end
            var before = m.Capture();
            m.Release(0, 0.7);
            var rec = ShotRecord.FromBytes(ShotRecord.Record(m).ToBytes());
            var played = new Match(Maps.Build(Maps.ItemKey), PhysicsParams.Default, 3);
            played.Restore(before);
            played.Release(0, 0.7);
            var pb = new ShotPlayback(played, rec);
            bool seen = false;
            while (!pb.Done)
            {
                pb.Advance(0.05);
                if (!pb.Done && played.Held[(int)me][0] != ItemKind.None) seen = true;
            }
            Assert.IsTrue(seen, "the box is taken during the playback");
            CollectionAssert.AreEqual(m.Capture().ToBytes(), played.Capture().ToBytes());
            Assert.AreEqual(1, played.Boxes.Count);
        }

        [Test]
        public void AFoulPutsTheBoxesAndHandsBack()
        {
            var m = ItemEnd();
            var me = m.Cur;
            m.Tickets[(int)me] = 0;
            Box(m, me, 20, 44);   // on the way to the left rail, low
            m.BeginShot();
            m.Release(-Math.PI / 4, 0.75);
            m.RunToRest();
            Assert.IsTrue(m.ResolveShot().Foul);
            m.ApplyRestore();
            Assert.AreEqual(1, m.Boxes.Count);
            Assert.IsTrue(m.Held[(int)me].All(k => k == ItemKind.None));
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
