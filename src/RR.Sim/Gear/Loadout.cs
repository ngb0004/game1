using System.Collections.Generic;
using RR.Sim.Combat;
using RR.Sim.Data;

namespace RR.Sim.Gear
{
    /// Resolved equipment: summed stats, item procs and on-use abilities.
    public sealed class Loadout
    {
        public readonly List<ItemDef> Items = new List<ItemDef>();
        public readonly StatSum Stats = new StatSum();
        public readonly List<string> Procs = new List<string>();
        public readonly List<string> UseAbilities = new List<string>();

        public static Loadout Resolve(GameData data, IEnumerable<string> itemIds)
        {
            var l = new Loadout();
            var slots = new HashSet<string>();
            foreach (var id in itemIds)
            {
                var item = data.GetItem(id);
                if (!slots.Add(item.Slot)) throw new GameDataException("Two items equipped in slot " + item.Slot);
                l.Items.Add(item);
                l.Stats.Add(item.Stats);
                l.Procs.AddRange(item.Procs);
                if (!string.IsNullOrEmpty(item.UseAbility)) l.UseAbilities.Add(item.UseAbility);
            }
            return l;
        }

        public int AverageItemLevel()
        {
            if (Items.Count == 0) return 0;
            int sum = 0;
            foreach (var i in Items) sum += i.ItemLevel;
            return sum / Items.Count;
        }
    }
}
