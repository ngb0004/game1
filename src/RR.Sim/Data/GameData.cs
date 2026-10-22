using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RR.Sim.Data
{
    /// All static game data, loaded once at boot. Immutable by convention.
    public sealed class GameData
    {
        public CombatConfig Config { get; private set; }
        public IReadOnlyDictionary<string, AbilityDef> Abilities { get; private set; }
        public IReadOnlyDictionary<string, AuraDef> Auras { get; private set; }
        public IReadOnlyDictionary<string, ProcDef> Procs { get; private set; }
        public IReadOnlyDictionary<string, ClassDef> Classes { get; private set; }
        public IReadOnlyDictionary<string, ItemDef> Items { get; private set; }
        public IReadOnlyDictionary<string, BossDef> Bosses { get; private set; }

        /// Hash of every data file. Recorded in fight logs so a replay can
        /// refuse to run against different tuning numbers.
        public string Version { get; private set; }

        public static readonly string[] FileNames =
        {
            "config.json", "abilities.json", "auras.json", "procs.json",
            "classes.json", "items.json", "bosses.json",
        };

        /// Builds game data from raw JSON text keyed by file name (see FileNames).
        /// Works the same from disk, embedded resources, Unity TextAssets or a server.
        public static GameData Load(IDictionary<string, string> files)
        {
            foreach (var name in FileNames)
            {
                if (!files.ContainsKey(name)) throw new GameDataException("Missing data file: " + name);
            }

            var data = new GameData
            {
                Config = Parse<CombatConfig>(files, "config.json"),
                Abilities = ById(Parse<List<AbilityDef>>(files, "abilities.json"), a => a.Id, "ability"),
                Auras = ById(Parse<List<AuraDef>>(files, "auras.json"), a => a.Id, "aura"),
                Procs = ById(Parse<List<ProcDef>>(files, "procs.json"), p => p.Id, "proc"),
                Classes = ById(Parse<List<ClassDef>>(files, "classes.json"), c => c.Id, "class"),
                Items = ById(Parse<List<ItemDef>>(files, "items.json"), i => i.Id, "item"),
                Bosses = ById(Parse<List<BossDef>>(files, "bosses.json"), b => b.Id, "boss"),
                Version = ComputeVersion(files),
            };
            data.Validate();
            return data;
        }

        static T Parse<T>(IDictionary<string, string> files, string name)
        {
            try
            {
                var result = JsonConvert.DeserializeObject<T>(files[name]);
                if (result == null) throw new GameDataException(name + " is empty");
                return result;
            }
            catch (JsonException e)
            {
                throw new GameDataException("Could not parse " + name + ": " + e.Message);
            }
        }

        static IReadOnlyDictionary<string, T> ById<T>(List<T> list, Func<T, string> id, string kind)
        {
            var dict = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                var key = id(item);
                if (string.IsNullOrEmpty(key)) throw new GameDataException("A " + kind + " has no id");
                if (dict.ContainsKey(key)) throw new GameDataException("Duplicate " + kind + " id: " + key);
                dict[key] = item;
            }
            return dict;
        }

        /// FNV-1a 64 over file names and contents, with line endings normalised
        /// so Windows and Linux checkouts hash the same.
        static string ComputeVersion(IDictionary<string, string> files)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (var name in FileNames)
            {
                var text = name + "\n" + files[name].Replace("\r\n", "\n");
                foreach (var b in Encoding.UTF8.GetBytes(text))
                {
                    hash ^= b;
                    hash *= prime;
                }
            }
            return hash.ToString("x16");
        }

        void Validate()
        {
            var errors = new List<string>();

            void CheckAura(string id, string where)
            {
                if (!string.IsNullOrEmpty(id) && !Auras.ContainsKey(id)) errors.Add(where + ": unknown aura '" + id + "'");
            }
            void CheckAbility(string id, string where)
            {
                if (!string.IsNullOrEmpty(id) && !Abilities.ContainsKey(id)) errors.Add(where + ": unknown ability '" + id + "'");
            }
            void CheckEffect(EffectDef e, string where)
            {
                if (e == null) { errors.Add(where + ": missing effect"); return; }
                switch (e.Type)
                {
                    case "applyAura":
                    case "removeAura":
                        if (string.IsNullOrEmpty(e.Aura)) errors.Add(where + ": " + e.Type + " needs 'aura'");
                        CheckAura(e.Aura, where);
                        break;
                    case "resetCooldown":
                        if (string.IsNullOrEmpty(e.Ability)) errors.Add(where + ": resetCooldown needs 'ability'");
                        CheckAbility(e.Ability, where);
                        break;
                    case "empowerNextCast":
                        CheckAbility(e.Ability, where);
                        break;
                    case "extraDamage":
                        if (e.Damage == null) errors.Add(where + ": extraDamage needs 'damage'");
                        break;
                    case "hasteWindow":
                    case "gainResource":
                        break;
                    default:
                        errors.Add(where + ": unknown effect type '" + e.Type + "'");
                        break;
                }
            }

            foreach (var a in Abilities.Values)
            {
                CheckAura(a.RequiresAura, "ability " + a.Id);
                CheckAura(a.ConsumesAura, "ability " + a.Id);
                foreach (var e in a.Effects) CheckEffect(e, "ability " + a.Id);
                if (a.Charges < 1) errors.Add("ability " + a.Id + ": charges must be >= 1");
            }
            foreach (var p in Procs.Values)
            {
                if (p.Trigger == null) errors.Add("proc " + p.Id + ": missing trigger");
                CheckEffect(p.Effect, "proc " + p.Id);
                if (p.Ui != null) CheckAbility(p.Ui.GlowAbility, "proc " + p.Id);
            }
            foreach (var c in Classes.Values)
            {
                foreach (var a in c.Abilities) CheckAbility(a, "class " + c.Id);
                foreach (var p in c.Procs)
                    if (!Procs.ContainsKey(p)) errors.Add("class " + c.Id + ": unknown proc '" + p + "'");
                foreach (var i in c.DefaultGear)
                    if (!Items.ContainsKey(i)) errors.Add("class " + c.Id + ": unknown item '" + i + "'");
                if (c.Resource == null) errors.Add("class " + c.Id + ": missing resource");
            }
            foreach (var i in Items.Values)
            {
                CheckAbility(i.UseAbility, "item " + i.Id);
                foreach (var p in i.Procs)
                    if (!Procs.ContainsKey(p)) errors.Add("item " + i.Id + ": unknown proc '" + p + "'");
            }
            foreach (var b in Bosses.Values)
            {
                if (b.Hp <= 0) errors.Add("boss " + b.Id + ": hp must be > 0");
                if (b.Phases.Count == 0) errors.Add("boss " + b.Id + ": needs at least one phase");
                foreach (var ph in b.Phases)
                {
                    foreach (var atk in ph.AttackPool)
                        if (!b.Attacks.ContainsKey(atk)) errors.Add("boss " + b.Id + ": unknown attack '" + atk + "'");
                    if (!string.IsNullOrEmpty(ph.OnEnter) && !b.Attacks.ContainsKey(ph.OnEnter))
                        errors.Add("boss " + b.Id + ": unknown onEnter attack '" + ph.OnEnter + "'");
                    if (ph.CadenceMs == null || ph.CadenceMs.Length != 2 || ph.CadenceMs[0] > ph.CadenceMs[1])
                        errors.Add("boss " + b.Id + ": cadenceMs must be [min, max]");
                }
                foreach (var kv in b.Attacks)
                {
                    var c = kv.Value.Counter;
                    if (c != "block" && c != "dodge" && c != "none")
                        errors.Add("boss " + b.Id + " attack " + kv.Key + ": counter must be block, dodge or none");
                    CheckAura(kv.Value.ApplyAura, "boss " + b.Id + " attack " + kv.Key);
                }
                foreach (var w in b.VulnerableWindows)
                    if (!b.Attacks.ContainsKey(w.AfterAttack)) errors.Add("boss " + b.Id + ": vulnerable window after unknown attack '" + w.AfterAttack + "'");
            }
            CheckAura(Config.Defense.PerfectAura, "config.defense.perfectAura");
            CheckAura(Config.Defense.Flow?.Aura, "config.defense.flow.aura");

            if (errors.Count > 0) throw new GameDataException("Invalid game data:\n  " + string.Join("\n  ", errors));
        }

        public ClassDef GetClass(string id) => Get(Classes, id, "class");
        public BossDef GetBoss(string id) => Get(Bosses, id, "boss");
        public AbilityDef GetAbility(string id) => Get(Abilities, id, "ability");
        public AuraDef GetAura(string id) => Get(Auras, id, "aura");
        public ItemDef GetItem(string id) => Get(Items, id, "item");
        public ProcDef GetProc(string id) => Get(Procs, id, "proc");

        static T Get<T>(IReadOnlyDictionary<string, T> dict, string id, string kind)
        {
            if (id != null && dict.TryGetValue(id, out var v)) return v;
            throw new GameDataException("Unknown " + kind + ": " + id);
        }

        /// Bosses in file order is not preserved by the dictionary; callers that
        /// list bosses for UI should sort explicitly.
        public IEnumerable<BossDef> BossesSorted() => Bosses.Values.OrderBy(b => b.Hp);
    }

    public sealed class GameDataException : Exception
    {
        public GameDataException(string message) : base(message) { }
    }
}
