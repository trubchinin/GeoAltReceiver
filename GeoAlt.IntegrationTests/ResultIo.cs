using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GeoAlt.IntegrationTests
{
    internal static class ResultIo
    {
        public static Dictionary<string, string> ReadResultCsv(string path)
        {
            var lines = File.ReadAllLines(path)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray();
            if (lines.Length < 2)
                throw new InvalidOperationException($"CSV має містити щонайменше 2 рядки: {path}");

            var headers = lines[0].Split(',');
            var values  = lines[1].Split(',');
            if (values.Length != headers.Length)
                throw new InvalidOperationException("К-сть значень не дорівнює к-сті заголовків у CSV.");

            var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                dict[headers[i].Trim()] = values[i].Trim();
            return dict;
        }

        public static Dictionary<string, string> ReadResultJson(string path)
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;
            var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in root.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.GetRawText(), // зберігаємо як є
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.True   => "true",
                    JsonValueKind.False  => "false",
                    _                    => prop.Value.GetRawText()
                };
            }
            return dict;
        }

        public static bool TryGetDouble(IDictionary<string,string> map, IEnumerable<string> keys, out double value)
        {
            foreach (var k in keys)
            {
                if (map.TryGetValue(k, out var s) &&
                    double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return true;
            }
            value = double.NaN;
            return false;
        }

        public static bool TryGetBool(IDictionary<string,string> map, IEnumerable<string> keys, out bool value)
        {
            foreach (var k in keys)
            {
                if (map.TryGetValue(k, out var s))
                {
                    if (bool.TryParse(s, out value)) return true;
                    // інколи булеві можуть бути як 0/1:
                    if (s == "0") { value = false; return true; }
                    if (s == "1") { value = true;  return true; }
                }
            }
            value = false;
            return false;
        }
    }
}
