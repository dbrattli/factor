# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Factor is a composable actor framework for the Erlang/BEAM runtime, written in F# and compiled to Erlang via [Fable.Beam](https://github.com/fable-compiler/Fable). Rx-style lazy composition naturally creates OTP supervision trees - the pipeline IS the supervision hierarchy.

## Build Commands

Using justfile (preferred):

```sh
just build    # Compile F# to Erlang via Fable.Beam
just check    # Type-check F# with dotnet build
just format   # Format source with Fantomas
just clean    # Clean build artifacts
just all      # Check and build
```

Or directly:

```sh
dotnet build src/       # Type-check F# project
```

Fable.Beam compiler is expected at `../fable/fable-beam/src/Fable.Cli`.

## Architecture

```text
Factor (source) → Operator (transform) → Handler (sink)
                        ↓
                 State Management
         (mutable variables / stream actors)
```

### Core Types (src/Types.fs)

- **Notification<'T>**: Rx grammar atoms (`OnNext of 'T`, `OnError of string`, `OnCompleted`)
- **Handle**: Resource cleanup handle with `Dispose: unit -> unit`
- **Handler<'T>**: Receives notifications via `Notify` callback
- **Factor<'T>**: Lazy push-based stream with `Subscribe: Handler<'T> -> Handle`

### Module Structure

- **src/Factor.fs**: Main API facade (`Factor.Reactive` module), re-exports all operators
- **src/Types.fs**: Core types (Factor, Handler, Notification, Handle)
- **src/SafeHandler.fs**: Enforces Rx grammar (OnNext*, then optionally OnError or OnCompleted)
- **src/Create.fs**: Creation operators (`create`, `single`, `empty`, `never`, `fail`, `ofList`, `defer`)
- **src/Process.fs**: BEAM process management FFI, stream actor FFI (`spawnLinked`, `trapExits`, `startStream`, `streamSubscribe`, etc.)
- **src/Transform.fs**: Transform operators (`map`, `mapi`, `flatMap`, `flatMapSpawned`, `mergeInner`, `mergeInnerSpawned`, `concatMap`, `concatMapi`, `switchInner`, `switchMap`, `switchMapi`, `tap`, `startWith`, `pairwise`, `scan`, `reduce`, `groupBy`)
- **src/Filter.fs**: Filter operators (`filter`, `take`, `skip`, `takeWhile`, `skipWhile`, `choose`, `distinctUntilChanged`, `distinct`, `takeUntil`, `takeLast`, `first`, `last`, `defaultIfEmpty`, `sample`)
- **src/Combine.fs**: Combining operators (`merge`, `merge2`, `combineLatest`, `withLatestFrom`, `zip`, `concat`, `concat2`, `amb`, `race`, `forkJoin`)
- **src/TimeShift.fs**: Time-based operators (`timer`, `interval`, `delay`, `debounce`, `throttle`, `timeout`)
- **src/Stream.fs**: Streams (`stream`, `singleStream`, `publish`, `share`) — `stream`/`singleStream` are BEAM actor processes
- **src/Error.fs**: Error handling (`retry`, `catch`)
- **src/Interop.fs**: Interop helpers (`tapSend`)
- **src/Actor.fs**: CPS-based actor computation expression (`actor { ... }` with `spawn`, `send`, `recv`)
- **src/Builder.fs**: Computation expression builder (`factor { ... }` — `let!` uses `flatMapSpawned` for supervision)

### Erlang Modules

- **src/erl/factor_actor.erl**: Process spawning, `child_loop`, exit/child handler registries
- **src/erl/factor_timer.erl**: Timer scheduling via `erlang:send_after`
- **src/erl/factor_stream.erl**: Stream actor processes (multicast + single-subscriber with buffering)

### Two Composition Modes

**Pipe operators** (`flatMap`, `mergeInner`) subscribe inline in the same process. Safe for mutable state (process dictionary, `Dictionary`, `HashSet`). No supervision boundaries.

**Computation expression** (`factor { let! ... }`) uses `flatMapSpawned`/`mergeInnerSpawned` to spawn linked child processes. Creates supervision boundaries but must NOT reference parent process dictionary state from the CE body.

### State Management

Most operators use **mutable variables** backed by the process dictionary. Streams (`stream()`, `singleStream()`) and `groupBy` sub-groups use **actor processes** to encapsulate state, enabling cross-process subscription.

Time-based operators use Erlang FFI via `factor_timer:schedule` and `factor_timer:cancel`.

### Rx Contract

The library enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- After a terminal event (OnError or OnCompleted), no further events are delivered
- `SafeHandler.wrap` handles this enforcement

### Message Dispatch

Processes subscribing to streams must handle `{factor_child, Ref, Notification}` messages. Processes using timers must handle `{factor_timer, Ref, Callback}`. Both `process_timers` (subscriber loop) and `child_loop` (spawned child loop) handle these.

## Dependencies

- .NET SDK 10+
- Fable.Core 5.0.0-beta.5
- Fable.Beam compiler (local, at `../fable/fable-beam/src/Fable.Cli`)
