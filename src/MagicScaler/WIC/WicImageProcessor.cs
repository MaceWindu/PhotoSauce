﻿using System;
using System.IO;

using PhotoSauce.MagicScaler.Interop;

namespace PhotoSauce.MagicScaler
{
	public static class WicImageProcessor
	{
		public static void ProcessImage(string imgPath, Stream outStream, ProcessImageSettings settings)
		{
			using (var ctx = new WicProcessingContext(settings))
			using (var dec = new WicDecoder(imgPath, ctx))
				processImage(dec, ctx, outStream);
		}

		public static void ProcessImage(ArraySegment<byte> imgBuffer, Stream outStream, ProcessImageSettings settings)
		{
			using (var ctx = new WicProcessingContext(settings))
			using (var dec = new WicDecoder(imgBuffer, ctx))
				processImage(dec, ctx, outStream);
		}

		public static void ProcessImage(Stream imgStream, Stream outStream, ProcessImageSettings settings)
		{
			using (var ctx = new WicProcessingContext(settings))
			using (var dec = new WicDecoder(imgStream, ctx))
				processImage(dec, ctx, outStream);
		}

		private static void processImage(WicDecoder dec, WicProcessingContext ctx, Stream ostm)
		{
			using (var frm = new WicFrameReader(dec, ctx))
			using (var met = new WicMetadataReader(frm))
			{
				if (!ctx.Settings.Normalized)
					ctx.Settings.Fixup((int)ctx.Width, (int)ctx.Height, ctx.IsRotated90);

				using (var qsc = new WicNativeScaler(met))
				using (var rot = new WicExifRotator(qsc))
				using (var cac = new WicConditionalCache(rot))
				using (var crp = new WicCropper(cac))
				using (var pix = new WicPixelFormatConverter(crp))
				using (var cmy = new WicCmykConverter(pix))
				using (var res = new WicScaler(cmy))
				using (var csc = new WicColorspaceConverter(res))
				using (var mat = new WicMatteTransform(csc))
				using (var pal = new WicPaletizer(mat))
				using (var enc = new WicEncoder(ostm.AsIStream(), ctx))
					enc.WriteSource(pal);
			}
		}
	}
}