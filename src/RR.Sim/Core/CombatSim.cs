using System;
using System.Collections.Generic;
using RR.Sim.Auras;
using RR.Sim.Boss;
using RR.Sim.Combat;
using RR.Sim.Core;
using RR.Sim.Data;
using RR.Sim.Defense;
using RR.Sim.Gear;

namespace RR.Sim
{
    /// One deterministic fight. Same data + setup + inputs = identical events.
    ///
    /// Tick order:
    ///   1. auras (DoT ticks, then expiry)
    ///   2. player (recharges, cast completion, queued ability)
    ///   3. inputs scheduled for this tick
    ///   4. boss (resolve attacks, phase change, next telegraph)
    ///   5. PPM procs
    ///   6. bookkeeping and end-of-fight checks
    public sealed class CombatSim
    {
        readonly List<InputCommand> _pending = new List<InputCommand>();
        readonly List<InputCommand> _processed = new List<InputCommand>();
        bool _endRequested;
        long _totalDamage;

        public GameData Data { get; }
        public FightSetup Setup { get; }
        public ClassDef Class { get; }
        public Loadout Gear { get; }
        public EventBus Events { get; } = new EventBus();

        /// The tick being processed during Step, and the next tick otherwise.
        public int Tick { get; private set; }
        public bool Ended { get; private set; }
        public FightEnded Result { get; private set; }
        public long TotalDamage => _totalDamage;

        public PlayerState Player { get; }
        public BossController Boss { get; }
        public AuraSystem Auras { get; }
        public ProcSystem Procs { get; }
        public DefenseSystem Defense { get; }
        internal SimRandom Rng { get; }

        /// Inputs in the exact order they were applied. This is what goes in the FightLog.
        public IReadOnlyList<InputCommand> ProcessedInputs => _processed;

        public CombatSim(GameData data, FightSetup setup)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Class = data.GetClass(setup.ClassId);
            var bossDef = data.GetBoss(setup.BossId);

            Rng = new SimRandom(setup.Seed);
            Gear = Loadout.Resolve(data, setup.Gear ?? Class.DefaultGear);

            int offset = Math.Max(0, Math.Min(setup.InputOffsetMs, data.Config.Defense.MaxInputOffsetMs));
            Auras = new AuraSystem(this);
            Defense = new DefenseSystem(this, offset);
            Boss = new BossController(this, bossDef);
            Player = new PlayerState(this, Class, Gear, setup.Level);

            var procIds = new List<string>(Class.Procs);
            procIds.AddRange(Gear.Procs);
            Procs = new ProcSystem(this, procIds);
        }

        public FightLog CreateLog() => new FightLog
        {
            DataVersion = Data.Version,
            Setup = Setup,
            Inputs = new List<InputCommand>(_processed),
        };

        /// Schedules an input. Inputs in the past are applied on the current tick.
        public void Enqueue(InputCommand cmd)
        {
            if (Ended || cmd == null) return;
            var c = new InputCommand(Math.Max(cmd.Tick, Tick), cmd.Type, cmd.AbilityId);
            int i = _pending.Count;
            while (i > 0 && _pending[i - 1].Tick > c.Tick) i--;
            _pending.Insert(i, c);
        }

        public void Step()
        {
            if (Ended) return;
            int t = Tick;

            if (t == 0)
            {
                Publish(new FightStarted
                {
                    ClassId = Class.Id,
                    BossId = Boss.Def.Id,
                    BossHp = Boss.Hp,
                    BossMaxHp = Boss.MaxHp,
                    EnrageTick = Boss.EnrageTick,
                    Resource = Player.Resource,
                    ResourceMax = Player.ResourceMax,
                });
            }

            Auras.Update(t);
            Player.Update(t);
            ProcessInputs(t);
            Boss.Update(t);
            Procs.OnTick(t);
            Player.AccountGcd(t);
            CheckEnd(t);

            Tick = t + 1;
        }

        /// Runs ticks until Tick == targetTick or the fight ends.
        public void AdvanceTo(int targetTick)
        {
            while (!Ended && Tick < targetTick) Step();
        }

        public void RunToEnd(int maxTicks = SimTime.TicksPerSecond * 60 * 30)
        {
            while (!Ended && Tick < maxTicks) Step();
        }

        void ProcessInputs(int t)
        {
            while (_pending.Count > 0 && _pending[0].Tick <= t && !Ended)
            {
                var cmd = _pending[0];
                _pending.RemoveAt(0);
                _processed.Add(cmd);
                switch (cmd.Type)
                {
                    case InputType.CastAbility:
                        Player.OnCastInput(cmd.AbilityId, t);
                        break;
                    case InputType.Block:
                        Defense.OnInput(DefenseType.Block, t);
                        break;
                    case InputType.Dodge:
                        Defense.OnInput(DefenseType.Dodge, t);
                        break;
                    case InputType.EndFight:
                        _endRequested = true;
                        break;
                }
            }
        }

        void CheckEnd(int t)
        {
            int elapsed = t + 1;
            bool dummy = Boss.Def.IsDummy;
            if (Boss.Dead) End(true, FightEndReason.Killed, elapsed);
            else if (_endRequested) End(false, FightEndReason.Abandoned, elapsed);
            else if (Boss.EnrageTick > 0 && elapsed >= Boss.EnrageTick)
                End(dummy, dummy ? FightEndReason.DummyComplete : FightEndReason.Enraged, elapsed);
        }

        void End(bool victory, FightEndReason reason, int duration)
        {
            Ended = true;
            Result = new FightEnded { Victory = victory, Reason = reason, DurationTicks = duration, TotalDamage = _totalDamage };
            Publish(Result);
        }

        internal void Publish(SimEvent e)
        {
            e.Tick = Tick;
            Events.Publish(e);
        }

        /// The one place damage is calculated and applied.
        internal void DealDamage(string sourceId, DamageDef dmg, IList<string> tags, double extraMult, bool periodic, bool fromProc)
        {
            if (dmg == null || Boss.Dead) return;
            var stats = Player.GetStats();
            double raw = DamageFormula.Raw(dmg, stats.AttackPower);
            double mods = extraMult * Auras.DamageDoneMult(tags) * Auras.DamageTakenMult();
            bool crit = Rng.Roll(stats.CritPct / 100.0);
            int amount = DamageFormula.Final(raw, mods, crit, Data.Config.CritMultiplier, stats.MasteryPct);
            amount = (int)Math.Min(amount, Boss.Hp);

            Boss.TakeDamage(amount);
            _totalDamage += amount;
            Publish(new DamageDealt
            {
                SourceId = sourceId,
                Amount = amount,
                Crit = crit,
                Periodic = periodic,
                FromProc = fromProc,
                BossHp = Boss.Hp,
                BossMaxHp = Boss.MaxHp,
            });

            // Proc damage never triggers further procs.
            if (fromProc) return;
            Procs.Fire("onHit", sourceId, tags, Tick);
            if (crit) Procs.Fire("onCrit", sourceId, tags, Tick);
        }

        /// Aura id an effect will apply, so presentation can tie a glow to it.
        internal string AuraIdForEffect(EffectDef e, string sourceId)
        {
            switch (e.Type)
            {
                case "applyAura": return e.Aura;
                case "empowerNextCast": return "empower:" + sourceId;
                case "hasteWindow": return "haste:" + sourceId;
                default: return null;
            }
        }

        internal void ApplyEffect(EffectDef e, string sourceId, string sourceName, int t)
        {
            switch (e.Type)
            {
                case "resetCooldown":
                    Player.ResetCooldown(e.Ability);
                    break;
                case "applyAura":
                    Auras.Apply(Data.GetAura(e.Aura), t, Math.Max(1, e.Amount));
                    break;
                case "removeAura":
                    Auras.Remove(AuraSystem.TargetOf(Data.GetAura(e.Aura)), e.Aura, false);
                    break;
                case "empowerNextCast":
                {
                    var def = Auras.GetOrCreateInline(AuraIdForEffect(e, sourceId), () => new AuraDef
                    {
                        Name = e.Name ?? sourceName ?? "Empowered",
                        Target = "self",
                        DurationMs = e.DurationMs > 0 ? e.DurationMs : 10000,
                        EmpowerPct = e.Pct,
                        ConsumedBy = new ConsumeDef
                        {
                            Abilities = e.Ability != null ? new List<string> { e.Ability } : new List<string>(),
                            AbilityTags = e.AbilityTags ?? new List<string>(),
                        },
                    });
                    Auras.Apply(def, t);
                    break;
                }
                case "hasteWindow":
                {
                    var def = Auras.GetOrCreateInline(AuraIdForEffect(e, sourceId), () => new AuraDef
                    {
                        Name = e.Name ?? sourceName ?? "Haste",
                        Target = "self",
                        DurationMs = e.DurationMs > 0 ? e.DurationMs : 6000,
                        Stats = new StatBlock { HastePct = e.Pct },
                    });
                    Auras.Apply(def, t);
                    break;
                }
                case "extraDamage":
                    DealDamage(sourceId, e.Damage, new List<string> { "proc" }, 1.0, false, true);
                    break;
                case "gainResource":
                    Player.GainResource(e.Amount, sourceId);
                    break;
                default:
                    throw new GameDataException("Unknown effect type: " + e.Type);
            }
        }

        /// Replays a log headlessly. Throws if the data version does not match.
        public static CombatSim Replay(GameData data, FightLog log, Action<SimEvent> listener = null)
        {
            if (log.DataVersion != data.Version)
                throw new InvalidOperationException("Fight log was recorded with data " + log.DataVersion + ", current data is " + data.Version);
            var sim = new CombatSim(data, log.Setup);
            if (listener != null) sim.Events.Subscribe(listener);
            foreach (var cmd in log.Inputs) sim.Enqueue(cmd);
            sim.RunToEnd();
            return sim;
        }
    }
}
