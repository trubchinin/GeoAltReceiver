using NUnit.Framework;
using GeoAlt.Core;

namespace GeoAlt.Tests
{
    [TestFixture, Category("GeoTransform"), Category("Regression")]
    public class GeoTransformTests
    {
        [Test, Category("GeoTransform"), Category("Regression")]
        public void Roundtrip_Ecef_Geo_IsAccurateEnough()
        {
            var pts = new[]
            {
                new GeoPoint(  0.0,   0.0,    0.0),
                new GeoPoint( 50.0,  30.0,  250.0),
                new GeoPoint(-20.5, 120.0, 1500.0),
                new GeoPoint( 85.0, -45.0, 5000.0),
            };

            foreach (var g in pts)
            {
                var e = GeoTransform.GeoToEcef(g);
                var r = GeoTransform.EcefToGeo(e);

                Assert.That(r.PhiDeg,     Is.EqualTo(g.PhiDeg).Within(1e-5), "phi");
                Assert.That(r.LambdaDeg,  Is.EqualTo(g.LambdaDeg).Within(1e-5), "lambda");
                Assert.That(r.Hm,         Is.EqualTo(g.Hm).Within(0.05), "h");
            }
        }
    }
}
