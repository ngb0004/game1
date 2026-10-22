using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using RR.Client;
using RR.Sim.Core;
using RR.Sim.Data;

namespace RR.Web.Services
{
    /// The JS render loop calls Frame() every animation frame with the inputs
    /// it captured (and their event timestamps). We step the sim and send back
    /// HUD state plus this frame's events, as JSON, for the canvas renderer.
    public sealed class FightBridge
    {
        readonly GameData _data;
        readonly ArrayBufferWriter<byte> _buffer = new ArrayBufferWriter<byte>(16 * 1024);
        bool _endNotified;

        public GameSession Session { get; }
        public event Action Ended;

        public FightBridge(GameData data, GameSession session)
        {
            _data = data;
            Session = session;
        }

        [JSInvokable]
        public void Start(double nowMs) => Session.Start(nowMs);

        [JSInvokable]
        public void Pause(double nowMs) => Session.Pause(nowMs);

        [JSInvokable]
        public void Resume(double nowMs) => Session.Resume(nowMs);

        /// inputs = [code, timeMs, code, timeMs, ...] with code = (int)ClientInput.
        [JSInvokable]
        public string Frame(double nowMs, double[] inputs)
        {
            if (inputs != null)
            {
                for (int i = 0; i + 1 < inputs.Length; i += 2)
                    Session.Input((ClientInput)(int)inputs[i], inputs[i + 1]);
            }
            var events = Session.Update(nowMs);
            var json = WriteFrame(nowMs, events);

            if (Session.Sim.Ended && !_endNotified)
            {
                _endNotified = true;
                Ended?.Invoke();
            }
            return json;
        }

        public string InitJson(PlayerSettings settings)
        {
            _buffer.Clear();
            using (var w = new Utf8JsonWriter(_buffer))
            {
                var sim = Session.Sim;
                var boss = sim.Boss.Def;
                w.WriteStartObject();

                w.WriteStartObject("boss");
                w.WriteString("id", boss.Id);
                w.WriteString("name", boss.Name);
                w.WriteString("title", boss.Title);
                w.WriteString("archetype", boss.Archetype);
                w.WriteNumber("maxHp", boss.Hp);
                w.WriteNumber("enrageMs", boss.EnrageMs);
                w.WriteBoolean("dummy", boss.IsDummy);
                w.WriteStartArray("phaseMarks");
                for (int i = 0; i < boss.Phases.Count - 1; i++) w.WriteNumberValue(boss.Phases[i].UntilHpPct);
                w.WriteEndArray();
                w.WriteEndObject();

                var res = sim.Class.Resource;
                w.WriteStartObject("res");
                w.WriteString("name", res.Name);
                w.WriteString("color", res.Color);
                w.WriteNumber("max", res.Max);
                w.WriteEndObject();

                w.WriteStartObject("abilities");
                foreach (var a in Session.Kit.All())
                {
                    w.WriteStartObject(a.Id);
                    w.WriteString("name", a.Name);
                    w.WriteString("icon", a.Icon);
                    w.WriteString("color", a.Color);
                    w.WriteString("anim", a.Animation);
                    w.WriteString("desc", a.Description);
                    w.WriteNumber("cost", a.ResourceCost);
                    w.WriteBoolean("onGcd", a.OnGcd);
                    w.WriteEndObject();
                }
                w.WriteEndObject();

                w.WriteStartObject("auras");
                foreach (var a in _data.Auras.Values)
                {
                    w.WriteStartObject(a.Id);
                    w.WriteString("name", a.Name);
                    w.WriteString("icon", a.Icon);
                    w.WriteString("color", a.Color);
                    w.WriteEndObject();
                }
                w.WriteEndObject();

                w.WriteStartObject("procs");
                foreach (var p in _data.Procs.Values) w.WriteString(p.Id, p.Name);
                w.WriteEndObject();

                w.WriteStartObject("settings");
                w.WriteBoolean("showKeys", settings.ShowKeys);
                w.WriteBoolean("sound", settings.Sound);
                w.WriteString("swapMode", settings.SwapMode == SwapMode.Hold ? "hold" : "toggle");
                w.WriteEndObject();

                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(_buffer.WrittenSpan);
        }

        string WriteFrame(double nowMs, IReadOnlyList<SimEvent> events)
        {
            var s = Session;
            var hud = s.Hud;
            int tick = s.Sim.Tick;
            _buffer.Clear();
            using (var w = new Utf8JsonWriter(_buffer))
            {
                w.WriteStartObject();
                w.WriteNumber("ms", s.FightMsAt(nowMs));
                w.WriteNumber("tick", tick);
                w.WriteBoolean("paused", s.Paused);
                w.WriteBoolean("ended", s.Sim.Ended);

                w.WriteNumber("hp", hud.BossHp);
                w.WriteNumber("maxHp", hud.BossMaxHp);
                w.WriteNumber("phase", hud.Phase);
                w.WriteNumber("enrage", hud.EnrageTick);
                w.WriteNumber("res", hud.Resource);
                w.WriteNumber("resMax", hud.ResourceMax);
                w.WriteNumber("gcdS", hud.GcdStartTick);
                w.WriteNumber("gcdE", hud.GcdEndTick);
                w.WriteNumber("stagger", hud.StaggerEndTick);
                w.WriteNumber("flow", hud.Flow);
                w.WriteNumber("flowMax", hud.FlowThreshold);
                w.WriteNumber("dps", Math.Round(hud.Dps(tick)));
                w.WriteNumber("dmg", hud.TotalDamage);
                w.WriteBoolean("barB", s.Swap.ShowingB);
                w.WriteBoolean("swapGlow", s.SwapGlow);

                w.WriteStartArray("bar");
                for (int i = 0; i < 4; i++) WriteButton(w, s.Kit.Slot(i, s.Swap.ShowingB));
                w.WriteEndArray();
                w.WriteStartArray("cds");
                WriteButton(w, s.Kit.Major);
                WriteButton(w, s.Kit.Minor);
                WriteButton(w, s.Kit.Trinket);
                w.WriteEndArray();

                WriteAuras(w, "pa", hud.PlayerAuras);
                WriteAuras(w, "ba", hud.BossAuras);

                w.WriteStartArray("tg");
                foreach (var t in hud.Telegraphs)
                {
                    w.WriteStartObject();
                    w.WriteNumber("i", t.Instance);
                    w.WriteString("id", t.AttackId);
                    w.WriteString("name", t.Name);
                    w.WriteString("c", t.Counter);
                    w.WriteNumber("s", t.StartTick);
                    w.WriteNumber("imp", t.ImpactTick);
                    w.WriteNumber("nw", t.NormalWindowTicks);
                    w.WriteNumber("pw", t.PerfectWindowTicks);
                    w.WriteBoolean("done", t.Resolved);
                    if (t.Resolved) w.WriteString("out", OutcomeName(t.Outcome));
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartArray("ev");
                foreach (var e in events) WriteEvent(w, e);
                w.WriteEndArray();

                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(_buffer.WrittenSpan);
        }

        void WriteButton(Utf8JsonWriter w, AbilityDef a)
        {
            if (a == null)
            {
                w.WriteNullValue();
                return;
            }
            var h = Session.Hud.Ability(a.Id);
            w.WriteStartObject();
            w.WriteString("id", a.Id);
            w.WriteNumber("ch", h.Charges);
            w.WriteNumber("mx", h.MaxCharges);
            w.WriteNumber("rs", h.RechargeStartTick);
            w.WriteNumber("re", h.RechargeEndTick);
            w.WriteBoolean("ok", Session.Hud.MeetsRequirements(a));
            w.WriteBoolean("glow", h.Glow || Session.IsReactiveReady(a));
            w.WriteBoolean("q", h.Queued);
            w.WriteEndObject();
        }

        static void WriteAuras(Utf8JsonWriter w, string name, List<AuraHud> auras)
        {
            w.WriteStartArray(name);
            foreach (var a in auras)
            {
                w.WriteStartObject();
                w.WriteString("id", a.Id);
                w.WriteString("name", a.Name);
                w.WriteString("icon", a.Icon);
                w.WriteString("color", a.Color);
                w.WriteNumber("st", a.Stacks);
                w.WriteNumber("s", a.StartTick);
                w.WriteNumber("e", a.ExpiresTick == int.MaxValue ? -1 : a.ExpiresTick);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        static string OutcomeName(AttackOutcome o) => o switch
        {
            AttackOutcome.Perfect => "perfect",
            AttackOutcome.Avoided => "avoided",
            AttackOutcome.Unavoidable => "unavoidable",
            _ => "hit",
        };

        static void WriteEvent(Utf8JsonWriter w, SimEvent e)
        {
            switch (e)
            {
                case DamageDealt d:
                    w.WriteStartObject();
                    w.WriteString("k", "dmg");
                    w.WriteString("src", d.SourceId);
                    w.WriteNumber("a", d.Amount);
                    w.WriteBoolean("c", d.Crit);
                    w.WriteBoolean("p", d.Periodic);
                    w.WriteEndObject();
                    break;
                case AbilityUsed u:
                    w.WriteStartObject();
                    w.WriteString("k", "use");
                    w.WriteString("id", u.AbilityId);
                    w.WriteEndObject();
                    break;
                case CastFailed f:
                    w.WriteStartObject();
                    w.WriteString("k", "fail");
                    w.WriteString("id", f.AbilityId);
                    w.WriteString("r", f.Reason.ToString());
                    w.WriteEndObject();
                    break;
                case DefenseUsed du:
                    w.WriteStartObject();
                    w.WriteString("k", "def");
                    w.WriteString("d", du.Type == DefenseType.Block ? "block" : "dodge");
                    w.WriteEndObject();
                    break;
                case TelegraphStarted tg:
                    w.WriteStartObject();
                    w.WriteString("k", "tele");
                    w.WriteString("id", tg.AttackId);
                    w.WriteString("c", tg.Counter);
                    w.WriteEndObject();
                    break;
                case AttackResolved ar:
                    w.WriteStartObject();
                    w.WriteString("k", "res");
                    w.WriteString("id", ar.AttackId);
                    w.WriteString("o", OutcomeName(ar.Outcome));
                    w.WriteNumber("err", SimTime.TicksToMs(ar.TimingErrorTicks));
                    w.WriteNumber("dmg", ar.DamageTaken);
                    w.WriteEndObject();
                    break;
                case ProcTriggered p:
                    w.WriteStartObject();
                    w.WriteString("k", "proc");
                    w.WriteString("id", p.ProcId);
                    w.WriteEndObject();
                    break;
                case FlowChanged fl when fl.Burst:
                    w.WriteStartObject();
                    w.WriteString("k", "flow");
                    w.WriteEndObject();
                    break;
                case BossPhaseChanged ph:
                    w.WriteStartObject();
                    w.WriteString("k", "phase");
                    w.WriteNumber("p", ph.Phase);
                    w.WriteEndObject();
                    break;
                case FightEnded end:
                    w.WriteStartObject();
                    w.WriteString("k", "end");
                    w.WriteBoolean("v", end.Victory);
                    w.WriteString("r", end.Reason.ToString());
                    w.WriteEndObject();
                    break;
            }
        }
    }
}
