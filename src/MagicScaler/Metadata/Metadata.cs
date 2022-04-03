// Copyright © Clinton Ingram and Contributors.  Licensed under the MIT License.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using TerraFX.Interop.Windows;

namespace PhotoSauce.MagicScaler
{
	internal sealed class NoopMetadataSource : IMetadataSource
	{
		public static readonly IMetadataSource Instance = new NoopMetadataSource();

		public bool TryGetMetadata<T>([NotNullWhen(true)] out T? metadata) where T : IMetadata
		{
			metadata = default;
			return false;
		}
	}

	internal readonly unsafe struct WicFrameMetadataReader : IMetadata
	{
		public readonly IWICMetadataQueryReader* Reader;
		public readonly IEnumerable<string> CopyNames;

		public WicFrameMetadataReader(IWICMetadataQueryReader* reader, IEnumerable<string> names)
		{
			Reader = reader;
			CopyNames = names;
		}
	}

	internal interface IIccProfileSource : IMetadata
	{
		int ProfileLength { get; }
		void CopyProfile(Span<byte> dest);
	}

	internal readonly record struct ColorProfileMetadata(ColorProfile Profile) : IMetadata { }

	internal readonly record struct OrientationMetadata(Orientation Orientation) : IMetadata { }

	internal readonly record struct ResolutionMetadata(Rational ResolutionX, Rational ResolutionY, ResolutionUnit Units) : IMetadata
	{
		public static readonly ResolutionMetadata Default = new(new(96, 1), new(96, 1), ResolutionUnit.Inch);

		public bool IsValid => ResolutionX.Denominator != 0 && ResolutionY.Denominator != 0;

		public ResolutionMetadata ToDpi() => Units switch {
			ResolutionUnit.Inch       => this,
			ResolutionUnit.Centimeter => new(((double)ResolutionX /  2.54).ToRational(), ((double)ResolutionY /  2.54).ToRational(), ResolutionUnit.Inch),
			ResolutionUnit.Meter      => new(((double)ResolutionX * 39.37).ToRational(), ((double)ResolutionY * 39.37).ToRational(), ResolutionUnit.Inch),
			_                         => new(((double)ResolutionX * 96.0 ).ToRational(), ((double)ResolutionY * 96.0 ).ToRational(), ResolutionUnit.Inch)
		};
	}

	internal interface IMetadataTransform
	{
		void Init(IMetadataSource source);
	}

	internal sealed class MagicMetadataFilter : IMetadataSource
	{
		private readonly PipelineContext context;
		private readonly IMetadataSource source;

		public MagicMetadataFilter(PipelineContext ctx) => (context, source) = (ctx, ctx.ImageFrame as IMetadataSource ?? ctx.Metadata);

		public unsafe bool TryGetMetadata<T>([NotNullWhen(true)] out T? metadata) where T : IMetadata
		{
			var settings = context.Settings;

			if (typeof(T) == typeof(ResolutionMetadata))
			{
				var res = new ResolutionMetadata(settings.DpiX.ToRational(), settings.DpiY.ToRational(), ResolutionUnit.Inch);
				if (settings.DpiX == default || settings.DpiY == default)
				{
					res = source.TryGetMetadata<ResolutionMetadata>(out var r) && r.IsValid ? r : ResolutionMetadata.Default;
					if (settings.DpiX != default)
						res = res.ToDpi() with { ResolutionX = settings.DpiX.ToRational() };
					if (settings.DpiY != default)
						res = res.ToDpi() with { ResolutionY = settings.DpiY.ToRational() };
				}

				metadata = (T)(object)res;
				return true;
			}

			if (typeof(T) == typeof(OrientationMetadata))
			{
				if (settings.OrientationMode == OrientationMode.Preserve && source.TryGetMetadata<OrientationMetadata>(out var orient) && orient.Orientation != Orientation.Normal)
				{
					metadata = (T)(object)(new OrientationMetadata(orient.Orientation));
					return true;
				}

				metadata = default;
				return false;
			}

			if (typeof(T) == typeof(ColorProfileMetadata))
			{
				if (settings.ColorProfileMode is ColorProfileMode.NormalizeAndEmbed or ColorProfileMode.Preserve || (settings.ColorProfileMode is ColorProfileMode.Normalize && context.DestColorProfile != ColorProfile.sRGB && context.DestColorProfile != ColorProfile.sGrey))
				{
					metadata = (T)(object)(new ColorProfileMetadata(context.DestColorProfile!));
					return true;
				}

				metadata = default;
				return false;
			}

			if (typeof(T) == typeof(WicFrameMetadataReader))
			{
				if (source is WicImageFrame wicfrm && wicfrm.WicMetadataReader is not null && settings.MetadataNames.Any())
				{
					metadata = (T)(object)(new WicFrameMetadataReader(wicfrm.WicMetadataReader, settings.MetadataNames));
					return true;
				}

				metadata = default;
				return false;
			}

			return source.TryGetMetadata(out metadata);
		}
	}

	/// <summary>A <a href="https://en.wikipedia.org/wiki/Rational_number">rational number</a>, as defined by an integer <paramref name="Numerator" /> and <paramref name="Denominator" />.</summary>
	/// <param name="Numerator">The numerator of the rational number.</param>
	/// <param name="Denominator">The denominator of the rational number.</param>
	internal readonly record struct Rational(uint Numerator, uint Denominator)
	{
		public override string ToString() => $"{Numerator}/{Denominator}";

		public static implicit operator Rational((uint n, uint d) f) => new(f.n, f.d);
		public static explicit operator double(Rational r) => r.Denominator is 0 ? double.NaN : (double)r.Numerator / r.Denominator;
	}

	/// <summary>Defines global/container metadata for a sequence of animated frames.</summary>
	internal readonly struct AnimationContainer : IMetadata
	{
		/// <summary>The width of the animation's logical screen.  Values less than 1 imply the width is equal to the width of the first frame.</summary>
		public readonly int ScreenWidth;

		/// <summary>The height of the animation's logical screen.  Values less than 1 imply the height is equal to the height of the first frame.</summary>
		public readonly int ScreenHeight;

		/// <summary>The number of times to loop the animation.  Values less than 1 imply inifinte looping.</summary>
		public readonly int LoopCount;

		/// <summary>The background color to restore when a frame's disposal method is RestoreBackground, in ARGB order.</summary>
		public readonly int BackgroundColor;

		/// <summary>True if this animation requires a persistent screen buffer onto which frames are rendered, otherwise false.</summary>
		public readonly bool RequiresScreenBuffer;

		public AnimationContainer(int screenWidth, int screenHeight, int loopCount = 0, int bgColor = 0, bool screenBuffer = false) =>
			(ScreenWidth, ScreenHeight, LoopCount, BackgroundColor, RequiresScreenBuffer) = (screenWidth, screenHeight, loopCount, bgColor, screenBuffer);
	}

	/// <summary>Defines metadata for a single frame within an animated image sequence.</summary>
	internal readonly struct AnimationFrame : IMetadata
	{
		// Rather arbitrary default of NTSC film speed
		internal static AnimationFrame Default = new(default, default, new Rational(1001, 24000), default, default);

		/// <summary>The horizontal offset of the frame's content, relative to the logical screen.</summary>
		public readonly int OffsetLeft;

		/// <summary>The vertical offset of the frame's content, relative to the logical screen.</summary>
		public readonly int OffsetTop;

		/// <summary>The amount of time, in seconds, the frame should be displayed.</summary>
		/// <remarks>For animated GIF output, the denominator will be normalized to <c>100</c>.</remarks>
		public readonly Rational Duration;

		/// <summary>The disposition of the frame.</summary>
		public readonly FrameDisposalMethod Disposal;

		/// <summary>True to indicate the frame contains transparent pixels, otherwise false.</summary>
		public readonly bool HasAlpha;

		public AnimationFrame(int offsetLeft, int offsetTop, Rational duration, FrameDisposalMethod disposal, bool alpha) =>
			(OffsetLeft, OffsetTop, Duration, Disposal, HasAlpha) = (offsetLeft, offsetTop, duration, disposal, alpha);
	}
}