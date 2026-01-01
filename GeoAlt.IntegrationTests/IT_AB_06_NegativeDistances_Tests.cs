using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("AB"), Category("System"), Category("Acceptance")]
    public class IT_AB_06_NegativeDistances_Tests
    {
        [Test, Category("IT"), Category("AB")]
        public async Task NegativeDistance_IsDiscarded_ButSolverSucceedsIfEnoughValidRows()
        {
            // Arrange: 4 valid + 1 invalid (ri < 0)
            string csv = SharedIntData.WriteTempCsv_NegativeDistances();

            // Act
            var run = await CliProcess.RunAsync(csv, verbose:true);

            // Assert
            // 1. ExitCode = 0 (бо 4 валідних лишилося)
            Assert.That(run.ExitCode, Is.EqualTo(0), "Solver мав зійтися на 4 валідних рядках");

            // 2. Protocol contains rejection info (BAD_NUMBER_FORMAT or similar logic validation error)
            // У CsvReaderValidator перевірка "r <= 0" видає BAD_NUMBER_FORMAT для поля ri.
            Assert.That(File.Exists(run.ProtocolTxt), Is.True);
            string protocol = await File.ReadAllTextAsync(run.ProtocolTxt);
            StringAssert.Contains("NEGATIVE_DISTANCE", protocol, "Має бути зафіксовано відхилення через від'ємну відстань");

            // 3. Result file logic checks (Used=4, Discarded=1)
            // Ми можемо перевірити це через CSV або JSON результат
            if (File.Exists(run.ResultJson))
            {
                var res = ResultIo.ReadResultJson(run.ResultJson);
                // "used": 4
                if (double.TryParse(res["used"], out double used))
                    Assert.That(used, Is.EqualTo(4), "Має бути використано 4 супутники");
                
                // "discarded": 1
                if (double.TryParse(res["discarded"], out double discarded))
                    Assert.That(discarded, Is.EqualTo(1), "Має бути відхилено 1 супутник");
            }
        }
    }
}
