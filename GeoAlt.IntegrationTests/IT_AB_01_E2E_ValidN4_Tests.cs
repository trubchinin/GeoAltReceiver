using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("ABC"), Category("System"), Category("Acceptance"), Category("Acceptance")]
    public class IT_AB_01_E2E_ValidN4_Tests
    {
        [Test, Category("IT"), Category("AB")]
        public async Task ValidCsvN4_EndToEnd_SucceedsAndProducesExports()
        {
            // Arrange: готуємо тимчасовий CSV (N=4)
            string csv = SharedIntData.WriteTempCsvN4();
            string baseName = Path.GetFileNameWithoutExtension(csv);
            string dir = Path.GetDirectoryName(csv)!;

            // Act: запускаємо CLI як зовнішній процес
            var res = await CliProcess.RunAsync(csv, verbose:false);

            // Assert: успішний код виходу й артефакти експорту існують
            Assert.That(res.ExitCode, Is.EqualTo(0), "CLI exit code має бути 0");
            Assert.That(File.Exists(res.ResultCsv), Is.True, "Очікується *_result.csv");
            Assert.That(File.Exists(res.ResultJson), Is.True, "Очікується *_result.json");
            Assert.That(File.Exists(res.ProtocolTxt), Is.True, "Очікується *_protocol.txt");

            // Додаткова перевірка протоколу: має містити ознаку збіжності
            string protocol = await File.ReadAllTextAsync(res.ProtocolTxt);
            StringAssert.Contains("converged=true", protocol, "Протокол має містити converged=true");
        }
    }
}
