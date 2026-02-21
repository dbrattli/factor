# Factor Architecture

Factor is a composable actor framework for the Erlang/BEAM runtime, written in F# and compiled to Erlang via Fable.Beam. This document explains the design principles and architectural decisions.

## Overview

Factor implements the Reactive Extensions pattern: composable, push-based streams of asynchronous events. It is a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx), leveraging the BEAM runtime for concurrency and fault tolerance.

```text
Factor (source) → Operator (transform) → Handler (sink)
                        ↓
                 State Management
         (mutable variables / stream actors)
```

## Core Types

All core types are defined in `src/Types.fs` (`Factor.Types` module):

### Notification

The atoms of the Rx grammar. Every event in a stream is one of:

```fsharp
type Notification<'T> =
    | OnNext of 'T       // A value
    | OnError of exn     // An error (terminal)
    | OnCompleted        // Stream end (terminal)
```

### Handler

A consumer of notifications. Contains a single Notify function:

```fsharp
type Handler<'T> = { Notify: Notification<'T> -> unit }
```

### Factor

A lazy, push-based stream. Contains a Subscribe function that connects a handler and returns a disposal handle:

```fsharp
type Factor<'T> = { Subscribe: Handler<'T> -> Handle }
```

### Handle

A handle for resource cleanup and unsubscription:

```fsharp
type Handle = { Dispose: unit -> unit }
```

## The Rx Contract

Factor enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- Zero or more `OnNext` values may be emitted
- At most one terminal event (`OnError` or `OnCompleted`)
- No events after a terminal event

The `SafeHandler.wrap` function enforces this contract by:

1. Tracking a mutable `stopped` flag
2. Ignoring events after terminal
3. Calling disposal on terminal events

## Data Flow

### Subscription Flow

```text
1. Handler created by consumer
2. Consumer calls factor.Subscribe(handler)
3. Factor sets up upstream subscription
4. Returns Handle to consumer

     Consumer                    Factor                       Source
        │                          │                            │
        │──Subscribe(handler)─────►│                            │
        │                          │──Subscribe(upstream)──────►│
        │◄──────Handle─────────────│◄──────────Handle───────────│
```

### Event Flow

```text
Source emits OnNext(value)
    ↓
Operator transforms/filters
    ↓
Handler receives notification
    ↓
On terminal: Handle.Dispose() called

     Source                     Operator                    Handler
        │                           │                          │
        │───OnNext(x)──────────────►│                          │
        │                           │───OnNext(f(x))──────────►│
        │───OnCompleted────────────►│                          │
        │                           │───OnCompleted───────────►│
        │                           │                          │
                                                    Dispose() called
```

## Module Organization

```text
src/
├── Types.fs          # Core types: Factor, Handler, Notification, Handle
├── SafeHandler.fs    # Rx grammar enforcement
├── Create.fs         # Creation: single, empty, never, fail, ofList, defer
├── Process.fs        # BEAM process management FFI, stream actor FFI
├── Transform.fs      # Transform: map, flatMap, flatMapSpawned, mergeInner,
│                     #   mergeInnerSpawned, concatMap, switchMap, scan, reduce, groupBy
├── Filter.fs         # Filter: filter, take, skip, distinct, sample
├── Combine.fs        # Combine: merge, zip, combineLatest, concat, amb, forkJoin
├── TimeShift.fs      # Time: timer, interval, delay, debounce, throttle, timeout
├── Stream.fs         # Streams: stream, singleStream, publish, share
├── Error.fs          # Error handling: retry, catch
├── Interop.fs        # Interop helpers: tapSend
├── Actor.fs          # CPS-based actor computation expression
├── Builder.fs        # Computation expression builder (factor { ... })
└── Factor.fs         # API facade (Factor.Reactive module), re-exports all operators

erl/
├── factor_actor.erl  # Process spawning, child_loop, exit/child registries
├── factor_timer.erl  # Timer scheduling via erlang:send_after
└── factor_stream.erl # Stream actor processes (multicast + single-subscriber)
```

## State Management

Factor uses two complementary approaches to state management:

### Mutable Variables (Inline)

Most operators use mutable variables for state. On BEAM (via Fable.Beam), mutable variables are backed by the Erlang process dictionary. All stateful operators run synchronously in the subscriber's process context.

### Stream Actors (Cross-Process)

Streams (`stream()`, `singleStream()`) and `groupBy` sub-groups are backed by dedicated BEAM actor processes that manage state via message passing. This solves the cross-process problem: when a child process needs to subscribe to a stream, the actor handles subscriber registration without accessing another process's dictionary.

## Two Composition Modes

### Pipe Operators (Inline)

`flatMap`, `mergeInner`, and other pipe operators subscribe inline in the subscriber's process. Mutable state (process dictionary, `Dictionary`, `HashSet`) is safe to use.

```fsharp
source
|> Reactive.groupBy key         // groupBy creates stream actor per group
|> Reactive.flatMap (fun (k, group) ->
    group |> Reactive.map transform)  // inline subscribe, same process
```

### Computation Expression (Process-Spawning)

`factor { let! ... }` desugars to `flatMapSpawned`, which spawns a linked child process for each inner subscription. This creates supervision boundaries — child crashes are caught and converted to `OnError`.

```fsharp
factor {
    let! x = source     // spawns child process
    let! y = other       // spawns child process
    return x + y
}
```

**Constraint:** The CE body runs in spawned child processes, so it must NOT reference parent process dictionary state (mutable variables, `Dictionary`). Only capture immutable values and actor-based streams.

### Notification Path

When CE children subscribe to stream actors, notifications follow a two-hop path:

```text
Stream Actor ──{factor_child}──► Child Process ──{factor_child}──► Parent Process
                                  (childLoop)                       (process_timers)
```

## Operator Patterns

### Stateless Operators

Simple transformations create a new handler inline:

```fsharp
let map (mapper: 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Subscribe = fun handler ->
        let upstream =
            { Notify = fun n ->
                match n with
                | OnNext x -> handler.Notify(OnNext(mapper x))
                | OnError e -> handler.Notify(OnError e)
                | OnCompleted -> handler.Notify(OnCompleted) }
        source.Subscribe(upstream) }
```

### Stateful Operators

Operators needing state use mutable variables (created at subscribe time in the subscriber's process):

```fsharp
let take (count: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe = fun handler ->
        let mutable remaining = count
        let upstream =
            { Notify = fun n ->
                if remaining > 0 then
                    match n with
                    | OnNext x ->
                        remaining <- remaining - 1
                        handler.Notify(OnNext x)
                        if remaining = 0 then
                            handler.Notify(OnCompleted)
                    | OnError e -> handler.Notify(OnError e)
                    | OnCompleted -> handler.Notify(OnCompleted) }
        source.Subscribe(upstream) }
```

### Composed Operators

Higher-level operators are composed from primitives:

```fsharp
// flatMap = map + mergeInner (inline, unlimited concurrency)
let flatMap mapper source =
    source |> map mapper |> mergeInner None

// flatMapSpawned = map + mergeInnerSpawned (child processes)
let flatMapSpawned mapper source =
    source |> map mapper |> mergeInnerSpawned

// concatMap = map + concatInner (sequential)
let concatMap mapper source =
    source |> map mapper |> concatInner

// concatInner = mergeInner with maxConcurrency=1
let concatInner source =
    mergeInner (Some 1) source
```

`mergeInner` accepts a `maxConcurrency` parameter:

- `None` — unlimited concurrency (subscribe to all immediately)
- `Some 1` — sequential processing (equivalent to `concatInner`)
- `Some n` — at most n inner factors active, queue the rest

### Actor-Based Operators

`groupBy` creates a `singleStream` actor per group key, routing values via message passing:

```fsharp
// Each group is a singleStream actor — buffers before subscribe,
// handles subscriber management via messages, works cross-process
let groupBy keySelector source =
    // Dictionary maps keys to (streamPid, notify) pairs
    // Stream actors handle buffering and subscriber management
    ...
```

## Stream Actors

Each stream is backed by a BEAM actor process (`factor_stream.erl`):

- **`stream()`** — multicast, `#{Ref => Pid}` subscriber map
- **`singleStream()`** — single subscriber with buffering

Subscribe is **synchronous** (send + wait for ack) to prevent races between subscribe and first notify. Stream actors are linked to their creator via `spawn_link`.

## Cold vs Hot Factors

### Cold Factors

Most operators produce **cold** factors:

- Each subscription triggers a new execution
- No sharing of side effects between subscribers
- Created by `ofList`, `single`, `timer`, etc.

### Hot Factors / Streams

**Streams** are both Handler and Factor:

- `stream()` — Multicast stream actor, multiple subscribers (no buffering)
- `singleStream()` — Single subscriber stream actor with buffering
- `publish()` — Convert cold to hot (manual connect)
- `share()` — Auto-connecting multicast with refcount

```fsharp
// Cold: each subscriber gets independent stream
let cold = Reactive.interval 100

// Hot: subscribers share same stream
let hot = cold |> Reactive.share
```

## Message Dispatch

Processes that subscribe to streams or use time-based operators must handle two message types:

- `{factor_timer, Ref, Callback}` — Timer callbacks from `erlang:send_after`
- `{factor_child, Ref, Notification}` — Notifications from stream actors or child processes

The `process_timers` loop (for subscribers) and `child_loop` (for spawned children) both handle these messages. Custom receive loops (e.g., cowboy WebSocket handlers) must also handle `{factor_child, ...}` to receive stream notifications.

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

### `factor { ... }` — Stream composition

Each `let!` spawns a child process (supervision boundary):

```fsharp
open Factor.Builder

let example =
    factor {
        let! x = Reactive.single 10
        let! y = Reactive.single 20
        let! z = Reactive.ofList [ 1; 2; 3 ]
        return x + y + z
    }
// Emits: 31, 32, 33 then completes
```

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

- `retry(n)` — Resubscribe on error, up to n times
- `catch(handler)` — On error, switch to fallback factor

Process crashes in spawned children are caught by exit monitors and converted to `OnError(ProcessExitException ...)` with a formatted reason.

## Design Principles

1. **Laziness**: Factors don't execute until subscribed
2. **Composability**: Small operators compose into complex pipelines
3. **Resource Safety**: Handles ensure cleanup on completion/error/unsubscribe
4. **Rx Contract**: Grammar enforcement prevents illegal states
5. **BEAM Integration**: Compiled to Erlang via Fable.Beam for lightweight processes and fault tolerance
6. **Dual Composition**: Inline pipes for performance, CE spawning for supervision
7. **Actor Encapsulation**: Streams and groups use actor processes to encapsulate mutable state across process boundaries
