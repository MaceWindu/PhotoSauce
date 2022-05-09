// Copyright © Clinton Ingram and Contributors.  Licensed under the MIT License.

using System;
using System.IO;
using System.Numerics;
using System.Diagnostics.CodeAnalysis;

using PhotoSauce.MagicScaler;
using PhotoSauce.Interop.Libwebp;
using static PhotoSauce.Interop.Libwebp.Libwebp;
using static PhotoSauce.Interop.Libwebp.Libwebpdemux;

namespace PhotoSauce.NativeCodecs.Libwebp;

internal sealed unsafe class WebpContainer : IImageContainer, IMetadataSource, IIccProfileSource, IExifSource
{
	private const uint iccpTag = 'I' | 'C' << 8 | 'C' << 16 | 'P' << 24;
	private const uint exifTag = 'E' | 'X' << 8 | 'I' << 16 | 'F' << 24;
	private const uint xmpTag  = 'X' | 'M' << 8 | 'P' << 16 | ' ' << 24;

	private readonly WebPFeatureFlags features;
	private readonly IDecoderOptions options;
	private readonly int frameCount, frameOffset;
	private byte* filebuff;
	private IntPtr handle;

	private WebpContainer(Stream stm, IDecoderOptions? opt, WebPFeatureFlags flags)
	{
		int len = checked((int)(stm.Length - stm.Position));
		filebuff = (byte*)WebPMalloc((uint)len);
		if (filebuff is null)
			throw new OutOfMemoryException();

		var buff = new Span<byte>(filebuff, len);
		stm.FillBuffer(buff);

		var data = new WebPData { bytes = filebuff, size = (uint)len };
		handle = WebPDemux(&data);
		if (handle == default)
			throw new InvalidDataException();

		features = flags;
		options = opt;

		uint fcount = WebPDemuxGetI(handle, WebPFormatFeature.WEBP_FF_FRAME_COUNT);
		var range = opt is IMultiFrameDecoderOptions mul ? mul.FrameRange : Range.All;
		(frameOffset, frameCount) = range.GetOffsetAndLength((int)fcount);
	}

	public string MimeType => ImageMimeTypes.Webp;

	public int FrameCount => frameCount;

	int IIccProfileSource.ProfileLength => (int)getChunk(iccpTag).size;

	int IExifSource.ExifLength => (int)getChunk(exifTag).size;

	public IImageFrame GetFrame(int index)
	{
		ensureHandle();

		index += frameOffset;
		if ((uint)index >= (uint)(frameOffset + frameCount))
			throw new ArgumentOutOfRangeException(nameof(index), "Invalid frame index.");

		WebPIterator iter;
		if (!WebpResult.Succeeded(WebPDemuxGetFrame(handle, index + 1, &iter)))
			throw new ArgumentOutOfRangeException(nameof(index));

		WebPBitstreamFeatures ffeat;
		WebpResult.Check(WebPGetFeatures(iter.fragment.bytes, iter.fragment.size, &ffeat));
		bool isAnimation = features.HasFlag(WebPFeatureFlags.ANIMATION_FLAG);
		bool isLossless = ffeat.format == 2;
		bool hasAlpha = ffeat.has_alpha != 0;
		bool decodePlanar = !hasAlpha && !isAnimation && !isLossless && (options is not IPlanarDecoderOptions opt || opt.AllowPlanar);

		return decodePlanar ? new WebpYuvFrame(this, ffeat, index) : new WebpRgbFrame(this, ffeat, index, isAnimation || hasAlpha);
	}

	private WebPData getChunk(uint fourcc)
	{
		var data = default(WebPData);

		WebPChunkIterator iter;
		if (WebpResult.Succeeded(WebPDemuxGetChunk(handle, (sbyte*)&fourcc, 1, &iter)))
		{
			data = iter.chunk;
			WebPDemuxReleaseChunkIterator(&iter);
		}

		return data;
	}

	void IIccProfileSource.CopyProfile(Span<byte> dest)
	{
		var data = getChunk(iccpTag);
		if (data.size != 0)
			new ReadOnlySpan<byte>(data.bytes, (int)data.size).CopyTo(dest);
	}

	void IExifSource.CopyExif(Span<byte> dest)
	{
		var data = getChunk(exifTag);
		if (data.size != 0)
			new ReadOnlySpan<byte>(data.bytes, (int)data.size).CopyTo(dest);
	}

	public bool TryGetMetadata<T>([NotNullWhen(true)] out T? metadata) where T : IMetadata
	{
		ensureHandle();

		if (typeof(T) == typeof(AnimationContainer) && features.HasFlag(WebPFeatureFlags.ANIMATION_FLAG))
		{
			int cw = (int)WebPDemuxGetI(handle, WebPFormatFeature.WEBP_FF_CANVAS_WIDTH);
			int ch = (int)WebPDemuxGetI(handle, WebPFormatFeature.WEBP_FF_CANVAS_HEIGHT);
			int fc = (int)WebPDemuxGetI(handle, WebPFormatFeature.WEBP_FF_FRAME_COUNT);
			int lc = (int)WebPDemuxGetI(handle, WebPFormatFeature.WEBP_FF_LOOP_COUNT);
			int bg = (int)WebPDemuxGetI(handle, WebPFormatFeature.WEBP_FF_BACKGROUND_COLOR);
			var anicnt = new AnimationContainer(cw, ch, fc, lc, bg, true);

			metadata = (T)(object)anicnt;
			return true;
		}

		if (typeof(T) == typeof(OrientationMetadata))
		{
			var orient = Orientation.Normal;
			if (features.HasFlag(WebPFeatureFlags.EXIF_FLAG))
			{
				var exifsrc = (IExifSource)this;
				using var buff = BufferPool.RentLocal<byte>(exifsrc.ExifLength);
				exifsrc.CopyExif(buff.Span);

				var rdr = ExifReader.Create(buff.Span);
				foreach (var tag in rdr)
				{
					if (tag.ID == ExifTags.Tiff.Orientation)
						orient = ((Orientation)rdr.CoerceValue<int>(tag)).Clamp();
				}
			}

			metadata = (T)(object)(new OrientationMetadata(orient));
			return true;
		}

		if (typeof(T) == typeof(IIccProfileSource) && features.HasFlag(WebPFeatureFlags.ICCP_FLAG))
		{
			metadata = (T)(object)this;
			return true;
		}

		if (typeof(T) == typeof(IExifSource) && features.HasFlag(WebPFeatureFlags.EXIF_FLAG))
		{
			metadata = (T)(object)this;
			return true;
		}

		metadata = default;
		return false;
	}

	public static WebpContainer? TryLoad(Stream imgStream, IDecoderOptions? options)
	{
		// 30 bytes are needed to get basic info for webp
		const int bufflen = 32;

		if ((imgStream.Length - imgStream.Position) < bufflen)
			return null;

		var buff = (Span<byte>)stackalloc byte[bufflen];
		imgStream.FillBuffer(buff);
		imgStream.Seek(-bufflen, SeekOrigin.Current);

		var state = default(WebPDemuxState);
		var flags = default(WebPFeatureFlags);
		fixed (byte* ptr = buff)
		{
			var data = new WebPData { bytes = ptr, size = bufflen };
			var demuxer = WebPDemuxPartial(&data, &state);

			if (state >= WebPDemuxState.WEBP_DEMUX_PARSED_HEADER)
				flags = (WebPFeatureFlags)WebPDemuxGetI(demuxer, WebPFormatFeature.WEBP_FF_FORMAT_FLAGS);

			WebPDemuxDelete(demuxer);
		}

		if (state >= WebPDemuxState.WEBP_DEMUX_PARSED_HEADER)
			return new WebpContainer(imgStream, options, flags);

		return null;
	}

	private void ensureHandle()
	{
		if (handle == default)
			throw new ObjectDisposedException(nameof(WebpContainer));
	}

	private void dispose(bool disposing)
	{
		if (filebuff is null)
			return;

		WebPFree(filebuff);
		filebuff = null;

		WebPDemuxDelete(handle);
		handle = default;

		if (disposing)
			GC.SuppressFinalize(this);
	}

	public void Dispose() => dispose(true);

	~WebpContainer() => dispose(false);

	private abstract class WebpFrame : IImageFrame, IMetadataSource
	{
		private readonly WebpContainer container;
		private readonly WebPBitstreamFeatures features;
		private readonly AnimationFrame frmmeta;
		private readonly int frmnum;
		private WebPDecBuffer buffer;

		public abstract IPixelSource PixelSource { get; }

		public WebpFrame(WebpContainer cont, WebPBitstreamFeatures feat, int index)
		{
			container = cont;
			features = feat;
			frmnum = index + 1;

			if (container.features.HasFlag(WebPFeatureFlags.ANIMATION_FLAG))
			{
				WebPIterator iter;
				WebpResult.Check(WebPDemuxGetFrame(cont.handle, frmnum, &iter));

				frmmeta = new AnimationFrame(
					iter.x_offset,
					iter.y_offset,
					new Rational((uint)iter.duration, 1000u),
					iter.dispose_method == WebPMuxAnimDispose.WEBP_MUX_DISPOSE_BACKGROUND ? FrameDisposalMethod.RestoreBackground : FrameDisposalMethod.Preserve,
					iter.has_alpha != 0
					// TODO iter.blend_method
				);

				WebPDemuxReleaseIterator(&iter);
			}
		}

		private void ensureDecoded(WebpPlane plane)
		{
			if (buffer.IsAllocated())
				return;

			container.ensureHandle();

			WebPIterator iter;
			WebpResult.Check(WebPDemuxGetFrame(container.handle, frmnum, &iter));

			WebPDecoderConfig cfg;
			WebpResult.Check(WebPInitDecoderConfig(&cfg));
			cfg.output.colorspace = plane == WebpPlane.Bgra ? WEBP_CSP_MODE.MODE_BGRA : plane == WebpPlane.Bgr ? WEBP_CSP_MODE.MODE_BGR : WEBP_CSP_MODE.MODE_YUV;
			cfg.input = features;

			WebpResult.Check(WebPDecode(iter.fragment.bytes, iter.fragment.size, &cfg));
			WebPDemuxReleaseIterator(&iter);

			buffer = cfg.output;
		}

		public bool TryGetMetadata<T>([NotNullWhen(true)] out T? metadata) where T : IMetadata
		{
			if (typeof(T) == typeof(AnimationFrame) && container.features.HasFlag(WebPFeatureFlags.ANIMATION_FLAG))
			{
				metadata = (T)(object)frmmeta;
				return true;
			}

			return container.TryGetMetadata(out metadata);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
				GC.SuppressFinalize(this);

			if (!buffer.IsAllocated())
				return;

			fixed (WebPDecBuffer* pbuff = &buffer)
				WebPFreeDecBuffer(pbuff);
		}

		public void Dispose() => Dispose(true);

		~WebpFrame() => Dispose(false);

		protected sealed class WebpDecBufferPixelSource : PixelSource
		{
			private readonly WebpFrame frame;
			private readonly WebpPlane plane;

			public override PixelFormat Format { get; }
			public override int Width { get; }
			public override int Height { get; }

			public WebpDecBufferPixelSource(WebpFrame frm, WebpPlane pln)
			{
				frame = frm;
				plane = pln;

				Format = plane switch {
					WebpPlane.Y   => PixelFormat.Y8,
					WebpPlane.U   => PixelFormat.Cb8,
					WebpPlane.V   => PixelFormat.Cr8,
					WebpPlane.Bgr => PixelFormat.Bgr24,
					_             => PixelFormat.Bgra32
				};
				Width = plane is WebpPlane.U or WebpPlane.V ? (frame.features.width + 1) >> 1 : frame.features.width;
				Height = plane is WebpPlane.U or WebpPlane.V ? (frame.features.height + 1) >> 1 : frame.features.height;
			}

			protected override void CopyPixelsInternal(in PixelArea prc, int cbStride, int cbBufferSize, byte* pbBuffer)
			{
				frame.ensureDecoded(plane);
				ref var decoded = ref frame.buffer;

				byte* pixels;
				int stride;
				if (plane is WebpPlane.Bgr or WebpPlane.Bgra)
				{
					var data = decoded.u.RGBA;
					pixels = data.rgba;
					stride = data.stride;
				}
				else
				{
					var data = decoded.u.YUVA;
					if (plane is WebpPlane.U)
					{
						pixels = data.u;
						stride = data.u_stride;
					}
					else if (plane is WebpPlane.V)
					{
						pixels = data.v;
						stride = data.v_stride;
					}
					else
					{
						pixels = data.y;
						stride = data.y_stride;
					}
				}

				int bpp = Format.BytesPerPixel;

				for (int i = 0; i < prc.Height; i++)
					Buffer.MemoryCopy(pixels + (prc.Y + i) * stride + prc.X * bpp, pbBuffer + i * cbStride, cbStride, prc.Width * bpp);
			}

			public override string ToString() => nameof(WebpFrame);
		}
	}

	private sealed class WebpRgbFrame : WebpFrame
	{
		private readonly WebpPlane plane;
		private PixelSource pixsrc;

		public override IPixelSource PixelSource => pixsrc ??= new WebpDecBufferPixelSource(this, plane);

		public WebpRgbFrame(WebpContainer cont, WebPBitstreamFeatures feat, int index, bool alpha) : base(cont, feat, index) => plane = alpha ? WebpPlane.Bgra : WebpPlane.Bgr;
	}

	private sealed class WebpYuvFrame : WebpFrame, IYccImageFrame
	{
		// WebP uses a non-standard matrix close to Rec.601 but rounded strangely
		// https://chromium.googlesource.com/webm/libwebp/+/refs/tags/v1.2.2/src/dsp/yuv.h
		private static readonly Matrix4x4 yccMatrix = new() {
			M11 =  0.2992f,
			M21 =  0.5874f,
			M31 =  0.1141f,
			M12 = -0.1688f,
			M22 = -0.3315f,
			M32 =  0.5003f,
			M13 =  0.5003f,
			M23 = -0.4189f,
			M33 = -0.0814f,
			M44 = 1
		};

		private PixelSource ysrc, usrc, vsrc;

		public ChromaPosition ChromaPosition => ChromaPosition.InterstitialHorizontal | ChromaPosition.CositedVertical;
		public Matrix4x4 RgbYccMatrix => yccMatrix;
		public bool IsFullRange => false;

		public override IPixelSource PixelSource => ysrc ??= new WebpDecBufferPixelSource(this, WebpPlane.Y);
		public IPixelSource PixelSourceCb => usrc ??= new WebpDecBufferPixelSource(this, WebpPlane.U);
		public IPixelSource PixelSourceCr => vsrc ??= new WebpDecBufferPixelSource(this, WebpPlane.V);

		public WebpYuvFrame(WebpContainer cont, WebPBitstreamFeatures feat, int index) : base(cont, feat, index) { }
	}
}