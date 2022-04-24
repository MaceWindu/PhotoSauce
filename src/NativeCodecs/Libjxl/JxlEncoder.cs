// Copyright © Clinton Ingram and Contributors.  Licensed under the MIT License.

using System;
using System.IO;
using System.Drawing;

using PhotoSauce.MagicScaler;
using PhotoSauce.Interop.Libjxl;
using static PhotoSauce.Interop.Libjxl.Libjxl;

namespace PhotoSauce.NativeCodecs.Libjxl;

internal sealed unsafe class JxlEncoder : IImageEncoder
{
	private readonly IJxlEncoderOptions options;
	private readonly Stream stream;

	private IntPtr encoder, encopt;
	private bool written;

	public static JxlEncoder Create(Stream outStream, IEncoderOptions? jxlOptions) => new(outStream, jxlOptions);

	private JxlEncoder(Stream outStream, IEncoderOptions? jxlOptions)
	{
		stream = outStream;
		options = jxlOptions is IJxlEncoderOptions opt ? opt : JxlLossyEncoderOptions.Default;

		encoder = JxlFactory.CreateEncoder();
		encopt = JxlEncoderOptionsCreate(encoder, default);
		JxlError.Check(JxlEncoderOptionsSetEffort(encopt, (int)options.EncodeSpeed));
		JxlError.Check(JxlEncoderOptionsSetDecodingSpeed(encopt, (int)options.DecodeSpeed));

		if (options is JxlLossyEncoderOptions lopt)
		{
			// current version of encoder will AV on write if this is set over 4 (including the default of 7) for lossy
			if (options.EncodeSpeed > JxlEncodeSpeed.Cheetah)
				JxlError.Check(JxlEncoderOptionsSetEffort(encopt, 4));

			JxlError.Check(JxlEncoderOptionsSetDistance(encopt, lopt.Distance));
		}
		else
		{
			JxlError.Check(JxlEncoderOptionsSetLossless(encopt, JXL_TRUE));
		}
	}

	public void WriteFrame(IPixelSource source, IMetadataSource metadata, Rectangle sourceArea)
	{
		if (written)
			throw new InvalidOperationException("An image frame has already been written, and this encoder does not yet support multiple frames.");

		if (sourceArea == default)
			sourceArea = new(0, 0, source.Width, source.Height);

		var srcfmt = PixelFormat.FromGuid(source.Format);
		var dstfmt =
			srcfmt == PixelFormat.Bgra32 ? PixelFormat.Rgba32 :
			srcfmt == PixelFormat.Bgr24 ? PixelFormat.Rgb24 :
			srcfmt == PixelFormat.Grey8 ? srcfmt :
			throw new NotSupportedException("Image format not supported.");

		int stride = sourceArea.Width * dstfmt.BytesPerPixel;
		using var pixbuff = BufferPool.RentLocal<byte>(stride * sourceArea.Height);

		if (srcfmt != dstfmt)
		{
			using var tran = new MagicScaler.Transforms.ConversionTransform(source.AsPixelSource(), dstfmt);
			((IPixelSource)tran).CopyPixels(sourceArea, stride, pixbuff.Span);
		}
		else
		{
			source.CopyPixels(sourceArea, stride, pixbuff.Span);
		}

		if (options is JxlLossyEncoderOptions lopt && lopt.Distance == default)
			JxlError.Check(JxlEncoderOptionsSetDistance(encopt, JxlLossyEncoderOptions.DistanceFromQuality(SettingsUtil.GetDefaultQuality(Math.Max(sourceArea.Width, sourceArea.Height)))));

		var basinf = default(JxlBasicInfo);
		JxlEncoderInitBasicInfo(&basinf);
		basinf.xsize = (uint)sourceArea.Width;
		basinf.ysize = (uint)sourceArea.Height;
		basinf.bits_per_sample = 8;
		basinf.num_color_channels = (uint)(dstfmt.ChannelCount - (dstfmt.AlphaRepresentation == PixelAlphaRepresentation.None ? 0 : 1));
		basinf.alpha_bits = dstfmt.AlphaRepresentation == PixelAlphaRepresentation.None ? 0 : basinf.bits_per_sample;
		basinf.alpha_premultiplied = dstfmt.AlphaRepresentation == PixelAlphaRepresentation.Associated ? JXL_TRUE : JXL_FALSE;
		basinf.uses_original_profile = JXL_TRUE;

		if (metadata.TryGetMetadata<OrientationMetadata>(out var orient))
			basinf.orientation = (JxlOrientation)orient.Orientation.Clamp();

		JxlError.Check(JxlEncoderSetBasicInfo(encoder, &basinf));

		if (metadata.TryGetMetadata<ColorProfileMetadata>(out var prof))
		{
			var profile = prof.Profile;
			fixed (byte* pp = &profile.ProfileBytes.GetDataRef())
				JxlError.Check(JxlEncoderSetICCProfile(encoder, pp, (uint)profile.ProfileBytes.Length));
		}
		else
		{
			var color = default(JxlColorEncoding);
			JxlColorEncodingSetToSRGB(&color, dstfmt.ColorRepresentation == PixelColorRepresentation.Grey ? JXL_TRUE : JXL_FALSE);
			JxlError.Check(JxlEncoderSetColorEncoding(encoder, &color));
		}

		var pixfmt = new JxlPixelFormat {
			num_channels = (uint)dstfmt.ChannelCount,
			data_type = JxlDataType.JXL_TYPE_UINT8
		};

		using var stmbuff = BufferPool.RentLocal<byte>(1 << 14);
		var stmspan = stmbuff.Span;
		fixed (byte* pb = pixbuff, pf = stmspan)
		{
			JxlError.Check(JxlEncoderAddImageFrame(encopt, &pixfmt, pb, (uint)stride * basinf.ysize));
			JxlEncoderCloseInput(encoder);

			var res = default(JxlEncoderStatus);
			do
			{
				byte* ppf = pf;
				nuint cb = (uint)stmspan.Length;

				res = JxlEncoderProcessOutput(encoder, &ppf, &cb);
				JxlError.Check(res);

				stream.Write(stmspan[..^((int)cb)]);
			}
			while (res == JxlEncoderStatus.JXL_ENC_NEED_MORE_OUTPUT);
		}

		written = true;
	}

	public void Commit()
	{
		if (!written)
			throw new InvalidOperationException("An image frame has not been written.");
	}

	private void dispose(bool disposing)
	{
		if (encoder == default)
			return;

		JxlEncoderDestroy(encoder);
		encoder = encopt = default;

		if (disposing)
			GC.SuppressFinalize(this);
	}

	public void Dispose() => dispose(true);

	~JxlEncoder() => dispose(false);
}
