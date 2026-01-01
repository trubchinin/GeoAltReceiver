using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("AB"), Category("System"), Category("Acceptance")]
    public class IT_AB_04_MissingColumn_Tests
    {
        [Test, Category("IT"), Category("AB")]
        public async Task MissingColumn_FailsWithNonZeroExit_AndNoExports()
        {
            // Arrange
            string csv = SharedIntData.WriteTempCsv_MissingColumn();

            // Act
            var res = await CliProcess.RunAsync(csv, verbose:true);

            // Assert: код виходу НЕ 0, експорт даних (CSV/JSON) НЕ створений, але Протокол СТВОРЕНИЙ (з помилками)
            Assert.That(res.ExitCode, Is.Not.EqualTo(0), "Очікується ненульовий exit-code для фатальної помилки схеми");
            Assert.That(File.Exists(res.ResultCsv), Is.False, "Не має бути *_result.csv");
            Assert.That(File.Exists(res.ResultJson), Is.False, "Не має бути *_result.json");
            
            // Протокол має бути, і в ньому має бути запис про помилку
            Assert.That(File.Exists(res.ProtocolTxt), Is.True, "Протокол МАЄ бути створений навіть при помидах (для звіту)");
            string proto = await File.ReadAllTextAsync(res.ProtocolTxt);
            StringAssert.Contains("MISSING_COLUMN", proto);
            Assert.That(
                res.StdOut.Contains("MISSING_COLUMN") || res.StdErr.Contains("MISSING_COLUMN"),
                Is.True, "Очікується згадка MISSING_COLUMN у stdout/err");
        }
    }
}
