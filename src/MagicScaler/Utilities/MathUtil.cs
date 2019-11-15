﻿using System;
using System.Numerics;
using System.Runtime.CompilerServices;

#if HWINTRINSICS
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

using static System.Math;

namespace PhotoSauce.MagicScaler
{
	internal static class MathUtil
	{
		private const int ishift = 15;
		private const int iscale = 1 << ishift;
		private const int imax = (1 << ishift + 1) - 1;
		private const int iround = iscale >> 1;
		private const float fscale = iscale;
		private const float ifscale = 1f / fscale;
		private const float fround = 0.5f;
		private const double dscale = iscale;
		private const double idscale = 1d / dscale;
		private const double dround = 0.5;

		public const ushort UQ15Max = imax;
		public const ushort UQ15One = iscale;
		public const ushort UQ15Round = iround;
		public const float FloatScale = fscale;
		public const float FloatRound = fround;
		public const double DoubleScale = dscale;
		public const double DoubleRound = dround;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Clamp(this int x, int min, int max) => Min(Max(min, x), max);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Clamp(this double x, double min, double max) => Min(Max(min, x), max);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector<T> Clamp<T>(this Vector<T> x, Vector<T> min, Vector<T> max) where T : unmanaged => Vector.Min(Vector.Max(min, x), max);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort ClampToUQ15(int x) => (ushort)Min(Max(0, x), UQ15Max);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort ClampToUQ15One(int x) => (ushort)Min(Max(0, x), UQ15One);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort ClampToUQ15One(ushort x) => Min(x, UQ15One);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte ClampToByte(int x) => (byte)Min(Max(0, x), byte.MaxValue);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Fix15(double x) => (int)Round(x * dscale);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Fix15(float x) =>
#if BUILTIN_MATHF
			(int)MathF.Round(x * fscale);
#else
			(int)Round(x * fscale);
#endif

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort FixToUQ15One(double x) => ClampToUQ15One((int)(x * dscale + dround));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort FixToUQ15One(float x) => ClampToUQ15One((int)(x * fscale + fround));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte FixToByte(double x) => ClampToByte((int)(x * byte.MaxValue + dround));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte FixToByte(float x) => ClampToByte((int)(x * byte.MaxValue + fround));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double UnFix15ToDouble(int x) => x * idscale;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float UnFix15ToFloat(int x) => x * ifscale;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int UnFix8(int x) => x + (iround >> 7) >> 8;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int UnFix15(int x) => x + iround >> ishift;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int UnFix22(int x) => x + (iround << 7) >> 22;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort UnFixToUQ15(int x) => ClampToUQ15(UnFix15(x));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort UnFixToUQ15One(int x) => ClampToUQ15One(UnFix15(x));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte UnFix15ToByte(int x) => ClampToByte(UnFix15(x));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte UnFix22ToByte(int x) => ClampToByte(UnFix22(x));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DivCeiling(int x, int y) => (x + (y - 1)) / y;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int PowerOfTwoFloor(int x, int powerOfTwo) => x & ~(powerOfTwo - 1);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int PowerOfTwoCeiling(int x, int powerOfTwo) => x + (powerOfTwo - 1) & ~(powerOfTwo - 1);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Sqrt(this float x) =>
#if BUILTIN_MATHF
			MathF.Sqrt(x);
#else
			(float)Math.Sqrt(x);
#endif

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Floor(this float x) =>
#if BUILTIN_MATHF
			MathF.Floor(x);
#else
			(float)Math.Floor(x);
#endif

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Abs(this float x) =>
#if BUILTIN_MATHF
			MathF.Abs(x);
#else
			Math.Abs(x);
#endif

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float MaxF(float x, float o) =>
#if BUILTIN_MATHF
			MathF.Max(x, o);
#else
			x < o ? o : x;
#endif

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float PowF(float x, float y) =>
#if BUILTIN_MATHF
			MathF.Pow(x, y);
#else
			(float)Pow(x, y);
#endif

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Lerp(float l, float h, float d) => h * d + l * (1f - d);

		public static bool IsRoughlyEqualTo(this float x, float y) => (x - y).Abs() < 0.0001f;

		unsafe public static bool IsRouglyEqualTo(this Matrix4x4 m1, Matrix4x4 m2)
		{
			const float epsilon = 0.001f;

#if HWINTRINSICS
			if (Sse.IsSupported)
			{
				var veps = Vector128.Create(epsilon);
				var vmsk = Vector128.Create(0x7fffffff).AsSingle();

				return
					Sse.MoveMask(Sse.CompareNotLessThan(Sse.And(Sse.Subtract(Sse.LoadVector128(&m1.M11), Sse.LoadVector128(&m2.M11)), vmsk), veps)) == 0 &&
					Sse.MoveMask(Sse.CompareNotLessThan(Sse.And(Sse.Subtract(Sse.LoadVector128(&m1.M21), Sse.LoadVector128(&m2.M21)), vmsk), veps)) == 0 &&
					Sse.MoveMask(Sse.CompareNotLessThan(Sse.And(Sse.Subtract(Sse.LoadVector128(&m1.M31), Sse.LoadVector128(&m2.M31)), vmsk), veps)) == 0 &&
					Sse.MoveMask(Sse.CompareNotLessThan(Sse.And(Sse.Subtract(Sse.LoadVector128(&m1.M41), Sse.LoadVector128(&m2.M41)), vmsk), veps)) == 0;
			}
#endif

			var md = m1 - m2;

			return
				md.M11.Abs() < epsilon && md.M12.Abs() < epsilon && md.M13.Abs() < epsilon && md.M14.Abs() < epsilon &&
				md.M21.Abs() < epsilon && md.M22.Abs() < epsilon && md.M23.Abs() < epsilon && md.M24.Abs() < epsilon &&
				md.M31.Abs() < epsilon && md.M32.Abs() < epsilon && md.M33.Abs() < epsilon && md.M34.Abs() < epsilon &&
				md.M41.Abs() < epsilon && md.M42.Abs() < epsilon && md.M43.Abs() < epsilon && md.M44.Abs() < epsilon;
		}

		// Implementation taken from https://source.dot.net/#System.Private.CoreLib/shared/System/Numerics/Matrix4x4.cs,1314
		// Because of the number of calculations and rounding steps, using float intermediates results in loss of precision.
		// This is the same logic but with double precision intermediate calculations.
		public static Matrix4x4 InvertPrecise(this Matrix4x4 matrix)
		{
			const double epsilon = 2.2250738585072014E-308;

			double a = matrix.M11, b = matrix.M12, c = matrix.M13, d = matrix.M14;
			double e = matrix.M21, f = matrix.M22, g = matrix.M23, h = matrix.M24;
			double i = matrix.M31, j = matrix.M32, k = matrix.M33, l = matrix.M34;
			double m = matrix.M41, n = matrix.M42, o = matrix.M43, p = matrix.M44;

			double kp_lo = k * p - l * o;
			double jp_ln = j * p - l * n;
			double jo_kn = j * o - k * n;
			double ip_lm = i * p - l * m;
			double io_km = i * o - k * m;
			double in_jm = i * n - j * m;

			double a11 = +(f * kp_lo - g * jp_ln + h * jo_kn);
			double a12 = -(e * kp_lo - g * ip_lm + h * io_km);
			double a13 = +(e * jp_ln - f * ip_lm + h * in_jm);
			double a14 = -(e * jo_kn - f * io_km + g * in_jm);

			double det = a * a11 + b * a12 + c * a13 + d * a14;

			if (Math.Abs(det) < epsilon)
				return new Matrix4x4(
					float.NaN, float.NaN, float.NaN, float.NaN,
					float.NaN, float.NaN, float.NaN, float.NaN,
					float.NaN, float.NaN, float.NaN, float.NaN,
					float.NaN, float.NaN, float.NaN, float.NaN
				);

			var result = new Matrix4x4();
			double invDet = 1 / det;

			result.M11 = (float)(a11 * invDet);
			result.M21 = (float)(a12 * invDet);
			result.M31 = (float)(a13 * invDet);
			result.M41 = (float)(a14 * invDet);

			result.M12 = (float)(-(b * kp_lo - c * jp_ln + d * jo_kn) * invDet);
			result.M22 = (float)(+(a * kp_lo - c * ip_lm + d * io_km) * invDet);
			result.M32 = (float)(-(a * jp_ln - b * ip_lm + d * in_jm) * invDet);
			result.M42 = (float)(+(a * jo_kn - b * io_km + c * in_jm) * invDet);

			double gp_ho = g * p - h * o;
			double fp_hn = f * p - h * n;
			double fo_gn = f * o - g * n;
			double ep_hm = e * p - h * m;
			double eo_gm = e * o - g * m;
			double en_fm = e * n - f * m;

			result.M13 = (float)(+(b * gp_ho - c * fp_hn + d * fo_gn) * invDet);
			result.M23 = (float)(-(a * gp_ho - c * ep_hm + d * eo_gm) * invDet);
			result.M33 = (float)(+(a * fp_hn - b * ep_hm + d * en_fm) * invDet);
			result.M43 = (float)(-(a * fo_gn - b * eo_gm + c * en_fm) * invDet);

			double gl_hk = g * l - h * k;
			double fl_hj = f * l - h * j;
			double fk_gj = f * k - g * j;
			double el_hi = e * l - h * i;
			double ek_gi = e * k - g * i;
			double ej_fi = e * j - f * i;

			result.M14 = (float)(-(b * gl_hk - c * fl_hj + d * fk_gj) * invDet);
			result.M24 = (float)(+(a * gl_hk - c * el_hi + d * ek_gi) * invDet);
			result.M34 = (float)(-(a * fl_hj - b * el_hi + d * ej_fi) * invDet);
			result.M44 = (float)(+(a * fk_gj - b * ek_gi + c * ej_fi) * invDet);

			return result;
		}

#if HWINTRINSICS
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float HorizontalAdd(this Vector128<float> v)
		{	                                      //  a | b | c | d
			var high = Sse3.IsSupported ?         //  b |___| d |___
				Sse3.MoveHighAndDuplicate(v) :
				Sse.Shuffle(v, v, 0b_11_11_01_01);
			var sums = Sse.Add(v, high);          // a+b|___|c+d|___
			high = Sse.MoveHighToLow(high, sums); // c+d|___|___|___

			return Sse.AddScalar(sums, high).ToScalar();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float HorizontalAdd(this Vector256<float> v) => HorizontalAdd(Sse.Add(v.GetLower(), v.GetUpper()));
#endif
	}
}
