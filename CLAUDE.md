# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fable.Actor is a cross-platform actor library written in F# and compiled via [Fable](https://github.com/fable-compiler/Fable) to BEAM (Erlang), Python, and JavaScript. It provides typed actors with supervision, designed to be the foundation that Rx libraries (like AsyncRx) can build on.

## Build Commands

```sh
dotnet build src/Fable.Actor    # Type-check
dotnet fantomas src -r           # Format
```

## Architecture

One F# project: `src/Fable.Actor/`

### Core Files

1. **Types.fs** — `Actor<'Msg>`, `Next<'State>`, `ReplyChannel<'Reply>`, `ChildExited`
2. **Platform.fs** — `IActorPlatform` erased interface, `[<ImportAll("fable_actor_platform")>]`
3. **Actor.fs** — `actor { }` CE, `spawn`, `spawnLinked`, `start`, `send`, `call`, `receive`, `kill`, `trapExits`

### Core Types

- **Actor<'Msg>**: Typed wrapper around a platform-specific process identifier
- **ActorOp<'T>**: CPS-based computation — blocking receive on BEAM, async/await on Python, promise on JS
- **Next<'State>**: `Continue of 'State` | `Stop` — actor handler return type
- **ReplyChannel<'Reply>**: Callback for synchronous request-response via `call`
- **ChildExited**: Notification when a linked child actor dies

### Platform Interface

Each target provides a native `fable_actor_platform` module implementing `IActorPlatform`:
- BEAM: `fable_actor_platform.erl` (processes, mailbox, selective receive)
- Python: `fable_actor_platform.py` (asyncio tasks)
- JS: TBD

### Design Principles

- **Actor is the only abstraction** — no Observable, Observer, or Rx types
- **`actor { }` CE is the composition mechanism** — maps to platform concurrency (BEAM process, asyncio task, JS promise)
- **Supervision via links** — `spawnLinked` + `trapExits` delivers EXIT signals as messages
- **Rx composition lives elsewhere** — AsyncRx uses `actor { }` instead of `MailboxProcessor`

## Dependencies

- .NET SDK 10+
- Fable.Core 5.0.0-rc.1
