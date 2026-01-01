using System.IO;
using NUnit.Framework;
using GeoAlt.Core;

namespace GeoAlt.Tests
{
    [TestFixture, Category("Runner"), Category("Smoke")]
    public class RunnerSmokeTests
    {
        [Test, Category("Runner"), Category("Smoke")]
        public void Runner_OnTempCsvN4_ProducesResultAndConverges()
        {
            string path = SharedTestData.WriteTempCsvN4();
            try
            {
                var res = Runner.Run(path, verbose:false);
                Assert.That(res.Used, Is.EqualTo(4));
                Assert.That(res.Discarded, Is.EqualTo(0));
                Assert.That(res.Converged, Is.True);

                string dir = Path.GetDirectoryName(path)!;
                string baseName = Path.GetFileNameWithoutExtension(path);
                Assert.That(File.Exists(Path.Combine(dir, baseName + "_result.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(dir, baseName + "_result.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(dir, baseName + "_protocol.txt")), Is.True);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
