using System;
using System.Collections.Generic;
using RR.Sim;
using RR.Sim.Core;
using RR.Sim.Data;
using RR.Sim.Scoring;

namespace RR.Client
{
    /// What the player physically pressed. Bar swap is resolved here, so the
    /// sim only ever sees ability ids.
    public enum ClientInput
    {
        Slot0, Slot1, Slot2, Slot3,
        SwapDown, SwapUp,
        Major, Minor, Trinket,
        Block, Dodge,
        EndFight,
    }

    /// Runs one fight in real time: maps device timestamps to sim ticks, feeds
    /// inputs, steps the fixed 60 Hz sim, and hands sim events to presentation.
    /// Engine-agnostic: Blazor and Unity both drive it with a clock in milliseconds.
    public sealed class GameSession
    {
        readonly List<SimEvent> _frameEvents = new List<SimEvent>();
        double _startMs = double.NaN;
        double _pausedAtMs = double.NaN;

        public CombatSim Sim { get; }
        public ClientKit Kit { get; }
        public HudState Hud { get; }
        public BarSwap Swap { get; }
        public FightReport Report { get; }
        public bool Paused => !double.IsNaN(_pausedAtMs);
        public bool Started => !double.IsNaN(_startMs);

        public GameSession(GameData data, FightSetup setup, SwapMode swapMode)
        {
            Sim = new CombatSim(data, setup);
            Kit = ClientKit.FromSim(Sim);
            Hud = new HudState(data, Kit);
            Swap = new BarSwap(swapMode);
            Report = FightReport.Attach(Sim);
            Sim.Events.Subscribe(e =>
            {
                Hud.Apply(e);
                _frameEvents.Add(e);
            });
        }

        public void Start(double nowMs)
        {
            _startMs = nowMs;
        }

        public void Pause(double nowMs)
        {
            if (!Paused) _pausedAtMs = nowMs;
        }

        public void Resume(double nowMs)
        {
            if (!Paused) return;
            _startMs += nowMs - _pausedAtMs;
            _pausedAtMs = double.NaN;
        }

        /// Sim tick a device timestamp falls in.
        public int TickAt(double timeMs)
        {
            if (!Started) return 0;
            return Math.Max(0, (int)Math.Floor((timeMs - _startMs) * SimTime.TicksPerSecond / 1000.0));
        }

        /// Fight-relative milliseconds for smooth (interpolated) presentation.
        public double FightMsAt(double nowMs)
        {
            if (!Started) return 0;
            if (Paused) nowMs = _pausedAtMs;
            return Math.Max(0, nowMs - _startMs);
        }

        /// Applies one physical input that happened at timeMs (device event time).
        public void Input(ClientInput input, double timeMs)
        {
            if (Sim.Ended || Paused || !Started) return;
            int tick = TickAt(timeMs);
            switch (input)
            {
                case ClientInput.Slot0:
                case ClientInput.Slot1:
                case ClientInput.Slot2:
                case ClientInput.Slot3:
                {
                    var ability = Kit.Slot(input - ClientInput.Slot0, Swap.ShowingB);
                    if (ability != null) Sim.Enqueue(InputCommand.Cast(tick, ability.Id));
                    break;
                }
                case ClientInput.SwapDown: Swap.Press(); break;
                case ClientInput.SwapUp: Swap.Release(); break;
                case ClientInput.Major: CastIf(Kit.Major, tick); break;
                case ClientInput.Minor: CastIf(Kit.Minor, tick); break;
                case ClientInput.Trinket: CastIf(Kit.Trinket, tick); break;
                case ClientInput.Block: Sim.Enqueue(InputCommand.Block(tick)); break;
                case ClientInput.Dodge: Sim.Enqueue(InputCommand.Dodge(tick)); break;
                case ClientInput.EndFight: Sim.Enqueue(new InputCommand(tick, InputType.EndFight)); break;
            }
        }

        void CastIf(AbilityDef a, int tick)
        {
            if (a != null) Sim.Enqueue(InputCommand.Cast(tick, a.Id));
        }

        /// Steps the sim up to and including the tick that contains nowMs and
        /// returns the events produced since the last call.
        public IReadOnlyList<SimEvent> Update(double nowMs)
        {
            _frameEvents.Clear();
            if (Started && !Paused)
            {
                // Cap catch-up so a stalled tab cannot freeze the page; beyond
                // that, the fight clock slips instead of fast-forwarding.
                int target = TickAt(nowMs) + 1;
                int maxSteps = SimTime.TicksPerSecond * 2;
                if (target - Sim.Tick > maxSteps) _startMs += SimTime.TicksToMs(target - Sim.Tick - maxSteps);
                Sim.AdvanceTo(Math.Min(target, Sim.Tick + maxSteps));
                Hud.Prune(Sim.Tick);
            }
            return _frameEvents;
        }

        /// A bar B ability is glowing while bar A is showing: glow the swap button.
        public bool SwapGlow
        {
            get
            {
                for (int i = 0; i < 4; i++)
                {
                    var hidden = Swap.ShowingB ? Kit.BarA[i] : Kit.BarB[i];
                    var hud = hidden != null ? Hud.Ability(hidden.Id) : null;
                    if (hud != null && (hud.Glow || IsReactiveReady(hidden))) return true;
                }
                return false;
            }
        }

        /// Proc-gated abilities (e.g. Overpower after a perfect block) glow while usable.
        public bool IsReactiveReady(AbilityDef a) =>
            !string.IsNullOrEmpty(a.RequiresAura) && Hud.HasPlayerAura(a.RequiresAura);

        public FightLog CreateLog() => Sim.CreateLog();
    }
}
