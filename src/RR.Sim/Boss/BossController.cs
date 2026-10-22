using System;
using System.Collections.Generic;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Boss
{
    /// Data-driven boss: HP, phases, seeded attack selection, telegraphs and
    /// vulnerability windows.
    public sealed class BossController
    {
        sealed class PendingAttack
        {
            public int Instance;
            public string Id;
            public AttackDef Def;
            public int ImpactTick;
            public int ResolveTick;
        }

        readonly CombatSim _sim;
        readonly List<PendingAttack> _pending = new List<PendingAttack>();
        readonly int _minGapTicks;
        int _nextAttackTick;
        int _instanceCounter;
        string _forcedNext;

        public BossDef Def { get; }
        public long Hp { get; private set; }
        public long MaxHp { get; }
        public int Phase { get; private set; }
        public bool Dead => Hp <= 0;
        public double HpPct => MaxHp > 0 ? Hp * 100.0 / MaxHp : 0;
        public int EnrageTick { get; }

        internal BossController(CombatSim sim, BossDef def)
        {
            _sim = sim;
            Def = def;
            MaxHp = def.Hp;
            Hp = def.Hp;
            EnrageTick = def.EnrageMs > 0 ? SimTime.MsToTicks(def.EnrageMs) : -1;
            _minGapTicks = SimTime.MsToTicks(sim.Data.Config.MinAttackGapMs);
            _nextAttackTick = SimTime.MsToTicks(def.FirstAttackMs);
        }

        internal void TakeDamage(int amount)
        {
            Hp = Math.Max(0, Hp - amount);
        }

        internal void Update(int t)
        {
            // Resolve due attacks (in start order).
            for (int i = 0; i < _pending.Count;)
            {
                var p = _pending[i];
                if (p.ResolveTick > t) { i++; continue; }
                _pending.RemoveAt(i);
                if (Dead) continue;
                _sim.Defense.Resolve(p.Instance, p.Id, p.Def, p.ImpactTick, t);
                OpenVulnerableWindows(p.Id, t);
            }

            if (Dead) return;

            UpdatePhase(t);

            if (t >= _nextAttackTick && _pending.Count == 0) StartNextAttack(t);
        }

        void UpdatePhase(int t)
        {
            while (Phase < Def.Phases.Count - 1 && HpPct <= Def.Phases[Phase].UntilHpPct)
            {
                Phase++;
                _sim.Publish(new BossPhaseChanged { Phase = Phase });
                var onEnter = Def.Phases[Phase].OnEnter;
                if (!string.IsNullOrEmpty(onEnter))
                {
                    _forcedNext = onEnter;
                    // Don't wait out the old cadence for the phase transition.
                    if (_pending.Count == 0) _nextAttackTick = Math.Min(_nextAttackTick, t);
                }
            }
        }

        void StartNextAttack(int t)
        {
            var phase = Def.Phases[Phase];
            string id;
            if (_forcedNext != null)
            {
                id = _forcedNext;
                _forcedNext = null;
            }
            else
            {
                if (phase.AttackPool.Count == 0) return;
                id = phase.AttackPool[_sim.Rng.NextInt(phase.AttackPool.Count)];
            }

            var def = Def.Attacks[id];
            int impact = t + SimTime.MsToTicks(def.TelegraphMs);
            var p = new PendingAttack
            {
                Instance = ++_instanceCounter,
                Id = id,
                Def = def,
                ImpactTick = impact,
                ResolveTick = impact + _sim.Defense.OffsetTicks,
            };
            _pending.Add(p);

            _sim.Publish(new TelegraphStarted
            {
                AttackInstance = p.Instance,
                AttackId = id,
                Name = def.Name ?? id,
                Counter = def.Counter,
                ImpactTick = impact,
                NormalWindowTicks = _sim.Defense.NormalWindowTicks(def),
                PerfectWindowTicks = _sim.Defense.PerfectWindowTicks(def),
                Damage = def.Damage,
            });

            int cadence = SimTime.MsToTicks(_sim.Rng.NextIntInclusive(phase.CadenceMs[0], phase.CadenceMs[1]));
            _nextAttackTick = Math.Max(t + cadence, p.ResolveTick + _minGapTicks);
        }

        void OpenVulnerableWindows(string attackId, int t)
        {
            foreach (var w in Def.VulnerableWindows)
            {
                if (w.AfterAttack != attackId) continue;
                var aura = _sim.Auras.GetOrCreateInline("vulnerable:" + attackId, () => new AuraDef
                {
                    Name = "Vulnerable",
                    Target = "target",
                    DurationMs = w.DurationMs,
                    DamageTakenPct = (w.DamageTakenMod - 1.0) * 100.0,
                    Icon = "💢",
                    Color = "#ffcc33",
                });
                _sim.Auras.Apply(aura, t);
            }
        }
    }
}
