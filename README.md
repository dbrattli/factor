# ActorX for Gleam

[![Package Version](https://img.shields.io/hexpm/v/actorx)](https://hex.pm/packages/actorx)
[![Hex Docs](https://img.shields.io/badge/hex-docs-ffaff3)](https://hexdocs.pm/actorx/)

ActorX - Reactive Extensions for Gleam using BEAM actors. A reactive programming library
for composing BEAM actors and building asynchronous, event-driven applications.

## Installation

```sh
gleam add actorx
```

## Example

```gleam
import actorx
import gleam/io
import gleam/int

pub fn main() {
  // Create an observable pipeline
  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    |> actorx.filter(fn(x) { x % 2 == 0 })  // Keep even numbers
    |> actorx.map(fn(x) { x * 10 })         // Multiply by 10
    |> actorx.take(3)                       // Take first 3

  // Create an observer
  let observer = actorx.make_observer(
    on_next: fn(x) { io.println("Value: " <> int.to_string(x)) },
    on_error: fn(_err) { Nil },
    on_completed: fn() { io.println("Done!") },
  )

  // Subscribe
  let _disposable = actorx.subscribe(observable, observer)
  // Output:
  // Value: 20
  // Value: 40
  // Value: 60
  // Done!
}
```

## Builder Pattern with `use`

ActorX supports Gleam's `use` keyword for monadic composition, similar to F#'s computation expressions:

```gleam
import actorx
import actorx/builder.{bind, return}

pub fn example() {
  use x <- bind(actorx.single(10))
  use y <- bind(actorx.single(20))
  use z <- bind(actorx.from_list([1, 2, 3]))
  return(x + y + z)
}
// Emits: 31, 32, 33 then completes
```

## Async Operators

ActorX provides time-based operators for async scenarios:

```gleam
import actorx
import actorx/types.{Disposable}
import gleam/io
import gleam/int

pub fn async_example() {
  // Emit 0, 1, 2, ... every 100ms, take first 5
  let observable = actorx.interval(100)
    |> actorx.take(5)
    |> actorx.map(fn(x) { x * 10 })

  let observer = actorx.make_observer(
    on_next: fn(x) { io.println(int.to_string(x)) },
    on_error: fn(_) { Nil },
    on_completed: fn() { io.println("Done!") },
  )

  let Disposable(dispose) = actorx.subscribe(observable, observer)
  // Output over 500ms: 0, 10, 20, 30, 40, Done!

  // Can dispose early to cancel
  // dispose()
}
```

### Subject Example

Subjects are both Observers and Observables - push values in, subscribe to receive them:

```gleam
import actorx

pub fn subject_example() {
  let #(input, output) = actorx.single_subject()

  // Subscribe to output
  let _disp = actorx.subscribe(output, my_observer)

  // Push values through input
  actorx.on_next(input, 1)
  actorx.on_next(input, 2)
  actorx.on_completed(input)
}
```

## Core Concepts

### Observable

An `Observable(a)` represents a push-based stream of values of type `a`. Observables are lazy - they don't produce values until subscribed to.

### Observer

An `Observer(a)` receives notifications from an Observable:

- `on_next(a)` - Called for each value
- `on_error(String)` - Called on error (terminal)
- `on_completed()` - Called when complete (terminal)

The Rx contract guarantees: `OnNext* (OnError | OnCompleted)?`

### Disposable

A `Disposable` represents a subscription that can be cancelled. Call `dispose()` to unsubscribe and release resources.

## Available Operators

### Creation

|      Operator      |              Description              |
| ------------------ | ------------------------------------- |
| `single(value)`    | Emit single value, then complete      |
| `empty()`          | Complete immediately                  |
| `never()`          | Never emit, never complete            |
| `fail(error)`      | Error immediately                     |
| `from_list(items)` | Emit all items from list              |
| `defer(factory)`   | Create observable lazily on subscribe |

### Transform

| Operator | Description |
| --- | --- |
| `map(source, fn)` | Transform each element |
| `mapi(source, fn)` | Transform with index: `fn(a, Int) -> b` |
| `flat_map(source, fn)` | Map to observables, merge results (= map + merge_inner) |
| `flat_mapi(source, fn)` | Map with index to observables, merge (= mapi + merge_inner) |
| `concat_map(source, fn)` | Map to observables, concatenate in order (= map + concat_inner) |
| `concat_mapi(source, fn)` | Map with index to observables, concatenate (= mapi + concat_inner) |
| `switch_map(source, fn)` | Map to observables, switch to latest (= map + switch_inner) |
| `switch_mapi(source, fn)` | Map with index to observables, switch (= mapi + switch_inner) |
| `merge_inner(source, max)` | Flatten Observable(Observable(a)) with concurrency limit |
| `concat_inner(source)` | Flatten Observable(Observable(a)) in order (= merge_inner with max=1) |
| `switch_inner(source)` | Flatten Observable(Observable(a)) by switching to latest |
| `scan(source, init, fn)` | Running accumulation, emit each step |
| `reduce(source, init, fn)` | Final accumulation, emit on completion |
| `group_by(source, fn)` | Group elements into sub-observables by key |
| `tap(source, fn)` | Side effect for each emission, pass through unchanged |
| `start_with(source, values)` | Prepend values before source emissions |
| `pairwise(source)` | Emit consecutive pairs: `[1,2,3]` → `[#(1,2), #(2,3)]` |

### Filter

| Operator | Description |
| --- | --- |
| `filter(source, predicate)` | Keep elements matching predicate |
| `take(source, n)` | Take first N elements |
| `skip(source, n)` | Skip first N elements |
| `take_while(source, predicate)` | Take while predicate is true |
| `skip_while(source, predicate)` | Skip while predicate is true |
| `choose(source, fn)` | Filter + map via Option |
| `distinct_until_changed(source)` | Skip consecutive duplicates |
| `distinct(source)` | Filter ALL duplicates (seen list) |
| `take_until(source, other)` | Take until other observable emits |
| `take_last(source, n)` | Emit last N elements on completion |
| `first(source)` | Take only first element (error if empty) |
| `last(source)` | Take only last element (error if empty) |
| `default_if_empty(source, val)` | Emit default if source is empty |
| `sample(source, sampler)` | Sample source when sampler emits |

### Timeshift (Async)

|          Operator          |                  Description                  |
| -------------------------- | --------------------------------------------- |
| `timer(delay_ms)`          | Emit `0` after delay, then complete           |
| `interval(period_ms)`      | Emit 0, 1, 2, ... at regular intervals        |
| `delay(source, ms)`        | Delay each emission by specified time         |
| `debounce(source, ms)`     | Emit only after silence period                |
| `throttle(source, ms)`     | Rate limit to at most one per period          |
| `timeout(source, ms)`      | Error if no emission within timeout period    |

### Subject

|      Operator      |                     Description                     |
| ------------------ | --------------------------------------------------- |
| `subject()`        | Multicast subject, allows multiple subscribers      |
| `single_subject()` | Single-subscriber subject, buffers until subscribed |
| `publish(source)`  | Connectable hot observable, call connect() to start |
| `share(source)`    | Auto-connecting multicast, refCount semantics       |

### Combine

| Operator | Description |
| --- | --- |
| `merge(sources)` | Merge multiple observables into one |
| `merge2(source1, source2)` | Merge two observables |
| `concat(sources)` | Subscribe to sources sequentially |
| `concat2(source1, source2)` | Concatenate two observables |
| `amb(sources)` | Race: first source to emit wins, others disposed |
| `race(sources)` | Alias for `amb` |
| `fork_join(sources)` | Wait for all to complete, emit list of last values |
| `combine_latest(s1, s2, fn)` | Combine latest values from two sources |
| `with_latest_from(source, s2, fn)` | Sample source with latest from another |
| `zip(source1, source2, fn)` | Pair elements by index |

### Error Handling

|        Operator         |                  Description                   |
| ----------------------- | ---------------------------------------------- |
| `retry(source, n)`      | Resubscribe on error, up to N retries          |
| `catch(source, fn)`     | On error, switch to fallback from handler      |

### Builder (for `use` syntax)

|         Function          |         Description          |
| ------------------------- | ---------------------------- |
| `bind(source, fn)`        | FlatMap for `use` syntax     |
| `return(value)`           | Lift value into observable   |
| `map_over(source, fn)`    | Map for `use` syntax         |
| `filter_with(source, fn)` | Filter for `use` syntax      |
| `for_each(list, fn)`      | Iterate list, concat results |

## Design

### Why Gleam + BEAM?

The original F# AsyncRx uses `MailboxProcessor` (actors) to:

1. Serialize notifications (no concurrent observer calls)
2. Enforce Rx grammar
3. Manage state safely

Gleam on BEAM is a natural fit because:

- BEAM has native lightweight processes (actors)
- OTP provides battle-tested actor primitives
- Millions of concurrent subscriptions are feasible
- Supervision trees for fault tolerance

### Architecture

```text
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  Observable │────▶│  Operator   │────▶│  Observer   │
│   (source)  │     │  (transform)│     │  (sink)     │
└─────────────┘     └─────────────┘     └─────────────┘
                           │
                    ┌──────┴──────┐
                    │ State Actor │
                    │ (for take,  │
                    │  skip, etc) │
                    └─────────────┘
```

Each stateful operator can use an actor to maintain state safely across async boundaries.

### Current Implementation

ActorX uses two complementary approaches for state management:

**Synchronous operators** (like `from_list`, `map`, `filter`) use Erlang's process dictionary for mutable state:

- Simple and performant for sync sources
- Avoids actor overhead for simple cases
- Works within a single process context

**Asynchronous operators** (like `timer`, `interval`, `delay`, `debounce`, `throttle`) use spawned processes:

- Each operator spawns a worker process that owns its state
- Communication via message passing with Gleam `Subject`
- Proper disposal via control messages to workers
- Safe across async boundaries

### Compositional Design

Higher-order operators are composed from primitives, following the standard Rx pattern:

```gleam
// flat_map = map + merge_inner (unlimited concurrency)
pub fn flat_map(source, mapper) {
  source |> map(mapper) |> merge_inner(None)
}

// concat_map = map + concat_inner (sequential)
pub fn concat_map(source, mapper) {
  source |> map(mapper) |> concat_inner()
}

// concat_inner is merge_inner with max_concurrency=1
pub fn concat_inner(source) {
  merge_inner(source, Some(1))
}
```

The `merge_inner` operator accepts a `max_concurrency` parameter:

- `None` - unlimited concurrency (subscribe to all inner observables immediately)
- `Some(1)` - sequential processing (equivalent to `concat_inner`)
- `Some(n)` - at most n inner observables active, queue the rest

This enables building new operators (like `switch_map = map + switch_inner`) from reusable primitives.

### Safe Observer

The `safe_observer` module provides Rx grammar enforcement:

- Tracks "stopped" state
- Ignores events after terminal
- Calls disposal on terminal events

## Actor Interop

ActorX provides operators for bridging between reactive streams and BEAM actors/processes.

### Creating Observables from Subjects

```gleam
import actorx
import gleam/erlang/process

pub fn from_subject_example() {
  // Create a bridge: process.Subject for pushing, Observable for consuming
  let #(subject, observable) = actorx.from_subject()

  // Subscribe to the observable
  let _disp = actorx.subscribe(observable, my_observer)

  // Push values from anywhere (other processes, callbacks, etc.)
  process.send(subject, 1)
  process.send(subject, 2)
  process.send(subject, 3)
}
```

### Sending to Actors

```gleam
import actorx

pub fn to_subject_example() {
  let actor_subject = get_some_actor_subject()

  // Forward all emissions to an actor while passing through
  actorx.interval(100)
  |> actorx.take(5)
  |> actorx.to_subject(actor_subject)
  |> actorx.subscribe(my_observer)
}
```

### Request-Response with Actors

```gleam
import actorx

pub fn call_actor_example() {
  let actor = get_some_actor()

  // Send request to actor, emit response as observable
  actorx.call_actor(actor, 1000, GetValue)
  |> actorx.map(fn(response) { process_response(response) })
  |> actorx.subscribe(my_observer)
}
```

### Interop Operators

|             Operator              |                    Description                     |
| --------------------------------- | -------------------------------------------------- |
| `from_subject()`                  | Create Subject/Observable pair for pushing values  |
| `to_subject(source, subj)`        | Forward emissions to a process.Subject             |
| `call_actor(actor, timeout, msg)` | Request-response call, emit response as observable |

## Examples

### Timeflies Demo

A classic Rx demo where letters trail behind your mouse cursor. Demonstrates `subject`, `flat_map`, `delay`, and WebSocket integration with Mist.

```sh
cd examples/timeflies
gleam run
# Open http://localhost:3000
```

## Development

```sh
gleam build  # Build the project
gleam test   # Run the tests
```

## License

MIT

## Related Projects

- [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx) - Original F# implementation
- [RxPY](https://github.com/ReactiveX/RxPY) - ReactiveX for Python
- [Reaxive](https://github.com/alfert/reaxive) - ReactiveX for Elixir
- [GenStage](https://github.com/elixir-lang/gen_stage) - Elixir's demand-driven streams
