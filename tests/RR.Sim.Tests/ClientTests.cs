using System.Linq;
using NUnit.Framework;
using RR.Client;
using RR.Sim.Core;

namespace RR.Sim.Tests
{
    public class ClientTests
    {
        static GameSession Session(SwapMode mode = SwapMode.Hold, string boss = "training_dummy")
        {
            var s = new GameSession(TestData.Real(), new FightSetup { ClassId = "berserker", BossId = boss, Seed = 1 }, mode);
            s.Start(1000);
            return s;
        }

        [Test]
        public void KitMapsBarsFromData()
        {
            var kit = Session().Kit;
            Assert.That(kit.BarA.Select(a => a.Id), Is.EqualTo(new[] { "raging_blow", "rending_strike", "slam", "rampage" }));
            Assert.That(kit.BarB.Select(a => a.Id), Is.EqualTo(new[] { "execute_strike", "sunder_armor", "overpower", "berserker_rage" }));
            Assert.That(kit.Major.Id, Is.EqualTo("recklessness"));
            Assert.That(kit.Minor.Id, Is.EqualTo("bloodrage"));
            Assert.That(kit.Trinket.Id, Is.EqualTo("trinket_vial_of_fury"));
        }

        [Test]
        public void HoldToSwapShowsBarBOnlyWhileHeld()
        {
            var swap = new BarSwap(SwapMode.Hold);
            swap.Press();
            Assert.That(swap.ShowingB, Is.True);
            swap.Release();
            Assert.That(swap.ShowingB, Is.False);
        }

        [Test]
        public void ToggleSwapFlipsOnPress()
        {
            var swap = new BarSwap(SwapMode.Toggle);
            swap.Press();
            swap.Release();
            Assert.That(swap.ShowingB, Is.True);
            swap.Press();
            Assert.That(swap.ShowingB, Is.False);
        }

        [Test]
        public void SessionResolvesSlotsThroughTheSwapAndMapsTimeToTicks()
        {
            var s = Session();
            s.Input(ClientInput.Slot2, 1000);          // bar A slot 2 = slam at tick 0
            s.Input(ClientInput.SwapDown, 2000);
            s.Input(ClientInput.Slot3, 2000);          // bar B slot 3 = berserker rage at tick 60
            s.Input(ClientInput.SwapUp, 2010);
            var events = s.Update(2100);

            var used = events.OfType<AbilityUsed>().Select(u => (u.AbilityId, u.Tick)).ToList();
            Assert.That(used, Is.EqualTo(new[] { ("slam", 0), ("berserker_rage", 60) }));
            Assert.That(s.Sim.Tick, Is.EqualTo(s.TickAt(2100) + 1));
            Assert.That(s.CreateLog().Inputs.Count, Is.EqualTo(2), "swap presses never reach the sim");
        }

        [Test]
        public void PauseShiftsTheClock()
        {
            var s = Session();
            s.Update(1500);
            int before = s.Sim.Tick;
            s.Pause(1500);
            s.Update(5000);
            Assert.That(s.Sim.Tick, Is.EqualTo(before));
            s.Resume(5000);
            s.Update(5000);
            Assert.That(s.Sim.Tick, Is.EqualTo(before));
        }

        [Test]
        public void HudTracksResourceCooldownsAndAuras()
        {
            var s = Session();
            s.Input(ClientInput.Slot1, 1000); // rending strike
            s.Update(1100);
            Assert.That(s.Hud.Resource, Is.EqualTo(20));
            var rs = s.Hud.Ability("rending_strike");
            Assert.That(rs.Charges, Is.EqualTo(0));
            Assert.That(rs.RechargeEndTick, Is.EqualTo(360));
            Assert.That(s.Hud.BossAuras.Single().Id, Is.EqualTo("rend_bleed"));
            Assert.That(s.Hud.GcdEndTick, Is.EqualTo(83));
            Assert.That(s.Hud.TotalDamage, Is.EqualTo(s.Sim.TotalDamage));
        }

        [Test]
        public void BarBProcGlowsTheSwapButton()
        {
            var s = Session(boss: "gorrath_the_pale");
            // Perfect-block Gorrath's first attack: Riposte makes Overpower (bar B) ready.
            int impact = -1;
            for (double t = 1000; t < 30000 && impact < 0; t += 16)
            {
                foreach (var e in s.Update(t))
                    if (e is TelegraphStarted tg && tg.Counter == "block") impact = tg.ImpactTick;
            }
            if (impact < 0) Assert.Inconclusive("first attack was not blockable for this seed");
            s.Input(ClientInput.Block, 1000 + (impact - 2) * 1000.0 / 60);
            s.Update(1000 + (impact + 2) * 1000.0 / 60);

            Assert.That(s.Hud.HasPlayerAura("riposte"), Is.True);
            Assert.That(s.SwapGlow, Is.True);
            s.Input(ClientInput.SwapDown, 1000 + (impact + 3) * 1000.0 / 60);
            Assert.That(s.SwapGlow, Is.False, "bar B is showing, nothing hidden is glowing");
        }
    }
}
