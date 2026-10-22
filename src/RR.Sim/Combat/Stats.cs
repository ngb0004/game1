using System;
using RR.Sim.Data;

namespace RR.Sim.Combat
{
    /// Final player stats after base, gear and auras.
    public struct Stats
    {
        public double AttackPower;
        public double CritPct;
        public double HastePct;
        public double MasteryPct;

        public override string ToString() =>
            "AP " + AttackPower.ToString("0") + ", crit " + CritPct.ToString("0.#") +
            "%, haste " + HastePct.ToString("0.#") + "%, mastery " + MasteryPct.ToString("0.#") + "%";
    }

    /// Mutable accumulator for stat blocks from base, gear and auras.
    public sealed class StatSum
    {
        public double FlatAttackPower;
        public double AttackPowerPct;
        public double CritPct;
        public double HastePct;
        public double MasteryPct;

        public void Add(StatBlock b, int stacks = 1)
        {
            if (b == null) return;
            FlatAttackPower += b.AttackPower * stacks;
            AttackPowerPct += b.AttackPowerPct * stacks;
            CritPct += b.CritPct * stacks;
            HastePct += b.HastePct * stacks;
            MasteryPct += b.MasteryPct * stacks;
        }

        public Stats ToStats() => new Stats
        {
            AttackPower = FlatAttackPower * (1 + AttackPowerPct / 100.0),
            CritPct = CritPct,
            HastePct = HastePct,
            MasteryPct = MasteryPct,
        };
    }

    /// The spec's damage formula, kept as a pure function so it is easy to test.
    public static class DamageFormula
    {
        public static double Raw(DamageDef d, double attackPower) => d.Base + attackPower * d.ApCoef;

        public static int Final(double raw, double mods, bool crit, double critMultiplier, double masteryPct)
        {
            double v = raw * mods * (crit ? critMultiplier : 1.0) * (1 + masteryPct / 100.0);
            if (v <= 0) return 0;
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        /// GCD in ticks: max(minGcd, baseGcd / (1 + haste%)).
        public static int GcdTicks(CombatConfig cfg, double hastePct)
        {
            double ms = Math.Max(cfg.MinGcdMs, cfg.BaseGcdMs / (1 + hastePct / 100.0));
            return Core.SimTime.MsToTicks((int)Math.Round(ms, MidpointRounding.AwayFromZero));
        }

        public static int HastedTicks(int ms, double hastePct)
        {
            double v = ms / (1 + hastePct / 100.0);
            return Core.SimTime.MsToTicks((int)Math.Round(v, MidpointRounding.AwayFromZero));
        }
    }
}
