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
Factor (source) → Operator (BEAM process) → Observer (process endpoint)
                        ↓
                 Process-local State
          (mutable variables in own process)
```

Every operator in the pipeline is a BEAM process. Messages flow between processes via mailbox sends. Creation operators (`ofList`, `single`, `empty`, `fail`) are the exception — they send messages directly to the downstream process's mailbox without spawning a process of their own.

### Core Types (src/Types.fs)

- **Msg<'T>**: Rx grammar atoms (`OnNext of 'T`, `OnError of exn`, `OnCompleted`)
- **Handle**: Resource cleanup handle with `Dispose: unit -> unit`
- **Observer<'T>**: Process endpoint `{ Pid: obj; Ref: obj }` — identifies a downstream process to send messages to
- **Sender<'T>**: Push-side handle for channels `{ ChannelPid: obj }` — used to send messages into a channel actor
- **Factor<'T>**: Lazy push-based actor with `Spawn: Observer<'T> -> Handle`

### Module Structure

- **src/Factor.fs**: Main API facade (`Factor.Reactive` module), re-exports all operators
- **src/Types.fs**: Core types (Factor, Observer, Sender, Msg, Handle)
- **src/Create.fs**: Creation operators (`create`, `single`, `empty`, `never`, `fail`, `ofList`, `defer`) — `single`, `empty`, `fail`, `ofList` send directly to downstream without spawning a process
- **src/Process.fs**: BEAM process management FFI, channel actor FFI (`spawnLinked`, `trapExits`, `startStream`, `streamSubscribe`, etc.)
- **src/Transform.fs**: Transform operators (`map`, `mapi`, `flatMap`, `mergeInner`, `concatMap`, `concatMapi`, `switchInner`, `switchMap`, `switchMapi`, `tap`, `startWith`, `pairwise`, `scan`, `reduce`, `groupBy`) — `mergeInner` takes `policy: SupervisionPolicy` and `maxConcurrency: int option`
- **src/Filter.fs**: Filter operators (`filter`, `take`, `skip`, `takeWhile`, `skipWhile`, `choose`, `distinctUntilChanged`, `distinct`, `takeUntil`, `takeLast`, `first`, `last`, `defaultIfEmpty`, `sample`)
- **src/Combine.fs**: Combining operators (`merge`, `merge2`, `combineLatest`, `withLatestFrom`, `zip`, `concat`, `concat2`, `amb`, `race`, `forkJoin`)
- **src/TimeShift.fs**: Time-based operators (`timer`, `interval`, `delay`, `debounce`, `throttle`, `timeout`)
- **src/Channel.fs**: Channels (`channel`, `singleChannel`, `publish`, `share`) — `channel`/`singleChannel` return `Sender<'T> * Factor<'T>`
- **src/Error.fs**: Error handling (`retry`, `catch`)
- **src/Interop.fs**: Interop helpers (`tapSend`)
- **src/Actor.fs**: CPS-based actor computation expression (`actor { ... }` with `spawn`, `send`, `recv`)
- **src/Flow.fs**: Computation expression builder (`flow { ... }` — `let!` uses `Transform.flatMap`, each binding is a process boundary)

### Erlang Modules

- **src/erl/factor_actor.erl**: Process spawning, `child_loop`, exit/child handler registries
- **src/erl/factor_timer.erl**: Timer scheduling via `erlang:send_after`
- **src/erl/factor_stream.erl**: Channel actor processes (multicast + single-subscriber with buffering)

### State Management

Every operator is its own BEAM process. Mutable state (process dictionary, `Dictionary`, `HashSet`) is local to that operator's process and cannot be captured across process boundaries. Channels (`channel()`, `singleChannel()`) use dedicated actor processes for multicast/buffering. Time-based operators use Erlang FFI via `factor_timer:schedule` and `factor_timer:cancel`.

### Rx Contract

The library enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- After a terminal event (OnError or OnCompleted), no further events are delivered
- Each operator process self-enforces by calling `Process.exitNormal()` upon receiving a terminal event — there is no separate SafeObserver wrapper

### Message Dispatch

All operator processes handle `{factor_child, Ref, Msg}` messages (upstream data) and `{factor_timer, Ref, Callback}` messages (timer callbacks). Processes with linked children also handle `{'EXIT', Pid, Reason}` for supervision. Dispatch uses process dictionary registries: `factor_children`, `factor_exits`.

## Dependencies

- .NET SDK 10+
- Fable.Core 5.0.0-beta.5
- Fable.Beam compiler (local, at `../fable/fable-beam/src/Fable.Cli`)
