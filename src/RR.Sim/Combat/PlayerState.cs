using System;
using System.Collections.Generic;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Combat
{
    public sealed class AbilityState
    {
        public AbilityDef Def;
        public int Charges;
        public int CooldownTicks;
        /// -1 when no recharge is running.
        public int RechargeStartTick = -1;
        public int RechargeEndTick = -1;
    }

    /// Player side of combat: kit, GCD, spell queue, casting, cooldowns,
    /// resource and stagger.
    public sealed class PlayerState
    {
        readonly CombatSim _sim;
        readonly List<AbilityState> _kit = new List<AbilityState>();
        readonly Dictionary<string, AbilityState> _byId = new Dictionary<string, AbilityState>();
        readonly StatSum _baseAndGear = new StatSum();
        readonly int _queueTicks;

        public int Resource { get; private set; }
        public int ResourceMax { get; }
        public int GcdEndTick { get; private set; }
        public AbilityDef Casting { get; private set; }
        public int CastEndTick { get; private set; } = -1;
        public int StaggerEndTick { get; internal set; }
        public AbilityDef Queued { get; private set; }
        /// Ticks during which a GCD (or cast) was rolling. For GCD uptime.
        public int GcdActiveTicks { get; private set; }
        public IReadOnlyList<AbilityState> Kit => _kit;

        internal PlayerState(CombatSim sim, ClassDef cls, Gear.Loadout gear, int level)
        {
            _sim = sim;
            ResourceMax = cls.Resource.Max;
            Resource = Math.Min(cls.Resource.Start, ResourceMax);
            _queueTicks = SimTime.MsToTicks(sim.Data.Config.SpellQueueMs);
            _baseAndGear.Add(cls.BaseStats);
            _baseAndGear.FlatAttackPower += gear.Stats.FlatAttackPower;
            _baseAndGear.AttackPowerPct += gear.Stats.AttackPowerPct;
            _baseAndGear.CritPct += gear.Stats.CritPct;
            _baseAndGear.HastePct += gear.Stats.HastePct;
            _baseAndGear.MasteryPct += gear.Stats.MasteryPct;

            foreach (var id in cls.Abilities)
            {
                var def = sim.Data.GetAbility(id);
                if (def.UnlockLevel <= level) AddAbility(def);
            }
            foreach (var id in gear.UseAbilities) AddAbility(sim.Data.GetAbility(id));
        }

        void AddAbility(AbilityDef def)
        {
            if (_byId.ContainsKey(def.Id)) return;
            var st = new AbilityState
            {
                Def = def,
                Charges = def.Charges,
                CooldownTicks = SimTime.MsToTicks(def.CooldownMs),
            };
            _kit.Add(st);
            _byId[def.Id] = st;
        }

        public AbilityState GetState(string abilityId) =>
            abilityId != null && _byId.TryGetValue(abilityId, out var st) ? st : null;

        public bool Knows(string abilityId) => _byId.ContainsKey(abilityId);

        public Stats GetStats()
        {
            var sum = new StatSum
            {
                FlatAttackPower = _baseAndGear.FlatAttackPower,
                AttackPowerPct = _baseAndGear.AttackPowerPct,
                CritPct = _baseAndGear.CritPct,
                HastePct = _baseAndGear.HastePct,
                MasteryPct = _baseAndGear.MasteryPct,
            };
            _sim.Auras.AddPlayerStats(sum);
            return sum.ToStats();
        }

        public int CurrentGcdTicks() => DamageFormula.GcdTicks(_sim.Data.Config, GetStats().HastePct);

        internal void Update(int t)
        {
            // Recharges complete in kit order so events are deterministic.
            for (int i = 0; i < _kit.Count; i++)
            {
                var st = _kit[i];
                bool changed = false;
                while (st.RechargeEndTick >= 0 && st.RechargeEndTick <= t)
                {
                    st.Charges++;
                    changed = true;
                    if (st.Charges < st.Def.Charges)
                    {
                        st.RechargeStartTick = st.RechargeEndTick;
                        st.RechargeEndTick += st.CooldownTicks;
                    }
                    else
                    {
                        st.RechargeStartTick = -1;
                        st.RechargeEndTick = -1;
                    }
                }
                if (changed) PublishCooldown(st);
            }

            if (Casting != null && CastEndTick <= t)
            {
                var def = Casting;
                Casting = null;
                CastEndTick = -1;
                Resolve(def, t);
            }

            if (Queued != null)
            {
                var q = Queued;
                int ready = ReadyTick(q, t);
                if (ready <= t)
                {
                    Queued = null;
                    TryExecuteNow(q, t);
                }
                else if (ready - t > _queueTicks)
                {
                    Queued = null;
                    Fail(q.Id, CastFailReason.QueueExpired);
                }
            }
        }

        internal void AccountGcd(int t)
        {
            if (t < GcdEndTick || Casting != null) GcdActiveTicks++;
        }

        /// Earliest tick the ability could start, ignoring resource and conditions.
        public int ReadyTick(AbilityDef def, int t)
        {
            int r = t;
            if (def.OnGcd)
            {
                r = Math.Max(r, GcdEndTick);
                if (Casting != null) r = Math.Max(r, CastEndTick);
            }
            r = Math.Max(r, StaggerEndTick);
            var st = _byId[def.Id];
            if (st.Charges == 0) r = Math.Max(r, st.RechargeEndTick);
            return r;
        }

        /// Null when the ability could be used right now.
        public CastFailReason? CheckUsable(string abilityId, int t)
        {
            var st = GetState(abilityId);
            if (st == null) return CastFailReason.UnknownAbility;
            if (ReadyTick(st.Def, t) > t) return TimingFailReason(st, t);
            return CheckConditions(st.Def);
        }

        CastFailReason? CheckConditions(AbilityDef def)
        {
            if (Resource < def.ResourceCost) return CastFailReason.NotEnoughResource;
            if (!string.IsNullOrEmpty(def.RequiresAura) && !_sim.Auras.Has(AuraTarget.Player, def.RequiresAura))
                return CastFailReason.MissingAura;
            if (def.RequiresTargetHpBelowPct > 0 && _sim.Boss.HpPct > def.RequiresTargetHpBelowPct)
                return CastFailReason.TargetHpTooHigh;
            return null;
        }

        CastFailReason TimingFailReason(AbilityState st, int t)
        {
            if (StaggerEndTick > t) return CastFailReason.Staggered;
            if (st.Charges == 0) return CastFailReason.OnCooldown;
            if (st.Def.OnGcd && Casting != null) return CastFailReason.Casting;
            return CastFailReason.OnGlobalCooldown;
        }

        /// Handles a CastAbility input, including the spell queue window.
        internal void OnCastInput(string abilityId, int t)
        {
            var st = GetState(abilityId);
            if (st == null)
            {
                Fail(abilityId, CastFailReason.UnknownAbility);
                return;
            }
            var def = st.Def;
            int ready = ReadyTick(def, t);
            if (ready <= t)
            {
                TryExecuteNow(def, t);
                return;
            }
            if (def.OnGcd && ready - t <= _queueTicks)
            {
                // Only one queued ability; the latest press wins.
                Queued = def;
                _sim.Publish(new AbilityQueued { AbilityId = def.Id });
                return;
            }
            Fail(def.Id, TimingFailReason(st, t));
        }

        void TryExecuteNow(AbilityDef def, int t)
        {
            var reason = CheckConditions(def);
            if (reason != null)
            {
                Fail(def.Id, reason.Value);
                return;
            }
            Execute(def, t);
        }

        void Execute(AbilityDef def, int t)
        {
            var st = _byId[def.Id];
            if (st.CooldownTicks > 0)
            {
                st.Charges--;
                if (st.RechargeEndTick < 0)
                {
                    st.RechargeStartTick = t;
                    st.RechargeEndTick = t + st.CooldownTicks;
                }
                PublishCooldown(st);
            }

            if (def.OnGcd)
            {
                GcdEndTick = t + CurrentGcdTicks();
                _sim.Publish(new GcdStarted { EndTick = GcdEndTick });
            }

            _sim.Publish(new AbilityUsed { AbilityId = def.Id });

            if (def.ResourceCost > 0)
            {
                Resource -= def.ResourceCost;
                _sim.Publish(new ResourceChanged { Value = Resource, Max = ResourceMax, Delta = -def.ResourceCost, SourceId = def.Id });
                _sim.Procs.Fire("onResourceSpent", def.Id, def.Tags, t);
            }

            if (!string.IsNullOrEmpty(def.ConsumesAura)) _sim.Auras.Remove(AuraTarget.Player, def.ConsumesAura, true);

            if (def.CastTimeMs > 0)
            {
                Casting = def;
                CastEndTick = t + DamageFormula.HastedTicks(def.CastTimeMs, GetStats().HastePct);
                _sim.Publish(new CastStarted { AbilityId = def.Id, EndTick = CastEndTick });
                _sim.Procs.Fire("onCast", def.Id, def.Tags, t);
                return;
            }

            Resolve(def, t);
            _sim.Procs.Fire("onCast", def.Id, def.Tags, t);
        }

        void Resolve(AbilityDef def, int t)
        {
            double mult = _sim.Auras.ConsumeEmpowers(def);
            if (def.TargetHpBonus != null && _sim.Boss.HpPct <= def.TargetHpBonus.BelowPct) mult *= def.TargetHpBonus.Mult;

            if (def.Damage != null) _sim.DealDamage(def.Id, def.Damage, def.Tags, mult, false, false);
            if (def.ResourceGain > 0) GainResource(def.ResourceGain, def.Id);
            foreach (var e in def.Effects) _sim.ApplyEffect(e, def.Id, def.Name, t);
        }

        public void GainResource(int amount, string sourceId)
        {
            if (amount <= 0) return;
            int target = Resource + amount;
            int next = Math.Min(target, ResourceMax);
            int delta = next - Resource;
            Resource = next;
            _sim.Publish(new ResourceChanged
            {
                Value = Resource, Max = ResourceMax, Delta = delta, Wasted = target - next, SourceId = sourceId,
            });
        }

        /// Restores one charge (fully resets single-charge abilities).
        internal void ResetCooldown(string abilityId)
        {
            var st = GetState(abilityId);
            if (st == null || st.Charges >= st.Def.Charges) return;
            st.Charges++;
            if (st.Charges >= st.Def.Charges)
            {
                st.RechargeStartTick = -1;
                st.RechargeEndTick = -1;
            }
            PublishCooldown(st);
        }

        internal void InterruptCast()
        {
            if (Casting == null) return;
            var def = Casting;
            Casting = null;
            CastEndTick = -1;
            _sim.Publish(new CastInterrupted { AbilityId = def.Id });
        }

        void PublishCooldown(AbilityState st)
        {
            _sim.Publish(new CooldownChanged
            {
                AbilityId = st.Def.Id,
                Charges = st.Charges,
                MaxCharges = st.Def.Charges,
                RechargeStartTick = st.RechargeStartTick,
                RechargeEndTick = st.RechargeEndTick,
            });
        }

        void Fail(string abilityId, CastFailReason reason) =>
            _sim.Publish(new CastFailed { AbilityId = abilityId, Reason = reason });
    }
}
