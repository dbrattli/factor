# Factor Architecture

Factor is a Reactive Extensions (Rx) library for the Erlang/BEAM runtime, written in F# and compiled to Erlang via Fable.Beam. This document explains the design principles and architectural decisions.

## Overview

Factor implements the Reactive Extensions pattern: composable, push-based streams of asynchronous events. It is a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx), leveraging the BEAM runtime for concurrency and fault tolerance.

```text
Observable (source) → Operator (transform) → Observer (sink)
                            ↓
                     State Management
                   (mutable variables / closures)
```

## Core Types

All core types are defined in `src/Types.fs` (`Factor.Types` module):

### Notification

The atoms of the Rx grammar. Every event in a stream is one of:

```fsharp
type Notification<'a> =
    | OnNext of 'a       // A value
    | OnError of string  // An error (terminal)
    | OnCompleted        // Stream end (terminal)
```

### Observer

A consumer of notifications. Contains a single Notify function:

```fsharp
type Observer<'a> = { Notify: Notification<'a> -> unit }
```

### Observable

A lazy, push-based stream. Contains a Subscribe function that connects an observer and returns a disposal handle:

```fsharp
type Observable<'a> = { Subscribe: Observer<'a> -> Disposable }
```

### Disposable

A handle for resource cleanup and unsubscription:

```fsharp
type Disposable = { Dispose: unit -> unit }
```

## The Rx Contract

Factor enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- Zero or more `OnNext` values may be emitted
- At most one terminal event (`OnError` or `OnCompleted`)
- No events after a terminal event

The `SafeObserver.wrap` function enforces this contract by:

1. Tracking a mutable `stopped` flag
2. Ignoring events after terminal
3. Calling disposal on terminal events

## Data Flow

### Subscription Flow

```text
1. Observer created by consumer
2. Consumer calls observable.Subscribe(observer)
3. Observable sets up upstream subscription
4. Returns Disposable to consumer

     Consumer                    Observable                    Source
        │                            │                           │
        │──Subscribe(observer)──────►│                           │
        │                            │──Subscribe(upstream)─────►│
        │◄───────Disposable──────────│◄────────Disposable────────│
```

### Event Flow

```text
Source emits OnNext(value)
    ↓
Operator transforms/filters
    ↓
Observer receives notification
    ↓
On terminal: Disposable.Dispose() called

     Source                     Operator                    Observer
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
├── Types.fs          # Core types: Observable, Observer, Notification, Disposable
├── SafeObserver.fs   # Rx grammar enforcement
├── Create.fs         # Creation: single, empty, never, fail, ofList, defer
├── Transform.fs      # Transform: map, flatMap, concatMap, switchMap, scan, reduce, groupBy
├── Filter.fs         # Filter: filter, take, skip, distinct, sample
├── Combine.fs        # Combine: merge, zip, combineLatest, concat, amb, forkJoin
├── TimeShift.fs      # Time: timer, interval, delay, debounce, throttle, timeout
├── Subject.fs        # Subjects: subject, singleSubject, publish, share
├── Error.fs          # Error handling: retry, catch
├── Interop.fs        # Interop helpers: tapSend
├── Builder.fs        # Computation expression builder (rx { ... })
└── Factor.fs         # API facade (Factor.Rx module), re-exports all operators
```

## State Management

Factor uses **mutable variables** for state management in all operators. On BEAM (via Fable.Beam), mutable variables are backed by the Erlang process dictionary, which provides per-process mutable storage.

This is a significant simplification over the original Gleam version, which spawned dedicated actors (processes) for every stateful operator. In F#, since all operators run synchronously in the subscriber's process context, simple mutable variables suffice.

### Stateless Operators

Simple transformations create a new observer inline:

```fsharp
let map (mapper: 'a -> 'b) (source: Observable<'a>) : Observable<'b> =
    { Subscribe = fun observer ->
        let upstreamObserver =
            { Notify = fun n ->
                match n with
                | OnNext x -> observer.Notify(OnNext(mapper x))
                | OnError e -> observer.Notify(OnError e)
                | OnCompleted -> observer.Notify(OnCompleted) }
        source.Subscribe(upstreamObserver) }
```

### Stateful Operators

Operators needing state use mutable variables:

```fsharp
let take (count: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe = fun observer ->
        let mutable remaining = count
        let upstreamObserver =
            { Notify = fun n ->
                if remaining > 0 then
                    match n with
                    | OnNext x ->
                        remaining <- remaining - 1
                        observer.Notify(OnNext x)
                        if remaining = 0 then
                            observer.Notify(OnCompleted)
                    | OnError e -> observer.Notify(OnError e)
                    | OnCompleted -> observer.Notify(OnCompleted) }
        source.Subscribe(upstreamObserver) }
```

### Composed Operators

Higher-level operators are composed from primitives:

```fsharp
// flatMap = map + mergeInner (unlimited concurrency)
let flatMap mapper source =
    source |> map mapper |> mergeInner None

// concatMap = map + concatInner
let concatMap mapper source =
    source |> map mapper |> concatInner

// switchMap = map + switchInner
let switchMap mapper source =
    source |> map mapper |> switchInner
```

Note that `concatInner` is itself implemented as `mergeInner (Some 1)`, making `mergeInner` the unified primitive for all concurrency-controlled flattening.

## Cold vs Hot Observables

### Cold Observables

Most operators produce **cold** observables:

- Each subscription triggers a new execution
- No sharing of side effects between subscribers
- Created by `ofList`, `single`, `timer`, etc.

### Hot Observables / Subjects

**Subjects** are both Observer and Observable:

- `subject()` - Multicast to multiple subscribers (no buffering)
- `singleSubject()` - Single subscriber with buffering
- `publish()` - Convert cold to hot (manual connect)
- `share()` - Auto-connecting multicast with refcount

```fsharp
// Cold: each subscriber gets independent stream
let cold = interval 100

// Hot: subscribers share same stream
let hot = cold |> share
```

## Flattening Strategies

When dealing with `Observable<Observable<'a>>`, three strategies exist:

| Strategy | Operator      | Behavior                                |
| -------- | ------------- | --------------------------------------- |
| Merge    | `mergeInner`  | Subscribe to all, forward all emissions |
| Concat   | `concatInner` | Process one at a time, preserve order   |
| Switch   | `switchInner` | Cancel previous on new arrival          |

### Concurrency Control with mergeInner

`mergeInner` accepts a `maxConcurrency` parameter:

```fsharp
// Unlimited concurrency (subscribe to all immediately)
source |> mergeInner None

// Sequential processing (equivalent to concatInner)
source |> mergeInner (Some 1)

// Limited concurrency (at most 3 active at once)
source |> mergeInner (Some 3)
```

## Time-Based Operators

Time operators use Erlang FFI via `Fable.Core.Emit` for scheduling:

- `timer(ms)` - Emit 0 after delay, then complete
- `interval(ms)` - Emit 0, 1, 2... at regular intervals
- `delay(ms)` - Delay each emission
- `debounce(ms)` - Emit only after silence period
- `throttle(ms)` - Rate limit to one per period
- `timeout(ms)` - Error if no emission within period

Erlang FFI bindings:

```fsharp
[<Emit("timer:apply_after($0, erlang, apply, [$1, []])")>]
let timerApplyAfter (ms: int) (callback: unit -> unit) : obj = nativeOnly

[<Emit("erlang:cancel_timer($0)")>]
let erlangCancelTimer (timer: obj) : unit = nativeOnly
```

## Computation Expression Builder

Factor provides an F# computation expression for monadic composition:

```fsharp
open Factor.Builder

let example =
    rx {
        let! x = single 10
        let! y = single 20
        let! z = ofList [1; 2; 3]
        return x + y + z
    }
// Emits: 31, 32, 33 then completes
```

## Error Handling

- `retry(n)` - Resubscribe on error, up to n times
- `catch(handler)` - On error, switch to fallback observable

Errors propagate downstream and terminate the stream. The Rx contract ensures no further events after an error.

## Design Principles

1. **Laziness**: Observables don't execute until subscribed
2. **Composability**: Small operators compose into complex pipelines
3. **Resource Safety**: Disposables ensure cleanup on completion/error/unsubscribe
4. **Rx Contract**: Grammar enforcement prevents illegal states
5. **BEAM Integration**: Compiled to Erlang via Fable.Beam for BEAM runtime benefits
6. **Simplicity**: Mutable variables instead of actors for state management
