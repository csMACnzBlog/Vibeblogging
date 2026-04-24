---
title: TCR for Agentic Loops: Guiding LLM Agents
date: 2026-04-24
tags: testing, tdd, ai, architecture, tutorial
image: tcr-for-agentic-loops-guiding-llm-agents.png
---

The [TCR post](tcr-test-commit-revert-in-csharp.html) on this blog showed how `test && commit || revert` forces human developers to take smaller steps by making the machine enforce discipline. But what happens when the "developer" is an autonomous LLM agent running a coding loop? It turns out TCR isn't just compatible with agentic coding — it might be *more* natural for agents than for humans.

This post explores how to wire TCR's feedback loop into an agentic system to keep AI-driven implementation safe, incremental, and auditable.

## Recap: What TCR Does

TCR is the command `test && commit || revert`. After every small change, you run it. Tests pass? Your code is committed. Tests fail? Your changes are silently deleted. No exceptions.

```bash
dotnet test && git add -A && git commit -m "TCR" || git restore .
```

For humans, the hard part is psychological — losing code stings, and CTRL+Z is always tempting. The discipline has to be maintained consciously.

For an LLM agent, there is no psychological resistance. It doesn't feel sunk cost. It doesn't cling to code it spent "effort" writing. If you wire TCR directly into the agent's execution loop, the revert isn't a rule to follow — it's just what the environment does.

## Why Agentic Coding Needs Guardrails

LLM coding agents are powerful, but they have predictable failure modes. They hallucinate APIs that don't exist. They over-build — solving three adjacent problems when you asked for one. They can spin in loops, making changes without clear progress signals.

The deeper problem is what you might call "vibe coding": the agent accumulates a large context of partial changes, none of which have been verified, while the human waits and hopes the end state is coherent. By the time tests run (if they run at all), the agent has wandered far from where any individual step can be assessed.

Tests are the only objective signal that code *works*. Every other signal — the agent's confidence, the code compiling, the logic "looking right" — is subjective. TCR makes tests the arbiter of forward progress. If the tests pass, the change is real. If they don't, it's gone.

## Mapping TCR onto an Agentic Loop

The structure maps cleanly. Think of it as two nested loops:

**Outer loop** — an orchestrator (human or agent) provides one small requirement at a time. Not "build the shopping cart." Something like: "add an item to the cart and return the new total." One unit of behaviour.

**Inner loop** — the implementing agent follows the red-green cycle:
1. Write a failing test that captures the requirement
2. Write just enough implementation to make it pass
3. Run TCR: `test && commit || revert`
4. If the commit fires, the requirement is done. If the revert fires, start the implementation step again with a smaller change.

The commit and revert aren't suggestions — they're hard constraints imposed on the agent by the shell environment. The agent cannot accumulate broken state. Every committed state is green. If the agent can't get to green in a single step, it reverts and tries a smaller approach.

Think of TCR as the agent's "save point" discipline. Each commit is a stable checkpoint the orchestrator can inspect, build on, or roll back to.

## Proposed Architecture: Agents and Subagents

A practical agentic TCR setup has four components:

**Orchestrator agent**: Decomposes a feature into a list of small, testable units. One unit = one TCR cycle. The orchestrator hands off one unit at a time to the implementer and waits for a green commit before proceeding.

**Test-writer subagent**: Given a requirement, writes a single failing test and nothing else. Its tool permissions are restricted to reading existing source files and creating/editing test files. It cannot touch implementation code. This is the red phase.

**Implementer subagent**: Given the failing test, writes just enough implementation to make it pass. Its job ends when TCR fires a commit. If TCR reverts instead, the agent must try a smaller implementation. The implementer subagent does not write tests.

**TCR hook / verifier**: The shell-level enforcer. Runs `test && git commit -am "TCR" || git restore .` after every edit cycle. Neither the test-writer nor the implementer can bypass this — it runs at the environment level, not the agent level.

An optional **refactoring subagent** can run after a green commit if the code needs cleanup. It operates under the same TCR constraint: any refactoring that breaks tests gets reverted.

```
Orchestrator
    └── [for each requirement unit]
            ├── Test-writer subagent → writes failing test
            ├── Implementer subagent → writes implementation
            │       └── TCR hook fires
            │               ├── PASS → git commit → next unit
            │               └── FAIL → git restore → retry smaller
            └── (optional) Refactoring subagent → cleanup under TCR
```

## Agent Configuration for TCR Viability

The architecture only holds if the agents can't cheat. In Claude Code, you configure this through a combination of CLAUDE.md instructions, hooks, and subagent definitions.

### CLAUDE.md

The project-level `CLAUDE.md` file gives the agent its standing instructions:

```markdown
## TCR Discipline

- Only commit when all tests pass.
- If tests fail after your changes, run `git restore .` immediately.
- Never skip the test run. Never stash. Never amend.
- Your implementation is not done until TCR fires a commit.
```

Plain-language rules help, but they're not sufficient on their own.

### Hooks

Claude Code's hook system (`PreToolUse`, `PostToolUse`, `Stop`) can enforce TCR at the environment level. A `PostToolUse` hook on file writes can automatically trigger the TCR script:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit|MultiEdit",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet test && git add -A && git commit -m 'TCR' || git restore ."
          }
        ]
      }
    ]
  }
}
```

With this hook active, every file write triggers the TCR cycle automatically. The agent doesn't decide whether to run it — the environment runs it unconditionally.

### Subagent Definitions

Claude Code's `.claude/agents/` directory lets you define specialist subagents with restricted tools and custom prompts. An implementer subagent might look like:

```markdown
---
name: tcr-implementer
description: Writes implementation to pass a failing test, then triggers TCR.
tools: Read, Write, Edit, Bash(dotnet test), Bash(git add), Bash(git commit), Bash(git restore .)
---

You are given a failing test. Your job is to write the minimum implementation to make it pass.

Rules:
- Do not modify test files.
- Run `dotnet test && git add -A && git commit -m 'TCR' || git restore .` after every implementation attempt.
- If TCR reverts your changes, try a smaller implementation.
- Stop when a commit fires.
```

The `tools` field restricts what Bash commands the subagent can run. `Bash(git stash)` is not in the list — the agent cannot stash its way around a revert.

### Permissions

In `claude_settings.json`, you lock down the relevant git operations:

```json
{
  "permissions": {
    "allow": [
      "Bash(git commit*)",
      "Bash(git restore .)",
      "Bash(git add*)",
      "Bash(dotnet test*)"
    ],
    "deny": [
      "Bash(git stash*)",
      "Bash(git reset*)",
      "Bash(git push*)"
    ]
  }
}
```

Denying `git stash` and `git reset` closes the most obvious escape hatches.

## Challenges and Design Considerations

**Preventing the agent from cheating.** The human equivalent of CTRL+Z is the agent deleting a test or writing a trivially passing assertion like `Assert.True(true)`. CLAUDE.md rules, restricted tool permissions, and (if you're paranoid) a pre-commit hook that validates test quality can all help. The key insight: if the agent can't stash, reset, or amend, its options narrow considerably.

**Fast test suites are non-negotiable.** If `dotnet test` takes two minutes, the agent burns through tokens waiting for feedback. TCR only works when the test cycle is fast — under 10 seconds ideally. Design your test architecture for speed from the start. Unit tests only at the inner loop level; integration tests in a separate suite outside the TCR cycle.

**Context window management.** Each TCR cycle produces a git commit and potentially a new iteration. Across many cycles, the agent's context window fills with accumulated tool calls and outputs. Designing the subagents with tight `maxTurns` limits and summarising completed cycles before handing off to the next subagent keeps the context clean.

**When to escalate vs. revert.** TCR's answer to "I can't make this work" is simple: revert and try smaller. But an agent needs to know when it's stuck in a loop rather than making progress. A sensible rule: after three consecutive reverts on the same requirement, escalate to the orchestrator rather than retrying indefinitely. The orchestrator can break the unit down further.

**Balancing autonomy with safety.** In an attended session, a human can interrupt if the agent goes off course. In unattended runs (overnight coding sessions, CI-triggered agents), the TCR loop acts as the safety net. Every commit is inspectable. Every revert is logged. The worst case is a session that produces fewer commits than expected — not a session that corrupts your codebase.

## Experimentation Prototype

The minimal viable setup is a single agent with TCR enforced via a `PostToolUse` hook. No subagents, no orchestrator — just one agent that follows TCR discipline.

The best kata to start with is **FizzBuzz** (if you've read the [TCR in C# post](tcr-test-commit-revert-in-csharp.html), you already know the steps) or **String Calculator** (incrementally adding arithmetic operations one at a time). Both have small, well-defined increments that map naturally onto TCR cycles.

For a more interesting test of the architecture, try the **Bowling Game kata** or **Mars Rover** — problems with enough complexity to see the orchestrator→test-writer→implementer loop in action across multiple cycles.

Success metrics to track:

| Metric | What it tells you |
|---|---|
| Commit frequency | How often TCR fires a commit vs. a revert |
| Revert rate | What fraction of attempts fail — lower is better |
| Test coverage delta | Coverage gained per commit — should be non-zero |
| Turns per commit | How many agent turns before TCR fires |

If the revert rate is high, the requirement units are too large. Break them down further. If turns-per-commit is high, the agent is searching too broadly — tighter subagent instructions help.

## Takeaways

TCR for agentic loops isn't a theoretical curiosity — it's a natural fit. The discipline TCR imposes on humans by removing CTRL+Z is exactly the constraint an agent needs imposed on it by its environment. An agent with no ego, no fatigue, and no temptation to preserve sunk-cost work is exactly the kind of "developer" TCR was designed for.

The structured loop — orchestrator decomposes, test-writer goes red, implementer goes green, TCR commits — produces something valuable: an always-green commit history with high test coverage by design, generated autonomously.

Key takeaways:

- **TCR maps directly onto the agentic TDD loop**: outer orchestrator loop, inner TCR cycle per requirement unit.
- The revert isn't a rule for the agent to follow — wire it into the environment via hooks so it's unconditional.
- Restrict `git stash` and `git reset` in agent permissions to close the obvious escape hatches.
- **Fast test suites are essential** — slow tests stall the loop and waste tokens.
- Start with FizzBuzz or String Calculator to prototype the loop before tackling complex katas.
- TCR-driven agentic sessions produce always-green histories and measurable coverage gains — exactly the audit trail you want from an autonomous coding session.
