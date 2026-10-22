using System.Collections.Generic;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Auras
{
    /// Trigger rules that roll the fight RNG and apply an effect.
    public sealed class ProcSystem
    {
        sealed class ProcInstance
        {
            public ProcDef Def;
            public int IcdTicks;
            public int ReadyTick;
            public double ChancePerTick;
        }

        readonly CombatSim _sim;
        readonly List<ProcInstance> _procs = new List<ProcInstance>();

        internal ProcSystem(CombatSim sim, IEnumerable<string> procIds)
        {
            _sim = sim;
            foreach (var id in procIds)
            {
                var def = sim.Data.GetProc(id);
                _procs.Add(new ProcInstance
                {
                    Def = def,
                    IcdTicks = SimTime.MsToTicks(def.IcdMs),
                    ChancePerTick = def.Ppm > 0 ? def.Ppm / (60.0 * SimTime.TicksPerSecond) : 0,
                });
            }
        }

        public int Count => _procs.Count;

        /// Fires every proc listening for this event whose filters match.
        internal void Fire(string evt, string sourceId, IList<string> tags, int t)
        {
            for (int i = 0; i < _procs.Count; i++)
            {
                var p = _procs[i];
                var trig = p.Def.Trigger;
                if (trig.Event != evt) continue;
                if (trig.Abilities != null && trig.Abilities.Count > 0 && (sourceId == null || !trig.Abilities.Contains(sourceId))) continue;
                if (trig.AbilityTags != null && trig.AbilityTags.Count > 0 && !AuraSystem.Intersects(trig.AbilityTags, tags)) continue;
                if (t < p.ReadyTick) continue;
                if (!_sim.Rng.Roll(p.Def.Chance)) continue;
                Trigger(p, t);
            }
        }

        /// PPM-style procs roll once per tick.
        internal void OnTick(int t)
        {
            for (int i = 0; i < _procs.Count; i++)
            {
                var p = _procs[i];
                if (p.Def.Trigger.Event != "onTick" || p.ChancePerTick <= 0) continue;
                if (t < p.ReadyTick) continue;
                if (!_sim.Rng.Roll(p.ChancePerTick)) continue;
                Trigger(p, t);
            }
        }

        void Trigger(ProcInstance p, int t)
        {
            p.ReadyTick = t + p.IcdTicks;
            var auraId = _sim.AuraIdForEffect(p.Def.Effect, p.Def.Id);
            _sim.Publish(new ProcTriggered
            {
                ProcId = p.Def.Id,
                GlowAbility = p.Def.Ui?.GlowAbility,
                AuraId = auraId,
            });
            _sim.ApplyEffect(p.Def.Effect, p.Def.Id, p.Def.Name, t);
        }
    }
}
