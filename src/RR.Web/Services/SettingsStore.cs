using Microsoft.JSInterop;
using Newtonsoft.Json;
using RR.Client;

namespace RR.Web.Services
{
    public sealed class PlayerSettings
    {
        public SwapMode SwapMode { get; set; } = SwapMode.Hold;
        public int InputOffsetMs { get; set; }
        public bool Sound { get; set; } = true;
        public bool ShowKeys { get; set; } = true;
    }

    public sealed class BestRecord
    {
        public int Score { get; set; }
        public double Dps { get; set; }
        public double Seconds { get; set; }
        public bool Victory { get; set; }
    }

    /// Local save (browser localStorage). Unity will use PlayerPrefs or a file.
    public sealed class SettingsStore
    {
        const string SettingsKey = "rr.settings.v1";
        const string BestKey = "rr.best.v1";

        readonly IJSRuntime _js;
        bool _loaded;

        public PlayerSettings Settings { get; private set; } = new PlayerSettings();
        public Dictionary<string, BestRecord> Best { get; private set; } = new Dictionary<string, BestRecord>();

        public SettingsStore(IJSRuntime js)
        {
            _js = js;
        }

        public async Task LoadAsync()
        {
            if (_loaded) return;
            _loaded = true;
            Settings = await Read<PlayerSettings>(SettingsKey) ?? new PlayerSettings();
            Best = await Read<Dictionary<string, BestRecord>>(BestKey) ?? new Dictionary<string, BestRecord>();
        }

        public Task SaveSettingsAsync() => Write(SettingsKey, Settings);

        /// Records a result; returns true if it is a new best for that boss.
        public async Task<bool> RecordAsync(string bossId, BestRecord record)
        {
            if (Best.TryGetValue(bossId, out var old) && record.Dps <= old.Dps) return false;
            Best[bossId] = record;
            await Write(BestKey, Best);
            return true;
        }

        async Task<T> Read<T>(string key) where T : class
        {
            try
            {
                var json = await _js.InvokeAsync<string>("RR.storage.get", key);
                return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception)
            {
                return null; // private mode, blocked storage or old format: start fresh
            }
        }

        async Task Write(string key, object value)
        {
            try
            {
                await _js.InvokeVoidAsync("RR.storage.set", key, JsonConvert.SerializeObject(value));
            }
            catch (Exception)
            {
                // Saving is best effort.
            }
        }
    }
}
