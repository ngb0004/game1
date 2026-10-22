using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RR.Sim.Combat;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Tests
{
    public class CombatTests
    {
        // Berserker default gear: 5% class haste + 3% helm = 8% -> 1389ms GCD -> 83 ticks.
        const int Gcd = 83;

        static CombatSim Dummy(GameData data = null) => TestData.Sim(data ?? TestData.Real(), "training_dummy");

        [Test]
        public void TimeConversionRoundsHalfUp()
        {
            Assert.That(SimTime.MsToTicks(1500), Is.EqualTo(90));
            Assert.That(SimTime.MsToTicks(200), Is.EqualTo(12));
            Assert.That(SimTime.MsToTicks(150), Is.EqualTo(9));
            Assert.That(SimTime.MsToTicks(1389), Is.EqualTo(83));
            Assert.That(SimTime.MsToTicks(0), Is.EqualTo(0));
        }

        [Test]
        public void GcdScalesWithHasteAndClampsAtMinimum()
        {
            var cfg = new CombatConfig();
            Assert.That(DamageFormula.GcdTicks(cfg, 0), Is.EqualTo(90));
            Assert.That(DamageFormula.GcdTicks(cfg, 50), Is.EqualTo(60));
            Assert.That(DamageFormula.GcdTicks(cfg, 100), Is.EqualTo(45));
            Assert.That(DamageFormula.GcdTicks(cfg, 300), Is.EqualTo(45), "clamped to 0.75s");
        }

        [Test]
        public void CastStartsGcdFromHastedStats()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.Step();
            var gcd = log.Last<GcdStarted>();
            Assert.That(gcd.Tick, Is.EqualTo(0));
            Assert.That(gcd.EndTick, Is.EqualTo(Gcd));
        }

        [Test]
        public void HasteAuraShortensGcd()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "bloodrage")); // +30% haste, off GCD
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.Step();
            // 1500 / 1.38 = 1087ms -> 65 ticks
            Assert.That(log.Last<GcdStarted>().EndTick, Is.EqualTo(65));
        }

        [Test]
        public void SpellQueueFiresOnTheExactTickTheGcdEnds()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.Enqueue(InputCommand.Cast(Gcd - 12, "rending_strike")); // 200ms early: queued
            sim.AdvanceTo(Gcd + 5);

            Assert.That(log.Of<AbilityQueued>().Single().AbilityId, Is.EqualTo("rending_strike"));
            var used = log.Of<AbilityUsed>().Select(u => (u.AbilityId, u.Tick)).ToList();
            Assert.That(used, Is.EqualTo(new[] { ("slam", 0), ("rending_strike", Gcd) }));
        }

        [Test]
        public void PressTooEarlyFailsInsteadOfQueueing()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.Enqueue(InputCommand.Cast(Gcd - 13, "rending_strike"));
            sim.AdvanceTo(Gcd + 5);

            Assert.That(log.Of<AbilityQueued>(), Is.Empty);
            Assert.That(log.Last<CastFailed>().Reason, Is.EqualTo(CastFailReason.OnGlobalCooldown));
            Assert.That(log.Of<AbilityUsed>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void OnlyTheLatestQueuedAbilityFires()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.Enqueue(InputCommand.Cast(Gcd - 10, "slam"));
            sim.Enqueue(InputCommand.Cast(Gcd - 5, "rending_strike"));
            sim.AdvanceTo(Gcd + 1);

            Assert.That(log.Of<AbilityUsed>().Last().AbilityId, Is.EqualTo("rending_strike"));
            Assert.That(log.Of<AbilityUsed>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void OffGcdAbilitiesWorkDuringTheGcd()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "slam"));
            sim.Enqueue(InputCommand.Cast(1, "berserker_rage"));
            sim.AdvanceTo(3);
            Assert.That(log.Of<AbilityUsed>().Select(u => u.AbilityId), Is.EqualTo(new[] { "slam", "berserker_rage" }));
            Assert.That(sim.Player.Resource, Is.EqualTo(8 + 25));
        }

        [Test]
        public void ChargesRechargeOneAtATime()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "raging_blow"));
            sim.Enqueue(InputCommand.Cast(Gcd, "raging_blow"));
            sim.Enqueue(InputCommand.Cast(Gcd * 2, "raging_blow"));
            sim.AdvanceTo(Gcd * 2 + 1);

            var rb = sim.Player.GetState("raging_blow");
            Assert.That(rb.Charges, Is.EqualTo(0));
            Assert.That(log.Last<CastFailed>().Reason, Is.EqualTo(CastFailReason.OnCooldown));

            sim.AdvanceTo(421); // 7s recharge started at tick 0 finishes at 420
            Assert.That(rb.Charges, Is.EqualTo(1));
            Assert.That(rb.RechargeStartTick, Is.EqualTo(420));
            Assert.That(rb.RechargeEndTick, Is.EqualTo(840));
        }

        [Test]
        public void SpendersNeedResourceAndOvercapIsWasted()
        {
            var data = TestData.WithTestBoss(tweakFiles: f => f["classes.json"][0]["resource"]["start"] = 90);
            var sim = TestData.Sim(data, "training_dummy");
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "rending_strike"));
            sim.AdvanceTo(1);

            var rc = log.Last<ResourceChanged>();
            Assert.That(rc.Value, Is.EqualTo(100));
            Assert.That(rc.Wasted, Is.EqualTo(10));

            var fresh = Dummy();
            var freshLog = new EventLog(fresh);
            fresh.Enqueue(InputCommand.Cast(0, "rampage"));
            fresh.AdvanceTo(1);
            Assert.That(freshLog.Last<CastFailed>().Reason, Is.EqualTo(CastFailReason.NotEnoughResource));
        }

        [Test]
        public void DamageFormulaMatchesSpec()
        {
            // raw 1000 * mods 1.1 * crit 2.0 * mastery 1.1 = 2420
            Assert.That(DamageFormula.Final(1000, 1.1, true, 2.0, 10), Is.EqualTo(2420));
            Assert.That(DamageFormula.Final(1000, 1.0, false, 2.0, 0), Is.EqualTo(1000));
            Assert.That(DamageFormula.Raw(new DamageDef { Base = 120, ApCoef = 1.1 }, 1000), Is.EqualTo(1220).Within(1e-9));
        }

        [Test]
        public void ProcInternalCooldownLimitsTriggers()
        {
            var data = TestData.WithTestBoss(tweakFiles: f =>
            {
                f["procs.json"] = JArray.Parse(@"[
                    { 'id': 'p', 'name': 'P', 'trigger': { 'event': 'onHit', 'abilityTags': ['melee'] },
                      'chance': 1.0, 'icdMs': 3000, 'effect': { 'type': 'gainResource', 'amount': 1 } } ]");
                f["classes.json"][0]["procs"] = new JArray("p");
            });
            var sim = TestData.Sim(data, "training_dummy");
            var log = new EventLog(sim);
            for (int i = 0; i < 10; i++) sim.Enqueue(InputCommand.Cast(i * Gcd, "slam"));
            sim.AdvanceTo(10 * Gcd);

            var ticks = log.Of<ProcTriggered>().Select(p => p.Tick).ToList();
            // 3s ICD = 180 ticks, casts every 83 ticks -> casts 0, 3, 6, 9.
            Assert.That(ticks, Is.EqualTo(new[] { 0, 3 * Gcd, 6 * Gcd, 9 * Gcd }));
        }

        [Test]
        public void BloodlustEdgeResetsExecuteStrike()
        {
            var data = TestData.WithTestBoss(tweakFiles: f =>
            {
                f["classes.json"][0]["baseStats"]["critPct"] = 100;
                var proc = f["procs.json"].First(p => (string)p["id"] == "proc_bloodlust_edge");
                proc["chance"] = 1.0;
                proc["icdMs"] = 0;
            });
            var sim = TestData.Sim(data, "training_dummy");
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "rending_strike"));
            sim.Enqueue(InputCommand.Cast(Gcd, "execute_strike"));
            sim.AdvanceTo(Gcd + 1);

            Assert.That(log.Of<AbilityUsed>().Select(u => u.AbilityId), Does.Contain("execute_strike"));
            Assert.That(log.Of<ProcTriggered>().Last().GlowAbility, Is.EqualTo("execute_strike"));
            Assert.That(sim.Player.GetState("execute_strike").Charges, Is.EqualTo(1), "cooldown was reset by its own crit");
        }

        [Test]
        public void PandemicRefreshCarriesUpToThirtyPercent()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "rending_strike"));
            sim.Enqueue(InputCommand.Cast(360, "rending_strike")); // 6s CD
            sim.AdvanceTo(400);

            var rend = log.Of<AuraApplied>().Where(a => a.AuraId == "rend_bleed").ToList();
            Assert.That(rend[0].ExpiresTick, Is.EqualTo(720));
            // 360 ticks left, carry capped at 30% of 720 = 216.
            Assert.That(rend[1].ExpiresTick, Is.EqualTo(360 + 720 + 216));
        }

        [Test]
        public void DotTicksSixTimesOverTwelveSeconds()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "rending_strike"));
            sim.AdvanceTo(800);
            var ticks = log.Of<DamageDealt>().Where(d => d.SourceId == "rend_bleed").Select(d => d.Tick).ToList();
            Assert.That(ticks, Is.EqualTo(new[] { 120, 240, 360, 480, 600, 720 }));
            Assert.That(log.Of<AuraRemoved>().Single(r => r.AuraId == "rend_bleed").Tick, Is.EqualTo(720));
        }

        [Test]
        public void EmpowerIsConsumedByTheNextMatchingCast()
        {
            var data = TestData.WithTestBoss(tweakFiles: f =>
            {
                var proc = f["procs.json"].First(p => (string)p["id"] == "proc_seething");
                proc["chance"] = 1.0;
                f["classes.json"][0]["resource"]["start"] = 100;
            });
            var sim = TestData.Sim(data, "training_dummy");
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "rampage"));
            sim.Enqueue(InputCommand.Cast(Gcd, "raging_blow"));
            sim.AdvanceTo(Gcd + 1);

            Assert.That(log.Of<AuraApplied>().Any(a => a.AuraId == "empower:proc_seething"));
            var removed = log.Of<AuraRemoved>().Single(r => r.AuraId == "empower:proc_seething");
            Assert.That(removed.Consumed, Is.True);
            Assert.That(removed.Tick, Is.EqualTo(Gcd));
        }

        [Test]
        public void ConditionalAbilityNeedsItsAura()
        {
            var sim = Dummy();
            var log = new EventLog(sim);
            sim.Enqueue(InputCommand.Cast(0, "overpower"));
            sim.AdvanceTo(1);
            Assert.That(log.Last<CastFailed>().Reason, Is.EqualTo(CastFailReason.MissingAura));
        }

        [Test]
        public void InvalidDataIsRejectedWithAClearMessage()
        {
            var files = TestData.Files();
            files["abilities.json"] = files["abilities.json"].Replace("\"aura\": \"rend_bleed\"", "\"aura\": \"no_such_aura\"");
            var ex = Assert.Throws<GameDataException>(() => GameData.Load(files));
            Assert.That(ex.Message, Does.Contain("no_such_aura"));
        }

        [Test]
        public void DataVersionIgnoresLineEndings()
        {
            var files = TestData.Files();
            var crlf = files.ToDictionary(kv => kv.Key, kv => kv.Value.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            Assert.That(GameData.Load(crlf).Version, Is.EqualTo(GameData.Load(files).Version));
        }
    }
}
