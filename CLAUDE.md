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
               (mutable variables / closures)
```

### Three-Level Error Hierarchy

```
Level 1: Result<'T, 'E>     - Within expressions, handled locally
Level 2: OnError of 'E      - Stream error, handled by catch/retry/mapError
Level 3: Process crash       - Untyped, caught by monitor, converted to OnError
```

### Core Types (src/Types.fs)

- **Notification<'T, 'E>**: Rx grammar atoms (`OnNext of 'T`, `OnError of 'E`, `OnCompleted`)
- **Handle**: Resource cleanup handle with `Dispose: unit -> unit`
- **Handler<'T, 'E>**: Receives notifications via `Notify` callback
- **Factor<'T, 'E>**: Lazy push-based stream with `Subscribe: Handler<'T, 'E> -> Handle`

### Module Structure

- **src/Factor.fs**: Main API facade (`Factor.Reactive` module), re-exports all operators
- **src/Types.fs**: Core types (Factor, Handler, Notification, Handle)
- **src/SafeHandler.fs**: Enforces Rx grammar (OnNext*, then optionally OnError or OnCompleted)
- **src/Create.fs**: Creation operators (`create`, `single`, `empty`, `never`, `fail`, `ofList`, `defer`)
- **src/Transform.fs**: Transform operators (`map`, `mapi`, `flatMap`, `flatMapi`, `concatMap`, `concatMapi`, `mergeInner`, `concatInner`, `switchInner`, `switchMap`, `switchMapi`, `tap`, `startWith`, `pairwise`, `scan`, `reduce`, `groupBy`)
- **src/Filter.fs**: Filter operators (`filter`, `take`, `skip`, `takeWhile`, `skipWhile`, `choose`, `distinctUntilChanged`, `distinct`, `takeUntil`, `takeLast`, `first`, `last`, `defaultIfEmpty`, `sample`)
- **src/Combine.fs**: Combining operators (`merge`, `merge2`, `combineLatest`, `withLatestFrom`, `zip`, `concat`, `concat2`, `amb`, `race`, `forkJoin`)
- **src/TimeShift.fs**: Time-based operators (`timer`, `interval`, `delay`, `debounce`, `throttle`, `timeout`)
- **src/Subject.fs**: Subjects (`subject`, `singleSubject`, `publish`, `share`)
- **src/Error.fs**: Error handling (`retry`, `catch`, `mapError`)
- **src/Interop.fs**: Interop helpers (`tapSend`)
- **src/Process.fs**: BEAM process management FFI (`spawnLinked`, `monitorProcess`, `demonitorProcess`, `killProcess`, `trapExits`)
- **src/Actor.fs**: CPS-based actor computation expression (`actor { ... }` with `spawn`, `send`, `recv`)
- **src/Builder.fs**: Computation expression builder (`factor { ... }` syntax with `bind`, `ret`, `combine`, `forEach`)

### State Management

The F# version uses **mutable variables** for state management. On BEAM (via Fable.Beam), these are backed by the process dictionary. This simplifies the code compared to the original Gleam actor-based approach since all stateful operators run synchronously in the subscriber's process context.

Time-based operators use Erlang FFI via `Fable.Core.Emit` for `factor_timer:schedule` and `factor_timer:cancel`.

### Rx Contract

The library enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- After a terminal event (OnError or OnCompleted), no further events are delivered
- `SafeHandler.wrap` handles this enforcement

## Dependencies

- .NET SDK 8+
- Fable.Core 5.0.0-beta.5
- Fable.Beam compiler (local, at `../fable/fable-beam/src/Fable.Cli`)
