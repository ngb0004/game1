# Rotation Raider

Mobile boss-fight prototype: WoW-style rotation combat (GCD, procs, priorities, cooldowns) plus
Dark Souls-style telegraphs answered with block and dodge. No movement, pure skill.

This repo is the **browser prototype**: the full combat sim in C#, playable in any browser
(phone or desktop), built so the simulation drops into Unity 6 unchanged once the mechanics are fun.

**Play:** https://ngb0004.github.io/game1/ (after the first deploy, see below)

## What's in it (spec milestones M0–M2)

- **Deterministic sim** (`RR.Sim`): 60 Hz integer ticks, seeded xorshift128+ RNG, no engine
  references. Same seed + data + inputs = identical fight, bit for bit (tested).
- **Combat:** hasted GCD, 200 ms spell queue, 10 abilities on bar A / bar B / major / minor, a
  trinket, charges, Fury resource with overcap tracking, crits, mastery.
- **Auras and procs:** buffs, debuffs, DoTs with pandemic refresh, stacks, ICDs, PPM procs,
  empower-next-cast, haste windows, cooldown resets with button glow (and swap-button glow).
- **Defense:** block/dodge with 400 ms normal and 150 ms perfect windows, perfect rewards, flow
  meter, stagger on hits, 300 ms recovery, latency calibration that the sim honours.
- **Bosses as data:** Training Dummy (60 s DPS check) and Gorrath the Pale (two phases, five
  attacks, a vulnerability window, 3 min enrage).
- **Scoring:** DPS vs per-boss benchmark curve, a 0–100 parse with WCL-style colors, full
  breakdown (damage by ability, procs, perfect rate, GCD uptime, overcap), downloadable fight log.

## Layout

```
data/                 Game data JSON (abilities, auras, procs, classes, items, bosses, config)
src/RR.Sim/           Pure C# deterministic simulation (netstandard2.1, C# 9) + Unity asmdef
src/RR.Client/        Engine-agnostic presentation logic: bar swap, HUD state, time->tick session
src/RR.Web/           Blazor WebAssembly front-end (stands in for Unity's RR.Game)
  wwwroot/js/rr.js    Canvas renderer, touch/keyboard input, sound
tests/RR.Sim.Tests/   NUnit 3 tests (EditMode-compatible)
tools/RR.SimRunner/   Headless runner: bot balance table, single fights, fight-log replay
tools/export-to-unity.sh  Copies sim, client logic, tests and data into a Unity project
docs/UNITY_PORT.md    How the Unity move works
```

The rule from the tech spec holds: combat is a deterministic simulation with no engine
dependencies. The browser (and later Unity) only feeds it input and draws its events.

## Controls

| Action | Touch | Keyboard |
| --- | --- | --- |
| Abilities (current bar) | right-thumb arc | `1` `2` `3` `4` |
| Swap to bar B | hold Swap | hold `Q` |
| Major / minor cooldown, trinket | small buttons | `E` / `R` / `T` |
| Block | big gold button | `Space` or `J` |
| Dodge | big red button | `Shift` or `K` |
| Pause | top-left button | `Esc` |

## Running it locally

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test tests/RR.Sim.Tests                         # 53 sim/client tests
dotnet run --project src/RR.Web                        # play at http://localhost:5080
dotnet run --project tools/RR.SimRunner -- balance     # bot balance table for every boss
dotnet run --project tools/RR.SimRunner -- fight gorrath_the_pale good 7
dotnet run --project tools/RR.SimRunner -- replay path/to/fightlog.json
```

### Balance snapshot (50 seeded runs per bot profile)

| Gorrath | win % | avg DPS | avg kill time |
| --- | --- | --- | --- |
| perfect bot | 100 | 9,224 | 117 s |
| good bot | 100 | 8,068 | 134 s |
| average bot | 50 | 6,014 | 176 s |
| sloppy bot (bar A mashing) | 0 | 3,228 | enrage |

Tuning lives entirely in `data/`; rerun `balance` after changing numbers.

## Deploying to GitHub Pages

`.github/workflows/pages.yml` runs the tests, a bot balance check, publishes the web build and
deploys it on every push to `main`. One-time setup: **Settings → Pages → Source: GitHub Actions**.

## Moving to Unity

See [docs/UNITY_PORT.md](docs/UNITY_PORT.md). In short: run `tools/export-to-unity.sh`, add the
Newtonsoft package, and write the Unity rendering/input layer against `GameSession` and the sim
events. The sim, client logic, data and tests carry over unchanged.
