# Fable.Actor

> **Warning: Experimental / Work in Progress**

Fable.Actor is a cross-platform actor library for F#, compiled via [Fable](https://github.com/fable-compiler/Fable) to BEAM (Erlang), Python, and JavaScript. It's a `MailboxProcessor` replacement that works across Fable targets, with BEAM-native supervision via process links.

**Key difference from MailboxProcessor:** actors do not assume shared memory. On BEAM, each actor runs in an isolated process — captured closures and mutable globals are copied, not shared. Code that relies on closing over mutable variables or sharing state through module-level references will not work correctly on BEAM. All communication must go through message passing (`send`/`receive`/`call`).

## Build

Requires .NET SDK 10+ and the [Fable](https://github.com/fable-compiler/Fable) compiler.

```sh
just build    # Compile F# to Erlang via Fable
just check    # Type-check F# with dotnet build
just format   # Format source with Fantomas
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
let count = call counter (fun rc -> GetCount rc)
// count = 2
```

### Actor with Computation Expression

The `actor { }` CE provides CPS-based receive that maps to each platform's concurrency primitive — blocking selective receive on BEAM, async/await on Python, promises on JS.

```fsharp
open Fable.Actor

let greeter = spawn (fun () ->
    let rec loop () = actor {
        let! msg = receive<string> ()
        printfn "Hello, %s!" msg
        return! loop ()
    }
    loop ())

send greeter "World"
```

### Linked Actors and Supervision

`spawnLinked` creates a child actor linked to the parent. If the child crashes, the parent gets an EXIT signal. Use `trapExits` to handle crashes instead of dying.

```fsharp
let supervisor = spawn (fun () ->
    trapExits ()
    let _worker = spawnLinked (fun () ->
        let rec loop () = actor {
            let! msg = receive<string> ()
            // process msg...
            return! loop ()
        }
        loop ())

    let rec loop () = actor {
        let! msg = receive<obj> ()
        // handle EXIT signals from crashed children
        return! loop ()
    }
    loop ())
```

### Timers

```fsharp
let ticker = spawn (fun () ->
    let me = self<string> ()
    schedule 1000 (fun () -> send me "tick") |> ignore

    let rec loop () = actor {
        let! msg = receive<string> ()
        printfn "%s" msg
        schedule 1000 (fun () -> send me "tick") |> ignore
        return! loop ()
    }
    loop ())
```

## Architecture

```
src/Fable.Actor/
  Types.fs      — Actor<'Msg>, Next<'State>, ReplyChannel, ChildExited
  Platform.fs   — IActorPlatform (16 methods), [<ImportAll("factor_platform")>]
  Actor.fs      — actor { }, spawn, spawnLinked, start, send, call, receive, kill, trapExits
  erl/          — BEAM platform implementation
```

### Platform Interface

Each target provides a native `factor_platform` module:

| Platform | Implementation | Concurrency model |
|----------|---------------|-------------------|
| BEAM | `factor_platform.erl` | Processes + mailbox |
| Python | `factor_platform.py` | asyncio tasks |
| JS | TBD | Promises |

### API

| Function | Description |
|----------|-------------|
| `spawn body` | Spawn an actor running an `actor { }` CE body |
| `spawnLinked body` | Spawn a linked child actor (EXIT on crash) |
| `start state handler` | Stateful actor with message handler loop |
| `send actor msg` | Fire-and-forget message send |
| `call actor msgFactory` | Synchronous request-response |
| `receive ()` | Receive next message (inside `actor { }`) |
| `self ()` | Get own actor reference |
| `kill actor` | Kill an actor immediately |
| `trapExits ()` | Enable supervision (EXIT signals become messages) |
| `schedule ms callback` | Schedule a timer callback |
| `cancelTimer timer` | Cancel a scheduled timer |
| `refEquals a b` | Compare platform references |

### Design Principles

- **Actor is the only abstraction** — no Observable, Observer, or Rx types
- **No shared memory** — actors communicate only via messages, no shared closures or mutable globals (critical for BEAM where each actor is an isolated process)
- **`actor { }` CE is the composition mechanism** — maps to platform concurrency
- **Supervision via links** — `spawnLinked` + `trapExits` for fault tolerance
- **Rx composition lives elsewhere** — use [AsyncRx](https://github.com/dbrattli/AsyncRx) with `actor { }` instead of `MailboxProcessor`

## Why?

`MailboxProcessor` doesn't work across Fable targets. Fable.Actor provides the same typed actor abstraction — spawn, send, receive, request-reply — compiled to native concurrency on each platform. On BEAM, you also get process links and supervision for free.

The library is designed as a foundation. Rx operators (map, filter, merge, flatMap, etc.) belong in [AsyncRx](https://github.com/dbrattli/AsyncRx), which can use `actor { }` instead of `MailboxProcessor` to gain cross-platform support.

## License

MIT

## Related Projects

- [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx) — Async Reactive Extensions for F#
- [Fable](https://github.com/fable-compiler/Fable) — F# to JS/Python/BEAM compiler
