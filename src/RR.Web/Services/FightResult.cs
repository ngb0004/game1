using RR.Sim.Core;
using RR.Sim.Data;
using RR.Sim.Scoring;

namespace RR.Web.Services
{
    public sealed class FightResult
    {
        public BossDef Boss { get; init; }
        public FightReport Report { get; init; }
        public FightLog Log { get; init; }
        public int Score { get; init; }
        public string Tier { get; init; }
        public bool NewBest { get; init; }
    }
}
