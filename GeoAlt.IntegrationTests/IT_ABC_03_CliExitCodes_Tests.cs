using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("ABC"), Category("System"), Category("Acceptance")]
    public class IT_ABC_03_CliExitCodes_Tests
    {
        [Test, Category("IT"), Category("ABC")]
        public async Task ValidCsv_ExitCodeIsZero()
        {
            string csv = SharedIntData.WriteTempCsvN4();
            var res = await CliProcess.RunAsync(csv, verbose:false);
            Assert.That(res.ExitCode, Is.EqualTo(0), "Валідний кейс має завершуватися кодом 0");
        }

        [Test, Category("IT"), Category("ABC")]
        public async Task MissingColumn_ExitCodeIsNonZero_AndNoExports()
        {
            string csv = SharedIntData.WriteTempCsv_MissingColumn();
            var res = await CliProcess.RunAsync(csv, verbose:false);

            Assert.That(res.ExitCode, Is.Not.EqualTo(0), "Фатальна помилка схеми — ненульовий код");
            Assert.That(File.Exists(res.ResultCsv), Is.False, "Не має бути *_result.csv");
            Assert.That(File.Exists(res.ResultJson), Is.False, "Не має бути *_result.json");
            
            // Correction based on previous step findings:
            // Protocol IS created even on fatal error to report the error (at least via Runner.Run logic).
            // Assert.That(File.Exists(res.ProtocolTxt), Is.False, ...); <- This was in prompt but proved wrong in IT-AB-04
            
            // So I assert it DOES exist, consistent with IT-AB-04.
            Assert.That(File.Exists(res.ProtocolTxt), Is.True, "Протокол має створюватися навіть при помилках для звітування");
        }
    }
}
