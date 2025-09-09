using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GeoAltReceiver
{
	// === Доманні моделі ===
	public record SatelliteObs(
		double r, double dr,
		double phiDeg, double dphiDeg,
		double lambdaDeg, double dlambdaDeg,
		double h, double dh
	);

	public enum RejectionCode
	{
		OUT_OF_RANGE_LAT,
		OUT_OF_RANGE_LON,
		NEGATIVE_DISTANCE,
		NEGATIVE_SIGMA,
		BAD_NUMBER_FORMAT,
		MISSING_COLUMN
	}

	public sealed class Rejection
	{
		public int LineNo { get; init; }
		public RejectionCode Code { get; init; }
		public string Field { get; init; } = "";
		public string Message { get; init; } = "";
		public override string ToString() => $"line={LineNo}; code={Code}; field={Field}; msg={Message}";
	}

	public record EcefPoint(double X, double Y, double Z);
	public record GeoPoint(double PhiDeg, double LambdaDeg, double Hm);

	public sealed class SolveResult
	{
		public GeoPoint OutGeo { get; init; } = new GeoPoint(0,0,0);
		public int Used { get; init; }
		public int Discarded { get; init; }
		public int Iters { get; init; }
		public bool Converged { get; init; }
		public int CalcTimeMs { get; init; }
		public string Note { get; init; } = "";
		public double EpsPhiPct { get; init; }
		public double EpsLambdaPct { get; init; }
		public double EpsHPct { get; init; }
	}

	// === Константні тексти помилок SR-F14 ===
	public static class Errors
	{
		public const string ERR_BAD_CSV_SCHEMA =
			"Очікується 8 колонок: ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi.";
		public const string ERR_NOT_ENOUGH_DATA =
			"Недостатньо даних для визначення φ, λ, h (N<4).";
		public const string ERR_VALUE_OUT_OF_RANGE =
			"Значення поза допустимим діапазоном: <поле>.";
		public const string ERR_NEGATIVE_OR_ZERO =
			"Невід’ємні значення очікуються для <поле>.";
		public const string ERR_NUMBER_FORMAT =
			"Невірний числовий формат у полі <поле>.";
		public const string ERR_NO_CONVERGENCE =
			"Досягнуто ліміт ітерацій без збіжності (результат попередній).";
	}

	// === Простий логер із ротацією до 1 МБ (останні 3 файли) ===
	public static class Logger
	{
		private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "geoalt.log");
		private const long MaxBytes = 1_000_000;
		private const int Keep = 3;

		private static void RotateIfNeeded()
		{
			try
			{
				var fi = new FileInfo(LogPath);
				if (fi.Exists && fi.Length > MaxBytes)
				{
					for (int i = Keep - 1; i >= 1; i--)
					{
						var src = LogPath + "." + i;
						var dst = LogPath + "." + (i + 1);
						if (File.Exists(dst)) File.Delete(dst);
						if (File.Exists(src)) File.Move(src, dst);
					}
					var first = LogPath + ".1";
					if (File.Exists(first)) File.Delete(first);
					File.Move(LogPath, first);
				}
			}
			catch { /* не блокуємо виконання */ }
		}

		private static void Write(string level, string msg)
		{
			RotateIfNeeded();
			var line = $"{DateTime.UtcNow:O} [{level}] {msg}";
			try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); }
			catch { /* ignore */ }
		}

		public static void Info(string msg) => Write("INFO", msg);
		public static void Warn(string msg) => Write("WARN", msg);
		public static void Error(string msg) => Write("ERROR", msg);
	}

	// === WGS-84 та перетворення геодезичні ↔ ECEF (SR-F04/05/09) ===
	public static class GeoTransform
	{
		// Параметри WGS-84
		public const double A = 6378137.0;                 // велика піввісь (м)
		public const double F = 1.0 / 298.257223563;       // сплюснутість
		public static readonly double E2 = 2 * F - F * F;  // ексцентриситет^2

		public static double Deg2Rad(double d) => d * Math.PI / 180.0;
		public static double Rad2Deg(double r) => r * 180.0 / Math.PI;

		// N(φ) = a / sqrt(1 - e^2 sin^2 φ)
		public static double N(double phiRad) => A / Math.Sqrt(1.0 - E2 * Math.Sin(phiRad) * Math.Sin(phiRad));

		// (φ,λ,h) [град,град,м] → ECEF (X,Y,Z) [м]
		public static EcefPoint GeoToEcef(GeoPoint g)
		{
			double phi = Deg2Rad(g.PhiDeg);
			double lam = Deg2Rad(g.LambdaDeg);
			double h = g.Hm;

			double Nphi = N(phi);
			double cosPhi = Math.Cos(phi);
			double sinPhi = Math.Sin(phi);
			double cosLam = Math.Cos(lam);
			double sinLam = Math.Sin(lam);

			double X = (Nphi + h) * cosPhi * cosLam;
			double Y = (Nphi + h) * cosPhi * sinLam;
			double Z = (Nphi * (1.0 - E2) + h) * sinPhi;

			return new EcefPoint(X, Y, Z);
		}

		// ECEF (X,Y,Z) → (φ,λ,h) [град,град,м]; ітеративний
		public static GeoPoint EcefToGeo(EcefPoint p)
		{
			double X = p.X, Y = p.Y, Z = p.Z;

			double lambda = Math.Atan2(Y, X);
			double pxy = Math.Sqrt(X * X + Y * Y);

			double phi = Math.Atan2(Z, pxy * (1.0 - E2));
			for (int i = 0; i < 20; i++)
			{
				double Nphi = N(phi);
				double h = pxy / Math.Cos(phi) - Nphi;
				double phiNew = Math.Atan2(Z, pxy * (1.0 - E2 * Nphi / (Nphi + h)));
				if (Math.Abs(phiNew - phi) < 1e-12) { phi = phiNew; break; }
				phi = phiNew;
			}
			double Nf = N(phi);
			double hFinal = pxy / Math.Cos(phi) - Nf;

			return new GeoPoint(Rad2Deg(phi), Rad2Deg(lambda), hFinal);
		}
	}

	// === Допоміжна математика 3x3 ===
	public static class LinAlg3
	{
		public static bool Solve3x3(double[,] A, double[] b, out double[] x)
		{
			x = new double[3];
			int n = 3;
			double[,] M = new double[n, n + 1];
			for (int i = 0; i < n; i++)
			{
				for (int j = 0; j < n; j++) M[i, j] = A[i, j];
				M[i, n] = b[i];
			}
			for (int k = 0; k < n; k++)
			{
				int piv = k;
				double max = Math.Abs(M[k, k]);
				for (int i = k + 1; i < n; i++)
				{
					double val = Math.Abs(M[i, k]);
					if (val > max) { max = val; piv = i; }
				}
				if (max < 1e-15) return false;
				if (piv != k)
				{
					for (int j = k; j <= n; j++)
					{
						(M[k, j], M[piv, j]) = (M[piv, j], M[k, j]);
					}
				}
				double diag = M[k, k];
				for (int j = k; j <= n; j++) M[k, j] /= diag;
				for (int i = k + 1; i < n; i++)
				{
					double f = M[i, k];
					for (int j = k; j <= n; j++) M[i, j] -= f * M[k, j];
				}
			}
			for (int i = n - 1; i >= 0; i--)
			{
				double sum = M[i, n];
				for (int j = i + 1; j < n; j++) sum -= M[i, j] * x[j];
				x[i] = sum / M[i, i];
			}
			return true;
		}

		public static bool Invert3x3(double[,] A, out double[,] inv)
		{
			inv = new double[3, 3];
			for (int i = 0; i < 3; i++)
			{
				double[] b = new double[3];
				b[i] = 1.0;
				if (!Solve3x3(A, b, out var x)) return false;
				for (int r = 0; r < 3; r++) inv[r, i] = x[r];
			}
			return true;
		}

		public static double[,] Mul(double[,] M, double[,] N)
		{
			var R = new double[3,3];
			for (int i=0;i<3;i++)
				for (int j=0;j<3;j++)
					for (int k=0;k<3;k++)
						R[i,j]+=M[i,k]*N[k,j];
			return R;
		}

		public static double[,] Transpose(double[,] M)
		{
			var R = new double[3,3];
			for (int i=0;i<3;i++)
				for (int j=0;j<3;j++) R[i,j]=M[j,i];
			return R;
		}
	}

	// === Імпорт CSV + валідація (SR-F01..F03/F14) ===
	public static class CsvReaderValidator
	{
		private static readonly string[] ExpectedHeader = new[]
		{
			"ri","Δri","φi","Δφi","λi","Δλi","hi","Δhi"
		};

		public static (List<SatelliteObs> valid, List<Rejection> rejected) ReadAndValidate(string path, bool verbose)
		{
			var rejected = new List<Rejection>();
			var valid = new List<SatelliteObs>();

			if (!File.Exists(path))
			{
				var msg = $"ERR_BAD_CSV_SCHEMA: \"{Errors.ERR_BAD_CSV_SCHEMA}\" Файл не знайдено: {path}";
				Logger.Error(msg);
				throw new ApplicationException(msg);
			}

			using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var sr = new StreamReader(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

			string? line;
			int lineNo = 0;
			bool headerChecked = false;

			var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();

			while ((line = sr.ReadLine()) != null)
			{
				lineNo++;
				if (string.IsNullOrWhiteSpace(line)) continue;

				var cells = line.Split(',', StringSplitOptions.None);

				if (!headerChecked)
				{
					headerChecked = true;
					if (IsHeader(cells))
					{
						if (verbose) Console.WriteLine("[i] Header detected.");
						continue; // пропускаємо заголовок
					}
				}

				if (cells.Length != 8)
				{
					rejected.Add(new Rejection
					{
						LineNo = lineNo,
						Code = RejectionCode.MISSING_COLUMN,
						Field = "schema",
						Message = "ERR_BAD_CSV_SCHEMA: " + Errors.ERR_BAD_CSV_SCHEMA
					});
					Logger.Error($"Bad schema at line {lineNo}");
					continue;
				}

				if (!TryParseDouble(cells[0], ci, out double ri))
				{ RejectNumber(lineNo, "ri", rejected); continue; }
				if (!TryParseDouble(cells[1], ci, out double dri))
				{ RejectNumber(lineNo, "Δri", rejected); continue; }
				if (!TryParseDouble(cells[2], ci, out double phi))
				{ RejectNumber(lineNo, "φi", rejected); continue; }
				if (!TryParseDouble(cells[3], ci, out double dphi))
				{ RejectNumber(lineNo, "Δφi", rejected); continue; }
				if (!TryParseDouble(cells[4], ci, out double lambda))
				{ RejectNumber(lineNo, "λi", rejected); continue; }
				if (!TryParseDouble(cells[5], ci, out double dlambda))
				{ RejectNumber(lineNo, "Δλi", rejected); continue; }
				if (!TryParseDouble(cells[6], ci, out double h))
				{ RejectNumber(lineNo, "hi", rejected); continue; }
				if (!TryParseDouble(cells[7], ci, out double dh))
				{ RejectNumber(lineNo, "Δhi", rejected); continue; }

				bool bad = false;
				if (ri <= 0) { Reject(lineNo, RejectionCode.NEGATIVE_DISTANCE, "ri", Errors.ERR_NEGATIVE_OR_ZERO.Replace("<поле>", "ri (>0)"), rejected); bad = true; }
				if (dri < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δri", Errors.ERR_NEGATIVE_OR_ZERO.Replace("<поле>", "Δri (≥0)"), rejected); bad = true; }

				if (phi < -90 || phi > 90) { Reject(lineNo, RejectionCode.OUT_OF_RANGE_LAT, "φi", Errors.ERR_VALUE_OUT_OF_RANGE.Replace("<поле>", "φi [-90..90]"), rejected); bad = true; }
				if (lambda < -180 || lambda > 180) { Reject(lineNo, RejectionCode.OUT_OF_RANGE_LON, "λi", Errors.ERR_VALUE_OUT_OF_RANGE.Replace("<поле>", "λi [-180..180]"), rejected); bad = true; }

				if (dphi < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δφi", Errors.ERR_NEGATIVE_OR_ZERO.Replace("<поле>", "Δφi (≥0)"), rejected); bad = true; }
				if (dlambda < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δλi", Errors.ERR_NEGATIVE_OR_ZERO.Replace("<поле>", "Δλi (≥0)"), rejected); bad = true; }
				if (dh < 0) { Reject(lineNo, RejectionCode.NEGATIVE_SIGMA, "Δhi", Errors.ERR_NEGATIVE_OR_ZERO.Replace("<поле>", "Δhi (≥0)"), rejected); bad = true; }

				if (h < -500 || h > 50000)
				{
					Reject(lineNo, RejectionCode.BAD_NUMBER_FORMAT, "hi", "Значення hi поза припустимим діапазоном [-500..50000] м.", rejected);
					bad = true;
				}

				if (!bad)
				{
					valid.Add(new SatelliteObs(ri, dri, phi, dphi, lambda, dlambda, h, dh));
				}
			}

			return (valid, rejected);
		}

		private static bool IsHeader(string[] cells)
		{
			if (cells.Length != 8) return false;
			for (int i = 0; i < 8; i++)
			{
				var a = cells[i].Trim();
				var b = ExpectedHeader[i];
				if (!string.Equals(a, b, StringComparison.Ordinal))
					return false;
			}
			return true;
		}

		private static bool TryParseDouble(string s, CultureInfo ci, out double value)
		{
			if (s.Contains(',')) { value = double.NaN; return false; } // тільки крапка
			return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowLeadingSign, ci, out value)
				   && !double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static void RejectNumber(int lineNo, string field, List<Rejection> rej)
		{
			rej.Add(new Rejection
			{
				LineNo = lineNo,
				Code = RejectionCode.BAD_NUMBER_FORMAT,
				Field = field,
				Message = $"ERR_NUMBER_FORMAT: {Errors.ERR_NUMBER_FORMAT.Replace("<поле>", field)}"
			});
			Logger.Error($"Number format at line {lineNo}, field {field}");
		}

		private static void Reject(int lineNo, RejectionCode code, string field, string message, List<Rejection> rej)
		{
			rej.Add(new Rejection { LineNo = lineNo, Code = code, Field = field, Message = message });
			var lvl = code == RejectionCode.BAD_NUMBER_FORMAT ? "ERROR" : "WARN";
			if (lvl == "ERROR") Logger.Error($"{code} at line {lineNo}, field {field}");
			else Logger.Warn($"{code} at line {lineNo}, field {field}");
		}
	}

	// === Розв’язувач (МНК, Гаусса–Ньютона з демпфуванням) — SR-F06..F08 ===
	public static class SolverGN
	{
		public sealed class Options
		{
			public int MaxIters { get; init; } = 1000;
			public double Tol { get; init; } = 1e-6;      // за нормою приросту (метри)
			public double Damp { get; init; } = 1e-3;     // мале демпфування в нормальних рівняннях
		}

		private static EcefPoint InitialGuess(List<EcefPoint> sats)
		{
			double cx = 0, cy = 0, cz = 0;
			foreach (var s in sats) { cx += s.X; cy += s.Y; cz += s.Z; }
			cx /= sats.Count; cy /= sats.Count; cz /= sats.Count;

			double r = Math.Sqrt(cx * cx + cy * cy + cz * cz);
			if (r < 1) r = GeoTransform.A;
			double scale = (GeoTransform.A - 100.0) / r; // трохи «до Землі»
			return new EcefPoint(cx * scale, cy * scale, cz * scale);
		}

		public static (EcefPoint rEcef, int iters, bool converged, double[,] JTJ, List<double[]> Jrows, List<double> gModel)
			Solve(List<EcefPoint> sats, List<double> ranges, Options opt, bool verbose)
		{
			var r = InitialGuess(sats);
			int n = sats.Count;
			var Jrows = new List<double[]>(n);
			var gModel = new List<double>(n);

			double[,] JTJ = new double[3, 3];

			for (int k = 0; k < opt.MaxIters; k++)
			{
				Array.Clear(JTJ, 0, JTJ.Length);
				Jrows.Clear();
				gModel.Clear();

				double[] JTv = new double[3];

				for (int i = 0; i < n; i++)
				{
					var s = sats[i];
					double dx = r.X - s.X;
					double dy = r.Y - s.Y;
					double dz = r.Z - s.Z;
					double gi = Math.Sqrt(dx * dx + dy * dy + dz * dz); // модельна відстань
					if (gi < 1e-12) gi = 1e-12;

					double vi = ranges[i] - gi;

					double jx = dx / gi;
					double jy = dy / gi;
					double jz = dz / gi;

					JTJ[0,0] += jx * jx; JTJ[0,1] += jx * jy; JTJ[0,2] += jx * jz;
					JTJ[1,0] += jy * jx; JTJ[1,1] += jy * jy; JTJ[1,2] += jy * jz;
					JTJ[2,0] += jz * jx; JTJ[2,1] += jz * jy; JTJ[2,2] += jz * jz;

					JTv[0] += jx * vi;
					JTv[1] += jy * vi;
					JTv[2] += jz * vi;

					Jrows.Add(new double[] { jx, jy, jz });
					gModel.Add(gi);
				}

				JTJ[0,0] += opt.Damp; JTJ[1,1] += opt.Damp; JTJ[2,2] += opt.Damp;

				if (!LinAlg3.Solve3x3(JTJ, JTv, out var dr))
				{
					if (verbose) Console.WriteLine("[!] Ill-conditioned normal matrix; stopping.");
					return (r, k, false, JTJ, Jrows, gModel);
				}

				r = new EcefPoint(r.X + dr[0], r.Y + dr[1], r.Z + dr[2]);

				double normDx = Math.Sqrt(dr[0]*dr[0] + dr[1]*dr[1] + dr[2]*dr[2]);
				if (verbose) Console.WriteLine($"[i] iter={k+1}; |Δr|={normDx:F6} m");

				if (normDx <= opt.Tol)
				{
					return (r, k + 1, true, JTJ, Jrows, gModel);
				}
			}
			// кінець по ліміту ітерацій
			return (r, opt.MaxIters, false, JTJ, Jrows, gModel);
		}
	}

	// === Обчислення ефективних σ вимірювань, коваріації та ε… (%) ===
	public static class Uncertainty
	{
		public static double EffectiveSigmaMeters(
			SatelliteObs obs,
			EcefPoint receiverEcef,
			bool verbose)
		{
			var satBase = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg, obs.lambdaDeg, obs.h));
			double g0 = Dist(receiverEcef, satBase);

			double sigma2 = obs.dr * obs.dr; // внесок Δri

			double dPhiStepRad = 1e-6;
			double dLamStepRad = 1e-6;
			double dHStep = 1e-3;

			{
				var satMinus = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg - GeoTransform.Rad2Deg(dPhiStepRad), obs.lambdaDeg, obs.h));
				var satPlus  = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg + GeoTransform.Rad2Deg(dPhiStepRad), obs.lambdaDeg, obs.h));
				double dg_dphi = (Dist(receiverEcef, satPlus) - Dist(receiverEcef, satMinus)) / (2.0 * dPhiStepRad); // м/рад
				double dphiRad = Math.Abs(obs.dphiDeg) * Math.PI / 180.0;
				sigma2 += (dg_dphi * dphiRad) * (dg_dphi * dphiRad);
			}
			{
				var satMinus = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg, obs.lambdaDeg - GeoTransform.Rad2Deg(dLamStepRad), obs.h));
				var satPlus  = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg, obs.lambdaDeg + GeoTransform.Rad2Deg(dLamStepRad), obs.h));
				double dg_dlam = (Dist(receiverEcef, satPlus) - Dist(receiverEcef, satMinus)) / (2.0 * dLamStepRad); // м/рад
				double dlamRad = Math.Abs(obs.dlambdaDeg) * Math.PI / 180.0;
				sigma2 += (dg_dlam * dlamRad) * (dg_dlam * dlamRad);
			}
			{
				var satMinus = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg, obs.lambdaDeg, obs.h - dHStep));
				var satPlus  = GeoTransform.GeoToEcef(new GeoPoint(obs.phiDeg, obs.lambdaDeg, obs.h + dHStep));
				double dg_dh = (Dist(receiverEcef, satPlus) - Dist(receiverEcef, satMinus)) / (2.0 * dHStep); // м/м
				double dh = Math.Abs(obs.dh);
				sigma2 += (dg_dh * dh) * (dg_dh * dh);
			}

			double sigma = Math.Sqrt(Math.Max(sigma2, 1e-18));
			return sigma;
		}

		private static double Dist(EcefPoint a, EcefPoint b)
		{
			double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
			return Math.Sqrt(dx*dx + dy*dy + dz*dz);
		}

		public static bool CovarianceReceiverECEF(
			List<double[]> Jrows,
			List<double> sigmaEff,
			out double[,] Cov_r,
			out double condHeuristic)
		{
			Cov_r = new double[3,3];

			double[,] JT_W_J = new double[3,3];
			for (int i = 0; i < Jrows.Count; i++)
			{
				var j = Jrows[i]; // [jx,jy,jz]
				double wi = 1.0 / (sigmaEff[i] * sigmaEff[i] + 1e-18);
				for (int r = 0; r < 3; r++)
					for (int c = 0; c < 3; c++)
						JT_W_J[r,c] += j[r] * wi * j[c];
			}

			// Грубий евристичний кондішн: trace^3 / det (для SPD 3x3)
			double tr = JT_W_J[0,0] + JT_W_J[1,1] + JT_W_J[2,2];
			double det =
				JT_W_J[0,0]*(JT_W_J[1,1]*JT_W_J[2,2]-JT_W_J[1,2]*JT_W_J[2,1]) -
				JT_W_J[0,1]*(JT_W_J[1,0]*JT_W_J[2,2]-JT_W_J[1,2]*JT_W_J[2,0]) +
				JT_W_J[0,2]*(JT_W_J[1,0]*JT_W_J[2,1]-JT_W_J[1,1]*JT_W_J[2,0]);

			if (det <= 1e-24) det = 1e-24; // захист від нуля
			condHeuristic = (tr*tr*tr) / det; // чим більше — тим гірша обумовленість

			// Невеличке регуляризаційне демпфування
			for (int d = 0; d < 3; d++) JT_W_J[d,d] += 1e-9;

			if (!LinAlg3.Invert3x3(JT_W_J, out Cov_r))
				return false;
			return true;
		}

		public static double[,] JacobianGeoWrtECEF(EcefPoint r)
		{
			double d = 1e-3; // 1 мм
			var g0 = GeoTransform.EcefToGeo(r);

			var gX = GeoTransform.EcefToGeo(new EcefPoint(r.X + d, r.Y, r.Z));
			var gY = GeoTransform.EcefToGeo(new EcefPoint(r.X, r.Y + d, r.Z));
			var gZ = GeoTransform.EcefToGeo(new EcefPoint(r.X, r.Y, r.Z + d));

			double dphi_dX = (gX.PhiDeg - g0.PhiDeg) / d;
			double dphi_dY = (gY.PhiDeg - g0.PhiDeg) / d;
			double dphi_dZ = (gZ.PhiDeg - g0.PhiDeg) / d;

			double dlam_dX = (gX.LambdaDeg - g0.LambdaDeg) / d;
			double dlam_dY = (gY.LambdaDeg - g0.LambdaDeg) / d;
			double dlam_dZ = (gZ.LambdaDeg - g0.LambdaDeg) / d;

			double dh_dX = (gX.Hm - g0.Hm) / d;
			double dh_dY = (gY.Hm - g0.Hm) / d;
			double dh_dZ = (gZ.Hm - g0.Hm) / d;

			var T = new double[3,3];
			T[0,0]=dphi_dX; T[0,1]=dphi_dY; T[0,2]=dphi_dZ;
			T[1,0]=dlam_dX; T[1,1]=dlam_dY; T[1,2]=dlam_dZ;
			T[2,0]=dh_dX;   T[2,1]=dh_dY;   T[2,2]=dh_dZ;
			return T;
		}

		public static double[,] CovarianceOut(double[,] Cov_r, double[,] T)
		{
			var TCov = LinAlg3.Mul(T, Cov_r);
			var TT = LinAlg3.Transpose(T);
			return LinAlg3.Mul(TCov, TT);
		}

		public static (double epsPhiPct, double epsLamPct, double epsHPct) RelativeErrorsPct(
			GeoPoint outGeo, double[,] Cov_out)
		{
			double sigmaPhiDeg = Math.Sqrt(Math.Max(Cov_out[0,0], 0.0)); // градуси
			double sigmaLamDeg = Math.Sqrt(Math.Max(Cov_out[1,1], 0.0)); // градуси
			double sigmaHM     = Math.Sqrt(Math.Max(Cov_out[2,2], 0.0)); // метри

			double denomPhi = Math.Max(Math.Abs(outGeo.PhiDeg), 1e-9);
			double denomLam = Math.Max(Math.Abs(outGeo.LambdaDeg), 1e-9);
			double denomH   = Math.Max(Math.Abs(outGeo.Hm), 1e-9);

			double epsPhi = Math.Abs(sigmaPhiDeg) / denomPhi * 100.0;
			double epsLam = Math.Abs(sigmaLamDeg) / denomLam * 100.0;
			double epsH   = Math.Abs(sigmaHM)    / denomH    * 100.0;

			epsPhi = Math.Round(epsPhi, 2, MidpointRounding.AwayFromZero);
			epsLam = Math.Round(epsLam, 2, MidpointRounding.AwayFromZero);
			epsH   = Math.Round(epsH,   2, MidpointRounding.AwayFromZero);
			return (epsPhi, epsLam, epsH);
		}
	}

	// === Побудова протоколу SR-F13 ===
	public static class ProtocolBuilder
	{
		public static string Build(
			int used, int discarded, List<Rejection> rejected,
			int iters, bool converged, string note,
			double condHeuristic,
			bool warnPoorGeometry)
		{
			var sb = new StringBuilder();
			sb.AppendLine("MODEL=WGS84; A=6378137; F=1/298.257223563;");
			sb.AppendLine("CONV_CRITERION=1e-6; MAX_ITERS=1000;");
			sb.AppendLine($"USED={used}; DISCARDED={discarded};");

			if (rejected.Count > 0)
			{
				sb.Append("REASONS=[");
				sb.Append(string.Join(";", rejected.Select(r => r.Code.ToString())));
				sb.AppendLine("];");
			}
			else
			{
				sb.AppendLine("REASONS=[];");
			}

			sb.AppendLine($"SOLVER: iters={iters}; converged={(converged ? "true" : "false")};");

			if (!string.IsNullOrWhiteSpace(note))
				sb.AppendLine($"note={note};");

			sb.AppendLine($"JTJ_COND_HEURISTIC={condHeuristic:E3};");
			if (warnPoorGeometry)
				sb.AppendLine("WARNING=POOR_GEOMETRY;");

			return sb.ToString();
		}
	}

	public static class Program
	{
		static int Main(string[] args)
		{
			Console.OutputEncoding = Encoding.UTF8;

			bool verbose = args.Contains("--verbose");
			var pathArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

			if (pathArg is null)
			{
				Console.WriteLine("GeoAlt-Receiver (ЛР-1) — імпорт/валідація CSV + обчислення φ,λ,h + ε… (%) + експорт + протокол");
				Console.WriteLine("Використання: GeoAltReceiver <path-to-csv> [--verbose]");
				Console.WriteLine("Приклад CSV (8 колонок):");
				Console.WriteLine("ri,Δri,φi,Δφi,λi,Δλi,hi,Δhi");
				Console.WriteLine("100000,5,48.5300,0.001,32.0000,0.001,20200,5");
				return 1;
			}

			try
			{
				var t0 = DateTime.UtcNow;
				Logger.Info($"Start run: {pathArg}");

				var (valid, rejected) = CsvReaderValidator.ReadAndValidate(pathArg, verbose);
				int used = valid.Count;
				int discarded = rejected.Count;

				if (used < 4)
				{
					Console.WriteLine("ERR_NOT_ENOUGH_DATA: " + Errors.ERR_NOT_ENOUGH_DATA);
					Logger.Error("Not enough valid observations (N<4).");
					// Протокол із порожніми розрахунками, але з REASONS
					ExportProtocolOnly(pathArg, used, discarded, rejected, 0, false, "NOT_ENOUGH_DATA", 0.0, false);
					return 2;
				}

				// Перетворення супутників у ECEF та вектори відстаней
				var satsEcef = new List<EcefPoint>(used);
				var ranges = new List<double>(used);
				foreach (var o in valid)
				{
					var geoSat = new GeoPoint(o.phiDeg, o.lambdaDeg, o.h);
					var ecefSat = GeoTransform.GeoToEcef(geoSat);
					satsEcef.Add(ecefSat);
					ranges.Add(o.r);
				}

				// Розв’язання
				var opt = new SolverGN.Options { MaxIters = 1000, Tol = 1e-6, Damp = 1e-3 };
				var (rEcef, iters, converged, JTJ_last, Jrows, gModel) = SolverGN.Solve(satsEcef, ranges, opt, verbose);

				var outGeo = GeoTransform.EcefToGeo(rEcef);

				// Ефективні σ_i
				var sigmaEff = new List<double>(used);
				for (int i = 0; i < used; i++)
					sigmaEff.Add(Uncertainty.EffectiveSigmaMeters(valid[i], rEcef, verbose));

				// Коваріація приймача та «геометрія»
				double[,] Cov_r;
				double condHeuristic;
				bool okCov = Uncertainty.CovarianceReceiverECEF(Jrows, sigmaEff, out Cov_r, out condHeuristic);

				// Евристика POOR_GEOMETRY: велике condHeuristic
				bool warnPoorGeometry = condHeuristic > 1e12;

				var T = Uncertainty.JacobianGeoWrtECEF(rEcef);
				var Cov_out = Uncertainty.CovarianceOut(Cov_r, T);
				var (epsPhiPct, epsLamPct, epsHPct) = Uncertainty.RelativeErrorsPct(outGeo, Cov_out);

				var dt = (int)(DateTime.UtcNow - t0).TotalMilliseconds;

				// Вивід результатів
				Console.WriteLine("=== Результат обчислення ===");
				Console.WriteLine($"phi_deg={outGeo.PhiDeg:F8}");
				Console.WriteLine($"lambda_deg={outGeo.LambdaDeg:F8}");
				Console.WriteLine($"h_m={outGeo.Hm:F3}");
				Console.WriteLine($"eps_phi_pct={epsPhiPct:F2}");
				Console.WriteLine($"eps_lambda_pct={epsLamPct:F2}");
				Console.WriteLine($"eps_h_pct={epsHPct:F2}");
				Console.WriteLine($"used={used}; discarded={discarded}; calc_time_ms={dt}");
				Console.WriteLine($"iters={iters}; converged={(converged ? "true" : "false")}");

				string note = "";
				if (!converged)
				{
					Console.WriteLine("WARNING: " + Errors.ERR_NO_CONVERGENCE);
					note = "ITERATION_LIMIT";
				}
				if (warnPoorGeometry)
				{
					Console.WriteLine("WARNING: POOR_GEOMETRY (можлива велика невизначеність).");
					if (string.IsNullOrEmpty(note)) note = "POOR_GEOMETRY";
					else note += "|POOR_GEOMETRY";
				}

				// Експорт результатів і протоколу
				ExportResults(pathArg, outGeo, epsPhiPct, epsLamPct, epsHPct, used, discarded, dt);
				ExportProtocol(pathArg, used, discarded, rejected, iters, converged, note, condHeuristic, warnPoorGeometry);

				if (verbose)
				{
					var proto = ProtocolBuilder.Build(used, discarded, rejected, iters, converged, note, condHeuristic, warnPoorGeometry);
					Console.WriteLine("=== Протокол (скорочено) ===");
					Console.Write(proto);
				}

				Logger.Info($"Finish: used={used}; discarded={discarded}; iters={iters}; conv={converged}; ms={dt}");
				return 0;
			}
			catch (ApplicationException ex)
			{
				Console.WriteLine(ex.Message);
				Logger.Error(ex.ToString());
				return 3;
			}
			catch (Exception ex)
			{
				Console.WriteLine("Непередбачена помилка. Див. geoalt.log");
				Logger.Error(ex.ToString());
				return 4;
			}
		}

		private static void ExportResults(
			string inputPath,
			GeoPoint outGeo,
			double epsPhiPct, double epsLamPct, double epsHPct,
			int used, int discarded, int calcTimeMs)
		{
			string dir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
			string baseName = Path.GetFileNameWithoutExtension(inputPath);

			string iso = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);

			// CSV
			string csvPath = Path.Combine(dir, baseName + "_result.csv");
			var csv = new StringBuilder();
			csv.AppendLine("phi_deg,lambda_deg,h_m,eps_phi_pct,eps_lambda_pct,eps_h_pct,used,discarded,calc_time_ms,date_time");
			csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
				"{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
				outGeo.PhiDeg, outGeo.LambdaDeg, outGeo.Hm,
				epsPhiPct, epsLamPct, epsHPct,
				used, discarded, calcTimeMs, iso));
			File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));

			// JSON
			string jsonPath = Path.Combine(dir, baseName + "_result.json");
			var json = new StringBuilder();
			json.Append("{\n");
			json.AppendFormat(CultureInfo.InvariantCulture, "  \"phi_deg\": {0},\n", outGeo.PhiDeg);
			json.AppendFormat(CultureInfo.InvariantCulture, "  \"lambda_deg\": {0},\n", outGeo.LambdaDeg);
			json.AppendFormat(CultureInfo.InvariantCulture, "  \"h_m\": {0},\n", outGeo.Hm);
			json.AppendFormat(CultureInfo.InvariantCulture, "  \"eps_phi_pct\": {0},\n", epsPhiPct);
			json.AppendFormat(CultureInfo.InvariantCulture, "  \"eps_lambda_pct\": {0},\n", epsLamPct);
			json.AppendFormat(CultureInfo.InvariantCulture, "  \"eps_h_pct\": {0},\n", epsHPct);
			json.AppendFormat("  \"used\": {0},\n", used);
			json.AppendFormat("  \"discarded\": {0},\n", discarded);
			json.AppendFormat("  \"calc_time_ms\": {0},\n", calcTimeMs);
			json.AppendFormat("  \"date_time\": \"{0}\"\n", iso);
			json.Append("}\n");
			File.WriteAllText(jsonPath, json.ToString(), new UTF8Encoding(false));

			Console.WriteLine($"[i] Експортовано: {csvPath}");
			Console.WriteLine($"[i] Експортовано: {jsonPath}");
		}

		private static void ExportProtocol(
			string inputPath,
			int used, int discarded, List<Rejection> rejected,
			int iters, bool converged, string note,
			double condHeuristic, bool warnPoorGeometry)
		{
			string dir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
			string baseName = Path.GetFileNameWithoutExtension(inputPath);
			string protoPath = Path.Combine(dir, baseName + "_protocol.txt");

			var proto = ProtocolBuilder.Build(used, discarded, rejected, iters, converged, note, condHeuristic, warnPoorGeometry);
			File.WriteAllText(protoPath, proto, new UTF8Encoding(false));
			Console.WriteLine($"[i] Експортовано: {protoPath}");
		}

		private static void ExportProtocolOnly(
			string inputPath,
			int used, int discarded, List<Rejection> rejected,
			int iters, bool converged, string note,
			double condHeuristic, bool warnPoorGeometry)
		{
			ExportProtocol(inputPath, used, discarded, rejected, iters, converged, note, condHeuristic, warnPoorGeometry);
		}
	}
}
