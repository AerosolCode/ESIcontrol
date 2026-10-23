using System;
using System.IO;
using System.Linq;

namespace ESIcontrol
{
    internal static class AppPaths
    {
        public static readonly string SharedRoot = ResolveSharedRoot();
        public static string ConditionsCsv => Path.Combine(SharedRoot, "Config", "conditions.csv");
        public static string EsiLogDirectory => Path.Combine(SharedRoot, "Logs", "ESIcontrol");
        public static string AiLogCsv => Path.Combine(SharedRoot, "Data", "ESIcontrol", "ai_mean_log.csv");
        public static string CameraCommands => Path.Combine(SharedRoot, "Camera", "commands");
        public static string DmaIncoming => Path.Combine(SharedRoot, "DMA", "incoming");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(SharedRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(ConditionsCsv));
            Directory.CreateDirectory(EsiLogDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(AiLogCsv));
            Directory.CreateDirectory(CameraCommands);
            Directory.CreateDirectory(DmaIncoming);
        }

        private static string ResolveSharedRoot()
        {
            string configured = Environment.GetEnvironmentVariable("ESI_SUITE_ROOT");
            if (string.IsNullOrWhiteSpace(configured))
            {
                string sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shared-root.txt");
                if (File.Exists(sidecar))
                    configured = File.ReadLines(sidecar)
                        .Select(line => line.Trim())
                        .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("#"));
            }

            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                    "ESIControlSuite");
            }

            configured = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            if (!Path.IsPathRooted(configured))
                configured = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
            return Path.GetFullPath(configured);
        }
    }
}
