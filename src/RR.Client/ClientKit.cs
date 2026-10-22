using System.Collections.Generic;
using RR.Sim;
using RR.Sim.Data;

namespace RR.Client
{
    /// Which ability sits on which button, derived from ability data.
    public sealed class ClientKit
    {
        public readonly AbilityDef[] BarA = new AbilityDef[4];
        public readonly AbilityDef[] BarB = new AbilityDef[4];
        public AbilityDef Major { get; private set; }
        public AbilityDef Minor { get; private set; }
        public AbilityDef Trinket { get; private set; }

        public static ClientKit FromSim(CombatSim sim)
        {
            var kit = new ClientKit();
            foreach (var st in sim.Player.Kit)
            {
                var a = st.Def;
                switch (a.Bar)
                {
                    case "A":
                        if (a.Slot >= 0 && a.Slot < 4) kit.BarA[a.Slot] = a;
                        break;
                    case "B":
                        if (a.Slot >= 0 && a.Slot < 4) kit.BarB[a.Slot] = a;
                        break;
                    case "majorCd": kit.Major = a; break;
                    case "minorCd": kit.Minor = a; break;
                    case "trinket": kit.Trinket = a; break;
                }
            }
            return kit;
        }

        public AbilityDef Slot(int slot, bool barB)
        {
            if (slot < 0 || slot > 3) return null;
            return barB ? BarB[slot] : BarA[slot];
        }

        public IEnumerable<AbilityDef> All()
        {
            foreach (var a in BarA) if (a != null) yield return a;
            foreach (var a in BarB) if (a != null) yield return a;
            if (Major != null) yield return Major;
            if (Minor != null) yield return Minor;
            if (Trinket != null) yield return Trinket;
        }

        public bool IsOnBarB(string abilityId)
        {
            foreach (var a in BarB) if (a != null && a.Id == abilityId) return true;
            return false;
        }
    }

    public enum SwapMode { Hold, Toggle }

    /// Bar swap lives in the input layer only: it changes which abilities the
    /// four buttons show. The sim only ever receives the resolved ability id.
    public sealed class BarSwap
    {
        public SwapMode Mode { get; set; }
        public bool ShowingB { get; private set; }

        public BarSwap(SwapMode mode)
        {
            Mode = mode;
        }

        public void Press()
        {
            ShowingB = Mode == SwapMode.Hold || !ShowingB;
        }

        public void Release()
        {
            if (Mode == SwapMode.Hold) ShowingB = false;
        }

        public void Reset() => ShowingB = false;
    }
}
