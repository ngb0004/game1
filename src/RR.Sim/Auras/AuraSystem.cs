using System;
using System.Collections.Generic;
using RR.Sim.Combat;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Auras
{
    public sealed class AuraInstance
    {
        public AuraDef Def;
        public AuraTarget Target;
        public int Stacks;
        public int StartTick;
        public int ExpiresTick;
        public int TickInterval;
        /// -1 for non-periodic auras.
        public int NextTickAt = -1;
    }

    /// Buffs, debuffs, DoTs and consumable empowers on both the player and the boss.
    public sealed class AuraSystem
    {
        const int Permanent = int.MaxValue;

        readonly CombatSim _sim;
        readonly List<AuraInstance> _player = new List<AuraInstance>();
        readonly List<AuraInstance> _boss = new List<AuraInstance>();
        readonly Dictionary<string, AuraDef> _inline = new Dictionary<string, AuraDef>();
        readonly List<AuraInstance> _scratch = new List<AuraInstance>();

        internal AuraSystem(CombatSim sim)
        {
            _sim = sim;
        }

        public IReadOnlyList<AuraInstance> PlayerAuras => _player;
        public IReadOnlyList<AuraInstance> BossAuras => _boss;

        List<AuraInstance> ListFor(AuraTarget target) => target == AuraTarget.Player ? _player : _boss;

        public static AuraTarget TargetOf(AuraDef def) => def.Target == "target" ? AuraTarget.Boss : AuraTarget.Player;

        public AuraInstance Get(AuraTarget target, string id)
        {
            var list = ListFor(target);
            for (int i = 0; i < list.Count; i++)
                if (list[i].Def.Id == id) return list[i];
            return null;
        }

        public bool Has(AuraTarget target, string id) => Get(target, id) != null;

        /// Inline auras (from empowerNextCast / hasteWindow effects) are created
        /// once per id and reused, so their identity is stable across a fight.
        internal AuraDef GetOrCreateInline(string id, Func<AuraDef> create)
        {
            if (!_inline.TryGetValue(id, out var def))
            {
                def = create();
                def.Id = id;
                _inline[id] = def;
            }
            return def;
        }

        internal void Apply(AuraDef def, int t, int stacks = 1)
        {
            var target = TargetOf(def);
            var list = ListFor(target);
            int duration = def.DurationMs > 0 ? SimTime.MsToTicks(def.DurationMs) : 0;
            stacks = Math.Max(1, stacks);

            var a = Get(target, def.Id);
            bool refreshed = a != null;
            if (a == null)
            {
                a = new AuraInstance
                {
                    Def = def,
                    Target = target,
                    Stacks = Math.Min(stacks, Math.Max(1, def.MaxStacks)),
                    StartTick = t,
                    ExpiresTick = duration > 0 ? t + duration : Permanent,
                    TickInterval = def.TickMs > 0 ? SimTime.MsToTicks(def.TickMs) : 0,
                };
                if (a.TickInterval > 0 && def.TickDamage != null) a.NextTickAt = t + a.TickInterval;
                list.Add(a);
            }
            else
            {
                switch (def.Refresh)
                {
                    case "pandemic":
                        if (duration > 0)
                        {
                            int remaining = Math.Max(0, a.ExpiresTick - t);
                            int carry = Math.Min(remaining, (int)(duration * _sim.Data.Config.PandemicPct));
                            a.ExpiresTick = t + duration + carry;
                        }
                        break;
                    case "addStack":
                        a.Stacks = Math.Min(a.Stacks + stacks, Math.Max(1, def.MaxStacks));
                        if (duration > 0) a.ExpiresTick = t + duration;
                        break;
                    default:
                        if (duration > 0) a.ExpiresTick = t + duration;
                        break;
                }
                a.StartTick = t;
            }

            _sim.Publish(new AuraApplied
            {
                AuraId = def.Id,
                Name = def.Name,
                Target = target,
                Stacks = a.Stacks,
                StartTick = t,
                ExpiresTick = a.ExpiresTick,
                Refreshed = refreshed,
            });
        }

        internal bool Remove(AuraTarget target, string id, bool consumed)
        {
            var list = ListFor(target);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Def.Id != id) continue;
                list.RemoveAt(i);
                _sim.Publish(new AuraRemoved { AuraId = id, Target = target, Consumed = consumed });
                return true;
            }
            return false;
        }

        /// DoT ticks first (a tick landing on the expiry tick still counts), then expiry.
        internal void Update(int t)
        {
            TickPeriodic(_boss, t);
            TickPeriodic(_player, t);
            Expire(_player, t);
            Expire(_boss, t);
        }

        void TickPeriodic(List<AuraInstance> list, int t)
        {
            // Index loop: a tick can trigger procs that append new auras.
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.NextTickAt < 0) continue;
                while (a.NextTickAt <= t && a.NextTickAt <= a.ExpiresTick)
                {
                    a.NextTickAt += a.TickInterval;
                    _sim.DealDamage(a.Def.Id, a.Def.TickDamage, a.Def.Tags, a.Stacks, true, false);
                }
            }
        }

        void Expire(List<AuraInstance> list, int t)
        {
            _scratch.Clear();
            for (int i = 0; i < list.Count; i++)
                if (list[i].ExpiresTick <= t) _scratch.Add(list[i]);
            foreach (var a in _scratch)
            {
                list.Remove(a);
                _sim.Publish(new AuraRemoved { AuraId = a.Def.Id, Target = a.Target, Consumed = false });
            }
        }

        internal void AddPlayerStats(StatSum sum)
        {
            for (int i = 0; i < _player.Count; i++) sum.Add(_player[i].Def.Stats, _player[i].Stacks);
        }

        /// Product of player damage-done auras that apply to these tags.
        internal double DamageDoneMult(IList<string> tags)
        {
            double m = 1.0;
            for (int i = 0; i < _player.Count; i++)
            {
                var d = _player[i].Def;
                if (d.DamageDonePct == 0) continue;
                if (d.DamageDoneTags != null && d.DamageDoneTags.Count > 0 && !Intersects(d.DamageDoneTags, tags)) continue;
                m *= 1 + d.DamageDonePct * _player[i].Stacks / 100.0;
            }
            return m;
        }

        /// Product of boss damage-taken auras (debuffs, vulnerability windows).
        internal double DamageTakenMult()
        {
            double m = 1.0;
            for (int i = 0; i < _boss.Count; i++)
            {
                var d = _boss[i].Def;
                if (d.DamageTakenPct != 0) m *= 1 + d.DamageTakenPct * _boss[i].Stacks / 100.0;
            }
            return m;
        }

        /// Removes every empower that this cast matches and returns the combined multiplier.
        internal double ConsumeEmpowers(AbilityDef ability)
        {
            double m = 1.0;
            _scratch.Clear();
            for (int i = 0; i < _player.Count; i++)
            {
                var c = _player[i].Def.ConsumedBy;
                if (c == null) continue;
                bool match = (c.Abilities != null && c.Abilities.Contains(ability.Id)) ||
                             (c.AbilityTags != null && Intersects(c.AbilityTags, ability.Tags));
                if (!match) continue;
                m *= 1 + _player[i].Def.EmpowerPct / 100.0;
                _scratch.Add(_player[i]);
            }
            foreach (var a in _scratch) Remove(AuraTarget.Player, a.Def.Id, true);
            return m;
        }

        internal static bool Intersects(IList<string> a, IList<string> b)
        {
            if (a == null || b == null) return false;
            for (int i = 0; i < a.Count; i++)
                if (b.Contains(a[i])) return true;
            return false;
        }
    }
}
