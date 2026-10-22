# Porting to Unity 6

The prototype is split so that everything except drawing and raw input moves to Unity as-is.

| This repo | Unity project | Changes needed |
| --- | --- | --- |
| `src/RR.Sim/**/*.cs` + `RR.Sim.asmdef` | `Assets/Scripts/Sim/` | none |
| `src/RR.Client/**/*.cs` + `RR.Client.asmdef` | `Assets/Scripts/Game/Client/` | none |
| `data/*.json` | `Assets/Data/` | none |
| `tests/RR.Sim.Tests/*.cs` + `RotationBot.cs` | `Assets/Tests/EditMode/` | none |
| `src/RR.Web` (Blazor) + `wwwroot/js/rr.js` | `Assets/Scripts/Game/` (MonoBehaviours) | rewrite: this is the presentation layer |
| `tools/RR.SimRunner` | stays outside Unity (`Tools/`) | none, keep it for balance and server re-sim |

Why it works: `RR.Sim` and `RR.Client` target .NET Standard 2.1 with C# 9 (Unity 6's language
level) and never reference an engine. Their asmdefs set `noEngineReferences: true`, so Unity
enforces the spec's "no UnityEngine in the sim" rule.

## Steps

1. Create a Unity 6 LTS project (2D URP template), then add packages:
   `com.unity.nuget.newtonsoft-json`, `com.unity.inputsystem`, `com.unity.test-framework`.
2. Run `tools/export-to-unity.sh /path/to/UnityProject`.
3. Open Test Runner → EditMode → Run All. The same tests that run in CI should pass.
   (`TestData` finds `Assets/Data` automatically.)
4. Write the presentation layer (`RR.Game` asmdef referencing `RR.Sim` and `RR.Client`).

## The presentation layer contract

The browser layer is small, and the Unity one mirrors it:

```csharp
// Boot: load JSON from Assets/Data (TextAsset, Addressables, or StreamingAssets)
var files = GameData.FileNames.ToDictionary(n => n, n => LoadText("Data/" + n));
var data = GameData.Load(files);

// Fight
var session = new GameSession(data, new FightSetup { ClassId = "berserker", BossId = "gorrath_the_pale",
    Seed = seed, InputOffsetMs = settings.InputOffsetMs }, SwapMode.Hold);
session.Start(NowMs());

// Input System callback: use the event's own timestamp, not the frame time.
void OnBlock(InputAction.CallbackContext ctx) => session.Input(ClientInput.Block, ctx.time * 1000.0);

// Every frame
foreach (var e in session.Update(NowMs()))
    presenter.Play(e);           // DamageDealt -> numbers, TelegraphStarted -> wind-up anim, ...
hud.Render(session.Hud, session.FightMsAt(NowMs()));   // buttons, sweeps, bars, buffs
```

- `NowMs()` and the input timestamps must share a clock. With the Input System, use
  `Time.realtimeSinceStartupAsDouble * 1000` for frames and `ctx.time * 1000` for events (both
  are on the realtime-since-startup clock).
- `session.Hud` (`HudState`) is built only from sim events: boss HP, resource, GCD, per-ability
  charges and recharge ticks, glows, auras, telegraphs, flow. Draw from it; never read sim internals.
- `session.Kit` tells you which ability is on which button; `session.Swap.ShowingB` which bar is up;
  `session.SwapGlow` whether the swap button should glow.
- Interpolate visuals with `session.FightMsAt(now)` (milliseconds) against tick values × 1000 / 60.
- `wwwroot/js/rr.js` (`onEvent`, `drawButtons`, `drawTelegraphs`) is the reference for what each
  event should look and sound like.

## Later: server verification

`CombatSim.CreateLog()` gives the seed, data version hash, setup (gear, latency offset) and the
exact input list. `CombatSim.Replay(data, log)` reproduces the fight; `tools/RR.SimRunner replay`
does it from the command line. An ASP.NET Core server can reference `RR.Sim` directly (spec §10).
Damage math still uses doubles; revisit fixed-point before trusting cross-platform re-sims.
