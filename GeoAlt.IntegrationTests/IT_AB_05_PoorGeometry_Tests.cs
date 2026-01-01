using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GeoAlt.IntegrationTests
{
    [TestFixture, Category("IT"), Category("AB"), Category("System"), Category("Acceptance")]
    public class IT_AB_05_PoorGeometry_Tests
    {
        [Test, Category("IT"), Category("AB")]
        public async Task PoorGeometry_EitherNonConvergedOrHighCondition_NotifiesInProtocol()
        {
            // Arrange
            string csv = SharedIntData.WriteTempCsv_PoorGeom4();

            // Act
            var run = await CliProcess.RunAsync(csv, verbose:true);

            // Assert: протокол існує
            Assert.That(File.Exists(run.ProtocolTxt), Is.True, "Очікується наявність *_protocol.txt");
            string protocol = await File.ReadAllTextAsync(run.ProtocolTxt);

            // 1) Якщо незбіжність — має бути converged=false
            bool mentionsNonConverged = protocol.Contains("converged=false");

            // 2) Або ж висока умовність (почитаємо JTJ_COND_HEURISTIC, якщо є)
            bool highCondition = false;
            var m = Regex.Match(protocol, @"JTJ_COND_HEURISTIC\s*=\s*([0-9.Ee\+\-]+)");
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                             System.Globalization.CultureInfo.InvariantCulture, out var cond))
            {
                highCondition = cond >= 1e5; // поріг-евристика для «поганої» геометрії
            }

            Assert.That(mentionsNonConverged || highCondition,
                "Очікується converged=false АБО велике значення JTJ_COND_HEURISTIC у протоколі");

            // Експорт може існувати навіть при незбіжності (якщо Used>=4),
            // але поле converged там не пишеться (лише координати).
            // Тому перевіряємо лише наявність файлів, якщо вони створилися.
            // (Тест не вимагає обов'язкового створення файлів при поганій геометрії, 
            //  головне — повідомлення в протоколі).
        }
    }
}
