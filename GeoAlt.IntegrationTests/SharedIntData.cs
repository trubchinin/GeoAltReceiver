using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GeoAlt.Core;

namespace GeoAlt.IntegrationTests
{
    internal static class SharedIntData
    {
        public static readonly GeoPoint RxTrue = new GeoPoint(50.0, 30.0, 250.0);

        private static List<GeoPoint> BuildSatellites()
        {
            double hSat = 20200000.0;
            double p = RxTrue.PhiDeg;
            double l = RxTrue.LambdaDeg;
            return new List<GeoPoint>
            {
                new GeoPoint(p + 20.0, l +   0.0, hSat),
                new GeoPoint(p - 15.0, l +  40.0, hSat),
                new GeoPoint(p +  5.0, l -  60.0, hSat),
                new GeoPoint(p - 25.0, l -  20.0, hSat),
                new GeoPoint(p + 10.0, l + 100.0, hSat),
                new GeoPoint(p - 35.0, l - 120.0, hSat),
                new GeoPoint(p + 30.0, l + 160.0, hSat),
            };
        }

        private static (List<EcefPoint> satsEcef, List<double> ranges) BuildGeometry()
        {
            var satsGeo = BuildSatellites();
            var satsEcef = new List<EcefPoint>(satsGeo.Count);
            foreach (var g in satsGeo) satsEcef.Add(GeoTransform.GeoToEcef(g));

            var rxEcef = GeoTransform.GeoToEcef(RxTrue);
            var ranges = new List<double>(satsEcef.Count);
            foreach (var s in satsEcef)
            {
                double dx = rxEcef.X - s.X;
                double dy = rxEcef.Y - s.Y;
                double dz = rxEcef.Z - s.Z;
                ranges.Add(Math.Sqrt(dx*dx + dy*dy + dz*dz));
            }
            return (satsEcef, ranges);
        }

        public static string WriteTempCsvN4()
        {
            var (satsEcefUnused, ranges) = BuildGeometry();
            var satsGeo = BuildSatellites();

            int n = 4;
            double dRi = 5.0, dPhi = 0.001, dLam = 0.001, dH = 5.0;

            string workDir = Path.Combine(Path.GetTempPath(), "geoalt_it_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string tmp = Path.Combine(workDir, "CSV_Valid_4rows.csv");

            using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
            sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
            for (int i = 0; i < n; i++)
            {
                var g = satsGeo[i];
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    ranges[i], dRi, g.PhiDeg, dPhi, g.LambdaDeg, dLam, g.Hm, dH);
                sw.WriteLine(line);
            }
            sw.Flush();
            return tmp;
        }

        // Валідний CSV із 7 рядками (для IT-AB-02)
        public static string WriteTempCsvN7()
        {
            var (satsEcefUnused, ranges) = typeof(SharedIntData)
                .GetMethod("BuildGeometry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null) is ValueTuple<System.Collections.Generic.List<GeoAlt.Core.EcefPoint>, System.Collections.Generic.List<double>> t
                ? (t.Item1, t.Item2)
                : throw new System.InvalidOperationException("BuildGeometry unavailable");

            // Отримаємо супутники із приватного BuildSatellites()
            var satsGeo = typeof(SharedIntData)
                .GetMethod("BuildSatellites", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null) as System.Collections.Generic.List<GeoAlt.Core.GeoPoint>;

            int n = satsGeo!.Count; // 7
            double dRi = 5.0, dPhi = 0.001, dLam = 0.001, dH = 5.0;

            string workDir = Path.Combine(Path.GetTempPath(), "geoalt_it_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string tmp = Path.Combine(workDir, "CSV_Valid_7rows.csv");

            using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
            sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
            for (int i = 0; i < n; i++)
            {
                var g = satsGeo[i];
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    ranges[i], dRi, g.PhiDeg, dPhi, g.LambdaDeg, dLam, g.Hm, dH);
                sw.WriteLine(line);
            }
            sw.Flush();
            return tmp;
        }

        // Невалідний CSV (відсутня 1 колонка) для IT-AB-04
        public static string WriteTempCsv_MissingColumn()
        {
            string workDir = Path.Combine(Path.GetTempPath(), "geoalt_it_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string tmp = Path.Combine(workDir, "CSV_MissingCol.csv");

            using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
            // Навмисно ВІДСУТНЯ остання колонка Δhi (лише 7 полів у заголовку)
            sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi");
            // Один рядок даних (значення довільні, але коректні за форматом)
            sw.WriteLine("100000,5,48.53,0.001,32.0,0.001,20200000");
            sw.Flush();
            return tmp;
        }

        // CSV із 5 рядками: 4 валідні + 1 з "поганим числом" (кома в φi).
        // Використовується для IT-ABC-01: без --verbose протокол стислий,
        // з --verbose має з'явитися BAD_NUMBER_FORMAT у протоколі.
        public static string WriteTempCsv_BadNumber_5Rows()
        {
            // Отримаємо геометрію та супутники через приватні методи
            var (satsEcefUnused, ranges) = typeof(SharedIntData)
                .GetMethod("BuildGeometry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null) is ValueTuple<System.Collections.Generic.List<GeoAlt.Core.EcefPoint>, System.Collections.Generic.List<double>> t
                ? (t.Item1, t.Item2)
                : throw new System.InvalidOperationException("BuildGeometry unavailable");

            var satsGeo = typeof(SharedIntData)
                .GetMethod("BuildSatellites", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null) as System.Collections.Generic.List<GeoAlt.Core.GeoPoint>;

            // 5 рядків: 4 валідні, 1 зі свідомо "битим" форматом (кома в φi)
            int n = 5;
            double dRi = 5.0, dPhi = 0.001, dLam = 0.001, dH = 5.0;

            string workDir = Path.Combine(Path.GetTempPath(), "geoalt_it_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string tmp = Path.Combine(workDir, "CSV_BadNumber_5rows.csv");

            using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
            sw.WriteLine("ri,Δri,φi,Δφі,λi,Δλi,hi,Δhi");
            // 0..3 — валідні
            for (int i = 0; i < n - 1; i++)
            {
                var g = satsGeo![i];
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    ranges[i], dRi, g.PhiDeg, dPhi, g.LambdaDeg, dLam, g.Hm, dH);
                sw.WriteLine(line);
            }
            // 4-й — "битий": у φi ставимо кому (імітація локалі)
            {
                var g = satsGeo![n - 1];
                // Увага: в полі φi — "48,5300" (кома), решта як у валідному кейсі
                string badLine =
                    $"{ranges[n-1].ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{dRi.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"48,5300," + // << свідомо з комою
                    $"{dPhi.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{g.LambdaDeg.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{dLam.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{g.Hm.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{dH.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                sw.WriteLine(badLine);
            }

            sw.Flush();
            return tmp;
        }

        // 4 дуже близькі за напрямком супутники → погана геометрія
        public static string WriteTempCsv_PoorGeom4()
        {
            double dRi = 5.0, dPhi = 0.0005, dLam = 0.0005, dH = 5.0;
            double hSat = 20200000.0;

            // Близько до приймача, але з мінімальним рознесенням
            var satsGeo = new System.Collections.Generic.List<GeoAlt.Core.GeoPoint>
            {
                new GeoAlt.Core.GeoPoint(RxTrue.PhiDeg + 0.05, RxTrue.LambdaDeg + 0.00, hSat),
                new GeoAlt.Core.GeoPoint(RxTrue.PhiDeg + 0.05, RxTrue.LambdaDeg + 0.05, hSat),
                new GeoAlt.Core.GeoPoint(RxTrue.PhiDeg + 0.10, RxTrue.LambdaDeg + 0.05, hSat),
                new GeoAlt.Core.GeoPoint(RxTrue.PhiDeg + 0.10, RxTrue.LambdaDeg + 0.00, hSat),
            };

            var satsEcef = new System.Collections.Generic.List<GeoAlt.Core.EcefPoint>(4);
            foreach (var g in satsGeo) satsEcef.Add(GeoAlt.Core.GeoTransform.GeoToEcef(g));

            var rxEcef = GeoAlt.Core.GeoTransform.GeoToEcef(RxTrue);
            var ranges = new System.Collections.Generic.List<double>(4);
            foreach (var s in satsEcef)
            {
                double dx = rxEcef.X - s.X;
                double dy = rxEcef.Y - s.Y;
                double dz = rxEcef.Z - s.Z;
                ranges.Add(System.Math.Sqrt(dx*dx + dy*dy + dz*dz));
            }

            string workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "geoalt_it_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(workDir);
            string tmp = System.IO.Path.Combine(workDir, "CSV_PoorGeom_4rows.csv");

            using var sw = new System.IO.StreamWriter(tmp, false, System.Text.Encoding.UTF8);
            sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
            for (int i = 0; i < 4; i++)
            {
                var g = satsGeo[i];
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    ranges[i], dRi, g.PhiDeg, dPhi, g.LambdaDeg, dLam, g.Hm, dH);
                sw.WriteLine(line);
            }
            sw.Flush();
            return tmp;
        }

        // CSV із 5 рядками: 4 валідні + 1 з від'ємною відстанню (ri = -100)
        public static string WriteTempCsv_NegativeDistances()
        {
            // Беремо 4 валідні
            var (satsEcefUnused, ranges) = typeof(SharedIntData)
                .GetMethod("BuildGeometry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null) is ValueTuple<System.Collections.Generic.List<GeoAlt.Core.EcefPoint>, System.Collections.Generic.List<double>> t
                ? (t.Item1, t.Item2)
                : throw new System.InvalidOperationException("BuildGeometry unavailable");

            var satsGeo = typeof(SharedIntData)
                .GetMethod("BuildSatellites", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, null) as System.Collections.Generic.List<GeoAlt.Core.GeoPoint>;

            int n = 4; 
            double dRi = 5.0, dPhi = 0.001, dLam = 0.001, dH = 5.0;

            string workDir = Path.Combine(Path.GetTempPath(), "geoalt_it_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string tmp = Path.Combine(workDir, "CSV_NegativeDist.csv");

            using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
            sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
            
            // 4 валідні
            for (int i = 0; i < n; i++)
            {
                var g = satsGeo![i];
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    ranges[i], dRi, g.PhiDeg, dPhi, g.LambdaDeg, dLam, g.Hm, dH);
                sw.WriteLine(line);
            }
            
            // 1 невалідний (ri < 0)
            // Використовуємо 0-й супутник але зіпсуємо Range
            {
                var g = satsGeo![0];
                double badRange = -100.5;
                string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    badRange, dRi, g.PhiDeg, dPhi, g.LambdaDeg, dLam, g.Hm, dH);
                sw.WriteLine(line);
            }

            sw.Flush();
            return tmp;
        }
    }
}
