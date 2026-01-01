using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("ABC"), Category("System"), Category("Acceptance")]
    public class IT_ABC_04_ExportConsistency_Tests
    {
        [Test, Category("IT"), Category("ABC")]
        public async Task ResultCsvAndJson_AreConsistent_ByKeySetAndTolerance()
        {
            // Arrange
            string csvIn = SharedIntData.WriteTempCsvN7();

            // Act
            var run = await CliProcess.RunAsync(csvIn, verbose:true);
            Assert.That(run.ExitCode, Is.EqualTo(0), "Очікується успішний запуск");
            Assert.That(File.Exists(run.ResultCsv), Is.True);
            Assert.That(File.Exists(run.ResultJson), Is.True);

            // Read
            var mapCsv  = ResultIo.ReadResultCsv(run.ResultCsv);
            var mapJson = ResultIo.ReadResultJson(run.ResultJson);

            // Ключі (з урахуванням можливих варіантів назв)
            var kPhi  = new[] { "phi_deg", "phi" };
            var kLam  = new[] { "lambda_deg", "lambda" };
            var kH    = new[] { "h_m", "h" };
            var kEpsP = new[] { "eps_phi_pct" };
            var kEpsL = new[] { "eps_lambda_pct" };
            var kEpsH = new[] { "eps_h_pct" };
            var kIt   = new[] { "iters", "iterations" };
            var kConv = new[] { "converged" };

            // Порівняння числових полів з допуском
            Assert.Multiple(() =>
            {
                Assert.That(ResultIo.TryGetDouble(mapCsv,  kPhi, out var phiCsv), Is.True,  "CSV: phi");
                Assert.That(ResultIo.TryGetDouble(mapJson, kPhi, out var phiJs),  Is.True,  "JSON: phi");
                Assert.That(phiCsv, Is.EqualTo(phiJs).Within(1e-6));

                Assert.That(ResultIo.TryGetDouble(mapCsv,  kLam, out var lamCsv), Is.True,  "CSV: lambda");
                Assert.That(ResultIo.TryGetDouble(mapJson, kLam, out var lamJs),  Is.True,  "JSON: lambda");
                Assert.That(lamCsv, Is.EqualTo(lamJs).Within(1e-6));

                Assert.That(ResultIo.TryGetDouble(mapCsv,  kH, out var hCsv),     Is.True,  "CSV: h");
                Assert.That(ResultIo.TryGetDouble(mapJson, kH, out var hJs),      Is.True,  "JSON: h");
                Assert.That(hCsv, Is.EqualTo(hJs).Within(1e-6));

                Assert.That(ResultIo.TryGetDouble(mapCsv,  kEpsP, out var epCsv), Is.True,  "CSV: eps_phi_pct");
                Assert.That(ResultIo.TryGetDouble(mapJson, kEpsP, out var epJs),  Is.True,  "JSON: eps_phi_pct");
                Assert.That(epCsv, Is.EqualTo(epJs).Within(1e-9));

                Assert.That(ResultIo.TryGetDouble(mapCsv,  kEpsL, out var elCsv), Is.True,  "CSV: eps_lambda_pct");
                Assert.That(ResultIo.TryGetDouble(mapJson, kEpsL, out var elJs),  Is.True,  "JSON: eps_lambda_pct");
                Assert.That(elCsv, Is.EqualTo(elJs).Within(1e-9));

                Assert.That(ResultIo.TryGetDouble(mapCsv,  kEpsH, out var ehCsv), Is.True,  "CSV: eps_h_pct");
                Assert.That(ResultIo.TryGetDouble(mapJson, kEpsH, out var ehJs),  Is.True,  "JSON: eps_h_pct");
                Assert.That(ehCsv, Is.EqualTo(ehJs).Within(1e-9));

                // Fields 'iters' and 'converged' are NOT in the exports (verified in GeoCore.cs),
                // so we don't test them here.
            });
        }
    }
}
