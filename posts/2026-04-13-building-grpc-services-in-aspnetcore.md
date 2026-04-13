---
title: Building gRPC Services in ASP.NET Core
date: 2026-04-13
tags: dotnet, aspnetcore, grpc, tutorial
image: building-grpc-services-in-aspnetcore.png
---

If you've built microservices with REST you've probably hit a point where JSON over HTTP starts feeling a bit heavy — verbose payloads, no shared contract, no streaming. gRPC fixes most of that. It's a high-performance RPC framework from Google, built on HTTP/2, and it has first-class support in ASP.NET Core.

The short version: you define your service in a `.proto` file, the toolchain generates the C# boilerplate, and you end up with a strongly typed client and server that serialize with Protocol Buffers — a compact binary format that's typically 3–10× smaller than equivalent JSON.

## Setting Up the Server Project

Start with an empty ASP.NET Core project and add the gRPC server package:

```bash
dotnet new web -n GrpcDemo.Server
cd GrpcDemo.Server
dotnet add package Grpc.AspNetCore
```

Wire up gRPC in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<GreeterService>();
app.Run();
```

That's all the plumbing on the server side. Now you need to describe what the service actually does.

## Defining a Service with Protocol Buffers

Create a folder called `Protos/` and add a file `greeter.proto`:

```proto
syntax = "proto3";

option csharp_namespace = "GrpcDemo.Server";

package greeter;

service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}

message HelloRequest {
  string name = 1;
}

message HelloReply {
  string message = 1;
}
```

Protocol Buffers use field numbers (the `= 1`, `= 2` etc.) instead of field names on the wire, which is why they're so compact. The `syntax = "proto3"` line means all fields are optional — if a field is missing from the wire, it gets its zero value in C#.

Tell the `.csproj` about the proto file:

```xml
<ItemGroup>
  <Protobuf Include="Protos/greeter.proto" GrpcServices="Server" />
</ItemGroup>
```

Build once and the toolchain generates `Greeter.GreeterBase` (and message types) for you. No hand-rolled DTOs.

## Implementing the Service

Create `GreeterService.cs` and extend the generated base class:

```csharp
using Grpc.Core;

public class GreeterService : Greeter.GreeterBase
{
    private readonly ILogger<GreeterService> _logger;

    public GreeterService(ILogger<GreeterService> logger)
    {
        _logger = logger;
    }

    public override Task<HelloReply> SayHello(
        HelloRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("Saying hello to {Name}", request.Name);

        return Task.FromResult(new HelloReply
        {
            Message = $"Hello, {request.Name}!"
        });
    }
}
```

Normal ASP.NET Core dependency injection works here — `ILogger`, `DbContext`, whatever you've registered. `ServerCallContext` gives you access to request metadata, cancellation tokens, and the ability to set response headers.

## Creating a Client

Add a separate console project and reference the same proto file, this time for the client role:

```bash
dotnet new console -n GrpcDemo.Client
dotnet add package Grpc.Net.Client
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
```

In `GrpcDemo.Client.csproj`:

```xml
<ItemGroup>
  <Protobuf Include="../GrpcDemo.Server/Protos/greeter.proto" GrpcServices="Client" />
</ItemGroup>
```

Setting `GrpcServices="Client"` generates only the client stub, not the server base class. Then in `Program.cs`:

```csharp
using Grpc.Net.Client;
using GrpcDemo.Server;

using var channel = GrpcChannel.ForAddress("https://localhost:7042");
var client = new Greeter.GreeterClient(channel);

var reply = await client.SayHelloAsync(new HelloRequest { Name = "world" });
Console.WriteLine(reply.Message); // Hello, world!
```

The channel is cheap to reuse across calls — create it once at startup, keep it alive for the life of the application. In an ASP.NET Core app, register it as a typed `HttpClient` via `AddGrpcClient<T>()` instead.

## Error Handling

gRPC has its own status code system instead of HTTP status codes. If something goes wrong, throw an `RpcException`:

```csharp
public override Task<HelloReply> SayHello(
    HelloRequest request,
    ServerCallContext context)
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        throw new RpcException(new Status(
            StatusCode.InvalidArgument,
            "Name must not be empty"));
    }

    return Task.FromResult(new HelloReply { Message = $"Hello, {request.Name}!" });
}
```

On the client, catch it like this:

```csharp
try
{
    var reply = await client.SayHelloAsync(new HelloRequest { Name = "" });
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
{
    Console.WriteLine($"Bad request: {ex.Status.Detail}");
}
```

The common status codes you'll reach for: `NotFound`, `InvalidArgument`, `Unauthenticated`, `PermissionDenied`, `Internal`, `Unavailable`, and `DeadlineExceeded`.

## Deadlines

Every gRPC call can carry a deadline — a wall-clock time by which the call must complete. If it doesn't, both sides get a `DeadlineExceeded` error and the operation is cancelled. It's cleaner than timeouts because the deadline travels with the call across service boundaries.

```csharp
var deadline = DateTime.UtcNow.AddSeconds(5);

var reply = await client.SayHelloAsync(
    new HelloRequest { Name = "world" },
    deadline: deadline);
```

On the server, `context.CancellationToken` is triggered when the deadline passes, so you can pass it to database queries, `HttpClient` calls, and anything else that accepts a `CancellationToken`:

```csharp
public override async Task<HelloReply> SayHello(
    HelloRequest request,
    ServerCallContext context)
{
    var result = await _repository.FindAsync(
        request.Name,
        context.CancellationToken);

    return new HelloReply { Message = $"Hello, {result.DisplayName}!" };
}
```

## Server Streaming

So far everything has been unary — one request, one response. gRPC also supports three streaming modes. Server streaming is the most common: the client sends one request and the server sends back a sequence of responses.

Define it in the proto file:

```proto
service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
  rpc StreamHellos (HelloRequest) returns (stream HelloReply);
}
```

The `stream` keyword before the return type is all it takes. Implement it on the server:

```csharp
public override async Task StreamHellos(
    HelloRequest request,
    IServerStreamWriter<HelloReply> responseStream,
    ServerCallContext context)
{
    foreach (var greeting in new[] { "Hello", "Hi", "Hey", "Greetings" })
    {
        if (context.CancellationToken.IsCancellationRequested) break;

        await responseStream.WriteAsync(new HelloReply
        {
            Message = $"{greeting}, {request.Name}!"
        });

        await Task.Delay(500, context.CancellationToken);
    }
}
```

Consume it on the client:

```csharp
using var call = client.StreamHellos(new HelloRequest { Name = "world" });

await foreach (var reply in call.ResponseStream.ReadAllAsync())
{
    Console.WriteLine(reply.Message);
}
```

The `ReadAllAsync()` extension turns the gRPC response stream into an `IAsyncEnumerable<T>`, which pairs naturally with `await foreach` and works with cancellation.

## Using AddGrpcClient in ASP.NET Core

If your client is itself an ASP.NET Core app, skip the manual `GrpcChannel` and use the built-in integration:

```csharp
builder.Services.AddGrpcClient<Greeter.GreeterClient>(options =>
{
    options.Address = new Uri("https://grpc-server:7042");
});
```

This wires up the client with the same `IHttpClientFactory` pipeline — you get retry policies, logging, and `HttpMessageHandler` middleware for free. Inject `Greeter.GreeterClient` wherever you need it, just like any other service.

## Wrapping Up

gRPC's sweet spot is internal service-to-service communication where you control both ends — microservices, backend-for-frontend calls, data pipelines. You get a strict contract enforced at compile time, compact binary serialization, HTTP/2 multiplexing, and first-class streaming without any extra libraries.

The `.proto`-first workflow takes a few minutes to get comfortable with, but once it clicks you'll wonder why you were hand-rolling REST contracts. If you're already running ASP.NET Core services, the migration cost is low — add two packages, write a `.proto` file, and replace your `HttpClient` calls with a generated client.
