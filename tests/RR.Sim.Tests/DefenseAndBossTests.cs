using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RR.Sim.Core;

namespace RR.Sim.Tests
{
    public class DefenseAndBossTests
    {
        // Test boss: first telegraph at tick 60, impact 1s later at tick 120.
        const int Impact = 120;

        static AttackOutcome Outcome(EventLog log) => log.Of<AttackResolved>().First().Outcome;

        static (CombatSim sim, EventLog log) Fight(int offsetMs = 0)
        {
            var sim = TestData.Sim(TestData.WithTestBoss(), offsetMs: offsetMs);
            return (sim, new EventLog(sim));
        }

        [TestCase(0, AttackOutcome.Perfect)]
        [TestCase(9, AttackOutcome.Perfect)]    // 150ms
        [TestCase(10, AttackOutcome.Avoided)]
        [TestCase(24, AttackOutcome.Avoided)]   // 400ms
        [TestCase(25, AttackOutcome.Hit)]
        [TestCase(-1, AttackOutcome.Hit)]       // after impact
        public void BlockWindows(int ticksBeforeImpact, AttackOutcome expected)
        {
            var (sim, log) = Fight();
            sim.Enqueue(InputCommand.Block(Impact - ticksBeforeImpact));
            sim.AdvanceTo(Impact + 1);
            Assert.That(Outcome(log), Is.EqualTo(expected));
        }

        [Test]
        public void WrongDefenseTakesTheHit()
        {
            var (sim, log) = Fight();
            sim.Enqueue(InputCommand.Dodge(Impact - 3));
            sim.AdvanceTo(Impact + 1);
            Assert.That(Outcome(log), Is.EqualTo(AttackOutcome.Hit));
            Assert.That(log.Of<PlayerHit>().Single().Damage, Is.EqualTo(5000));
        }

        [Test]
        public void PerfectBlockGrantsRewardAuraAndRiposte()
        {
            var (sim, log) = Fight();
            sim.Enqueue(InputCommand.Block(Impact - 2));
            sim.AdvanceTo(Impact + 1);

            Assert.That(log.Of<PerfectDefense>().Single().Type, Is.EqualTo(DefenseType.Block));
            Assert.That(sim.Auras.Has(AuraTarget.Player, "perfect_edge"));
            Assert.That(sim.Auras.Has(AuraTarget.Player, "riposte"));
            Assert.That(log.Of<ProcTriggered>().Single().GlowAbility, Is.EqualTo("overpower"));
            Assert.That(sim.Player.CheckUsable("overpower", sim.Tick), Is.Null);
        }

        [Test]
        public void LatencyOffsetShiftsPressesAndDelaysResolution()
        {
            // 100ms offset = 6 ticks. A raw press 3 ticks after impact counts as 3 before.
            var (sim, log) = Fight(offsetMs: 100);
            sim.Enqueue(InputCommand.Block(Impact + 3));
            sim.AdvanceTo(Impact + 10);

            var resolved = log.Of<AttackResolved>().Single();
            Assert.That(resolved.Outcome, Is.EqualTo(AttackOutcome.Perfect));
            Assert.That(resolved.Tick, Is.EqualTo(Impact + 6));
            Assert.That(resolved.TimingErrorTicks, Is.EqualTo(-3));
        }

        [Test]
        public void OffsetIsClampedToConfigMaximum()
        {
            var (sim, _) = Fight(offsetMs: 1000);
            Assert.That(sim.Defense.OffsetTicks, Is.EqualTo(9)); // 150ms
        }

        [Test]
        public void DefenseRecoveryPreventsSpam()
        {
            var (sim, log) = Fight();
            sim.Enqueue(InputCommand.Block(50));
            sim.Enqueue(InputCommand.Block(60)); // within 300ms recovery
            sim.Enqueue(InputCommand.Block(68)); // recovery ends at 50 + 18
            sim.AdvanceTo(70);
            Assert.That(log.Of<DefenseUsed>().Select(d => d.Tick), Is.EqualTo(new[] { 50, 68 }));
            Assert.That(log.Of<DefenseRejected>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void TakingAHitStaggersThePlayer()
        {
            var (sim, log) = Fight();
            sim.AdvanceTo(Impact + 1);
            var hit = log.Of<PlayerHit>().Single();
            Assert.That(hit.StaggerEndTick, Is.EqualTo(Impact + 48)); // 800ms

            sim.Enqueue(InputCommand.Cast(Impact + 10, "slam"));
            sim.AdvanceTo(Impact + 12);
            Assert.That(log.Last<CastFailed>().Reason, Is.EqualTo(CastFailReason.Staggered));
        }

        [Test]
        public void FiveConsecutivePerfectsTriggerFlow()
        {
            var (sim, log) = Fight();
            // Cadence 5s: impacts at 120, 420, 720, 1020, 1320.
            for (int i = 0; i < 5; i++) sim.Enqueue(InputCommand.Block(Impact + i * 300 - 2));
            sim.AdvanceTo(Impact + 4 * 300 + 1);

            Assert.That(log.Of<PerfectDefense>().Count(), Is.EqualTo(5));
            Assert.That(log.Of<FlowChanged>().Count(f => f.Burst), Is.EqualTo(1));
            Assert.That(sim.Auras.Has(AuraTarget.Player, "flow_burst"));
            Assert.That(sim.Defense.Flow, Is.EqualTo(0));
        }

        [Test]
        public void HitResetsFlow()
        {
            var (sim, log) = Fight();
            sim.Enqueue(InputCommand.Block(Impact - 2));
            sim.AdvanceTo(Impact + 300 + 1); // second attack lands unblocked
            Assert.That(log.Of<FlowChanged>().Select(f => f.Value), Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void PhaseChangeForcesOnEnterAttack()
        {
            var data = TestData.WithTestBoss(boss =>
            {
                boss["hp"] = 8000;
                boss["phases"] = JArray.Parse(@"[
                    { 'untilHpPct': 75, 'attackPool': ['smash'], 'cadenceMs': [5000, 5000] },
                    { 'untilHpPct': 0, 'attackPool': ['smash'], 'cadenceMs': [5000, 5000], 'onEnter': 'sweep' } ]");
            });
            var sim = TestData.Sim(data);
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "slam")); // ~2.5k non-crit, enough to pass 75%
            sim.Step();

            Assert.That(log.Of<BossPhaseChanged>().Single().Phase, Is.EqualTo(1));
            var tg = log.Of<TelegraphStarted>().Single();
            Assert.That(tg.AttackId, Is.EqualTo("sweep"));
            Assert.That(tg.Counter, Is.EqualTo("dodge"));
            Assert.That(tg.Tick, Is.EqualTo(0));
        }

        [Test]
        public void VulnerableWindowOpensAfterItsAttack()
        {
            var data = TestData.WithTestBoss(boss =>
                boss["vulnerableWindows"] = JArray.Parse("[ { 'afterAttack': 'smash', 'durationMs': 5000, 'damageTakenMod': 1.2 } ]"));
            var sim = TestData.Sim(data);
            var log = new EventLog(sim);
            sim.AdvanceTo(Impact + 1);
            var vuln = log.Of<AuraApplied>().Single(a => a.AuraId == "vulnerable:smash");
            Assert.That(vuln.Target, Is.EqualTo(AuraTarget.Boss));
            Assert.That(vuln.ExpiresTick, Is.EqualTo(Impact + 300));
        }

        [Test]
        public void EnrageEndsTheFightAsALoss()
        {
            var data = TestData.WithTestBoss(boss => boss["enrageMs"] = 2000);
            var sim = TestData.Sim(data);
            sim.RunToEnd();
            Assert.That(sim.Result.Victory, Is.False);
            Assert.That(sim.Result.Reason, Is.EqualTo(FightEndReason.Enraged));
            Assert.That(sim.Result.DurationTicks, Is.EqualTo(120));
        }

        [Test]
        public void DummyTimerCompletesTheFight()
        {
            var sim = TestData.Sim(TestData.Real(), "training_dummy");
            sim.RunToEnd();
            Assert.That(sim.Result.Victory, Is.True);
            Assert.That(sim.Result.Reason, Is.EqualTo(FightEndReason.DummyComplete));
            Assert.That(sim.Result.DurationTicks, Is.EqualTo(3600));
        }

        [Test]
        public void EndingEarlyIsAbandonedEvenOnTheDummy()
        {
            var sim = TestData.Sim(TestData.Real(), "training_dummy");
            sim.Enqueue(new InputCommand(600, InputType.EndFight));
            sim.RunToEnd();
            Assert.That(sim.Result.Victory, Is.False);
            Assert.That(sim.Result.Reason, Is.EqualTo(FightEndReason.Abandoned));
            Assert.That(sim.Result.DurationTicks, Is.EqualTo(601));
        }

        [Test]
        public void KillingTheBossWins()
        {
            var data = TestData.WithTestBoss(boss => boss["hp"] = 1000);
            var sim = TestData.Sim(data);
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.RunToEnd();
            Assert.That(sim.Result.Victory, Is.True);
            Assert.That(sim.Result.Reason, Is.EqualTo(FightEndReason.Killed));
            Assert.That(sim.TotalDamage, Is.EqualTo(1000), "overkill is not counted");
        }
    }
}
