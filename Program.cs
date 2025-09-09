using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GeoAltReceiver
{
    // === Доменні моделі ===
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

    // === Простенький логгер з ротацією до 1 МБ ===
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
                    // зсуваємо старі
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
            catch { /* не заважаємо виконанню */ }
        }

        private static void Write(string level, string msg)
        {
            RotateIfNeeded();
            var line = $"{DateTime.UtcNow:O} [{level}] {msg}";
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);
    }

    // === CSV Reader + Validator згідно SR-F01..F03/F14 ===
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

            // фіксуємо культуру: десяткова крапка ".", незалежна від локалі
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
                    else
                    {
                        // якщо це не заголовок, продовжуємо як з даними — вимога дозволяє його відсутність
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

                // парсимо числа суворо у InvariantCulture
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

                // семантична валідація за SR-F03
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
                    // не критично для читання, але за SR-F03 ми відхиляємо рядок
                    Reject(lineNo, RejectionCode.BAD_NUMBER_FORMAT, "hi", "Значення hi поза припустимим діапазоном [-500..50000] м.", rejected);
                    bad = true;
                }

                if (!bad)
                {
                    valid.Add(new SatelliteObs(ri, dri, phi, dphi, lambda, dlambda, h, dh));
                    if (verbose) Console.WriteLine($"[ok] line={lineNo}");
                }
            }

            return (valid, rejected);
        }

        private static bool IsHeader(string[] cells)
        {
            if (cells.Length != 8) return false;
            // порівнюємо без урахування регістру та пробілів
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
            // забороняємо кому як десятковий роздільник
            if (s.Contains(',')) { value = double.NaN; return false; }
            return double.TryParse(s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign, ci, out value)
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

    public static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool verbose = args.Contains("--verbose");
            var pathArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            if (pathArg is null)
            {
                Console.WriteLine("GeoAlt-Receiver (ЛР-1) — імпорт і валідація CSV");
                Console.WriteLine("Використання: GeoAltReceiver <path-to-csv> [--verbose]");
                Console.WriteLine("Приклад CSV (8 колонок):");
                Console.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
                Console.WriteLine("100000,5,48.5300,0.001,32.0000,0.001,20200,5");
                return 1;
            }

            try
            {
                var t0 = DateTime.UtcNow;
                Logger.Info($"Start import: {pathArg}");

                var (valid, rejected) = CsvReaderValidator.ReadAndValidate(pathArg, verbose);

                int used = valid.Count;
                int discarded = rejected.Count;

                var dt = (int)(DateTime.UtcNow - t0).TotalMilliseconds;

                // Тут наразі тільки етап ЛР-1: імпорт/валідація.
                // Обчислювальний модуль (SolverGN, GeoTransform, Uncertainty) додамо на наступному кроці.

                Console.WriteLine("=== Підсумок імпорту/валідації ===");
                Console.WriteLine($"used={used}; discarded={discarded}; calc_time_ms={dt}");
                if (rejected.Any())
                {
                    Console.WriteLine("Відхилені рядки (причини):");
                    foreach (var r in rejected)
                        Console.WriteLine($"  - {r}");
                }

                Logger.Info($"Finish import: used={used}; discarded={discarded}; ms={dt}");
                // Вихідний код 0 — успіх, навіть якщо є відхилені записи (це не аварія, а очікувана поведінка).
                return 0;
            }
            catch (ApplicationException ex)
            {
                Console.WriteLine(ex.Message);
                Logger.Error(ex.ToString());
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Непередбачена помилка. Див. geoalt.log");
                Logger.Error(ex.ToString());
                return 3;
            }
        }
    }
}
