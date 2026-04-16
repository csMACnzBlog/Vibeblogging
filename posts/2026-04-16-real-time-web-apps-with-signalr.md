---
title: Real-Time Web Apps with SignalR
date: 2026-04-16
tags: aspnetcore, dotnet, csharp, signalr, tutorial
image: real-time-web-apps-with-signalr.png
---

Most web apps follow a simple pattern: the client asks, the server answers. That works great for fetching a list of orders or loading a profile page. But what about a live dashboard, a chat app, or notifications that appear the moment something happens on the server?

That's where SignalR comes in. It gives you real-time, two-way communication between your ASP.NET Core server and connected clients — without polling every few seconds.

## What SignalR Actually Does

SignalR abstracts over several transport mechanisms (WebSockets, Server-Sent Events, long polling) and automatically picks the best one available. You write one API, and SignalR handles the messy negotiation behind the scenes.

The core concept is a **Hub** — a class on the server that connected clients can call, and that can push messages back to clients.

## Setting Up the Hub

Install the package (it's part of `Microsoft.AspNetCore.SignalR` which ships with ASP.NET Core, so no extra package is needed):

```csharp
using Microsoft.AspNetCore.SignalR;

public class NotificationsHub : Hub
{
    public async Task SendNotification(string message)
    {
        await Clients.All.SendAsync("ReceiveNotification", message);
    }
}
```

That's the whole hub. When a client calls `SendNotification`, the server broadcasts a `ReceiveNotification` event to every connected client.

Register and map it in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.MapHub<NotificationsHub>("/notifications");

app.Run();
```

## Connecting from the Browser

You'll need the SignalR JavaScript client. The simplest way is a CDN reference:

```html
<script src="https://unpkg.com/@microsoft/signalr@latest/dist/browser/signalr.min.js"></script>
```

Then wire it up:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/notifications")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveNotification", (message) => {
    const li = document.createElement("li");
    li.textContent = message;
    document.getElementById("messages").appendChild(li);
});

await connection.start();
```

`withAutomaticReconnect()` handles dropped connections gracefully — it retries with backoff instead of just dying. Always include it.

## Pushing from the Server

The really useful pattern isn't clients talking to each other — it's the *server* pushing updates to clients. You can do that by injecting `IHubContext<T>` anywhere in your app:

```csharp
public class OrderService
{
    private readonly IHubContext<NotificationsHub> _hubContext;

    public OrderService(IHubContext<NotificationsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PlaceOrderAsync(Order order)
    {
        // ... business logic ...

        await _hubContext.Clients.All.SendAsync(
            "ReceiveNotification",
            $"New order placed: {order.Id}");
    }
}
```

This is a clean way to add real-time notifications without coupling your domain logic to the SignalR hub directly.

## Targeting Specific Clients with Groups

Sending to everyone is fine for global alerts. But for things like per-user notifications or room-based chat, you want groups.

Add a client to a group when they connect:

```csharp
public class ChatHub : Hub
{
    public async Task JoinRoom(string roomName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName)
            .SendAsync("UserJoined", Context.ConnectionId);
    }

    public async Task SendMessage(string roomName, string message)
    {
        await Clients.Group(roomName)
            .SendAsync("ReceiveMessage", Context.ConnectionId, message);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Groups are cleaned up automatically on disconnect
        await base.OnDisconnectedAsync(exception);
    }
}
```

Groups are ephemeral — they exist as long as at least one member is connected. No persistence or cleanup is needed.

## Sending to a Specific User

If you've configured authentication, SignalR can route messages to a specific user by their identity:

```csharp
await _hubContext.Clients.User(userId)
    .SendAsync("ReceiveNotification", "Your order shipped!");
```

This works because SignalR maps the `ClaimTypes.NameIdentifier` claim to connection IDs behind the scenes. Any time a user has multiple browser tabs open, all their connections receive the message.

## A Few Gotchas

**Scale-out needs a backplane.** If you're running multiple server instances, a message sent on instance A won't reach clients connected to instance B. You'll need a backplane — Redis is the most common:

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis("your-redis-connection-string");
```

**Strongly-typed hubs avoid typos.** Instead of stringly-typed `SendAsync("ReceiveMessage", ...)`, define an interface:

```csharp
public interface IChatClient
{
    Task ReceiveMessage(string user, string message);
}

public class ChatHub : Hub<IChatClient>
{
    public async Task SendMessage(string message)
    {
        await Clients.All.ReceiveMessage(Context.User!.Identity!.Name!, message);
    }
}
```

Compile-time safety for your event names and parameter types — worth the extra interface.

**Message size matters.** SignalR serializes messages as JSON by default. For high-frequency, high-volume scenarios, consider switching to MessagePack:

```csharp
builder.Services.AddSignalR()
    .AddMessagePackProtocol();
```

It's a binary format that cuts payload size significantly for numeric data.

## When to Use SignalR

SignalR is a good fit when:

- Updates need to arrive within a second or two of the event
- You're pushing from server to many clients (live feeds, dashboards)
- You need two-way communication (chat, collaborative editing)

It's probably overkill when:

- Polling every 30 seconds is fine for your use case
- You only need one-way pushes and Server-Sent Events are simpler
- The data changes rarely and users can just refresh

## Wrapping Up

SignalR gives you real-time communication in ASP.NET Core with minimal ceremony. The hub model is intuitive, the JavaScript client handles reconnection, and `IHubContext<T>` lets you push from anywhere in your app.

Start with a simple hub, get the basic push working, then add groups or typed hubs as your needs grow. The infrastructure is already there — you don't need to build any of it yourself.
