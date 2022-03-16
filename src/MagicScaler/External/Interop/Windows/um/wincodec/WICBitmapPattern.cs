// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT).
// See third-party-notices in the repository root for more information.

// Ported from um/wincodec.h in the Windows SDK for Windows 10.0.22000.0
// Original source is Copyright © Microsoft. All rights reserved.

namespace TerraFX.Interop.Windows;

internal unsafe partial struct WICBitmapPattern
{
    public ULARGE_INTEGER Position;

    [NativeTypeName("ULONG")]
    public uint Length;

    public byte* Pattern;

    public byte* Mask;

    public BOOL EndOfStream;
}
