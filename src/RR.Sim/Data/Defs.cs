using System.Collections.Generic;

namespace RR.Sim.Data
{
    // Plain DTOs that mirror the JSON in /data. Property names are matched
    // case-insensitively by Newtonsoft, so JSON stays camelCase.

    public sealed class DamageDef
    {
        public int Base { get; set; }
        public double ApCoef { get; set; }
    }

    public sealed class StatBlock
    {
        public int AttackPower { get; set; }
        public double AttackPowerPct { get; set; }
        public double CritPct { get; set; }
        public double HastePct { get; set; }
        public double MasteryPct { get; set; }
    }

    public sealed class EffectDef
    {
        /// resetCooldown | applyAura | removeAura | empowerNextCast | hasteWindow | extraDamage | gainResource
        public string Type { get; set; }
        public string Aura { get; set; }
        public string Ability { get; set; }
        public List<string> AbilityTags { get; set; }
        public int Amount { get; set; }
        public double Pct { get; set; }
        public int DurationMs { get; set; }
        public DamageDef Damage { get; set; }
        /// Display name for auras created inline by empowerNextCast / hasteWindow.
        public string Name { get; set; }
    }

    public sealed class HpBonusDef
    {
        public double BelowPct { get; set; }
        public double Mult { get; set; } = 1.0;
    }

    public sealed class AbilityDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// builder | spender | cooldown | dot | utility | finisher
        public string Type { get; set; }
        /// A | B | majorCd | minorCd | trinket
        public string Bar { get; set; }
        public int Slot { get; set; }
        public int UnlockLevel { get; set; } = 1;
        public bool OnGcd { get; set; } = true;
        public int CastTimeMs { get; set; }
        public int CooldownMs { get; set; }
        public int Charges { get; set; } = 1;
        public int ResourceCost { get; set; }
        public int ResourceGain { get; set; }
        public DamageDef Damage { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        /// Usable only while this player aura is active.
        public string RequiresAura { get; set; }
        /// Removed from the player when the ability is used.
        public string ConsumesAura { get; set; }
        /// Usable only while the boss is at or below this HP percent (0 = no requirement).
        public double RequiresTargetHpBelowPct { get; set; }
        public HpBonusDef TargetHpBonus { get; set; }
        public List<EffectDef> Effects { get; set; } = new List<EffectDef>();

        // Presentation hints only. The sim never reads these.
        public string Animation { get; set; }
        public string Vfx { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
    }

    public sealed class ConsumeDef
    {
        public List<string> Abilities { get; set; } = new List<string>();
        public List<string> AbilityTags { get; set; } = new List<string>();
    }

    public sealed class AuraDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// self (player) | target (boss)
        public string Target { get; set; } = "self";
        /// 0 = permanent
        public int DurationMs { get; set; }
        public int MaxStacks { get; set; } = 1;
        /// refresh | pandemic | addStack
        public string Refresh { get; set; } = "refresh";
        public StatBlock Stats { get; set; }
        /// Player aura: +% damage done per stack.
        public double DamageDonePct { get; set; }
        /// Restricts DamageDonePct to abilities with one of these tags (empty = all).
        public List<string> DamageDoneTags { get; set; } = new List<string>();
        /// Boss aura: +% damage taken per stack.
        public double DamageTakenPct { get; set; }
        public int TickMs { get; set; }
        public DamageDef TickDamage { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        /// Removed by the next cast that matches; that cast gets +EmpowerPct damage.
        public ConsumeDef ConsumedBy { get; set; }
        public double EmpowerPct { get; set; }

        public string Icon { get; set; }
        public string Color { get; set; }
    }

    public sealed class TriggerDef
    {
        /// onHit | onCrit | onCast | onPerfectBlock | onPerfectDodge | onResourceSpent | onTick
        public string Event { get; set; }
        public List<string> AbilityTags { get; set; } = new List<string>();
        public List<string> Abilities { get; set; } = new List<string>();
    }

    public sealed class ProcUiDef
    {
        public string GlowAbility { get; set; }
        public string Sound { get; set; }
    }

    public sealed class ProcDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public TriggerDef Trigger { get; set; }
        public double Chance { get; set; } = 1.0;
        /// Procs-per-minute for onTick triggers.
        public double Ppm { get; set; }
        public int IcdMs { get; set; }
        public EffectDef Effect { get; set; }
        public ProcUiDef Ui { get; set; }
    }

    public sealed class ResourceDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Max { get; set; } = 100;
        public int Start { get; set; }
        public string Color { get; set; }
    }

    public sealed class ClassDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public ResourceDef Resource { get; set; }
        public StatBlock BaseStats { get; set; } = new StatBlock();
        public List<string> Abilities { get; set; } = new List<string>();
        public List<string> Procs { get; set; } = new List<string>();
        public List<string> DefaultGear { get; set; } = new List<string>();
    }

    public sealed class ItemDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Slot { get; set; }
        public string Rarity { get; set; }
        public int ItemLevel { get; set; }
        public StatBlock Stats { get; set; } = new StatBlock();
        public List<string> Procs { get; set; } = new List<string>();
        /// On-use trinket ability id.
        public string UseAbility { get; set; }
        public string Skin { get; set; }
        public bool Announce { get; set; }
    }

    public sealed class PhaseDef
    {
        public double UntilHpPct { get; set; }
        public List<string> AttackPool { get; set; } = new List<string>();
        /// [min, max] milliseconds between attack starts.
        public int[] CadenceMs { get; set; } = { 3000, 3000 };
        /// Attack id forced as the next attack when the phase begins.
        public string OnEnter { get; set; }
    }

    public sealed class AttackDef
    {
        public string Name { get; set; }
        public int TelegraphMs { get; set; }
        /// block | dodge | none
        public string Counter { get; set; } = "block";
        public int Damage { get; set; }
        /// 0 = use combat config default.
        public int PerfectWindowMs { get; set; }
        public int NormalWindowMs { get; set; }
        public int StaggerMs { get; set; }
        /// Aura applied to the player on a failed defense.
        public string ApplyAura { get; set; }
        public string Anim { get; set; }
        public string Vfx { get; set; }
    }

    public sealed class VulnerableWindowDef
    {
        public string AfterAttack { get; set; }
        public int DurationMs { get; set; }
        public double DamageTakenMod { get; set; } = 1.0;
    }

    public sealed class ParsePoint
    {
        public double Score { get; set; }
        public double Dps { get; set; }
    }

    public sealed class BossDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Archetype { get; set; }
        public string Skin { get; set; }
        public long Hp { get; set; }
        /// 0 = no enrage.
        public int EnrageMs { get; set; }
        /// Training dummy: reaching the timer completes the fight instead of failing it.
        public bool IsDummy { get; set; }
        public int FirstAttackMs { get; set; } = 3000;
        public List<PhaseDef> Phases { get; set; } = new List<PhaseDef>();
        public Dictionary<string, AttackDef> Attacks { get; set; } = new Dictionary<string, AttackDef>();
        public List<VulnerableWindowDef> VulnerableWindows { get; set; } = new List<VulnerableWindowDef>();
        public string LootTable { get; set; }
        /// Benchmark curve: DPS needed for each score. Sorted by DPS ascending.
        public List<ParsePoint> Parse { get; set; } = new List<ParsePoint>();
    }

    public sealed class FlowDef
    {
        public bool Enabled { get; set; } = true;
        public int Threshold { get; set; } = 5;
        public string Aura { get; set; }
    }

    public sealed class DefenseConfig
    {
        public int NormalWindowMs { get; set; } = 400;
        public int PerfectWindowMs { get; set; } = 150;
        public int RecoveryMs { get; set; } = 300;
        public int StaggerMs { get; set; } = 800;
        public string PerfectAura { get; set; }
        public FlowDef Flow { get; set; } = new FlowDef();
        public int MaxInputOffsetMs { get; set; } = 150;
    }

    public sealed class CombatConfig
    {
        public int BaseGcdMs { get; set; } = 1500;
        public int MinGcdMs { get; set; } = 750;
        public int SpellQueueMs { get; set; } = 200;
        public double CritMultiplier { get; set; } = 2.0;
        public double PandemicPct { get; set; } = 0.3;
        /// Minimum gap between one attack's impact and the next telegraph.
        public int MinAttackGapMs { get; set; } = 400;
        public DefenseConfig Defense { get; set; } = new DefenseConfig();
    }
}
