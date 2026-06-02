---
title: ArrayPool in C#: Reusing Buffers Wisely
date: 2026-06-02
tags: csharp, dotnet, performance, memory
image: arraypool-in-csharp-reusing-buffers-wisely.png
---

If you've ever profiled a hot path and seen allocation spikes from `new byte[...]`, you're not alone.

I hit this while parsing lots of small payloads in a loop. The code was simple and readable, but GC pressure kept showing up in traces. `ArrayPool<T>` ended up being one of the easiest wins: less garbage, steadier throughput, and no scary complexity once you know the guardrails.

## Why `ArrayPool<T>` helps

`ArrayPool<T>` lets you rent an array and return it when you're done, so buffers can be reused instead of constantly allocated.

```csharp
using System;
using System.Buffers;

public static class BufferWorker
{
    public static int CountDigits(ReadOnlySpan<char> input)
    {
        char[] rented = ArrayPool<char>.Shared.Rent(input.Length);

        try
        {
            input.CopyTo(rented);

            int count = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (char.IsDigit(rented[i]))
                {
                    count++;
                }
            }

            return count;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented, clearArray: false);
        }
    }
}
```

The key thing here is `try/finally`. If you rent, you must return — even when something throws.

## Remember: rented arrays can be bigger than requested

This catches people all the time (including me the first time).

```csharp
byte[] rented = ArrayPool<byte>.Shared.Rent(1000);
Console.WriteLine(rented.Length); // Could be 1024, 2048, etc.
```

So track your *logical length* separately and only process the portion you actually filled.

```csharp
using System;
using System.Buffers;

public static class PacketSerializer
{
    public static byte[] Serialize(ReadOnlySpan<byte> payload)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(payload.Length + 4);

        try
        {
            int written = 0;

            BitConverter.TryWriteBytes(rented.AsSpan(written, 4), payload.Length);
            written += 4;

            payload.CopyTo(rented.AsSpan(written));
            written += payload.Length;

            return rented.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }
}
```

## When should you clear before returning?

`clearArray: false` is faster, but previous contents stay in the array until overwritten. That's usually fine for non-sensitive data. For secrets (tokens, passwords, keys), clear first.

```csharp
using System;
using System.Buffers;
using System.Security.Cryptography;

public static class SensitiveBufferExample
{
    public static void HashSecret(ReadOnlySpan<byte> secret)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(secret.Length);

        try
        {
            secret.CopyTo(rented);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(rented.AsSpan(0, secret.Length), hash);
        }
        finally
        {
            Array.Clear(rented, 0, secret.Length);
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }
}
```

I prefer explicit clearing of only the used segment, then returning with `clearArray: false`. It's clear (pun intended) and avoids clearing extra capacity you never touched.

## Practical rules I use

- Reach for `ArrayPool<T>` in hot paths where arrays are short-lived and frequent.
- Always return in `finally`.
- Never assume `rented.Length == requestedLength`.
- Treat rented buffers as temporary workspace, not values to store.
- Clear sensitive data before returning.

## Final thought

`ArrayPool<T>` isn't a silver bullet, and I wouldn't sprinkle it everywhere. But in tight loops and high-throughput code, it's one of those small changes that can noticeably smooth out GC behavior.

If your profiler keeps pointing at temporary array allocations, this is a great tool to try next.
