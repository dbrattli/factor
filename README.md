# Fable.Actor

> **Warning: Experimental / Work in Progress**

Fable.Actor is a cross-platform actor library for F#, compiled via [Fable](https://github.com/fable-compiler/Fable) to BEAM (Erlang), Python, and JavaScript. It's a `MailboxProcessor` replacement that works across Fable targets, with BEAM-native supervision via process links.

**Key difference from MailboxProcessor:** actors do not assume shared memory. On BEAM, each actor runs in an isolated process — captured closures and mutable globals are copied, not shared. Code that relies on closing over mutable variables or sharing state through module-level references will not work correctly on BEAM. All communication must go through message passing (`send`/`receive`/`call`).

## Build

Requires .NET SDK 10+ and the [Fable](https://github.com/fable-compiler/Fable) compiler.

```sh
just check    # Type-check F# with dotnet build
just build    # Compile F# to Erlang via Fable
just format   # Format source with Fantomas
```

## Test

```sh
just test-native   # Run .NET tests
just test-python   # Compile to Python via Fable, then run
just test-beam     # Compile to Erlang via Fable, then run
just test          # Run .NET + Python tests
```

## Quick Start

### Stateful Actor

```fsharp
open Fable.Actor.Types
open Fable.Actor

type CounterMsg =
    | Increment
    | GetCount of ReplyChannel<int>

let counter = start 0 (fun count msg ->
    match msg with
    | Increment -> Continue (count + 1)
    | GetCount rc ->
        rc.Reply count
        Continue count)

send counter Increment
send counter Increment
let! count = call counter (fun rc -> GetCount rc)
// count = 2
```

### Actor with Computation Expression

The `actor { }` CE maps to each platform's concurrency primitive — `MailboxProcessor` on .NET/Python/JS, CPS-based blocking receive on BEAM.

```fsharp
open Fable.Actor

let greeter = spawn (fun inbox ->
    let rec loop () = actor {
        let! msg = inbox.Receive()
        printfn "Hello, %s!" msg
        return! loop ()
    }
    loop ())

send greeter "World"
```

### Linked Actors and Supervision

`spawnLinked` creates a child actor linked to the parent. If the child crashes, the parent gets an EXIT signal. Use `trapExits` to handle crashes instead of dying.

```fsharp
let supervisor = spawn (fun inbox ->
    trapExits ()
    let _worker = spawnLinked inbox (fun childInbox ->
        let rec loop () = actor {
            let! msg = childInbox.Receive()
            // process msg...
            return! loop ()
        }
        loop ())

    let rec loop () = actor {
        let! msg = inbox.Receive()
        // handle EXIT signals from crashed children
        return! loop ()
    }
    loop ())
```

### Timers

```fsharp
let ticker = start 0 (fun count msg ->
    match msg with
    | "tick" ->
        printfn "tick %d" count
        Continue (count + 1)
    | _ -> Continue count)

schedule 1000 (fun () -> send ticker "tick") |> ignore
```

## Architecture

```
src/Fable.Actor/
  Types.fs      — ReplyChannel, Next<'State>, ChildExited
  Platform.fs   — BEAM: IActorPlatform + [<ImportAll("factor_platform")>]
                  Non-BEAM: empty (uses MailboxProcessor directly)
  Actor.fs      — actor { }, spawn, spawnLinked, start, send, call, kill, schedule
  erl/          — BEAM platform implementation (native processes)
```

### Platform Strategy

| Platform | Actor wraps | Concurrency model |
|----------|------------|-------------------|
| .NET | `MailboxProcessor` | Async + threads |
| Python | `MailboxProcessor` (Fable) | asyncio |
| JS | `MailboxProcessor` (Fable) | Promises (TBD) |
| BEAM | Native process | Erlang processes + mailbox |

On non-BEAM targets, `Actor<'Msg>` is a thin wrapper around `MailboxProcessor<'Msg>`. No platform-specific runtime needed — Fable's built-in `MailboxProcessor` handles everything. On BEAM, actors map to real Erlang processes with native supervision.

### API

| Function | Description |
|----------|-------------|
| `spawn body` | Spawn an actor: `spawn (fun inbox -> actor { ... })` |
| `spawnLinked parent body` | Spawn a linked child actor (EXIT on crash) |
| `start state handler` | Stateful actor with message handler loop |
| `send actor msg` | Fire-and-forget message send |
| `call actor msgFactory` | Async request-response (returns `ActorOp<'Reply>`) |
| `kill actor` | Kill an actor immediately |
| `trapExits ()` | Enable supervision (EXIT signals become messages) |
| `schedule ms callback` | Schedule a timer callback |
| `cancelTimer timer` | Cancel a scheduled timer |

### Design Principles

- **Actor is the only abstraction** — no Observable, Observer, or Rx types
- **No shared memory** — actors communicate only via messages (critical for BEAM)
- **`actor { }` CE is the composition mechanism** — `async { }` on non-BEAM, CPS on BEAM
- **MailboxProcessor-compatible** — same `inbox.Receive()` / `actor.Post()` API
- **Supervision via links** — `spawnLinked` + `trapExits` for fault tolerance
- **Rx composition lives elsewhere** — use [AsyncRx](https://github.com/dbrattli/AsyncRx) with `actor { }` instead of `MailboxProcessor`

## Why?

`MailboxProcessor` assumes shared memory — closures can capture mutable state, and multiple agents can reference the same objects. On BEAM, each actor is an isolated process with its own heap, so shared mutable references silently break. Fable.Actor provides a clean actor abstraction where all communication goes through message passing (`send`/`receive`/`call`), making it safe to compile to native processes on BEAM while also working on Python and .NET.

## License

MIT

## Related Projects

- [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx) — Async Reactive Extensions for F#
- [Fable](https://github.com/fable-compiler/Fable) — F# to JS/Python/BEAM compiler
