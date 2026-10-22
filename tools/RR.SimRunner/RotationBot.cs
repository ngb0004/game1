using System;
using System.Collections.Generic;
using System.Linq;
using RR.Sim;
using RR.Sim.Core;

namespace RR.Tools
{
    /// Skill profile for the bot: how well it defends and how tight its rotation is.
    public sealed class BotProfile
    {
        public string Name = "good";
        public double PerfectChance = 0.7;
        public double NormalChance = 0.25;
        /// Uses the spell queue / reacts this many ticks after the GCD ends.
        public int MinDelayTicks = 0;
        public int MaxDelayTicks = 3;
        /// false = mashes bar A builders and Rampage only.
        public bool FullRotation = true;
        public bool UseCooldowns = true;

        public static BotProfile Perfect => new BotProfile { Name = "perfect", PerfectChance = 1.0, NormalChance = 0, MinDelayTicks = 0, MaxDelayTicks = 0 };
        public static BotProfile Good => new BotProfile();
        public static BotProfile Average => new BotProfile
        {
            Name = "average", PerfectChance = 0.35, NormalChance = 0.45, MinDelayTicks = 6, MaxDelayTicks = 20,
        };
        public static BotProfile Sloppy => new BotProfile
        {
            Name = "sloppy", PerfectChance = 0.1, NormalChance = 0.5, MinDelayTicks = 15, MaxDelayTicks = 40,
            FullRotation = false, UseCooldowns = false,
        };

        public static BotProfile ByName(string name)
        {
            switch (name)
            {
                case "perfect": return Perfect;
                case "good": return Good;
                case "average": return Average;
                case "sloppy": return Sloppy;
                default: throw new ArgumentException("Unknown bot profile: " + name);
            }
        }
    }

    /// A priority-list player that only produces inputs, exactly like a human.
    /// It reads sim state the way a player reads the HUD.
    public sealed class RotationBot
    {
        readonly CombatSim _sim;
        readonly BotProfile _p;
        readonly Random _rng;
        readonly SortedDictionary<int, InputType> _defense = new SortedDictionary<int, InputType>();
        int _nextGcdPress = -1;

        public RotationBot(CombatSim sim, BotProfile profile, int seed)
        {
            _sim = sim;
            _p = profile;
            _rng = new Random(seed);
            sim.Events.Subscribe(OnEvent);
        }

        void OnEvent(SimEvent e)
        {
            if (e is TelegraphStarted tg && tg.Counter != "none")
            {
                var type = tg.Counter == "dodge" ? InputType.Dodge : InputType.Block;
                double roll = _rng.NextDouble();
                int offset = _sim.Defense.OffsetTicks;
                int lead;
                if (roll < _p.PerfectChance) lead = _rng.Next(0, tg.PerfectWindowTicks);
                else if (roll < _p.PerfectChance + _p.NormalChance) lead = _rng.Next(tg.PerfectWindowTicks + 1, tg.NormalWindowTicks);
                else lead = -_rng.Next(5, 20); // too late
                int at = tg.ImpactTick - lead + offset;
                if (at > e.Tick) _defense[at] = type;
            }
        }

        /// Call before every sim.Step().
        public void BeforeStep()
        {
            int t = _sim.Tick;
            if (_defense.Count > 0)
            {
                var first = _defense.First();
                if (first.Key <= t)
                {
                    _defense.Remove(first.Key);
                    _sim.Enqueue(new InputCommand(t, first.Value));
                }
            }

            if (_p.UseCooldowns)
            {
                foreach (var cd in new[] { "recklessness", "bloodrage", "trinket_vial_of_fury" })
                    if (Usable(cd, t)) _sim.Enqueue(InputCommand.Cast(t, cd));
                if (_p.FullRotation && _sim.Player.Resource <= 60 && Usable("berserker_rage", t))
                    _sim.Enqueue(InputCommand.Cast(t, "berserker_rage"));
            }

            var player = _sim.Player;
            int gcdEnd = Math.Max(player.GcdEndTick, player.StaggerEndTick);
            if (t < gcdEnd)
            {
                _nextGcdPress = -1;
                return;
            }
            if (_nextGcdPress < 0) _nextGcdPress = gcdEnd + _rng.Next(_p.MinDelayTicks, _p.MaxDelayTicks + 1);
            if (t < _nextGcdPress) return;

            var pick = Choose(t);
            if (pick != null)
            {
                _sim.Enqueue(InputCommand.Cast(t, pick));
                _nextGcdPress = -1;
            }
        }

        bool Usable(string id, int t) => _sim.Player.Knows(id) && _sim.Player.CheckUsable(id, t) == null;

        string Choose(int t)
        {
            var player = _sim.Player;
            int fury = player.Resource;
            if (_p.FullRotation)
            {
                if (Usable("overpower", t)) return "overpower";
                if (Usable("execute_strike", t)) return "execute_strike";
                if (fury >= 80 && Usable("rampage", t)) return "rampage";
                var sunder = _sim.Auras.Get(AuraTarget.Boss, "sundered");
                if ((sunder == null || sunder.ExpiresTick - t < 4 * 60) && Usable("sunder_armor", t)) return "sunder_armor";
                if (_sim.Auras.Has(AuraTarget.Player, "empower:proc_seething") && Usable("raging_blow", t)) return "raging_blow";
                var rb = player.GetState("raging_blow");
                if (rb != null && rb.Charges == 2 && Usable("raging_blow", t)) return "raging_blow";
                if (Usable("rending_strike", t)) return "rending_strike";
                if (Usable("raging_blow", t)) return "raging_blow";
                return Usable("slam", t) ? "slam" : null;
            }

            // Sloppy: random bar A button that works.
            var barA = new List<string> { "raging_blow", "rending_strike", "slam", "rampage" };
            var usable = barA.Where(a => Usable(a, t)).ToList();
            return usable.Count == 0 ? null : usable[_rng.Next(usable.Count)];
        }
    }
}
