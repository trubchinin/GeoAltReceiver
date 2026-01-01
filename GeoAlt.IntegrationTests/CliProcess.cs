using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    internal static class CliProcess
    {
        public sealed class RunResult
        {
            public int ExitCode { get; init; }
            public string StdOut { get; init; } = "";
            public string StdErr { get; init; } = "";
            public string CsvPath { get; init; } = "";
            public string ResultCsv { get; init; } = "";
            public string ResultJson { get; init; } = "";
            public string ProtocolTxt { get; init; } = "";
        }

        private static string FindAppCsproj()
        {
            string start = TestContext.CurrentContext.TestDirectory;
            var dir = new DirectoryInfo(start);
            for (int up = 0; up < 7 && dir != null; up++, dir = dir.Parent)
            {
                var candidate1 = Path.Combine(dir.FullName, "GeoAlt.App", "GeoAlt.App.csproj");
                if (File.Exists(candidate1)) return candidate1;

                var found = Directory.EnumerateFiles(dir.FullName, "GeoAlt.App.csproj", SearchOption.AllDirectories)
                                     .FirstOrDefault();
                if (found != null) return found;
            }
            throw new FileNotFoundException("Не знайдено GeoAlt.App.csproj (перевірте структуру рішення).");
        }

        public static async Task<RunResult> RunAsync(string csvPath, bool verbose)
        {
            string appProj = FindAppCsproj();

            string baseName = Path.GetFileNameWithoutExtension(csvPath);
            string dir = Path.GetDirectoryName(csvPath)!;

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{appProj}\" -- \"{csvPath}\"" + (verbose ? " --verbose" : ""),
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var p = new Process { StartInfo = psi };
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();

            string resultCsv = Path.Combine(dir, baseName + "_result.csv");
            string resultJson = Path.Combine(dir, baseName + "_result.json");
            string protocol   = Path.Combine(dir, baseName + "_protocol.txt");

            return new RunResult
            {
                ExitCode = p.ExitCode,
                StdOut = sbOut.ToString(),
                StdErr = sbErr.ToString(),
                CsvPath = csvPath,
                ResultCsv = resultCsv,
                ResultJson = resultJson,
                ProtocolTxt = protocol
            };
        }
    }
}
