using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using RR.Sim.Core;
using RR.Sim.Scoring;
using RR.Tools;

namespace RR.Sim.Tests
{
    public class DeterminismTests
    {
        static List<string> Signature(CombatSim sim)
        {
            var sig = new List<string>();
            sim.Events.Subscribe(e => sig.Add(e.GetType().Name + JsonConvert.SerializeObject(e)));
            return sig;
        }

        static (CombatSim sim, List<string> sig) BotFight(ulong seed, string profile = "good")
        {
            var sim = TestData.Sim(TestData.Real(), "gorrath_the_pale", seed);
            var sig = Signature(sim);
            var bot = new RotationBot(sim, BotProfile.ByName(profile), (int)seed);
            while (!sim.Ended)
            {
                bot.BeforeStep();
                sim.Step();
            }
            return (sim, sig);
        }

        [Test]
        public void SameSeedSameRandomSequence()
        {
            var a = new SimRandom(123);
            var b = new SimRandom(123);
            var c = new SimRandom(124);
            var seqA = Enumerable.Range(0, 100).Select(_ => a.NextULong()).ToList();
            Assert.That(Enumerable.Range(0, 100).Select(_ => b.NextULong()), Is.EqualTo(seqA));
            Assert.That(Enumerable.Range(0, 100).Select(_ => c.NextULong()), Is.Not.EqualTo(seqA));
        }

        [Test]
        public void RandomDoublesAreInRange()
        {
            var r = new SimRandom(7);
            for (int i = 0; i < 10000; i++)
            {
                var d = r.NextDouble();
                Assert.That(d, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
            }
        }

        [Test]
        public void FightLogReplaysBitForBit()
        {
            var (live, liveSig) = BotFight(42);
            var json = JsonConvert.SerializeObject(live.CreateLog());
            var log = JsonConvert.DeserializeObject<FightLog>(json);

            var replaySig = new List<string>();
            var replay = CombatSim.Replay(TestData.Real(), log,
                e => replaySig.Add(e.GetType().Name + JsonConvert.SerializeObject(e)));

            Assert.That(replay.Result.DurationTicks, Is.EqualTo(live.Result.DurationTicks));
            Assert.That(replay.TotalDamage, Is.EqualTo(live.TotalDamage));
            Assert.That(replaySig.Count, Is.EqualTo(liveSig.Count));
            Assert.That(replaySig, Is.EqualTo(liveSig));
        }

        [Test]
        public void DifferentSeedsGiveDifferentFights()
        {
            var (a, _) = BotFight(1);
            var (b, _) = BotFight(2);
            Assert.That(a.TotalDamage == b.TotalDamage && a.Result.DurationTicks == b.Result.DurationTicks, Is.False);
        }

        [Test]
        public void ReplayRefusesDifferentDataVersion()
        {
            var (live, _) = BotFight(3);
            var log = live.CreateLog();
            log.DataVersion = "0000000000000000";
            Assert.Throws<System.InvalidOperationException>(() => CombatSim.Replay(TestData.Real(), log));
        }

        [Test]
        public void ReportIsConsistentWithTheFight()
        {
            var sim = TestData.Sim(TestData.Real(), "gorrath_the_pale", 9);
            var report = FightReport.Attach(sim);
            var bot = new RotationBot(sim, BotProfile.Good, 9);
            while (!sim.Ended)
            {
                bot.BeforeStep();
                sim.Step();
            }
            Assert.That(report.TotalDamage, Is.EqualTo(sim.TotalDamage));
            Assert.That(report.Sources.Sum(s => s.Damage), Is.EqualTo(sim.TotalDamage));
            Assert.That(report.DurationTicks, Is.EqualTo(sim.Result.DurationTicks));
            Assert.That(report.GcdUptime, Is.GreaterThan(0.8).And.LessThanOrEqualTo(1.0));
            Assert.That(report.Attacks, Is.EqualTo(report.Avoided + report.Perfects + report.HitsTaken));
        }

        [Test]
        public void ParseScoreFollowsTheBenchmarkCurve()
        {
            var boss = TestData.Real().GetBoss("gorrath_the_pale");
            Assert.That(ParseScore.Score(boss, 0), Is.EqualTo(0));
            Assert.That(ParseScore.Score(boss, 6700), Is.EqualTo(50));
            Assert.That(ParseScore.Score(boss, 100000), Is.EqualTo(100));
            Assert.That(ParseScore.Tier(99), Is.EqualTo("pink"));
            Assert.That(ParseScore.Tier(50), Is.EqualTo("blue"));
            Assert.That(ParseScore.Tier(10), Is.EqualTo("grey"));
        }
    }
}
