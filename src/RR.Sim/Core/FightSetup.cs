using System.Collections.Generic;

namespace RR.Sim.Core
{
    public sealed class FightSetup
    {
        public string ClassId { get; set; }
        public string BossId { get; set; }
        public ulong Seed { get; set; }
        public int Level { get; set; } = 60;
        /// Equipped item ids. Null = the class's default gear.
        public List<string> Gear { get; set; }
        /// Player latency calibration, clamped to config.defense.maxInputOffsetMs.
        public int InputOffsetMs { get; set; }
    }

    public enum InputType { CastAbility, Block, Dodge, EndFight }

    /// One player input at a sim tick. The sim only ever sees resolved ability
    /// ids, never which button or bar was pressed.
    public sealed class InputCommand
    {
        public int Tick { get; set; }
        public InputType Type { get; set; }
        public string AbilityId { get; set; }

        public InputCommand() { }

        public InputCommand(int tick, InputType type, string abilityId = null)
        {
            Tick = tick;
            Type = type;
            AbilityId = abilityId;
        }

        public static InputCommand Cast(int tick, string abilityId) => new InputCommand(tick, InputType.CastAbility, abilityId);
        public static InputCommand Block(int tick) => new InputCommand(tick, InputType.Block);
        public static InputCommand Dodge(int tick) => new InputCommand(tick, InputType.Dodge);

        public override string ToString() => Tick + ":" + Type + (AbilityId != null ? ":" + AbilityId : "");
    }

    /// Everything needed to reproduce a fight bit for bit: same data version,
    /// setup (seed, gear, offset) and inputs gives the same result.
    public sealed class FightLog
    {
        public int FormatVersion { get; set; } = 1;
        public string DataVersion { get; set; }
        public FightSetup Setup { get; set; }
        public List<InputCommand> Inputs { get; set; } = new List<InputCommand>();
    }
}
