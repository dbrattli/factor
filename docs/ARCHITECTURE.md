# ActorX Architecture

ActorX is a Reactive Extensions (Rx) library for Gleam targeting the Erlang/BEAM runtime. This document explains the design principles and architectural decisions.

## Overview

ActorX implements the Reactive Extensions pattern: composable, push-based streams of asynchronous events. It is a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx) to Gleam, leveraging the BEAM's actor model for concurrency.

```text
Observable (source) → Operator (transform) → Observer (sink)
                            ↓
                     State Management
                   (actors / process dict)
```

## Core Types

All core types are defined in `src/actorx/types.gleam`:

### Notification

The atoms of the Rx grammar. Every event in a stream is one of:

```gleam
pub type Notification(a) {
  OnNext(a)       // A value
  OnError(String) // An error (terminal)
  OnCompleted     // Stream end (terminal)
}
```

### Observer

A consumer of notifications. Wraps a single notify function:

```gleam
pub type Observer(a) {
  Observer(notify: fn(Notification(a)) -> Nil)
}
```

### Observable

A lazy, push-based stream. Wraps a subscribe function that connects an observer and returns a disposal handle:

```gleam
pub type Observable(a) {
  Observable(subscribe: fn(Observer(a)) -> Disposable)
}
```

### Disposable

A handle for resource cleanup and unsubscription:

```gleam
pub type Disposable {
  Disposable(dispose: fn() -> Nil)
}
```

## The Rx Contract

ActorX enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- Zero or more `OnNext` values may be emitted
- At most one terminal event (`OnError` or `OnCompleted`)
- No events after a terminal event

The `safe_observer.wrap()` function enforces this contract by:

1. Tracking a "stopped" flag using the process dictionary
2. Ignoring events after terminal
3. Calling disposal on terminal events

## Data Flow

### Subscription Flow

```text
1. Observer created by consumer
2. Consumer calls observable.subscribe(observer)
3. Observable sets up upstream subscription
4. Returns Disposable to consumer

     Consumer                    Observable                    Source
        │                            │                           │
        │──subscribe(observer)──────►│                           │
        │                            │──subscribe(upstream)─────►│
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
On terminal: Disposable.dispose() called

     Source                     Operator                    Observer
        │                           │                          │
        │───OnNext(x)──────────────►│                          │
        │                           │───OnNext(f(x))──────────►│
        │───OnCompleted────────────►│                          │
        │                           │───OnCompleted───────────►│
        │                           │                          │
                                                    dispose() called
```

## Module Organization

```text
src/actorx/
├── types.gleam        # Core types: Observable, Observer, Notification, Disposable
├── create.gleam       # Creation: single, empty, never, fail, from_list, defer
├── transform.gleam    # Transform: map, flat_map, concat_map, switch_map, scan, reduce
├── filter.gleam       # Filter: filter, take, skip, distinct, sample
├── combine.gleam      # Combine: merge, zip, combine_latest, concat, amb, fork_join
├── timeshift.gleam    # Time: timer, interval, delay, debounce, throttle, timeout
├── subject.gleam      # Subjects: subject, single_subject, publish, share
├── error.gleam        # Error handling: retry, catch
├── safe_observer.gleam# Rx grammar enforcement
├── interop.gleam      # Actor interop: from_subject, to_subject, call_actor
└── builder.gleam      # Monadic composition for `use` syntax
```

The main `src/actorx.gleam` file is an API facade that re-exports all public functions.

## State Management Patterns

ActorX uses two patterns for mutable state:

### 1. Process Dictionary (Synchronous Operators)

For simple synchronous operators that need minimal state (like a "stopped" flag), ActorX uses Erlang's process dictionary via FFI:

```gleam
// From safe_observer.gleam
@external(erlang, "erlang", "put")
fn erlang_put(key: a, value: b) -> c

@external(erlang, "erlang", "get")
fn erlang_get(key: a) -> b

@external(erlang, "erlang", "make_ref")
fn erlang_make_ref() -> a
```

This pattern creates a unique reference as a key and stores/retrieves values in the calling process's dictionary.

### 2. Actor-Based (Asynchronous Operators)

For operators that:

- Need to coordinate multiple async streams
- Require complex state management
- Must handle concurrent subscriptions

ActorX spawns dedicated coordinator actors using `gleam_otp`:

```gleam
// Pattern used across async operators
process.spawn(fn() {
  let control: Subject(OperatorMsg) = process.new_subject()
  process.send(control_ready, control)
  operator_loop(control, initial_state)
})
```

The actor communicates via typed messages and maintains state through recursive function calls.

## Operator Patterns

### Stateless Operators

Simple transformations that don't need state create a new observer inline:

```gleam
pub fn map(source: Observable(a), mapper: fn(a) -> b) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    let upstream_observer = Observer(notify: fn(n) {
      case n {
        OnNext(x) -> downstream(OnNext(mapper(x)))
        OnError(e) -> downstream(OnError(e))
        OnCompleted -> downstream(OnCompleted)
      }
    })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}
```

### Stateful Operators (Actor-Based)

Operators needing state spawn an actor that:

1. Receives a control Subject for typed messages
2. Maintains state via recursive calls
3. Handles disposal via a Dispose message

```gleam
type TakeMsg(a) {
  TakeNext(a)
  TakeError(String)
  TakeCompleted
  TakeDispose
}

pub fn take(source: Observable(a), count: Int) -> Observable(a) {
  Observable(subscribe: fn(observer) {
    // Spawn coordinator actor
    process.spawn(fn() {
      let control = process.new_subject()
      process.send(control_ready, control)
      take_loop(control, downstream, count)
    })
    // ... subscribe to source, return disposable
  })
}
```

### Composed Operators

Higher-level operators are composed from primitives:

```gleam
// flat_map = map + merge_inner (unlimited concurrency)
pub fn flat_map(source: Observable(a), mapper: fn(a) -> Observable(b)) -> Observable(b) {
  source
  |> map(mapper)
  |> merge_inner(None)
}

// concat_map = map + concat_inner
pub fn concat_map(source: Observable(a), mapper: fn(a) -> Observable(b)) -> Observable(b) {
  source
  |> map(mapper)
  |> concat_inner()
}

// switch_map = map + switch_inner
pub fn switch_map(source: Observable(a), mapper: fn(a) -> Observable(b)) -> Observable(b) {
  source
  |> map(mapper)
  |> switch_inner()
}
```

Note that `concat_inner` is itself implemented as `merge_inner(source, Some(1))`, making `merge_inner` the unified primitive for all concurrency-controlled flattening.

## Cold vs Hot Observables

### Cold Observables

Most operators produce **cold** observables:

- Each subscription triggers a new execution
- No sharing of side effects between subscribers
- Created by `from_list`, `single`, `timer`, etc.

### Hot Observables / Subjects

**Subjects** are both Observer and Observable:

- `subject()` - Multicast to multiple subscribers (no buffering)
- `single_subject()` - Single subscriber with buffering
- `publish()` - Convert cold to hot (manual connect)
- `share()` - Auto-connecting multicast with refcount

```gleam
// Cold: each subscriber gets independent stream
let cold = actorx.interval(100)

// Hot: subscribers share same stream
let hot = cold |> actorx.share()
```

## Flattening Strategies

When dealing with `Observable(Observable(a))`, three strategies exist:

| Strategy |    Operator    |                Behavior                 |
| -------- | -------------- | --------------------------------------- |
| Merge    | `merge_inner`  | Subscribe to all, forward all emissions |
| Concat   | `concat_inner` | Process one at a time, preserve order   |
| Switch   | `switch_inner` | Cancel previous on new arrival          |

### Concurrency Control with merge_inner

`merge_inner` accepts a `max_concurrency` parameter to control how many inner observables can be active simultaneously:

```gleam
// Unlimited concurrency (subscribe to all immediately)
source |> merge_inner(None)

// Sequential processing (equivalent to concat_inner)
source |> merge_inner(Some(1))

// Limited concurrency (at most 3 active at once)
source |> merge_inner(Some(3))
```

When at capacity, incoming observables are queued and subscribed to as active ones complete. This unifies merge and concat behavior under a single primitive.

## Time-Based Operators

Time operators use `process.sleep` and `process.send_after` for scheduling:

- `timer(ms)` - Emit 0 after delay, then complete
- `interval(ms)` - Emit 0, 1, 2... at regular intervals
- `delay(ms)` - Delay each emission
- `debounce(ms)` - Emit only after silence period
- `throttle(ms)` - Rate limit to one per period
- `timeout(ms)` - Error if no emission within period

## Actor Interop

ActorX provides bridges between reactive streams and OTP actors:

```gleam
// Create observable from gleam/erlang/process.Subject
let #(subject, observable) = actorx.from_subject()

// Send to an actor while passing through
source |> actorx.to_subject(actor_subject)

// Request-response call to an actor
actorx.call_actor(actor, 1000, GetValue)
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
5. **BEAM Integration**: Leverages actors for concurrency and fault isolation
