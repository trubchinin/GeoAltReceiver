using System;
using System.Linq;
using System.Text;
using GeoAlt.Core;

namespace GeoAlt.App
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool verbose = args.Contains("--verbose");
            string? pathArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            if (pathArg is null)
            {
                Console.WriteLine("GeoAlt-Receiver (ЛР-2) — консольний запуск через GeoAlt.Core.Runner");
                Console.WriteLine("Використання: GeoAlt.App <path-to-csv> [--verbose]");
                Console.WriteLine("Приклад CSV (8 колонок):");
                Console.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
                Console.WriteLine("100000,5,48.5300,0.001,32.0000,0.001,20200,5");
                return 1;
            }

            var res = Runner.Run(pathArg, verbose);
            
            // Return 1 if critical failure (not enough data) or didn't converge?
            // For IT-AB-04 ("Missing Column"), used will be 0 (or <4).
            // So checking Used < 4 is a good condition for non-zero exit.
            if (res.Used < 4 || !res.Converged)
            {
                return 1;
            }
            return 0;
        }
    }
}
