using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RR.Sim;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Sim.Tests
{
    /// Loads the real /data files, optionally patched per test.
    public static class TestData
    {
        static Dictionary<string, string> _files;

        public static Dictionary<string, string> Files()
        {
            if (_files == null)
            {
                var dir = FindDataDir();
                _files = GameData.FileNames.ToDictionary(n => n, n => File.ReadAllText(Path.Combine(dir, n)));
            }
            return new Dictionary<string, string>(_files);
        }

        public static GameData Real() => GameData.Load(Files());

        /// Real data with a deterministic test boss: one attack every 5s.
        public static GameData WithTestBoss(Action<JObject> tweakBoss = null, Action<Dictionary<string, JToken>> tweakFiles = null)
        {
            var files = Files();
            var bosses = JArray.Parse(files["bosses.json"]);
            var boss = JObject.Parse(@"{
                'id': 'test_boss', 'name': 'Test Boss', 'hp': 1000000, 'enrageMs': 600000, 'firstAttackMs': 1000,
                'phases': [ { 'untilHpPct': 0, 'attackPool': ['smash'], 'cadenceMs': [5000, 5000] } ],
                'attacks': {
                    'smash': { 'name': 'Smash', 'telegraphMs': 1000, 'counter': 'block', 'damage': 5000 },
                    'sweep': { 'name': 'Sweep', 'telegraphMs': 1000, 'counter': 'dodge', 'damage': 5000 }
                },
                'parse': [ { 'score': 0, 'dps': 0 }, { 'score': 100, 'dps': 10000 } ]
            }");
            tweakBoss?.Invoke(boss);
            bosses.Add(boss);
            files["bosses.json"] = bosses.ToString();

            if (tweakFiles != null)
            {
                var tokens = files.ToDictionary(kv => kv.Key, kv => JToken.Parse(kv.Value));
                tweakFiles(tokens);
                files = tokens.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            }
            return GameData.Load(files);
        }

        public static CombatSim Sim(GameData data, string boss = "test_boss", ulong seed = 1, int offsetMs = 0)
        {
            return new CombatSim(data, new FightSetup { ClassId = "berserker", BossId = boss, Seed = seed, InputOffsetMs = offsetMs });
        }

        /// data/ in this repo, or Assets/Data inside a Unity project.
        static string FindDataDir()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    foreach (var rel in new[] { "data", Path.Combine("Assets", "Data") })
                    {
                        var candidate = Path.Combine(dir.FullName, rel);
                        if (File.Exists(Path.Combine(candidate, "config.json"))) return candidate;
                    }
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("data/ folder not found");
        }
    }

    /// Records every event a sim publishes.
    public sealed class EventLog
    {
        public readonly List<SimEvent> All = new List<SimEvent>();

        public EventLog(CombatSim sim)
        {
            sim.Events.Subscribe(All.Add);
        }

        public IEnumerable<T> Of<T>() where T : SimEvent => All.OfType<T>();
        public T Last<T>() where T : SimEvent => All.OfType<T>().LastOrDefault();
    }
}
