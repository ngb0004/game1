using System;
using System.Collections.Generic;
using System.Linq;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Scoring
{
    public sealed class SourceBreakdown
    {
        public string SourceId;
        public long Damage;
        public int Hits;
        public int Crits;
        public int Casts;
    }

    /// Post-fight breakdown, built purely from the event stream so a server
    /// re-sim produces the same numbers.
    public sealed class FightReport
    {
        readonly Dictionary<string, SourceBreakdown> _sources = new Dictionary<string, SourceBreakdown>();
        readonly Dictionary<string, int> _procs = new Dictionary<string, int>();
        int _gcdStart = -1;
        int _gcdEnd = -1;
        long _gcdTicks;

        public string BossId;
        public bool Victory;
        public FightEndReason Reason;
        public int DurationTicks;
        public long TotalDamage;
        public int Attacks;
        public int Avoided;
        public int Perfects;
        public int HitsTaken;
        public long DamageTaken;
        public int ResourceWasted;
        public int ResourceGenerated;
        public int FlowBursts;
        public int FailedCasts;

        public double DurationSeconds => DurationTicks / (double)SimTime.TicksPerSecond;
        public double Dps => DurationTicks > 0 ? TotalDamage / DurationSeconds : 0;
        /// Perfects over avoidable attacks.
        public double PerfectRate => Attacks > 0 ? Perfects / (double)Attacks : 0;
        public double AvoidRate => Attacks > 0 ? (Avoided + Perfects) / (double)Attacks : 0;
        public double GcdUptime => DurationTicks > 0 ? Math.Min(1.0, _gcdTicks / (double)DurationTicks) : 0;
        public IReadOnlyDictionary<string, int> ProcCounts => _procs;

        public IEnumerable<SourceBreakdown> Sources =>
            _sources.Values.OrderByDescending(s => s.Damage).ThenBy(s => s.SourceId, StringComparer.Ordinal);

        public static FightReport Attach(CombatSim sim)
        {
            var r = new FightReport { BossId = sim.Boss.Def.Id };
            sim.Events.Subscribe(r.OnEvent);
            return r;
        }

        SourceBreakdown Source(string id)
        {
            if (!_sources.TryGetValue(id, out var s))
            {
                s = new SourceBreakdown { SourceId = id };
                _sources[id] = s;
            }
            return s;
        }

        public void OnEvent(SimEvent e)
        {
            switch (e)
            {
                case DamageDealt d:
                    var s = Source(d.SourceId);
                    s.Damage += d.Amount;
                    s.Hits++;
                    if (d.Crit) s.Crits++;
                    TotalDamage += d.Amount;
                    break;
                case AbilityUsed u:
                    Source(u.AbilityId).Casts++;
                    break;
                case CastFailed _:
                    FailedCasts++;
                    break;
                case GcdStarted g:
                    CloseGcd(g.Tick);
                    _gcdStart = g.Tick;
                    _gcdEnd = g.EndTick;
                    break;
                case ProcTriggered p:
                    _procs.TryGetValue(p.ProcId, out var n);
                    _procs[p.ProcId] = n + 1;
                    break;
                case ResourceChanged rc:
                    ResourceWasted += rc.Wasted;
                    if (rc.Delta > 0) ResourceGenerated += rc.Delta;
                    break;
                case AttackResolved a:
                    if (a.Outcome == AttackOutcome.Unavoidable) break;
                    Attacks++;
                    if (a.Outcome == AttackOutcome.Perfect) Perfects++;
                    else if (a.Outcome == AttackOutcome.Avoided) Avoided++;
                    break;
                case PlayerHit h:
                    HitsTaken++;
                    DamageTaken += h.Damage;
                    break;
                case FlowChanged f:
                    if (f.Burst) FlowBursts++;
                    break;
                case FightEnded end:
                    CloseGcd(end.DurationTicks);
                    Victory = end.Victory;
                    Reason = end.Reason;
                    DurationTicks = end.DurationTicks;
                    break;
            }
        }

        void CloseGcd(int now)
        {
            if (_gcdStart < 0) return;
            _gcdTicks += Math.Max(0, Math.Min(_gcdEnd, now) - _gcdStart);
            _gcdStart = -1;
        }
    }

    public static class ParseScore
    {
        /// 0-100 score from the boss's benchmark curve (piecewise linear in DPS).
        public static int Score(BossDef boss, double dps)
        {
            var pts = boss.Parse;
            if (pts == null || pts.Count == 0) return 0;
            var sorted = pts.OrderBy(p => p.Dps).ToList();
            if (dps <= sorted[0].Dps) return Clamp(sorted[0].Score * (sorted[0].Dps > 0 ? dps / sorted[0].Dps : 0));
            for (int i = 1; i < sorted.Count; i++)
            {
                var a = sorted[i - 1];
                var b = sorted[i];
                if (dps <= b.Dps)
                {
                    double f = (dps - a.Dps) / (b.Dps - a.Dps);
                    return Clamp(a.Score + f * (b.Score - a.Score));
                }
            }
            return Clamp(sorted[sorted.Count - 1].Score);
        }

        static int Clamp(double v) => (int)Math.Max(0, Math.Min(100, Math.Floor(v)));

        /// grey, green, blue, purple, orange, pink (99+).
        public static string Tier(int score)
        {
            if (score >= 99) return "pink";
            if (score >= 95) return "orange";
            if (score >= 75) return "purple";
            if (score >= 50) return "blue";
            if (score >= 25) return "green";
            return "grey";
        }
    }
}
