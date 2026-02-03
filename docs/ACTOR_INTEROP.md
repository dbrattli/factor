# Actor Interop Design Document

This document describes the design for bridging BEAM actors with reactive streams in
ActorX, enabling actor orchestration via Rx operators.

## Overview

ActorX provides reactive streams on the BEAM, but many BEAM applications use OTP actors
(processes with Subjects) for state management and concurrency. This module bridges
these two paradigms, allowing:

- **Actors as event sources** - Convert OTP Subjects to Observables
- **Streams driving actors** - Send Observable emissions to actor mailboxes
- **Request-response patterns** - Model actor call/reply as Observable

## New Operators

|                     Operator                      |                   Purpose                   |   Type   |
| ------------------------------------------------- | ------------------------------------------- | -------- |
| `from_subject()`                                  | Create Subject/Observable bridge            | Hot      |
| `to_subject(Observable(a), Subject(a))`           | Send emissions to Subject (passthrough)     | Operator |
| `call_actor(Subject(msg), timeout, make_request)` | Request-response as Observable              | Cold     |

## API Design

### `from_subject` - Create Subject/Observable Bridge

Creates a Subject and Observable pair for bridging actors with reactive streams.

```gleam
pub fn from_subject() -> #(Subject(a), Observable(a))
```

**Characteristics:**

- **Hot**: Values sent before subscription may be lost
- **Never completes**: Use `take_until` to add completion semantics
- **Multicast**: Multiple subscribers all receive the same values

**Implementation approach:**

- Spawns a coordinator process that creates and owns both the source Subject and control Subject
- Uses `process.select_map(source, FSValue)` to receive values sent to the source
- Manages subscriber list using the same pattern as `subject.gleam`

```gleam
// Coordinator selects from multiple sources
let selector =
  process.new_selector()
  |> process.select(control)
  |> process.select_map(source, FSValue)  // Map source values to internal message
```

**Example usage:**

```gleam
// Create the Subject/Observable bridge
let #(events_subject, events_observable) = actorx.from_subject()
let #(shutdown_subject, shutdown_observable) = actorx.from_subject()

// Pass subject to a producer actor
let _producer = start_event_producer(events_subject)

// Subscribe to the observable
events_observable
|> actorx.filter(is_high_priority)
|> actorx.take_until(shutdown_observable)
|> actorx.subscribe(alert_observer)

// Later, to stop: process.send(shutdown_subject, Nil)
```

**Note:** The Subject is created by the coordinator process, which allows it to receive messages sent to that Subject. This is a fundamental requirement of Gleam's Subject model - only the process that created a Subject can receive from it.

### `to_subject` - Send Emissions to Actor

Passthrough operator that sends each value to a Subject while forwarding downstream.

```gleam
pub fn to_subject(source: Observable(a), target: Subject(a)) -> Observable(a)
```

**Characteristics:**

- **Passthrough**: Values flow to both the Subject and downstream observer
- **Only sends OnNext**: Terminal events (OnError, OnCompleted) are not sent to Subject
- **Synchronous**: `process.send` is called before forwarding downstream

**Implementation approach:**

```gleam
Observable(subscribe: fn(observer) {
  let Observer(downstream) = observer
  let upstream_observer = Observer(notify: fn(n) {
    case n {
      OnNext(value) -> {
        process.send(target, value)  // Send to actor
        downstream(OnNext(value))     // Forward downstream
      }
      OnError(e) -> downstream(OnError(e))
      OnCompleted -> downstream(OnCompleted)
    }
  })
  let Observable(subscribe) = source
  subscribe(upstream_observer)
})
```

**Example usage:**

```gleam
// Send stream values to a counter actor
actorx.interval(100)
|> actorx.take(10)
|> actorx.map(fn(n) { Increment(n) })
|> actorx.to_subject(counter_inbox)
|> actorx.subscribe(log_observer)
```

### `call_actor` - Request-Response Pattern

Creates a cold Observable that performs a request-response call to an actor.

```gleam
pub fn call_actor(
  actor_subject: Subject(msg),
  timeout_ms: Int,
  make_request: fn(Subject(response)) -> msg,
) -> Observable(response)
```

**Characteristics:**

- **Cold**: Each subscription triggers a new request
- **Single emission**: Emits one response then completes
- **Timeout support**: Emits OnError if no response within timeout

**Implementation approach:**

1. On subscribe, create a reply Subject
2. Call `make_request(reply_subject)` to construct the message
3. Send message to actor via `process.send`
4. Wait for response with `process.receive(reply, timeout_ms)`
5. Emit response as OnNext, then OnCompleted
6. On timeout, emit OnError

**Example usage:**

```gleam
type CounterMsg {
  Increment(Int)
  GetValue(Subject(Int))
}

// Single call
actorx.call_actor(counter, 1000, GetValue)
|> actorx.subscribe(observer)

// Periodic polling with flat_map
actorx.interval(1000)
|> actorx.flat_map(fn(_) {
  actorx.call_actor(counter, 1000, GetValue)
})
|> actorx.subscribe(value_observer)
```

## Usage Patterns

### Pattern 1: Actor as Event Source

```gleam
// Create the Subject/Observable bridge
let #(events_subject, events_observable) = actorx.from_subject()

// Pass subject to an actor that produces events
let _actor = start_producer(events_subject)

// Reactive processing of actor events
events_observable
|> actorx.filter(high_priority)
|> actorx.debounce(100)
|> actorx.subscribe(alert_observer)
```

### Pattern 2: Stream Driving Actor

```gleam
// Process a stream and send commands to an actor
actorx.from_list(commands)
|> actorx.delay(100)
|> actorx.to_subject(worker_inbox)
|> actorx.subscribe(log_observer)
```

### Pattern 3: Request-Response Orchestration

```gleam
// Fan-out work to a pool of worker actors
let workers = [worker1, worker2, worker3]

actorx.from_list(work_items)
|> actorx.flat_mapi(fn(item, i) {
  let worker = list.at(workers, i % list.length(workers))
  actorx.call_actor(worker, 5000, fn(reply) { Process(item, reply) })
})
|> actorx.subscribe(results_observer)
```

### Pattern 4: Actor State Subscription

```gleam
// Create a Subject/Observable for state updates
let #(state_updates_subject, state_observable) = actorx.from_subject()

// Pass to an actor that will send state updates
let _state_actor = start_state_actor(state_updates_subject)

// Subscribe reactively to state changes
state_observable
|> actorx.distinct_until_changed()
|> actorx.subscribe(ui_observer)
```

## Design Decisions

### Hot vs Cold Semantics

- **`from_subject`**: Hot - the Subject exists independently of subscriptions. Values sent before subscription are lost. This matches BEAM's fire-and-forget message passing.

- **`call_actor`**: Cold - each subscription triggers a new request. This enables retry semantics and composition with operators like `flat_map`.

### No Completion Signal for `from_subject`

OTP Subjects have no inherent completion concept. Rather than inventing one, we leave `from_subject` as never-completing. Users can add completion semantics using existing operators:

```gleam
let #(events_subject, events_observable) = from_subject()
let #(shutdown_subject, shutdown_observable) = from_subject()

events_observable
|> take_until(shutdown_observable)  // Complete when shutdown emits
|> take(100)                        // Or complete after 100 events
```

### Backpressure Considerations

BEAM actor mailboxes are unbounded, matching Rx's push semantics. For high-volume streams, users should apply backpressure operators before sending to actors:

```gleam
high_volume_stream
|> throttle(100)      // At most 10 messages/second
|> to_subject(actor)
```

### Error Handling

- **`from_subject`**: No error propagation from Subject (BEAM messages don't fail)
- **`to_subject`**: Errors pass through to downstream, not to Subject
- **`call_actor`**: Timeout produces OnError; actor crashes are not detected (standard BEAM behavior)

## File Structure

```text
src/actorx/
├── interop.gleam    # Actor interop operators
└── ...

test/
├── interop_test.gleam # Tests for actor interop
└── ...
```

The main `src/actorx.gleam` re-exports the new operators.

## Test Plan

### `from_subject` Tests

- Values sent to Subject emit to subscribers
- Multiple subscribers all receive values
- Disposal stops subscription
- Works with async values (timer-based sends)

### `to_subject` Tests

- Values forwarded to Subject AND downstream
- OnError/OnCompleted not sent to Subject
- Chaining works correctly

### `call_actor` Tests

- Response received and emitted
- Timeout produces OnError
- Each subscription makes independent call
- Works with flat_map for orchestration
- Disposal cancels pending call (if possible)

## Future Considerations

### Potential Extensions

- **`from_process`**: Monitor a process and emit on exit
- **`supervise`**: Restart observable on actor crash
- **Typed actors**: Integration with `gleam_otp` typed actors

### Integration with gleam_otp

The current design uses raw `Subject(a)` from `gleam/erlang/process`. Future work could integrate with `gleam_otp`'s typed actors for additional type safety.
