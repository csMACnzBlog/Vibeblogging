---
title: Composition Over Inheritance - Flexible Systems
date: 2026-02-27
tags: csharp, design-patterns, architecture, composition, best-practices
image: composition-over-inheritance-building-flexible-systems.png
---

Remember when we kicked off this [design patterns series](design-patterns-series-composition-over-complexity.html)? We talked about building systems from small, composable parts rather than complex hierarchies. Today we're diving into one of the most important principles in that toolkit: composition over inheritance.

If you read yesterday's [SOLID Principles post](solid-principles-foundation-of-good-design.html), you'll recognize this fits right in with the Open/Closed Principle and Dependency Inversion. Those principles give us the "why" – this post gives us the "how."

## The Inheritance Trap

Let's start with a problem you've probably hit before. You're building a game, and you need different types of enemies. Inheritance seems perfect, right?

```csharp
public class Enemy
{
    public int Health { get; set; }
    public virtual void Attack() => Console.WriteLine("Generic attack!");
    public virtual void Move() => Console.WriteLine("Moving...");
}

public class FlyingEnemy : Enemy
{
    public override void Move() => Console.WriteLine("Flying through the air!");
}

public class ShootingEnemy : Enemy
{
    public override void Attack() => Console.WriteLine("Shooting projectile!");
}
```

Looks fine so far. But here's where it gets messy. What if you need a flying enemy that shoots? You can't inherit from both `FlyingEnemy` and `ShootingEnemy` (C# doesn't do multiple inheritance). You could create `FlyingShootingEnemy`, but that's just the start of your problems.

Now you need a swimming enemy. And a teleporting one. And one that shoots AND teleports AND flies. Your class hierarchy explodes into dozens of combinations, each one a new class. That's the fragile base class problem in action.

```csharp
// This gets out of hand fast
public class FlyingShootingEnemy : Enemy { }
public class FlyingTeleportingEnemy : Enemy { }
public class ShootingTeleportingEnemy : Enemy { }
public class FlyingShootingTeleportingEnemy : Enemy { }
// And on and on...
```

## Enter Composition

Here's the key insight: instead of making enemies *be* things through inheritance, let's make them *have* things through composition. We'll break behaviors into small, focused components.

```csharp
// Small, focused interfaces
public interface IMovementBehavior
{
    void Move();
}

public interface IAttackBehavior
{
    void Attack();
}

// Concrete implementations
public class FlyMovement : IMovementBehavior
{
    public void Move() => Console.WriteLine("Flying through the air!");
}

public class WalkMovement : IMovementBehavior
{
    public void Move() => Console.WriteLine("Walking on the ground");
}

public class ProjectileAttack : IAttackBehavior
{
    public void Attack() => Console.WriteLine("Shooting projectile!");
}

public class MeleeAttack : IAttackBehavior
{
    public void Attack() => Console.WriteLine("Swinging weapon!");
}
```

Now our enemy just holds references to these behaviors:

```csharp
public class Enemy
{
    private readonly IMovementBehavior _movement;
    private readonly IAttackBehavior _attack;
    
    public int Health { get; set; }
    
    public Enemy(IMovementBehavior movement, IAttackBehavior attack)
    {
        _movement = movement;
        _attack = attack;
    }
    
    public void Move() => _movement.Move();
    public void Attack() => _attack.Attack();
}
```

Look at what we can do now:

```csharp
// Create any combination without new classes
var flyingShooter = new Enemy(new FlyMovement(), new ProjectileAttack());
var walkingBrawler = new Enemy(new WalkMovement(), new MeleeAttack());
var flyingBrawler = new Enemy(new FlyMovement(), new MeleeAttack());

flyingShooter.Move();    // "Flying through the air!"
flyingShooter.Attack();  // "Shooting projectile!"
```

Want to add swimming? Just create a `SwimMovement` class. Want enemies that can do multiple attacks? Add a `ComboAttack` class. No inheritance hierarchy to maintain, no exponential class explosion.

## Real-World Example: Document Processors

Let's look at something you'd actually build at work. You need to process documents – reading from different sources, transforming them, and writing to different destinations.

**The inheritance approach:**

```csharp
public abstract class DocumentProcessor
{
    public abstract string Read();
    public abstract string Transform(string content);
    public abstract void Write(string content);
    
    public void Process()
    {
        var content = Read();
        var transformed = Transform(content);
        Write(transformed);
    }
}

public class FileToUpperCaseToDatabase : DocumentProcessor
{
    public override string Read() => File.ReadAllText("input.txt");
    public override string Transform(string content) => content.ToUpper();
    public override void Write(string content) 
        => SaveToDatabase(content);
}

public class ApiToLowerCaseToFile : DocumentProcessor
{
    public override string Read() => FetchFromApi();
    public override string Transform(string content) => content.ToLower();
    public override void Write(string content) 
        => File.WriteAllText("output.txt", content);
}
```

You can see the problem already. Every combination of source, transformation, and destination needs its own class. That's 3 sources × 5 transformations × 3 destinations = 45 classes. Yikes.

**The composition approach:**

```csharp
public interface IReader
{
    string Read();
}

public interface ITransformer
{
    string Transform(string content);
}

public interface IWriter
{
    void Write(string content);
}

// Concrete implementations
public class FileReader : IReader
{
    private readonly string _path;
    public FileReader(string path) => _path = path;
    public string Read() => File.ReadAllText(_path);
}

public class ApiReader : IReader
{
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;
    public ApiReader(string endpoint, HttpClient httpClient)
    {
        _endpoint = endpoint;
        _httpClient = httpClient;
    }
    public string Read() 
        => _httpClient.GetStringAsync(_endpoint).Result; // In production, use async/await
}

public class UpperCaseTransformer : ITransformer
{
    public string Transform(string content) => content.ToUpper();
}

public class MarkdownToHtmlTransformer : ITransformer
{
    public string Transform(string content) 
        => Markdig.Markdown.ToHtml(content);
}

public class FileWriter : IWriter
{
    private readonly string _path;
    public FileWriter(string path) => _path = path;
    public void Write(string content) 
        => File.WriteAllText(_path, content);
}

public class DatabaseWriter : IWriter
{
    public void Write(string content) 
        => SaveToDatabase(content); // Pseudocode - implement based on your DB
}
```

Now we have one processor class that works with any combination:

```csharp
public class DocumentProcessor
{
    private readonly IReader _reader;
    private readonly ITransformer _transformer;
    private readonly IWriter _writer;
    
    public DocumentProcessor(
        IReader reader, 
        ITransformer transformer, 
        IWriter writer)
    {
        _reader = reader;
        _transformer = transformer;
        _writer = writer;
    }
    
    public void Process()
    {
        var content = _reader.Read();
        var transformed = _transformer.Transform(content);
        _writer.Write(transformed);
    }
}

// Use it
var processor = new DocumentProcessor(
    new FileReader("input.txt"),
    new MarkdownToHtmlTransformer(),
    new DatabaseWriter()
);

processor.Process();
```

That's 3 + 5 + 3 = 11 classes instead of 45. And adding a new reader, transformer, or writer doesn't require touching any existing code (hello, Open/Closed Principle!).

## Chaining Behaviors

Composition really shines when you need to combine behaviors. Let's say you want to apply multiple transformations:

```csharp
public class ChainedTransformer : ITransformer
{
    private readonly IEnumerable<ITransformer> _transformers;
    
    public ChainedTransformer(params ITransformer[] transformers)
    {
        _transformers = transformers;
    }
    
    public string Transform(string content)
    {
        var result = content;
        foreach (var transformer in _transformers)
        {
            result = transformer.Transform(result);
        }
        return result;
    }
}

// Use it
var processor = new DocumentProcessor(
    new FileReader("input.md"),
    new ChainedTransformer(
        new MarkdownToHtmlTransformer(),
        new AddMetadataTransformer(),
        new SanitizeHtmlTransformer()
    ),
    new FileWriter("output.html")
);
```

Try doing that with inheritance. You'd need a `MarkdownToHtmlWithMetadataAndSanitization` class, and another for every other combination. With composition, you just stack them up like Lego blocks.

## Runtime Flexibility

Here's something inheritance can't do at all: changing behavior at runtime. With composition, it's trivial:

```csharp
public class ConfigurableEnemy
{
    private IMovementBehavior _movement;
    private IAttackBehavior _attack;
    
    public ConfigurableEnemy(
        IMovementBehavior movement, 
        IAttackBehavior attack)
    {
        _movement = movement;
        _attack = attack;
    }
    
    public void SetMovement(IMovementBehavior movement) 
        => _movement = movement;
    
    public void SetAttack(IAttackBehavior attack) 
        => _attack = attack;
    
    public void Move() => _movement.Move();
    public void Attack() => _attack.Attack();
}

// In use
var enemy = new ConfigurableEnemy(
    new WalkMovement(), 
    new MeleeAttack()
);

enemy.Move();  // Walking

// Enemy picks up a jetpack!
enemy.SetMovement(new FlyMovement());
enemy.Move();  // Flying

// Enemy finds a gun!
enemy.SetAttack(new ProjectileAttack());
enemy.Attack();  // Shooting
```

Your game objects can evolve based on player actions, enemy states, or whatever your game logic needs. You can't change your inheritance hierarchy at runtime – once a `WalkingEnemy` is instantiated, it walks forever.

## When to Use Each

So should you never use inheritance? Not quite. Inheritance still has its place:

**Use inheritance when:**
- You have a true "is-a" relationship (a `Dog` is an `Animal`)
- You're modeling a domain where the hierarchy is stable and well-understood
- Subclasses genuinely add functionality without breaking parent behavior
- You need polymorphism and all subclasses follow the Liskov Substitution Principle

**Use composition when:**
- You need to combine behaviors in different ways
- Behaviors might change at runtime
- You're seeing lots of similar classes with small differences
- Your inheritance tree is getting deep or wide
- You find yourself thinking "I need multiple inheritance here"

In practice, you'll often use both. You might have a base `Enemy` class that provides common functionality (health, position), but compose in behaviors for movement and attacks. That's perfectly fine – composition and inheritance aren't enemies, they're complementary tools.

## The Real Benefits

Let me tell you what this means for your day-to-day coding life.

**Testing gets easier.** You can test each behavior in isolation. Want to verify your `ProjectileAttack` logic? Instantiate it directly – no need for a full enemy setup. Mock interfaces trivially for unit tests.

**Changes stay localized.** Need to fix a bug in flying movement? Change `FlyMovement`. Every enemy using it gets the fix automatically. With inheritance, you might need to check if subclasses override your buggy method.

**Understanding is simpler.** When you see `new Enemy(new FlyMovement(), new ProjectileAttack())`, you know exactly what that enemy does. With inheritance, you need to trace through the class hierarchy to understand all the behaviors.

**Reuse becomes natural.** That `FileReader` you wrote for documents? It'll work just fine in your log processing pipeline. That `UpperCaseTransformer`? Perfect for your email template system. Small, focused classes naturally reuse better than inheritance hierarchies.

## Wrapping Up

Composition over inheritance isn't about rejecting inheritance entirely – it's about reaching for the right tool. When you find yourself building complex hierarchies or wishing for multiple inheritance, that's your signal to compose instead.

Build small parts that do one thing well. Combine them to create complex behaviors. Your future self (and your teammates) will thank you when the requirements change and you can swap out one component instead of refactoring an entire class hierarchy.

Next up in this series: we'll look at the Strategy Pattern, which is composition in action with a focus on interchangeable algorithms. You'll see how the patterns we've built today formalize into a named pattern you can reach for every time you need pluggable behavior.

Until then, go compose something beautiful!
