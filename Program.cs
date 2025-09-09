using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GeoAltReceiver
{
    // === Доманні моделі ===
    public record SatelliteObs(
        double r, double dr,
        double phiDeg, double dphiDeg,
        double lambdaDeg, double dlambdaDeg,
        double h, double dh
    );

    public enum RejectionCode
    {
        OUT_OF_RANGE_LAT,
        OUT_OF_RANGE_LON,
        NEGATIVE_DISTANCE,
        NEGATIVE_SIGMA,
        BAD_NUMBER_FORMAT,
        MISSING_COLUMN
    }

    public sealed class Rejection
    {
        public int LineNo { get; init; }
        public RejectionCode Code { get; init; }
        public string Field { get; init; } = "";
        public string Message { get; init; } = "";
        public override string ToString() => $"line={LineNo}; code={Code}; field={Field}; msg={Message}";
    }

    public record EcefPoint(double X, double Y, double Z);
    public record GeoPoint(double PhiDeg, double LambdaDeg, double Hm);

    public sealed class SolveResult
    {
        public GeoPoint OutGeo { get; init; } = new GeoPoint(0,0,0);
        public int Used { get; init; }
        public int Discarded { get; init; }
        public int Iters { get; init; }
        public bool Converged { get; init; }
        public int CalcTimeMs { get; init; }
        public string Note { get; init; } = "";
    }

    // === Простий логер із ротацією до 1 МБ (останні 3 файли) ===
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "geoalt.log");
        private const long MaxBytes = 1_000_000;
        private const int Keep = 3;

        private static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    for (int i = Keep - 1; i >= 1; i--)
                    {
                        var src = LogPath + "." + i;
                        var dst = LogPath + "." + (i + 1);
                        if (File.Exists(dst)) File.Delete(dst);
                        if (File.Exists(src)) File.Move(src, dst);
                    }
                    var first = LogPath + ".1";
                    if (File.Exists(first)) File.Delete(first);
                    File.Move(LogPath, first);
                }
            }
            catch { /* не блокуємо виконання */ }
        }

        private static void Write(string level, string msg)
        {
            RotateIfNeeded();
            var line = $"{DateTime.UtcNow:O} [{level}] {msg}";
            try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* ignore */ }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);
    }

    // === WGS-84 та перетворення геодезичні ↔ ECEF (SR-F04/05/09) ===
    public static class GeoTransform
    {
        // Параметри WGS-84
        public const double A = 6378137.0;                 // велика піввісь (м)
        public const double F = 1.0 / 298.257223563;       // сплюснутість
        public static readonly double E2 = 2 * F - F * F;  // ексцентриситет^2

        public static double Deg2Rad(double d) => d * Math.PI / 180.0;
        public static double Rad2Deg(double r) => r * 180.0 / Math.PI;

        // N(φ) = a / sqrt(1 - e^2 sin^2 φ)
        public static double N(double phiRad) => A / Math.Sqrt(1.0 - E2 * Math.Sin(phiRad) * Math.Sin(phiRad));

        // (φ,λ,h) [град,град,м] → ECEF (X,Y,Z) [м]
        public static EcefPoint GeoToEcef(GeoPoint g)
        {
            double phi = Deg2Rad(g.PhiDeg);
            double lam = Deg2Rad(g.LambdaDeg);
            double h = g.Hm;

            double Nphi = N(phi);
            double cosPhi = Math.Cos(phi);
            double sinPhi = Math.Sin(phi);
            double cosLam = Math.Cos(lam);
            double sinLam = Math.Sin(lam);

            double X = (Nphi + h) * cosPhi * cosLam;
            double Y = (Nphi + h) * cosPhi * sinLam;
            double Z = (Nphi * (1.0 - E2) + h) * sinPhi;

            return new EcefPoint(X, Y, Z);
        }

        // ECEF (X,Y,Z) → (φ,λ,h) [град,град,м]; ітеративний метод (точність до ~1e-12 рад)
        public static GeoPoint EcefToGeo(EcefPoint p)
        {
            double X = p.X, Y = p.Y, Z = p.Z;

            double lambda = Math.Atan2(Y, X);
            double pxy = Math.Sqrt(X * X + Y * Y);

            // початкове наближення для φ
            double phi = Math.Atan2(Z, pxy * (1.0 - E2));
            for (int i = 0; i < 20; i++)
            {
                double Nphi = N(phi);
                double h = pxy / Math.Cos(phi) - Nphi;
                double phiNew = Math.Atan2(Z, pxy * (1.0 - E2 * Nphi / (Nphi + h)));
                if (Math.Abs(phiNew - phi) < 1e-12) { phi = phiNew; break; }
                phi = phiNew;
            }
            double Nf = N(phi);
            double hFinal = pxy / Math.Cos(phi) - Nf;

            return new GeoPoint(Rad2Deg(phi), Rad2Deg(lambda), hFinal);
        }
    }

    // === Імпорт CSV + валідація (SR-F01..F03/F14) ===
    public static class CsvReaderValidator
    {
        private static readonly string[] ExpectedHeader = new[]
        {
            "ri","Δri","φi","Δφi","λi","Δλi","hi","Δhi"
        };

        public static (List<SatelliteObs> valid, List<Rejection> rejected) ReadAndValidate(string path, bool verbose)
        {
            var rejected = new List<Rejection>();
            var valid = new List<SatelliteObs>();

            if (!File.Exists(path))
            {
                var msg = $"ERR_BAD_CSV_SCHEMA: \"Очікується 8 колонок: ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi.\" Файл не знайдено: {path}";
                Logger.Error(msg);
                throw new ApplicationException(msg);
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sr = new StreamReader(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            string? line;
            int lineNo = 0;
            bool headerChecked = false;

            var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();

            while ((line = sr.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cells = line.Split(',', StringSplitOptions.None);

                if (!headerChecked)
                {
                    headerChecked = true;
                    if (IsHeader(cells))
                    {
                        if (verbose) Console.WriteLine("[i] Header detected.");
                        continue; // пропускаємо заголовок
                    }
                }

                if (cells.Length != 8)
                {
                    rejected.Add(new Rejection
                    {
                        LineNo = lineNo,
                        Code = RejectionCode.MISSING_COLUMN,
                        Field = "schema",
                        Message = "ERR_BAD_CSV_SCHEMA: Очікується 8 колонок: ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi."
                    });
                    Logger.Error($"Bad schema at line {lineNo}");
                    continue;
                }

                if (!TryParseDouble(cells[0], ci, out double ri))
                { RejectNumber(lineNo, "ri", rejected); continue; }
                if (!TryParseDouble(cells[1], ci, out double dri))
                { RejectNumber(lineNo, "Δri", rejected); continue; }
                if (!TryParseDouble(cells[2], ci, out double phi))
                { RejectNumber(lineNo, "φi", rejected); continue; }
                if (!TryParseDouble(cells[3], ci, out double dphi))
                { RejectNumber(lineNo, "Δφi", rejected); continue; }
                if (!TryParseDouble(cells[4], ci, out double lambda))
                { RejectNumber(lineNo, "λi", rejected); continue; }
                if (!TryParseDouble(cells[5], ci, out double dlambda))
                { RejectNumber(lineNo, "Δλi", rejected); continue; }
                if (!TryParseDouble(cells[6], ci, out double h))
                { RejectNumber(lineNo, "hi", rejected); continue; }
                if (!TryParseDouble(cells[7], ci, out double dh))
                { RejectNumber(lineNo, "Δhi", rejected); continue; }

                bool bad = false;
                if (ri <= 0) { Reject(lineNo, RejectionCode.NEGATIVE_DISTANCE, "ri", "Невід’ємні значення очікуються для ri (>0).", rejected); bad = true; }
                if (dri < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δri", "Невід’ємні значення очікуються для Δri (≥0).", rejected); bad = true; }

                if (phi < -90 || phi > 90) { Reject(lineNo, RejectionCode.OUT_OF_RANGE_LAT, "φi", "Значення поза допустимим діапазоном [-90..90].", rejected); bad = true; }
                if (lambda < -180 || lambda > 180) { Reject(lineNo, RejectionCode.OUT_OF_RANGE_LON, "λi", "Значення поза допустимим діапазоном [-180..180].", rejected); bad = true; }

                if (dphi < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δφi", "Невід’ємні значення очікуються для Δφi (≥0).", rejected); bad = true; }
                if (dlambda < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δλi", "Невід’ємні значення очікуються для Δλi (≥0).", rejected); bad = true; }
                if (dh < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δhi", "Невід’ємні значення очікуються для Δhi (≥0).", rejected); bad = true; }

                if (h < -500 || h > 50000)
                {
                    Reject(lineNo, RejectionCode.BAD_NUMBER_FORMAT, "hi", "Значення hi поза припустимим діапазоном [-500..50000] м.", rejected);
                    bad = true;
                }

                if (!bad)
                {
                    valid.Add(new SatelliteObs(ri, dri, phi, dphi, lambda, dlambda, h, dh));
                }
            }

            return (valid, rejected);
        }

        private static bool IsHeader(string[] cells)
        {
            if (cells.Length != 8) return false;
            for (int i = 0; i < 8; i++)
            {
                var a = cells[i].Trim();
                var b = ExpectedHeader[i];
                if (!string.Equals(a, b, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool TryParseDouble(string s, CultureInfo ci, out double value)
        {
            if (s.Contains(',')) { value = double.NaN; return false; } // тільки крапка
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowLeadingSign, ci, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void RejectNumber(int lineNo, string field, List<Rejection> rej)
        {
            rej.Add(new Rejection
            {
                LineNo = lineNo,
                Code = RejectionCode.BAD_NUMBER_FORMAT,
                Field = field,
                Message = $"ERR_NUMBER_FORMAT: Невірний числовий формат у полі {field}."
            });
            Logger.Error($"Number format at line {lineNo}, field {field}");
        }

        private static void Reject(int lineNo, RejectionCode code, string field, string message, List<Rejection> rej)
        {
            rej.Add(new Rejection { LineNo = lineNo, Code = code, Field = field, Message = message });
            var lvl = code == RejectionCode.BAD_NUMBER_FORMAT ? "ERROR" : "WARN";
            if (lvl == "ERROR") Logger.Error($"{code} at line {lineNo}, field {field}");
            else Logger.Warn($"{code} at line {lineNo}, field {field}");
        }
    }

    // === Розв’язувач (МНК, Гаусса–Ньютона з демпфуванням) — SR-F06..F08 ===
    public static class SolverGN
    {
        public sealed class Options
        {
            public int MaxIters { get; init; } = 1000;
            public double Tol { get; init; } = 1e-6;      // за нормою приросту (метри)
            public double Damp { get; init; } = 1e-3;     // мале демпфування в нормальних рівняннях
        }

        // Початкове наближення: центроїд супутників (ECEF), трохи зсунути до середнього радіуса
        private static EcefPoint InitialGuess(List<EcefPoint> sats)
        {
            double cx = 0, cy = 0, cz = 0;
            foreach (var s in sats) { cx += s.X; cy += s.Y; cz += s.Z; }
            cx /= sats.Count; cy /= sats.Count; cz /= sats.Count;

            double r = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            if (r < 1) r = GeoTransform.A; // підстрахуємо
            double scale = (GeoTransform.A - 100.0) / r; // трохи «до Землі»
            return new EcefPoint(cx * scale, cy * scale, cz * scale);
        }

        // Рішення 3x3 (A x = b) через Гауссове виключення (без зовн. бібліотек)
        private static bool Solve3x3(double[,] A, double[] b, out double[] x)
        {
            x = new double[3];
            int n = 3;
            // Розширена матриця
            double[,] M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, n] = b[i];
            }
            // Прямий хід з частковим вибором головного елемента
            for (int k = 0; k < n; k++)
            {
                int piv = k;
                double max = Math.Abs(M[k, k]);
                for (int i = k + 1; i < n; i++)
                {
                    double val = Math.Abs(M[i, k]);
                    if (val > max) { max = val; piv = i; }
                }
                if (max < 1e-15) return false; // виродження

                if (piv != k)
                {
                    for (int j = k; j <= n; j++)
                    {
                        (M[k, j], M[piv, j]) = (M[piv, j], M[k, j]);
                    }
                }
                double diag = M[k, k];
                for (int j = k; j <= n; j++) M[k, j] /= diag;

                for (int i = k + 1; i < n; i++)
                {
                    double f = M[i, k];
                    for (int j = k; j <= n; j++) M[i, j] -= f * M[k, j];
                }
            }
            // Зворотній хід
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = M[i, n];
                for (int j = i + 1; j < n; j++) sum -= M[i, j] * x[j];
                x[i] = sum / M[i, i]; // M[i,i] == 1
            }
            return true;
        }

        public static (EcefPoint rEcef, int iters, bool converged) Solve(List<EcefPoint> sats, List<double> ranges, Options opt, bool verbose)
        {
            // r — шукане положення приймача (ECEF)
            var r = InitialGuess(sats);
            if (verbose) Console.WriteLine($"[i] Init guess ECEF: X={r.X:F3} Y={r.Y:F3} Z={r.Z:F3}");

            for (int k = 0; k < opt.MaxIters; k++)
            {
                // Побудова Якобіана J і вектору нев’язок v (розмір N)
                int n = sats.Count;
                double[,] JTJ = new double[3, 3]; // нормальні рівняння J^T J
                double[] JTv = new double[3];     // J^T v

                double normDx;

                for (int i = 0; i < n; i++)
                {
                    var s = sats[i];
                    double dx = r.X - s.X;
                    double dy = r.Y - s.Y;
                    double dz = r.Z - s.Z;
                    double gi = Math.Sqrt(dx * dx + dy * dy + dz * dz); // модельна відстань
                    if (gi < 1e-12) gi = 1e-12;

                    // Нев’язка: виміряне - модель
                    double vi = ranges[i] - gi;

                    // Рядок Якобіана: ∂g/∂r = ( (r - s) / ||r - s|| )
                    double jx = dx / gi;
                    double jy = dy / gi;
                    double jz = dz / gi;

                    // Акумулюємо JTJ і JTv
                    JTJ[0,0] += jx * jx; JTJ[0,1] += jx * jy; JTJ[0,2] += jx * jz;
                    JTJ[1,0] += jy * jx; JTJ[1,1] += jy * jy; JTJ[1,2] += jy * jz;
                    JTJ[2,0] += jz * jx; JTJ[2,1] += jz * jy; JTJ[2,2] += jz * jz;

                    JTv[0] += jx * vi;
                    JTv[1] += jy * vi;
                    JTv[2] += jz * vi;
                }

                // Демпфування (стабілізація)
                JTJ[0,0] += opt.Damp; JTJ[1,1] += opt.Damp; JTJ[2,2] += opt.Damp;

                if (!Solve3x3(JTJ, JTv, out var dr))
                {
                    if (verbose) Console.WriteLine("[!] Ill-conditioned normal matrix; stopping.");
                    return (r, k, false);
                }

                // Оновлюємо оцінку
                r = new EcefPoint(r.X + dr[0], r.Y + dr[1], r.Z + dr[2]);

                normDx = Math.Sqrt(dr[0]*dr[0] + dr[1]*dr[1] + dr[2]*dr[2]);
                if (verbose) Console.WriteLine($"[i] iter={k+1}; |Δr|={normDx:F6} m");

                if (normDx <= opt.Tol)
                {
                    return (r, k + 1, true);
                }
            }
            return (r, opt.MaxIters, false);
        }
    }

    public static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool verbose = args.Contains("--verbose");
            var pathArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            if (pathArg is null)
            {
                Console.WriteLine("GeoAlt-Receiver (ЛР-1) — імпорт/валідація CSV + обчислення φ,λ,h");
                Console.WriteLine("Використання: GeoAltReceiver <path-to-csv> [--verbose]");
                Console.WriteLine("Приклад CSV (8 колонок):");
                Console.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
                Console.WriteLine("100000,5,48.5300,0.001,32.0000,0.001,20200,5");
                return 1;
            }

            try
            {
                var t0 = DateTime.UtcNow;
                Logger.Info($"Start run: {pathArg}");

                var (valid, rejected) = CsvReaderValidator.ReadAndValidate(pathArg, verbose);
                int used = valid.Count;
                int discarded = rejected.Count;

                if (used < 4)
                {
                    Console.WriteLine("ERR_NOT_ENOUGH_DATA: Недостатньо даних для визначення φ, λ, h (N<4).");
                    Logger.Error("Not enough valid observations (N<4).");
                    return 2;
                }

                // Перетворюємо супутники в ECEF
                var satsEcef = new List<EcefPoint>(used);
                var ranges = new List<double>(used);
                foreach (var o in valid)
                {
                    var geoSat = new GeoPoint(o.phiDeg, o.lambdaDeg, o.h);
                    var ecefSat = GeoTransform.GeoToEcef(geoSat);
                    satsEcef.Add(ecefSat);
                    ranges.Add(o.r);
                }

                // Розв’язуємо положення приймача (ECEF)
                var opt = new SolverGN.Options { MaxIters = 1000, Tol = 1e-6, Damp = 1e-3 };
                var (rEcef, iters, converged) = SolverGN.Solve(satsEcef, ranges, opt, verbose);

                // Перетворюємо в геодезичні координати приймача
                var outGeo = GeoTransform.EcefToGeo(rEcef);

                var dt = (int)(DateTime.UtcNow - t0).TotalMilliseconds;

                // Вивід результатів (UR01 + частина SR-F11)
                Console.WriteLine("=== Результат обчислення ===");
                Console.WriteLine($"phi_deg={outGeo.PhiDeg:F8}");
                Console.WriteLine($"lambda_deg={outGeo.LambdaDeg:F8}");
                Console.WriteLine($"h_m={outGeo.Hm:F3}");
                Console.WriteLine($"used={used}; discarded={discarded}; calc_time_ms={dt}");
                Console.WriteLine($"iters={iters}; converged={(converged ? "true" : "false")}");

                // Попереджувальні нотатки
                if (!converged)
                    Console.WriteLine("WARNING: Досягнуто ліміт ітерацій без збіжності (результат попередній).");

                Logger.Info($"Finish: used={used}; discarded={discarded}; iters={iters}; conv={converged}; ms={dt}");
                return 0;
            }
            catch (ApplicationException ex)
            {
                Console.WriteLine(ex.Message);
                Logger.Error(ex.ToString());
                return 3;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Непередбачена помилка. Див. geoalt.log");
                Logger.Error(ex.ToString());
                return 4;
            }
        }
    }
}
