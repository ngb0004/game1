using System.Collections.Generic;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Client
{
    public sealed class AbilityHud
    {
        public AbilityDef Def;
        public int Charges;
        public int MaxCharges;
        public int RechargeStartTick = -1;
        public int RechargeEndTick = -1;
        public bool Glow;
        public bool Queued;
    }

    public sealed class AuraHud
    {
        public string Id;
        public string Name;
        public string Icon;
        public string Color;
        public AuraTarget Target;
        public int Stacks;
        public int StartTick;
        public int ExpiresTick;
    }

    public sealed class TelegraphHud
    {
        public int Instance;
        public string AttackId;
        public string Name;
        public string Counter;
        public int StartTick;
        public int ImpactTick;
        public int NormalWindowTicks;
        public int PerfectWindowTicks;
        public int Damage;
        public bool Resolved;
        public int ResolvedTick;
        public AttackOutcome Outcome;
    }

    /// Everything the HUD shows, built only from sim events (the presentation
    /// never reads sim internals). Static ability/aura data is fine to read.
    public sealed class HudState
    {
        readonly GameData _data;
        readonly Dictionary<string, AbilityHud> _abilities = new Dictionary<string, AbilityHud>();
        readonly Dictionary<string, string> _glowByAura = new Dictionary<string, string>();

        public string BossId;
        public long BossHp;
        public long BossMaxHp;
        public int Phase;
        public int EnrageTick = -1;
        public int Resource;
        public int ResourceMax;
        public int GcdStartTick;
        public int GcdEndTick;
        public string CastingId;
        public int CastStartTick;
        public int CastEndTick;
        public int StaggerEndTick;
        public int Flow;
        public int FlowThreshold = 5;
        public long TotalDamage;
        public int LastTick;
        public FightEnded Result;
        public readonly List<AuraHud> PlayerAuras = new List<AuraHud>();
        public readonly List<AuraHud> BossAuras = new List<AuraHud>();
        public readonly List<TelegraphHud> Telegraphs = new List<TelegraphHud>();

        public HudState(GameData data, ClientKit kit)
        {
            _data = data;
            foreach (var a in kit.All())
                _abilities[a.Id] = new AbilityHud { Def = a, Charges = a.Charges, MaxCharges = a.Charges };
        }

        public AbilityHud Ability(string id) => id != null && _abilities.TryGetValue(id, out var a) ? a : null;

        public bool HasPlayerAura(string id)
        {
            foreach (var a in PlayerAuras) if (a.Id == id) return true;
            return false;
        }

        public double BossHpPct => BossMaxHp > 0 ? BossHp * 100.0 / BossMaxHp : 0;

        /// Resource and condition checks for the disabled look; timing is shown by the sweeps.
        public bool MeetsRequirements(AbilityDef a)
        {
            if (Resource < a.ResourceCost) return false;
            if (!string.IsNullOrEmpty(a.RequiresAura) && !HasPlayerAura(a.RequiresAura)) return false;
            if (a.RequiresTargetHpBelowPct > 0 && BossHpPct > a.RequiresTargetHpBelowPct) return false;
            return true;
        }

        public double Dps(int tick) => tick > 0 ? TotalDamage / SimTime.TicksToSeconds(tick) : 0;

        public void Apply(SimEvent e)
        {
            LastTick = e.Tick;
            switch (e)
            {
                case FightStarted s:
                    BossId = s.BossId;
                    BossHp = s.BossHp;
                    BossMaxHp = s.BossMaxHp;
                    EnrageTick = s.EnrageTick;
                    Resource = s.Resource;
                    ResourceMax = s.ResourceMax;
                    break;
                case DamageDealt d:
                    BossHp = d.BossHp;
                    TotalDamage += d.Amount;
                    break;
                case ResourceChanged r:
                    Resource = r.Value;
                    ResourceMax = r.Max;
                    break;
                case GcdStarted g:
                    GcdStartTick = g.Tick;
                    GcdEndTick = g.EndTick;
                    break;
                case CastStarted c:
                    CastingId = c.AbilityId;
                    CastStartTick = c.Tick;
                    CastEndTick = c.EndTick;
                    break;
                case CastInterrupted _:
                    CastingId = null;
                    break;
                case CooldownChanged cd:
                {
                    var a = Ability(cd.AbilityId);
                    if (a == null) break;
                    a.Charges = cd.Charges;
                    a.MaxCharges = cd.MaxCharges;
                    a.RechargeStartTick = cd.RechargeStartTick;
                    a.RechargeEndTick = cd.RechargeEndTick;
                    break;
                }
                case AbilityQueued q:
                    foreach (var a in _abilities.Values) a.Queued = a.Def.Id == q.AbilityId;
                    break;
                case AbilityUsed u:
                {
                    foreach (var a in _abilities.Values) a.Queued = false;
                    var ab = Ability(u.AbilityId);
                    if (ab != null) ab.Glow = false;
                    if (CastingId == u.AbilityId) CastingId = null;
                    break;
                }
                case CastFailed f:
                {
                    var a = Ability(f.AbilityId);
                    if (a != null && f.Reason == CastFailReason.QueueExpired) a.Queued = false;
                    break;
                }
                case ProcTriggered p:
                {
                    var a = Ability(p.GlowAbility);
                    if (a == null) break;
                    a.Glow = true;
                    if (p.AuraId != null) _glowByAura[p.AuraId] = a.Def.Id;
                    break;
                }
                case AuraApplied ap:
                    UpsertAura(ap);
                    break;
                case AuraRemoved rm:
                {
                    var list = rm.Target == AuraTarget.Player ? PlayerAuras : BossAuras;
                    list.RemoveAll(a => a.Id == rm.AuraId);
                    if (_glowByAura.TryGetValue(rm.AuraId, out var glowing))
                    {
                        _glowByAura.Remove(rm.AuraId);
                        var a = Ability(glowing);
                        if (a != null) a.Glow = false;
                    }
                    break;
                }
                case TelegraphStarted tg:
                    Telegraphs.Add(new TelegraphHud
                    {
                        Instance = tg.AttackInstance,
                        AttackId = tg.AttackId,
                        Name = tg.Name,
                        Counter = tg.Counter,
                        StartTick = tg.Tick,
                        ImpactTick = tg.ImpactTick,
                        NormalWindowTicks = tg.NormalWindowTicks,
                        PerfectWindowTicks = tg.PerfectWindowTicks,
                        Damage = tg.Damage,
                    });
                    break;
                case AttackResolved ar:
                    foreach (var t in Telegraphs)
                    {
                        if (t.Instance != ar.AttackInstance) continue;
                        t.Resolved = true;
                        t.ResolvedTick = ar.Tick;
                        t.Outcome = ar.Outcome;
                    }
                    break;
                case PlayerHit h:
                    StaggerEndTick = h.StaggerEndTick;
                    CastingId = null;
                    break;
                case FlowChanged fl:
                    Flow = fl.Value;
                    FlowThreshold = fl.Threshold;
                    break;
                case BossPhaseChanged ph:
                    Phase = ph.Phase;
                    break;
                case FightEnded end:
                    Result = end;
                    break;
            }
        }

        void UpsertAura(AuraApplied ap)
        {
            var list = ap.Target == AuraTarget.Player ? PlayerAuras : BossAuras;
            AuraHud hud = null;
            foreach (var a in list) if (a.Id == ap.AuraId) hud = a;
            if (hud == null)
            {
                _data.Auras.TryGetValue(ap.AuraId, out var def);
                hud = new AuraHud
                {
                    Id = ap.AuraId,
                    Name = ap.Name ?? def?.Name ?? ap.AuraId,
                    Icon = def?.Icon ?? DefaultIcon(ap.AuraId),
                    Color = def?.Color ?? "#888888",
                    Target = ap.Target,
                };
                list.Add(hud);
            }
            hud.Stacks = ap.Stacks;
            hud.StartTick = ap.StartTick;
            hud.ExpiresTick = ap.ExpiresTick;
        }

        static string DefaultIcon(string auraId)
        {
            if (auraId.StartsWith("haste:")) return "💨";
            if (auraId.StartsWith("empower:")) return "⬆️";
            if (auraId.StartsWith("vulnerable:")) return "💢";
            return "•";
        }

        /// Drops telegraphs that resolved a while ago.
        public void Prune(int tick, int keepTicks = 30)
        {
            Telegraphs.RemoveAll(t => t.Resolved && tick - t.ResolvedTick > keepTicks);
        }
    }
}
