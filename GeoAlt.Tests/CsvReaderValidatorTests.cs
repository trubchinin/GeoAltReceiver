using System.IO;
using NUnit.Framework;
using GeoAlt.Core;

namespace GeoAlt.Tests
{
    [TestFixture, Category("Validator")]
    public class CsvReaderValidatorTests
    {
        [Test, Category("Validator"), Category("Regression")]
        public void ValidCsv_N4_AllAccepted()
        {
            string path = SharedTestData.WriteTempCsvN4();
            try
            {
                var (valid, rejected) = CsvReaderValidator.ReadAndValidate(path, verbose:false);
                Assert.That(valid.Count, Is.EqualTo(4));
                Assert.That(rejected.Count, Is.EqualTo(0));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test, Category("Validator")]
        public void BadNumber_CommaDecimal_Rejected()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
                sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
                // значение с запятой в φi → плохой формат числа
                sw.WriteLine("100000,5,48,5300,0.001,32.0000,0.001,20200,5");
                sw.Flush();

                var (valid, rejected) = CsvReaderValidator.ReadAndValidate(tmp, verbose:false);
                Assert.That(valid.Count, Is.EqualTo(0));
                Assert.That(rejected.Count, Is.GreaterThanOrEqualTo(1));
                // Updated based on previous findings: comma causes split -> MISSING_COLUMN
                Assert.That(rejected[0].Code, Is.EqualTo(RejectionCode.MISSING_COLUMN)); 
                Assert.That(rejected[0].Field, Is.EqualTo("schema"));
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Test, Category("Validator")]
        public void MissingColumns_Rejected()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                using var sw = new StreamWriter(tmp, false, System.Text.Encoding.UTF8);
                sw.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi"); // 7 колонок
                sw.WriteLine("100000,5,48.53,0.001,32.0,0.001,20200");
                sw.Flush();

                var (valid, rejected) = CsvReaderValidator.ReadAndValidate(tmp, verbose:false);
                Assert.That(valid.Count, Is.EqualTo(0));
                Assert.That(rejected.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(rejected[0].Code, Is.EqualTo(RejectionCode.MISSING_COLUMN));
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}
