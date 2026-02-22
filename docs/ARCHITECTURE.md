# Factor Architecture

Factor is a composable actor framework for the Erlang/BEAM runtime, written in F# and compiled to Erlang via Fable.Beam. This document explains the design principles and architectural decisions.

## Overview

Factor implements the Reactive Extensions pattern: composable, push-based actors for asynchronous events. It is a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx), leveraging the BEAM runtime for concurrency and fault tolerance.

Every operator in a Factor pipeline spawns a BEAM process. The pipeline IS the supervision tree.

```text
Factor (source) → Operator (process) → Operator (process) → Observer (endpoint)
                       ↓                     ↓
                 Linked child            Linked child
              (mutable state in        (mutable state in
               own process dict)        own process dict)
```

## Core Types

All core types are defined in `src/Types.fs` (`Factor.Types` module):

### Msg

The atoms of the Rx grammar. Every event in a Factor is one of:

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

### Sender

The push-side handle for a channel actor. Used to send messages into a channel:

```fsharp
type Sender<'T> = { ChannelPid: obj }
```

### Factor

A lazy, push-based actor. Contains a Spawn function that connects an observer and returns a disposal handle:

```fsharp
type Factor<'T> = { Spawn: Observer<'T> -> Handle }
```

### Handle

A handle for resource cleanup and unsubscription:

```fsharp
type Handle = { Dispose: unit -> unit }
```

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

Enforcement is structural: each operator process calls `Process.exitNormal()` after forwarding a terminal event (OnError or OnCompleted) to the downstream observer. Since the process exits, no further messages can be processed. The linked process tree ensures cleanup propagates automatically through BEAM's EXIT signal mechanism.

```fsharp
// Terminal event handling pattern in every operator:
| OnError e ->
    Process.onError downstream e
    Process.exitNormal ()       // Process dies — no more messages handled
| OnCompleted ->
    Process.onCompleted downstream
    Process.exitNormal ()       // Process dies — no more messages handled
```

## Data Flow

### Spawn Flow

When a pipeline is spawned, each operator spawns a linked BEAM process that subscribes to its source. The chain of `spawnLinked` calls creates a linked process tree:

```text
1. Consumer creates an observer endpoint (Pid + Ref)
2. Consumer calls factor.Spawn(observer)
3. Factor spawns a linked process
4. Spawned process creates its own observer endpoint
5. Spawned process subscribes to its source
6. Returns Handle (kill pid) to consumer

     Consumer              Operator Process            Source
        │                        │                       │
        │──Spawn(observer)──►    │                       │
        │                   spawnLinked()                 │
        │                        │──Spawn(self)─────────►│
        │◄──Handle(kill pid)─────│                       │
```

### Event Flow

Events flow as Erlang messages between operator processes:

```text
Source sends {factor_child, Ref, OnNext(x)} to Operator Pid
    ↓
Operator process receives message, dispatches via Ref lookup
    ↓
Handler transforms value, sends {factor_child, Ref, OnNext(f(x))} to downstream Pid
    ↓
On terminal: handler sends terminal to downstream, then exits (Process.exitNormal)
    ↓
EXIT signal propagates through linked process tree

     Source Process          Operator Process          Consumer Process
          │                       │                         │
          │──{factor_child}──────►│                         │
          │                       │──{factor_child}────────►│
          │──{factor_child}──────►│  (OnCompleted)          │
          │                       │──{factor_child}────────►│
          │                       │  exitNormal()           │
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

## Module Organization

```text
src/
├── Types.fs          # Core types: Factor, Observer, Sender, Msg, Handle
├── Erlang.fs         # Erlang FFI primitives (receive, monotonicTime)
├── Process.fs        # BEAM process management, message loops, observer/sender helpers
├── Create.fs         # Creation: single, empty, never, fail, ofList, defer
├── Transform.fs      # Transform: map, flatMap, mergeInner, concatMap, switchMap,
│                     #   scan, reduce, groupBy
├── Filter.fs         # Filter: filter, take, skip, distinct, sample
├── Combine.fs        # Combine: merge, zip, combineLatest, concat, amb, forkJoin
├── TimeShift.fs      # Time: timer, interval, delay, debounce, throttle, timeout
├── Channel.fs        # Channels: channel, singleChannel, publish, share
├── Error.fs          # Error handling: retry, catch
├── Interop.fs        # Interop helpers: tapSend
├── Actor.fs          # CPS-based actor computation expression
├── Flow.fs           # Computation expression builder (flow { ... })
└── Factor.fs         # API facade (Factor.Reactive module), re-exports all operators

erl/
├── factor_actor.erl  # Process spawning, child_loop, exit/child registries
├── factor_timer.erl  # Timer scheduling via erlang:send_after
└── factor_stream.erl # Channel actor processes (multicast + single-subscriber)
```

## Process-Per-Operator Model

Every operator in Factor spawns a dedicated BEAM process via `Process.spawnLinked`. This is the single composition model — there is no distinction between "inline" and "spawned" variants.

### How It Works

1. When `factor.Spawn(downstream)` is called, the operator spawns a linked child process
2. The child process creates a `Ref` and registers a message handler in its process dictionary
3. The child constructs an `Observer<'T>` endpoint (`{ Pid = self(); Ref = ref }`) and subscribes to its source
4. The child enters `Process.childLoop()`, blocking on `receive` to dispatch incoming messages
5. On terminal events, the child forwards the event downstream and calls `Process.exitNormal()`

### Benefits

- **Process isolation**: Each operator's mutable state (counters, accumulators, buffers) lives in its own process dictionary, completely isolated from other operators
- **Natural supervision**: Linked processes form a supervision tree automatically. The pipeline structure IS the fault-tolerance structure
- **BEAM-native**: Leverages lightweight BEAM processes (microsecond spawn, ~300 bytes each) and OTP conventions
- **Simplified model**: One composition mode, one mental model. No need to choose between inline and spawned variants

### Notification Path

Messages flow between operator processes as Erlang tuples:

```text
Source Process ──{factor_child, Ref, Msg}──► Operator Process ──{factor_child, Ref, Msg}──► Consumer
```

When operators subscribe to channel actors, messages take a two-hop path:

```text
Channel Actor ──{factor_child, Ref, Msg}──► Operator Process ──{factor_child, Ref, Msg}──► Consumer
```

## State Management

### Operator State (Process-Local)

Each operator's mutable state lives in its own spawned process. On BEAM (via Fable.Beam), mutable variables compile to process dictionary operations, which are inherently process-local. Since every operator is its own process, there are no shared-state concerns between operators.

```fsharp
// scan operator — accumulator lives in the scan process's dictionary
let scan (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Spawn = fun downstream ->
        let pid = Process.spawnLinked (fun () ->
            let mutable acc = initial          // Process-local mutable state
            let ref = Process.makeRef ()
            Process.registerChild ref (fun msg ->
                let n = unbox<Msg<'T>> msg
                match n with
                | OnNext x ->
                    acc <- accumulator acc x    // Safe: only this process touches acc
                    Process.onNext downstream acc
                | OnError e -> Process.onError downstream e; Process.exitNormal ()
                | OnCompleted -> Process.onCompleted downstream; Process.exitNormal ())
            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
            source.Spawn(self) |> ignore
            Process.childLoop ())
        { Dispose = fun () -> Process.killProcess pid } }
```

### Channel Actors (Cross-Process Multicast)

Channels (`channel()`, `singleChannel()`) and `groupBy` sub-groups are backed by dedicated BEAM actor processes (`factor_stream.erl`) that manage subscriber registries via message passing. Channel actors enable multicast: multiple operator processes can subscribe to the same channel.

## Operator Patterns

### Standard Operator Pattern

Most operators follow the same structural pattern — spawn a linked process, register a handler, subscribe to source, enter the child loop:

```fsharp
let map (mapper: 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Spawn = fun downstream ->
        let pid = Process.spawnLinked (fun () ->
            let ref = Process.makeRef ()
            Process.registerChild ref (fun msg ->
                let n = unbox<Msg<'T>> msg
                match n with
                | OnNext x -> Process.onNext downstream (mapper x)
                | OnError e -> Process.onError downstream e; Process.exitNormal ()
                | OnCompleted -> Process.onCompleted downstream; Process.exitNormal ())
            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
            source.Spawn(self) |> ignore
            Process.childLoop ())
        { Dispose = fun () -> Process.killProcess pid } }
```

Key elements:
1. **`Process.spawnLinked`** — creates a linked child process
2. **`Process.makeRef`** — unique reference for message dispatch
3. **`Process.registerChild ref handler`** — registers handler in process dictionary
4. **`Observer<'T> = { Pid = self(); Ref = ref }`** — creates the endpoint for the source to send to
5. **`source.Spawn(self)`** — subscribes to the upstream source
6. **`Process.childLoop()`** — enters the receive loop (blocks until messages arrive)
7. **`Process.exitNormal()`** — terminates the process on terminal events

### Creation Operators (No Process Spawn)

Creation operators (`single`, `empty`, `fail`, `ofList`) do NOT spawn processes. They send messages directly to the downstream observer's mailbox. BEAM mailbox buffering handles the synchronous-to-asynchronous transition:

```fsharp
let ofList (items: 'T list) : Factor<'T> =
    { Spawn = fun observer ->
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
- `Some n` — at most n inner factors active, queue the rest

`mergeInner` also accepts a `SupervisionPolicy` controlling how child process crashes are handled (terminate, skip, or restart).

### Actor-Based Operators

`groupBy` creates a `singleChannel` actor per group key, routing values via message passing:

```fsharp
// Each group is a singleChannel actor — buffers before subscribe,
// handles subscriber management via messages, works cross-process
let groupBy keySelector source =
    // Dictionary maps keys to (channelPid, send) pairs
    // Channel actors handle buffering and subscriber management
    ...
```

## Channel Actors

Each channel is backed by a BEAM actor process (`factor_stream.erl`):

- **`channel()`** — multicast, `#{Ref => Pid}` subscriber map. Returns `Sender<'T> * Factor<'T>`
- **`singleChannel()`** — single subscriber with buffering. Returns `Sender<'T> * Factor<'T>`

The `Sender<'T>` handle provides functions to push messages into the channel:

```fsharp
let (sender, factor) = Reactive.channel ()
// Push values via Sender
Reactive.pushNext sender 42
Reactive.pushCompleted sender
```

Spawn is **synchronous** (send + wait for ack) to prevent races between subscribing and the first send. Channel actors are linked to their creator via `spawn_link`.

## Cold vs Hot Factors

### Cold Factors

Most operators produce **cold** factors:

- Each spawn triggers a new execution
- No sharing of side effects between subscribers
- Created by `ofList`, `single`, `timer`, etc.

### Hot Factors / Channels

**Channels** separate the push side (`Sender<'T>`) from the subscribe side (`Factor<'T>`):

- `channel()` — Multicast channel actor, multiple subscribers (no buffering). Returns `Sender<'T> * Factor<'T>`
- `singleChannel()` — Single subscriber channel actor with buffering. Returns `Sender<'T> * Factor<'T>`
- `publish()` — Convert cold to hot (manual connect)
- `share()` — Auto-connecting multicast with refcount

```fsharp
// Cold: each subscriber gets independent execution
let cold = Reactive.interval 100

// Hot: subscribers share same execution
let hot = cold |> Reactive.share
```

## Message Dispatch

Processes that subscribe to channels or use time-based operators must handle these message types in their receive loop:

- `{factor_timer, Ref, Callback}` — Timer callbacks from `erlang:send_after`
- `{factor_child, Ref, Msg}` — Messages from channel actors, source operators, or child processes
- `{'EXIT', Pid, Reason}` — Linked process exits (when trap_exit is enabled)

The `childLoop` function handles all three message types by dispatching through process dictionary registries (`factor_children` for child messages, `factor_exits` for EXIT signals). Custom receive loops (e.g., cowboy WebSocket handlers) must also handle `{factor_child, ...}` to receive channel messages.

## Time-Based Operators

Time operators use a native Erlang module (`factor_timer`) that schedules callbacks via `erlang:send_after`. Callbacks are delivered as messages to `self()` and executed by the message pump:

- `timer(ms)` — Emit 0 after delay, then complete
- `interval(ms)` — Emit 0, 1, 2... at regular intervals
- `delay(ms)` — Delay each emission
- `debounce(ms)` — Emit only after silence period
- `throttle(ms)` — Rate limit to one per period
- `timeout(ms)` — Error if no emission within period

## Computation Expression Builder

Factor provides two computation expressions:

### `flow { ... }` — Factor composition

Each `let!` desugars to `flatMap`, which spawns child processes creating supervision boundaries:

```fsharp
open Factor.Flow

let example =
    flow {
        let! x = Reactive.single 10
        let! y = Reactive.single 20
        let! z = Reactive.ofList [ 1; 2; 3 ]
        return x + y + z
    }
// Emits: 31, 32, 33 then completes
```

Since every operator already spawns a process, `let!` using `flatMap` is consistent with the rest of the framework. Each binding creates a linked child process for the inner factor, forming a supervision sub-tree.

### `actor { ... }` — CPS-based actors

For message-passing actors with typed PIDs:

```fsharp
open Factor.Actor

let pid = spawn (fun ctx ->
    actor {
        let! msg = ctx.Recv()
        // handle msg
    })
```

## Error Handling

- `retry(n)` — Re-spawn on error, up to n times
- `catch(handler)` — On error, switch to fallback factor

Process crashes in child processes are caught by exit monitors (when `trapExits` is enabled) and converted to `OnError(ProcessExitException ...)` with a formatted reason. The `SupervisionPolicy` on `mergeInner` controls whether crashes terminate the pipeline, are skipped, or trigger a restart.

## Design Principles

1. **Laziness**: Factors don't execute until spawned
2. **Composability**: Small operators compose into complex pipelines
3. **Process Isolation**: Every operator is a BEAM process. Mutable state is always process-local and never shared. The pipeline structure IS the supervision hierarchy
4. **Rx Contract**: Operator processes self-enforce the grammar by exiting on terminal events
5. **BEAM Integration**: Compiled to Erlang via Fable.Beam for lightweight processes and fault tolerance
6. **Resource Safety**: Handles kill operator processes; EXIT signals propagate through the linked tree for automatic cleanup
7. **Actor Encapsulation**: Channels and groups use actor processes to enable cross-process multicast and subscriber management
