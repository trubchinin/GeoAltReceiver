using NUnit.Framework;
using GeoAlt.Core;

namespace GeoAlt.Tests
{
    [TestFixture, Category("Solver"), Category("Regression")]
    public class SolverGNTests
    {
        [Test, Category("Solver"), Category("Regression")]
        public void Solve_KnownGeometry_ConvergesAndNearTrueGeo()
        {
            var (sats, ranges) = SharedTestData.BuildGeometry();

            var opt = new SolverGN.Options { MaxIters = 1000, Tol = 1e-6, Damp = 1e-3 };
            var (rEcef, iters, converged, JTJ, Jrows, gModel) = SolverGN.Solve(sats, ranges, opt, verbose:false);

            Assert.That(converged, Is.True);
            Assert.That(iters, Is.GreaterThan(0).And.LessThanOrEqualTo(1000));

            var geo = GeoTransform.EcefToGeo(rEcef);

            Assert.That(geo.PhiDeg,    Is.EqualTo(SharedTestData.RxTrue.PhiDeg).Within(1e-3));
            Assert.That(geo.LambdaDeg, Is.EqualTo(SharedTestData.RxTrue.LambdaDeg).Within(1e-3));
            Assert.That(geo.Hm,        Is.EqualTo(SharedTestData.RxTrue.Hm).Within(20.0));
        }
    }
}
