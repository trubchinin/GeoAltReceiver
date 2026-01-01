using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("AB"), Category("System"), Category("Acceptance")]
    public class IT_AB_02_ValidN7_Eps_Tests
    {
        [Test, Category("IT"), Category("AB")]
        public async Task ValidCsvN7_ProducesEpsAndProtocol()
        {
            // Arrange
            string csv = SharedIntData.WriteTempCsvN7();

            // Act (verbose=true, щоб протокол мав діагностику)
            var res = await CliProcess.RunAsync(csv, verbose:true);

            // Assert: успішний вихід і всі артефакти присутні
            Assert.That(res.ExitCode, Is.EqualTo(0), "Очікується exit-code 0");
            Assert.That(File.Exists(res.ResultCsv), Is.True, "Очікується *_result.csv");
            Assert.That(File.Exists(res.ResultJson), Is.True, "Очікується *_result.json");
            Assert.That(File.Exists(res.ProtocolTxt), Is.True, "Очікується *_protocol.txt");

            // Протокол містить метрику умовності (або подібний діагностичний ключ)
            string protocol = await File.ReadAllTextAsync(res.ProtocolTxt);
            StringAssert.Contains("JTJ_COND_HEURISTIC", protocol, "Протокол має містити JTJ_COND_HEURISTIC");

            // Результати містять відносні похибки ε…%
            string csvText = await File.ReadAllTextAsync(res.ResultCsv);
            StringAssert.Contains("eps_phi_pct", csvText, "CSV має містити eps_phi_pct");
            StringAssert.Contains("eps_lambda_pct", csvText, "CSV має містити eps_lambda_pct");
            StringAssert.Contains("eps_h_pct", csvText, "CSV має містити eps_h_pct");

            string jsonText = await File.ReadAllTextAsync(res.ResultJson);
            StringAssert.Contains("eps_phi_pct", jsonText, "JSON має містити eps_phi_pct");
            StringAssert.Contains("eps_lambda_pct", jsonText, "JSON має містити eps_lambda_pct");
            StringAssert.Contains("eps_h_pct", jsonText, "JSON має містити eps_h_pct");
        }
    }
}
