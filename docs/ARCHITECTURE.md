# Factor Architecture

Factor is a composable actor framework for the Erlang/BEAM runtime, written in F# and compiled to Erlang via Fable.Beam. This document explains the design principles and architectural decisions.

## Overview

Factor implements the Reactive Extensions pattern: composable, push-based actors for asynchronous events. It is a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx), leveraging the BEAM runtime for concurrency and fault tolerance.

Every operator in a Factor pipeline spawns a BEAM process. The pipeline IS the supervision tree.

```text
Observable (source) → Operator (process) → Operator (process) → Observer (endpoint)
                           ↓                     ↓
                     Linked child            Linked child
                  (mutable state in        (mutable state in
                   own process dict)        own process dict)
```

## Core Types

All core types are defined in `src/Factor.Agent/Types.fs` (`Factor.Agent.Types` module):

### Msg

The atoms of the Rx grammar. Every event in a pipeline is one of:

```fsharp
type Msg<'T> =
    | OnNext of 'T       // A value
    | OnError of exn     // An error (terminal)
    | OnCompleted        // End (terminal)
```

### Observer

A process endpoint that receives messages. Contains the `Pid` and `Ref` of a BEAM process registered to handle `{factor_child, Ref, Msg}` messages:

```fsharp
type Observer<'T> = { Pid: obj; Ref: obj }
```

Messages are delivered to an observer via Erlang message passing: `Pid ! {factor_child, Ref, boxed_msg}`. The receiving process dispatches messages by looking up the `Ref` in its process dictionary registry.

### Observable

A lazy, push-based stream. Contains a Subscribe function that connects an observer and returns a disposal handle:

```fsharp
type Observable<'T> = { Subscribe: Observer<'T> -> Handle }
```

### Handle

A handle for resource cleanup and unsubscription:

```fsharp
type Handle = { Dispose: unit -> unit }
```

### ChannelMsg

Protocol messages for channel agents. Parameterizes channel behavior:

```fsharp
type ChannelMsg<'T> =
    | Notify of Msg<'T>
    | Subscribe of Observer<'T> * ReplyChannel<unit>
    | Unsubscribe of obj
```

Push uses `{factor_msg, ...}` protocol (Agent.send), subscribe uses `Agent.call` for synchronous ack, unsubscribe uses `Agent.send`.

### SupervisionPolicy

Controls what happens when a spawned child process crashes:

```fsharp
type SupervisionPolicy =
    | Terminate          // Propagate as OnError, kill pipeline (default)
    | Skip               // Ignore the crash, continue with other inners
    | Restart of int     // Resubscribe the failed inner, up to N times
```

## The Rx Contract

Factor enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- Zero or more `OnNext` values may be emitted
- At most one terminal event (`OnError` or `OnCompleted`)
- No events after a terminal event

Enforcement is structural: each operator process exits when its agent CE loop ends (no `return! loop` on terminal events). Process exit cascades via BEAM links — no wrapper needed.

```fsharp
// Terminal event handling in every operator:
// The agent CE loop simply doesn't recurse — process exits naturally
let rec loop () =
    agent {
        let! msg = Operator.recvMsg<'T> ref
        match msg with
        | OnNext x ->
            Process.onNext downstream (mapper x)
            return! loop ()
        | OnError e -> Process.onError downstream e       // no loop → exits
        | OnCompleted -> Process.onCompleted downstream   // no loop → exits
    }
```

## Data Flow

### Subscribe Flow

When a pipeline is subscribed, each operator spawns a linked BEAM process that subscribes to its source. The chain of `spawnLinked` calls creates a linked process tree:

```text
1. Consumer creates an observer endpoint (Pid + Ref)
2. Consumer calls observable.Subscribe(observer)
3. Observable spawns a linked process
4. Spawned process creates its own observer endpoint
5. Spawned process subscribes to its source
6. Returns Handle (kill pid) to consumer

     Consumer              Operator Process            Source
        │                        │                       │
        │──Subscribe(observer)──►│                       │
        │                   spawnLinked()                 │
        │                        │──Subscribe(self)─────►│
        │◄──Handle(kill pid)─────│                       │
```

### Event Flow

Events flow as Erlang messages between operator processes:

```text
Source sends {factor_child, Ref, OnNext(x)} to Operator Pid
    ↓
Operator process receives message via selective receive (recvMsg)
    ↓
Handler transforms value, sends {factor_child, Ref, OnNext(f(x))} to downstream Pid
    ↓
On terminal: handler sends terminal to downstream, loop ends, process exits
    ↓
EXIT signal propagates through linked process tree

     Source Process          Operator Process          Consumer Process
          │                       │                         │
          │──{factor_child}──────►│                         │
          │                       │──{factor_child}────────►│
          │──{factor_child}──────►│  (OnCompleted)          │
          │                       │──{factor_child}────────►│
          │                       │  loop ends → exits      │
          │                       X                         │
```

### Pipeline as Process Tree

A composed pipeline creates a tree of linked BEAM processes:

```text
Consumer Process
  └── map process (linked)
        └── filter process (linked)
              └── source process (linked)
```

If any operator process crashes, the EXIT signal propagates up through the linked chain. The consumer's exit handler converts this to an `OnError` or handles it according to the supervision policy.

## Architectural Layers

Factor is organized into three F# projects with clean dependencies:

```text
Factor.Reactive  →  Factor.Beam  →  Factor.Agent
  (operators)       (BEAM impl)     (abstract types)
```

### Factor.Agent — Abstract Types

Cross-platform contract. No platform code, no dependencies beyond Fable.Core.

```text
src/Factor.Agent/
└── Types.fs          # Agent, Observer, Observable, Msg, Handle, ChannelMsg,
                      # ReplyChannel, Next, SupervisionPolicy, exceptions
```

### Factor.Beam — BEAM Implementation

Layered from primitives to operator machinery:

```text
src/Factor.Beam/
├── Erlang.fs         # Raw Erlang FFI (receive, monotonicTime)
├── Process.fs        # BEAM process primitives + observer message protocol
├── Agent.fs          # Agent CE (agent { }), spawn, start, send, call
├── Operator.fs       # Operator process machinery: selective receive,
│                     #   message loops, composable operator helpers
│                     #   (forNext, forNextStateful, ofMsgStateful, ofMsg2)
└── erl/
    ├── factor_actor.erl  # Process spawning, selective receive, exit/child registries
    └── factor_timer.erl  # Timer scheduling via erlang:send_after
```

**Process.fs** — BEAM process primitives:
- `spawnLinked`, `killProcess`, `trapExits`, `selfPid`, `makeRef`
- `registerChild`/`unregisterChild`, `registerExit`/`unregisterExit`
- `exitNormal`, `formatReason`, `refEquals`
- Observer helpers: `notify`, `onNext`, `onError`, `onCompleted`

**Agent.fs** — Typed agent abstraction:
- `agent { }` CE with `let!` for selective receive
- `spawn` — raw CPS agent with typed Pid
- `start` — stateful agent with message handler
- `send` — async message send
- `call` — synchronous request-response

**Operator.fs** — Operator process machinery:
- `childLoop` — generic message pump (timers, child messages, EXIT signals)
- `processTimers` — timed message pump for tests
- `recvMsg<'T>` — selective receive for single-source operators
- `recvAnyMsg` — selective receive for multi-source operators
- `spawnOp` — spawn linked operator process from agent CE
- `forNext`, `forNextStateful`, `ofMsgStateful`, `ofMsg2` — composable operator templates

### Factor.Reactive — Rx Operators

All operators, channels, and the public API facade:

```text
src/Factor.Reactive/
├── Types.fs          # Re-exports from Factor.Agent.Types
├── Channel.fs        # Channels: push helpers, channel, multicast, singleSubscriber,
│                     #   publish, share
├── Create.fs         # Creation: create, single, empty, never, fail, ofList, defer
├── Transform.fs      # Transform: map, flatMap, mergeInner, concatMap, switchMap,
│                     #   scan, reduce, groupBy, pairwise, tap, startWith
├── Filter.fs         # Filter: filter, take, skip, distinct, sample, first, last
├── Combine.fs        # Combine: merge, zip, combineLatest, concat, amb, forkJoin
├── TimeShift.fs      # Time: timer, interval, delay, debounce, throttle, timeout
├── Error.fs          # Error handling: retry, catch
├── Interop.fs        # Interop helpers: tapSend
├── Builder.fs        # Computation expression builder (observable { ... })
└── Reactive.fs       # API facade (Factor.Reactive.Reactive module)
```

## Process-Per-Operator Model

Every operator in Factor spawns a dedicated BEAM process via `Process.spawnLinked`. This is the single composition model — there is no distinction between "inline" and "spawned" variants.

### How It Works

Most operators use the agent CE pattern with selective receive:

1. `spawnOp` spawns a linked child process
2. The child creates a `Ref` and an `Observer<'T>` endpoint
3. The child subscribes to its source
4. The child enters a recursive `agent { }` CE loop using `recvMsg` or `recvAnyMsg`
5. On terminal events, the loop ends — the process exits naturally

### Operator Helpers

Operators are built from composable templates in `Operator.fs`:

- **`forNext`** — stateless single-source (map, filter, tap, choose)
- **`forNextStateful`** — stateful single-source (mapi, skip, skipWhile, distinctUntilChanged, distinct)
- **`ofMsgStateful`** — full Msg control with state (scan, reduce, take, takeWhile, pairwise, first, last, defaultIfEmpty, takeLast)
- **`ofMsg2`** — dual-source with state (combineLatest, withLatestFrom, zip, takeUntil, sample)

Complex operators (mergeInner, switchInner, amb, forkJoin, groupBy, delay, debounce, throttle, timeout) use `spawnOp` + `recvMsg`/`recvAnyMsg` directly with custom agent CE loops.

### Benefits

- **Process isolation**: Each operator's mutable state (counters, accumulators, buffers) lives in its own process, completely isolated from other operators
- **Natural supervision**: Linked processes form a supervision tree automatically. The pipeline structure IS the fault-tolerance structure
- **BEAM-native**: Leverages lightweight BEAM processes (microsecond spawn, ~300 bytes each) and OTP conventions
- **Simplified model**: One composition mode, one mental model. No need to choose between inline and spawned variants

### Notification Path

Messages flow between operator processes as Erlang tuples:

```text
Source Process ──{factor_child, Ref, Msg}──► Operator Process ──{factor_child, Ref, Msg}──► Consumer
```

When operators subscribe to channel agents, the channel broadcasts via the same protocol:

```text
Channel Agent ──{factor_child, Ref, Msg}──► Operator Process ──{factor_child, Ref, Msg}──► Consumer
```

Push into channels uses a different protocol:

```text
Producer ──{factor_msg, {notify, Msg}}──► Channel Agent ──{factor_child, Ref, Msg}──► Subscribers
```

## State Management

### Operator State (Process-Local)

Each operator's mutable state lives in its own spawned process. On BEAM (via Fable.Beam), mutable variables compile to process dictionary operations, which are inherently process-local. Since every operator is its own process, there are no shared-state concerns between operators.

```fsharp
// scan operator — accumulator is recursive loop state in the agent CE
let scan (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Observable<'T>) : Observable<'U> =
    Operator.ofMsgStateful source initial (fun downstream acc msg ->
        match msg with
        | OnNext x ->
            let newAcc = accumulator acc x
            Process.onNext downstream newAcc
            Some newAcc
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            Process.onCompleted downstream
            None)
```

### Channel Agents (Cross-Process Multicast)

Channels are backed by `Agent<ChannelMsg<'T>>` — stateful agents that manage subscriber lists via message passing. Each channel agent handles three message types: `Notify` (broadcast to subscribers), `Subscribe` (add subscriber with synchronous ack), and `Unsubscribe` (remove subscriber).

Pre-composed channels:
- **`multicast()`** — broadcasts to all subscribers, no buffering
- **`singleSubscriber()`** — single subscriber with buffering before subscribe

## Operator Patterns

### Standard Operator Pattern (agent CE)

Most operators use operator helpers that encapsulate the `spawnOp` + `recvMsg` + agent CE pattern:

```fsharp
let map (mapper: 'T -> 'U) (source: Observable<'T>) : Observable<'U> =
    Operator.forNext source (fun downstream x ->
        Process.onNext downstream (mapper x))
```

Under the hood, `forNext` spawns a linked process with a selective receive loop:

```fsharp
// What forNext does internally:
spawnOp (fun () ->
    let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
    source.Subscribe(upstream) |> ignore

    let rec loop () =
        agent {
            let! msg = recvMsg<'T> ref
            match msg with
            | OnNext x ->
                onNextFn downstream x
                return! loop ()
            | OnError e -> Process.onError downstream e
            | OnCompleted -> Process.onCompleted downstream
        }

    loop ())
```

### Creation Operators (No Process Spawn)

Creation operators (`single`, `empty`, `fail`, `ofList`) do NOT spawn processes. They send messages directly to the downstream observer's mailbox. BEAM mailbox buffering handles the synchronous-to-asynchronous transition:

```fsharp
let ofList (items: 'T list) : Observable<'T> =
    { Subscribe = fun observer ->
        for x in items do
            Process.onNext observer x
        Process.onCompleted observer
        emptyHandle () }
```

These are safe because `Process.onNext` is just an Erlang send (`Pid ! {factor_child, Ref, Msg}`), which is non-blocking and buffers in the receiver's mailbox.

### Composed Operators

Higher-level operators are composed from primitives:

```fsharp
// flatMap = map + mergeInner (each inner in its own linked child process)
let flatMap mapper source =
    source |> map mapper |> mergeInner Terminate None

// concatMap = map + concatInner (sequential)
let concatMap mapper source =
    source |> map mapper |> concatInner

// concatInner = mergeInner with maxConcurrency=1
let concatInner source =
    mergeInner Terminate (Some 1) source

// switchMap = map + switchInner (latest only)
let switchMap mapper source =
    source |> map mapper |> switchInner
```

`mergeInner` accepts a `maxConcurrency` parameter:

- `None` — unlimited concurrency (spawn all immediately)
- `Some 1` — sequential processing (equivalent to `concatInner`)
- `Some n` — at most n inner observables active, queue the rest

`mergeInner` also accepts a `SupervisionPolicy` controlling how child process crashes are handled (terminate, skip, or restart).

## Channel Architecture

Channels bridge the gap between agents and observables — they're "wormholes" parameterized by an agent that defines their behavior.

### How Channels Work

```fsharp
// Low-level: wrap any Agent<ChannelMsg<'T>>
let channel (agent: Agent<ChannelMsg<'T>>) : Observer<'T> * Observable<'T>

// Pre-composed
let multicast<'T> () : Observer<'T> * Observable<'T>
let singleSubscriber<'T> () : Observer<'T> * Observable<'T>
```

The push side returns `Observer<'T>` (same type as downstream endpoints). Push helpers send `ChannelMsg` to the channel agent:

```fsharp
let pushNext (observer: Observer<'T>) (value: 'T) : unit =
    Agent.send { Pid = observer.Pid } (Notify(OnNext value))
```

Subscribe uses `Agent.call` for synchronous ack, preventing races between subscribing and the first send.

### publish / share

- **`publish`** — converts cold to hot (manual connect). Manages subscriber list in the subscribing process.
- **`share`** — auto-connecting multicast with refcount. Connects on first subscriber, disconnects when last unsubscribes.

## Cold vs Hot Observables

### Cold Observables

Most operators produce **cold** observables:

- Each subscribe triggers a new execution
- No sharing of side effects between subscribers
- Created by `ofList`, `single`, `timer`, etc.

### Hot Observables / Channels

**Channels** separate the push side (`Observer<'T>`) from the subscribe side (`Observable<'T>`):

- `multicast()` — Multicast channel agent, multiple subscribers (no buffering). Returns `Observer<'T> * Observable<'T>`
- `singleSubscriber()` — Single subscriber channel agent with buffering. Returns `Observer<'T> * Observable<'T>`
- `publish()` — Convert cold to hot (manual connect)
- `share()` — Auto-connecting multicast with refcount

```fsharp
// Cold: each subscriber gets independent execution
let cold = Reactive.interval 100

// Hot: subscribers share same execution
let hot = cold |> Reactive.share
```

## Message Dispatch

Operator processes handle these message types via selective receive:

- `{factor_child, Ref, Msg}` — Messages from source operators, channel actors, or child processes
- `{factor_timer, Ref, Callback}` — Timer callbacks from `erlang:send_after`
- `{'EXIT', Pid, Reason}` — Linked process exits (when `trapExits` is enabled)

The `recvMsg` and `recvAnyMsg` functions provide selective receive — they block waiting for `{factor_child, ...}` messages while dispatching timer callbacks and EXIT signals as side effects. The `childLoop` function provides a generic message pump for operators that don't use selective receive (timer, interval, mergeInner parent).

## Time-Based Operators

Time operators use a native Erlang module (`factor_timer`) that schedules callbacks via `erlang:send_after`. Timer callbacks are delivered as `{factor_timer, Ref, Callback}` messages and fire as side effects during `recvMsg`:

- `timer(ms)` — Emit 0 after delay, then complete
- `interval(ms)` — Emit 0, 1, 2... at regular intervals
- `delay(ms)` — Delay each emission
- `debounce(ms)` — Emit only after silence period
- `throttle(ms)` — Rate limit to one per period
- `timeout(ms)` — Error if no emission within period

## Computation Expression Builder

Factor provides two computation expressions:

### `observable { ... }` — Observable composition

Each `let!` desugars to `flatMap`, which spawns child processes creating supervision boundaries:

```fsharp
open Factor.Reactive.Builder

let example =
    observable {
        let! x = Reactive.single 10
        let! y = Reactive.single 20
        let! z = Reactive.ofList [ 1; 2; 3 ]
        return x + y + z
    }
// Emits: 31, 32, 33 then completes
```

Since every operator already spawns a process, `let!` using `flatMap` is consistent with the rest of the framework. Each binding creates a linked child process for the inner observable, forming a supervision sub-tree.

### `agent { ... }` — CPS-based actors

For message-passing actors with typed Pids and selective receive:

```fsharp
open Factor.Beam.Agent

let pid = spawn (fun () ->
    let rec loop () =
        agent {
            let! msg = recv ()
            // handle msg
            return! loop ()
        }
    loop ())
```

## Error Handling

- `retry(n)` — Re-subscribe on error, up to n times
- `catch(handler)` — On error, switch to fallback observable

Process crashes in child processes are caught by exit monitors (when `trapExits` is enabled) and converted to `OnError(ProcessExitException ...)` with a formatted reason. The `SupervisionPolicy` on `mergeInner` controls whether crashes terminate the pipeline, are skipped, or trigger a restart.

## Design Principles

1. **Laziness**: Observables don't execute until subscribed
2. **Composability**: Small operators compose into complex pipelines
3. **Process Isolation**: Every operator is a BEAM process. Mutable state is always process-local and never shared. The pipeline structure IS the supervision hierarchy
4. **Rx Contract**: Operator processes self-enforce the grammar by exiting when the agent CE loop ends on terminal events
5. **BEAM Integration**: Compiled to Erlang via Fable.Beam for lightweight processes and fault tolerance
6. **Resource Safety**: Handles kill operator processes; EXIT signals propagate through the linked tree for automatic cleanup
7. **Layered Architecture**: Process → Agent → Operator → Observable → Channel → Composed operators
