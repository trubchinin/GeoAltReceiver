using NUnit.Framework;
using GeoAlt.Core;

namespace GeoAlt.Tests
{
    [TestFixture, Category("Uncertainty")]
    public class UncertaintyTests
    {
        [Test, Category("Uncertainty")]
        public void EffectiveSigma_PositiveAndFinite()
        {
            var (sats, ranges) = SharedTestData.BuildGeometry();
            var rxEcef = GeoTransform.GeoToEcef(SharedTestData.RxTrue);

            var g = SharedTestData.BuildSatellites()[0];
            var obs = new SatelliteObs(
                r: ranges[0], dr: 5.0,
                phiDeg: g.PhiDeg, dphiDeg: 0.001,
                lambdaDeg: g.LambdaDeg, dlambdaDeg: 0.001,
                h: g.Hm, dh: 5.0);

            double sigma = Uncertainty.EffectiveSigmaMeters(obs, rxEcef, verbose:false);
            Assert.That(sigma, Is.GreaterThan(0.0));
            Assert.That(double.IsFinite(sigma), Is.True);
        }

        [Test, Category("Uncertainty"), Category("Regression")]
        public void CovarianceAndRelativeErrors_FiniteNonNegative()
        {
            var (sats, ranges) = SharedTestData.BuildGeometry();
            var opt = new SolverGN.Options { MaxIters = 1000, Tol = 1e-6, Damp = 1e-3 };
            var (rEcef, iters, converged, JTJ, Jrows, gModel) = SolverGN.Solve(sats, ranges, opt, verbose:false);
            Assert.That(converged, Is.True);

            var rxGeo = GeoTransform.EcefToGeo(rEcef);

            var sigmaEff = new System.Collections.Generic.List<double>(sats.Count);
            for (int i = 0; i < sats.Count; i++)
            {
                var g = SharedTestData.BuildSatellites()[i];
                var obs = new SatelliteObs(ranges[i], 5.0, g.PhiDeg, 0.001, g.LambdaDeg, 0.001, g.Hm, 5.0);
                sigmaEff.Add(Uncertainty.EffectiveSigmaMeters(obs, rEcef, verbose:false));
            }

            Assert.That(Uncertainty.CovarianceReceiverECEF(Jrows, sigmaEff, out var Cov_r, out var cond), Is.True);
            Assert.That(cond, Is.GreaterThan(0.0));
            var T = Uncertainty.JacobianGeoWrtECEF(rEcef);
            var Cov_out = Uncertainty.CovarianceOut(Cov_r, T);
            var (ePhi, eLam, eH) = Uncertainty.RelativeErrorsPct(rxGeo, Cov_out);

            Assert.That(ePhi, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(eLam, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(eH,   Is.GreaterThanOrEqualTo(0.0));
            Assert.That(double.IsFinite(ePhi) && double.IsFinite(eLam) && double.IsFinite(eH), Is.True);
        }
    }
}
