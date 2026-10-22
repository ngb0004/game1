using Newtonsoft.Json;
using RR.Sim;
using RR.Sim.Core;
using RR.Sim.Data;
using RR.Sim.Scoring;
using RR.Tools;

// Headless sim runner.
//
//   dotnet run --project tools/RR.SimRunner -- balance [boss] [runs]
//   dotnet run --project tools/RR.SimRunner -- fight <boss> <profile> [seed]
//   dotnet run --project tools/RR.SimRunner -- replay <fightlog.json>

var dataDir = FindDataDir();
var data = GameData.Load(GameData.FileNames.ToDictionary(n => n, n => File.ReadAllText(Path.Combine(dataDir, n))));
var cmd = args.Length > 0 ? args[0] : "balance";

switch (cmd)
{
    case "balance":
    {
        var bosses = args.Length > 1 ? new[] { args[1] } : data.Bosses.Keys.OrderBy(k => k).ToArray();
        int runs = args.Length > 2 ? int.Parse(args[2]) : 50;
        Console.WriteLine($"data {data.Version}, {runs} runs per profile");
        foreach (var boss in bosses)
        {
            Console.WriteLine();
            Console.WriteLine($"== {data.GetBoss(boss).Name} ==");
            Console.WriteLine($"{"profile",-9} {"win%",5} {"avgDPS",7} {"minDPS",7} {"maxDPS",7} {"avgTime",7} {"score",5} {"perf%",5} {"gcd%",5}");
            foreach (var profile in new[] { "perfect", "good", "average", "sloppy" })
            {
                var reports = Enumerable.Range(1, runs).Select(seed => RunBot(data, boss, BotProfile.ByName(profile), (ulong)seed).report).ToList();
                var b = data.GetBoss(boss);
                Console.WriteLine(
                    $"{profile,-9} {reports.Count(r => r.Victory) * 100.0 / runs,5:0} {reports.Average(r => r.Dps),7:0} {reports.Min(r => r.Dps),7:0} " +
                    $"{reports.Max(r => r.Dps),7:0} {reports.Average(r => r.DurationSeconds),7:0.0} {reports.Average(r => ParseScore.Score(b, r.Dps)),5:0} " +
                    $"{reports.Average(r => r.PerfectRate) * 100,5:0} {reports.Average(r => r.GcdUptime) * 100,5:0}");
            }
        }
        break;
    }
    case "fight":
    {
        var boss = args[1];
        var profile = BotProfile.ByName(args.Length > 2 ? args[2] : "good");
        ulong seed = args.Length > 3 ? ulong.Parse(args[3]) : 1;
        var (sim, r) = RunBot(data, boss, profile, seed);
        PrintReport(data, r);
        var path = Path.Combine(Path.GetTempPath(), $"fightlog-{boss}-{seed}.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(sim.CreateLog(), Formatting.Indented));
        Console.WriteLine($"fight log: {path}");
        break;
    }
    case "replay":
    {
        var log = JsonConvert.DeserializeObject<FightLog>(File.ReadAllText(args[1]));
        FightReport report = null;
        var sim = CombatSim.Replay(data, log, e => { (report ??= new FightReport { BossId = log.Setup.BossId }).OnEvent(e); });
        PrintReport(data, report);
        break;
    }
    default:
        Console.Error.WriteLine("usage: balance [boss] [runs] | fight <boss> <profile> [seed] | replay <log.json>");
        return 1;
}
return 0;

static (CombatSim sim, FightReport report) RunBot(GameData data, string boss, BotProfile profile, ulong seed)
{
    var sim = new CombatSim(data, new FightSetup { ClassId = "berserker", BossId = boss, Seed = seed });
    var report = FightReport.Attach(sim);
    var bot = new RotationBot(sim, profile, (int)seed * 7919);
    while (!sim.Ended)
    {
        bot.BeforeStep();
        sim.Step();
    }
    return (sim, report);
}

static void PrintReport(GameData data, FightReport r)
{
    var boss = data.GetBoss(r.BossId);
    int score = ParseScore.Score(boss, r.Dps);
    Console.WriteLine($"{boss.Name}: {(r.Victory ? "VICTORY" : "DEFEAT")} ({r.Reason}) in {r.DurationSeconds:0.0}s");
    Console.WriteLine($"DPS {r.Dps:0}  score {score} ({ParseScore.Tier(score)})  damage {r.TotalDamage:N0}");
    Console.WriteLine($"perfect {r.Perfects}/{r.Attacks}  avoided {r.Avoided}  hits {r.HitsTaken}  GCD uptime {r.GcdUptime:P0}  fury wasted {r.ResourceWasted}");
    foreach (var s in r.Sources)
        Console.WriteLine($"  {s.SourceId,-24} {s.Damage,10:N0} {s.Damage * 100.0 / Math.Max(1, r.TotalDamage),5:0.0}%  casts {s.Casts,3}  hits {s.Hits,3}  crits {s.Crits,3}");
    foreach (var p in r.ProcCounts.OrderBy(p => p.Key))
        Console.WriteLine($"  proc {p.Key,-22} x{p.Value}");
}

static string FindDataDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "data");
        if (File.Exists(Path.Combine(candidate, "config.json"))) return candidate;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Could not find the data/ folder");
}
