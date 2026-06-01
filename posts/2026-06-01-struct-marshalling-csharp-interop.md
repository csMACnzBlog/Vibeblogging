---
title: Struct Marshalling in C# Interop
date: 2026-06-01
tags: csharp, dotnet, interop, performance
image: struct-marshalling-csharp-interop.png
---

If you've ever called native code from C#, you've probably had that "wait, why is this crashing?" moment.

Most of the time, the bug isn't your business logic. It's marshalling. The managed and unmanaged worlds disagree about memory layout, string encoding, or who owns a pointer, and everything gets weird fast.

Here's a practical way to stay out of trouble.

## Start with blittable structs whenever you can

A blittable type has the same binary representation in managed and unmanaged memory. That's the happy path because the runtime can pass it across the boundary without expensive conversions.

```csharp
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct Point2D
{
    public int X;
    public int Y;
}

internal static class NativeMethods
{
    [LibraryImport("native", EntryPoint = "distance_squared")]
    public static partial int DistanceSquared(Point2D a, Point2D b);
}
```

`int`, `double`, and fixed-size numeric fields are usually safe choices. Trouble starts when we add references (`string`, arrays, `object`) without being explicit.

## Be explicit about layout

Never assume native layout rules match C# defaults. Declare intent with `StructLayout`.

```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PacketHeader
{
    public byte Version;
    public byte Flags;
    public ushort PayloadLength;
}
```

`Pack = 1` can be necessary when native code expects byte-packed fields. But don't apply it blindly — packed structs can hurt alignment and performance on some platforms.

When you need exact offsets, use `LayoutKind.Explicit`:

```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit)]
public struct Color32
{
    [FieldOffset(0)] public byte R;
    [FieldOffset(1)] public byte G;
    [FieldOffset(2)] public byte B;
    [FieldOffset(3)] public byte A;

    [FieldOffset(0)] public uint Rgba;
}
```

That's great for overlaying different views of the same bytes.

## Strings need a conscious decision

String marshalling is one of the easiest places to get subtle bugs. Decide encoding up front.

```csharp
using System.Runtime.InteropServices;

internal static partial class NativeMethods
{
    [LibraryImport("native", EntryPoint = "set_name", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetName(string name);
}
```

If your native API expects UTF-8, say UTF-8. If it expects UTF-16 (`wchar_t*` on Windows), model that intentionally. Ambiguity is where mojibake and truncation sneak in.

## Validate your struct size in tests

A tiny guard test can save hours of debugging later.

```csharp
using System.Runtime.InteropServices;
using Xunit;

public class InteropLayoutTests
{
    [Fact]
    public void PacketHeader_HasExpectedSize()
    {
        Assert.Equal(4, Marshal.SizeOf<PacketHeader>());
    }
}
```

If someone later adds a field or changes packing, you'll catch it before shipping.

## Pinning for buffer interop

When native code needs a pointer to managed data, pin it so the GC doesn't move it mid-call.

```csharp
using System;
using System.Runtime.InteropServices;

internal static unsafe partial class NativeMethods
{
    [LibraryImport("native", EntryPoint = "checksum")]
    public static partial uint Checksum(byte* data, nuint length);
}

public static unsafe uint ComputeChecksum(ReadOnlySpan<byte> payload)
{
    fixed (byte* ptr = payload)
    {
        return NativeMethods.Checksum(ptr, (nuint)payload.Length);
    }
}
```

That `fixed` block creates a safe window for native access without copying.

## Quick checklist I use in real projects

Before I ship interop code, I run through this:

- Is layout explicit (`Sequential`/`Explicit`) where needed?
- Do managed field sizes match native field sizes exactly?
- Is string encoding explicitly configured?
- Are buffers pinned for the full native call?
- Do I have at least one `Marshal.SizeOf<T>()` test for key structs?

Interop can feel scary at first, but it gets predictable once you treat boundaries as contracts. Be explicit, verify assumptions in tests, and you'll avoid most of the painful bugs before they ever happen.
