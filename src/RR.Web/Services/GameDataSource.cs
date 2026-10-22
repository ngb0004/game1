using RR.Sim.Data;

namespace RR.Web.Services
{
    public static class GameDataSource
    {
        /// Loads /data/*.json, embedded into this assembly at build time.
        public static GameData LoadEmbedded()
        {
            var asm = typeof(GameDataSource).Assembly;
            var files = new Dictionary<string, string>();
            foreach (var name in GameData.FileNames)
            {
                using var stream = asm.GetManifestResourceStream("data/" + name)
                    ?? throw new InvalidOperationException("Missing embedded data file " + name);
                using var reader = new StreamReader(stream);
                files[name] = reader.ReadToEnd();
            }
            return GameData.Load(files);
        }
    }
}
