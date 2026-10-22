using System.Collections.Generic;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Defense
{
    /// Block/dodge presses, timing windows, perfect rewards, flow meter and stagger.
    ///
    /// Latency calibration: a press at raw tick T counts as happening at
    /// T - offset. To let late (but calibrated) presses count, every attack
    /// resolves offset ticks after its impact. With offset 0 it resolves on impact.
    public sealed class DefenseSystem
    {
        struct Press
        {
            public DefenseType Type;
            public int RawTick;
            public int EffectiveTick;
        }

        readonly CombatSim _sim;
        readonly DefenseConfig _cfg;
        readonly List<Press> _presses = new List<Press>();
        readonly int _recoveryTicks;
        readonly int _historyTicks;

        public int OffsetTicks { get; }
        public int RecoveryEndTick { get; private set; }
        public int Flow { get; private set; }

        internal DefenseSystem(CombatSim sim, int inputOffsetMs)
        {
            _sim = sim;
            _cfg = sim.Data.Config.Defense;
            OffsetTicks = SimTime.MsToTicks(inputOffsetMs);
            _recoveryTicks = SimTime.MsToTicks(_cfg.RecoveryMs);
            _historyTicks = SimTime.TicksPerSecond * 3;
        }

        internal void OnInput(DefenseType type, int t)
        {
            if (t < RecoveryEndTick)
            {
                _sim.Publish(new DefenseRejected { Type = type });
                return;
            }
            RecoveryEndTick = t + _recoveryTicks;
            var press = new Press { Type = type, RawTick = t, EffectiveTick = t - OffsetTicks };
            _presses.Add(press);
            // Keep a short history only.
            while (_presses.Count > 0 && _presses[0].RawTick < t - _historyTicks) _presses.RemoveAt(0);
            _sim.Player.InterruptCast();
            _sim.Publish(new DefenseUsed { Type = type, EffectiveTick = press.EffectiveTick });
        }

        public int NormalWindowTicks(AttackDef a) => SimTime.MsToTicks(a.NormalWindowMs > 0 ? a.NormalWindowMs : _cfg.NormalWindowMs);
        public int PerfectWindowTicks(AttackDef a) => SimTime.MsToTicks(a.PerfectWindowMs > 0 ? a.PerfectWindowMs : _cfg.PerfectWindowMs);

        /// Resolves an attack against the presses recorded for it. Called by the
        /// boss at impactTick + OffsetTicks.
        internal AttackOutcome Resolve(int instance, string attackId, AttackDef atk, int impactTick, int t)
        {
            if (atk.Counter == "none")
            {
                _sim.Publish(new AttackResolved
                {
                    AttackInstance = instance, AttackId = attackId, Outcome = AttackOutcome.Unavoidable, DamageTaken = atk.Damage,
                });
                if (!string.IsNullOrEmpty(atk.ApplyAura)) _sim.Auras.Apply(_sim.Data.GetAura(atk.ApplyAura), t);
                return AttackOutcome.Unavoidable;
            }

            int normal = NormalWindowTicks(atk);
            int perfect = PerfectWindowTicks(atk);

            // The latest press inside the window decides the outcome; a wrong
            // button there means the hit lands.
            Press? last = null;
            for (int i = _presses.Count - 1; i >= 0; i--)
            {
                var p = _presses[i];
                if (p.EffectiveTick > impactTick) continue;
                if (p.EffectiveTick < impactTick - normal) break;
                last = p;
                break;
            }

            var wanted = atk.Counter == "dodge" ? DefenseType.Dodge : DefenseType.Block;
            if (last.HasValue && last.Value.Type == wanted)
            {
                int error = last.Value.EffectiveTick - impactTick;
                bool isPerfect = -error <= perfect;
                if (isPerfect)
                {
                    OnPerfect(wanted, attackId, t);
                    _sim.Publish(new AttackResolved
                    {
                        AttackInstance = instance, AttackId = attackId, Outcome = AttackOutcome.Perfect, TimingErrorTicks = error,
                    });
                    return AttackOutcome.Perfect;
                }
                _sim.Publish(new AttackResolved
                {
                    AttackInstance = instance, AttackId = attackId, Outcome = AttackOutcome.Avoided, TimingErrorTicks = error,
                });
                return AttackOutcome.Avoided;
            }

            OnHit(attackId, atk, t);
            _sim.Publish(new AttackResolved
            {
                AttackInstance = instance, AttackId = attackId, Outcome = AttackOutcome.Hit, DamageTaken = atk.Damage,
                TimingErrorTicks = last.HasValue ? last.Value.EffectiveTick - impactTick : 0,
            });
            return AttackOutcome.Hit;
        }

        void OnPerfect(DefenseType type, string attackId, int t)
        {
            _sim.Publish(new PerfectDefense { Type = type, AttackId = attackId });
            if (!string.IsNullOrEmpty(_cfg.PerfectAura)) _sim.Auras.Apply(_sim.Data.GetAura(_cfg.PerfectAura), t);

            if (_cfg.Flow != null && _cfg.Flow.Enabled)
            {
                Flow++;
                bool burst = Flow >= _cfg.Flow.Threshold;
                if (burst)
                {
                    Flow = 0;
                    if (!string.IsNullOrEmpty(_cfg.Flow.Aura)) _sim.Auras.Apply(_sim.Data.GetAura(_cfg.Flow.Aura), t);
                }
                _sim.Publish(new FlowChanged { Value = burst ? _cfg.Flow.Threshold : Flow, Threshold = _cfg.Flow.Threshold, Burst = burst });
                if (burst) _sim.Publish(new FlowChanged { Value = 0, Threshold = _cfg.Flow.Threshold });
            }

            _sim.Procs.Fire(type == DefenseType.Block ? "onPerfectBlock" : "onPerfectDodge", attackId, null, t);
        }

        void OnHit(string attackId, AttackDef atk, int t)
        {
            int stagger = SimTime.MsToTicks(atk.StaggerMs > 0 ? atk.StaggerMs : _cfg.StaggerMs);
            var player = _sim.Player;
            if (t + stagger > player.StaggerEndTick) player.StaggerEndTick = t + stagger;
            player.InterruptCast();
            _sim.Publish(new PlayerHit { AttackId = attackId, Damage = atk.Damage, StaggerEndTick = player.StaggerEndTick });

            if (Flow > 0 && _cfg.Flow != null && _cfg.Flow.Enabled)
            {
                Flow = 0;
                _sim.Publish(new FlowChanged { Value = 0, Threshold = _cfg.Flow.Threshold });
            }
            if (!string.IsNullOrEmpty(atk.ApplyAura)) _sim.Auras.Apply(_sim.Data.GetAura(atk.ApplyAura), t);
        }
    }
}
