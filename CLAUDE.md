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
dotnet build src/Factor.Actor && dotnet build src/Factor.Beam && dotnet build src/Factor.Reactive
```

Fable.Beam compiler is expected at `../fable/fable-beam/src/Fable.Cli`.

## Architecture

```text
Process → Actor → Observer/Observable → Channel → Composed Operators
```

Three F# projects with clean dependencies: `Factor.Reactive → Factor.Beam → Factor.Actor`

Every operator in the pipeline is a BEAM process. Messages flow between processes via mailbox sends. Creation operators (`ofList`, `single`, `empty`, `fail`) are the exception — they send messages directly to the downstream process's mailbox without spawning a process of their own.

### Core Types (src/Factor.Actor/Types.fs)

- **Msg<'T>**: Rx grammar atoms (`OnNext of 'T`, `OnError of exn`, `OnCompleted`)
- **Handle**: Resource cleanup handle with `Dispose: unit -> unit`
- **Observer<'T>**: Process endpoint `{ Pid: obj; Ref: obj }` — identifies a downstream process to send messages to
- **Observable<'T>**: Lazy push-based stream with `Subscribe: Observer<'T> -> Handle`
- **Actor<'Msg>**: Typed wrapper around a BEAM process Pid
- **ChannelMsg<'T>**: Channel protocol (`Notify of Msg<'T>`, `Subscribe of Observer<'T> * ReplyChannel<unit>`, `Unsubscribe of obj`)
- **ReplyChannel<'Reply>**: Callback for synchronous request-response via `Actor.call`
- **Next<'State>**: Actor handler return type (`Continue of 'State` | `Stop`)

### Project Structure

**Factor.Actor** — Abstract types (cross-platform contract):
- `Types.fs`: All shared types (Actor, Observer, Observable, Msg, Handle, ChannelMsg, etc.)

**Factor.Beam** — BEAM implementation:
- `Erlang.fs`: Raw Erlang FFI (receive, monotonicTime)
- `Process.fs`: BEAM process primitives + observer message protocol (`spawnLinked`, `killProcess`, `trapExits`, `selfPid`, `makeRef`, `onNext`, `onError`, `onCompleted`, `refEquals`)
- `Agent.fs`: Typed actor abstraction — `actor { }` CE, `spawn`, `start`, `send`, `call`
- `Operator.fs`: Operator process machinery — selective receive (`recvMsg`, `recvAnyMsg`), `spawnOp`, operator templates (`forNext`, `forNextStateful`, `ofMsgStateful`, `ofMsg2`), `childLoop`, `processTimers`
- `erl/factor_actor.erl`: Process spawning, selective receive, exit/child handler registries
- `erl/factor_timer.erl`: Timer scheduling via `erlang:send_after`

**Factor.Reactive** — Rx operators:
- `Channel.fs`: Push helpers (`pushNext`, `pushError`, `pushCompleted`), channel constructors (`channel`, `multicast`, `singleSubscriber`, `publish`, `share`)
- `Create.fs`: Creation operators (`create`, `single`, `empty`, `never`, `fail`, `ofList`, `defer`)
- `Transform.fs`: Transform operators (`map`, `mapi`, `flatMap`, `mergeInner`, `concatMap`, `switchMap`, `scan`, `reduce`, `groupBy`, `tap`, `startWith`, `pairwise`)
- `Filter.fs`: Filter operators (`filter`, `take`, `skip`, `takeWhile`, `skipWhile`, `choose`, `distinctUntilChanged`, `distinct`, `takeUntil`, `takeLast`, `first`, `last`, `defaultIfEmpty`, `sample`)
- `Combine.fs`: Combining operators (`merge`, `merge2`, `combineLatest`, `withLatestFrom`, `zip`, `concat`, `concat2`, `amb`, `race`, `forkJoin`)
- `TimeShift.fs`: Time-based operators (`timer`, `interval`, `delay`, `debounce`, `throttle`, `timeout`)
- `Error.fs`: Error handling (`retry`, `catch`)
- `Interop.fs`: Interop helpers (`tapSend`)
- `Builder.fs`: Computation expression builder (`observable { ... }` — `let!` uses `Transform.flatMap`)
- `Reactive.fs`: API facade (`Factor.Reactive.Reactive` module), re-exports all operators

### State Management

Every operator is its own BEAM process. Mutable state (process dictionary, `Dictionary`, `HashSet`) is local to that operator's process and cannot be captured across process boundaries. Channels (`multicast()`, `singleSubscriber()`) are backed by `Actor<ChannelMsg<'T>>`. Time-based operators use Erlang FFI via `factor_timer:schedule` and `factor_timer:cancel`.

### Rx Contract

The library enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- After a terminal event (OnError or OnCompleted), no further events are delivered
- Each operator process self-enforces: the agent CE loop ends (no `return! loop`) on terminal events, the process exits naturally, and BEAM links cascade the termination

### Message Dispatch

Two message protocols:
- `{factor_child, Ref, Msg}` — operator-to-operator data flow (via `Process.onNext`/`onError`/`onCompleted`)
- `{factor_msg, Msg}` — actor messages (via `Actor.send`), used by channel push helpers

Operator processes also handle `{factor_timer, Ref, Callback}` (timer callbacks) and `{'EXIT', Pid, Reason}` (supervision). Timer callbacks fire as side effects during `recvMsg`/`recvAnyMsg`.

## Dependencies

- .NET SDK 10+
- Fable.Core 5.0.0-beta.5
- Fable.Beam compiler (local, at `../fable/fable-beam/src/Fable.Cli`)
