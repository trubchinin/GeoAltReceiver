using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GeoAlt.Core;

namespace GeoAlt.Tests
{
    internal static class SharedTestData
    {
        // Базова "істина" для приймача (раціональні значення)
        public static readonly GeoPoint RxTrue = new GeoPoint(50.000000, 30.000000, 250.0);

        // Синтетичні супутники (геодезичні координати, висота ~20 200 км)
        public static List<GeoPoint> BuildSatellites()
        {
            double hSat = 20200000.0; // м
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

        public static (List<EcefPoint> satsEcef, List<double> ranges) BuildGeometry()
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

        // Записує тимчасовий CSV із 4 першими супутниками (мінімум для рішення)
        public static string WriteTempCsvN4()
        {
            var (satsEcefUnused, ranges) = BuildGeometry();
            var satsGeo = BuildSatellites();

            // Візьмемо перші 4 супутники
            int n = 4;
            double dRi = 5.0;     // м
            double dPhi = 0.001;  // град
            double dLam = 0.001;  // град
            double dH   = 5.0;    // м

            string tmp = Path.Combine(Path.GetTempPath(), $"CSV_TestN4_{Guid.NewGuid():N}.csv");
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

        // Добрий CSV (7 рядків)
        public static (string path, int n) WriteTempCsvN7()
        {
            var (satsEcefUnused, ranges) = BuildGeometry();
            var satsGeo = BuildSatellites();

            int n = satsGeo.Count; // 7
            double dRi = 5.0, dPhi = 0.001, dLam = 0.001, dH = 5.0;

            string tmp = Path.Combine(Path.GetTempPath(), $"CSV_TestN7_{Guid.NewGuid():N}.csv");
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
            return (tmp, n);
        }
    }
}
