namespace RR.Sim.Core
{
    /// Everything the presentation layer learns about a fight arrives as one of
    /// these. Events carry the state needed to draw them, so the presentation
    /// never has to read sim internals.
    public abstract class SimEvent
    {
        public int Tick;
    }

    public enum DefenseType { Block, Dodge }

    public enum AttackOutcome { Hit, Avoided, Perfect, Unavoidable }

    public enum FightEndReason { Killed, Enraged, DummyComplete, Abandoned }

    public enum AuraTarget { Player, Boss }

    public enum CastFailReason
    {
        UnknownAbility, OnGlobalCooldown, OnCooldown, NotEnoughResource, Staggered,
        Casting, MissingAura, TargetHpTooHigh, QueueExpired, FightOver,
    }

    public sealed class FightStarted : SimEvent
    {
        public string ClassId;
        public string BossId;
        public long BossHp;
        public long BossMaxHp;
        /// -1 when the boss has no enrage timer.
        public int EnrageTick;
        public int Resource;
        public int ResourceMax;
    }

    public sealed class AbilityUsed : SimEvent
    {
        public string AbilityId;
    }

    public sealed class CastStarted : SimEvent
    {
        public string AbilityId;
        public int EndTick;
    }

    public sealed class CastInterrupted : SimEvent
    {
        public string AbilityId;
    }

    public sealed class CastFailed : SimEvent
    {
        public string AbilityId;
        public CastFailReason Reason;
    }

    public sealed class AbilityQueued : SimEvent
    {
        public string AbilityId;
    }

    public sealed class GcdStarted : SimEvent
    {
        public int EndTick;
    }

    public sealed class CooldownChanged : SimEvent
    {
        public string AbilityId;
        public int Charges;
        public int MaxCharges;
        /// -1 when no recharge is running.
        public int RechargeStartTick;
        public int RechargeEndTick;
    }

    public sealed class ResourceChanged : SimEvent
    {
        public int Value;
        public int Max;
        public int Delta;
        /// Amount lost to the cap.
        public int Wasted;
        public string SourceId;
    }

    public sealed class DamageDealt : SimEvent
    {
        public string SourceId;
        public int Amount;
        public bool Crit;
        public bool Periodic;
        public bool FromProc;
        public long BossHp;
        public long BossMaxHp;
    }

    public sealed class ProcTriggered : SimEvent
    {
        public string ProcId;
        public string GlowAbility;
        /// Aura the proc applied, if any (the glow lasts as long as it does).
        public string AuraId;
    }

    public sealed class AuraApplied : SimEvent
    {
        public string AuraId;
        public string Name;
        public AuraTarget Target;
        public int Stacks;
        public int StartTick;
        /// int.MaxValue for permanent auras.
        public int ExpiresTick;
        public bool Refreshed;
    }

    public sealed class AuraRemoved : SimEvent
    {
        public string AuraId;
        public AuraTarget Target;
        public bool Consumed;
    }

    public sealed class TelegraphStarted : SimEvent
    {
        public int AttackInstance;
        public string AttackId;
        public string Name;
        /// block | dodge | none
        public string Counter;
        public int ImpactTick;
        public int NormalWindowTicks;
        public int PerfectWindowTicks;
        public int Damage;
    }

    public sealed class DefenseUsed : SimEvent
    {
        public DefenseType Type;
        /// Tick after applying the player's latency offset.
        public int EffectiveTick;
    }

    public sealed class DefenseRejected : SimEvent
    {
        public DefenseType Type;
    }

    public sealed class AttackResolved : SimEvent
    {
        public int AttackInstance;
        public string AttackId;
        public AttackOutcome Outcome;
        public int DamageTaken;
        /// Signed: negative = pressed before impact. 0 when no defense counted.
        public int TimingErrorTicks;
    }

    public sealed class PerfectDefense : SimEvent
    {
        public DefenseType Type;
        public string AttackId;
    }

    public sealed class PlayerHit : SimEvent
    {
        public string AttackId;
        public int Damage;
        public int StaggerEndTick;
    }

    public sealed class FlowChanged : SimEvent
    {
        public int Value;
        public int Threshold;
        public bool Burst;
    }

    public sealed class BossPhaseChanged : SimEvent
    {
        public int Phase;
    }

    public sealed class FightEnded : SimEvent
    {
        public bool Victory;
        public FightEndReason Reason;
        public int DurationTicks;
        public long TotalDamage;
    }
}
