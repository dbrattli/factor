# Factor

> **Warning: Experimental / Work in Progress**

Factor is a composable actor framework for the Erlang/BEAM runtime, written in F# and compiled to Erlang via [Fable.Beam](https://github.com/fable-compiler/Fable). Rx-style lazy composition naturally creates OTP supervision trees — the pipeline IS the supervision hierarchy.

## Build

Requires .NET SDK 10+ and the [Fable.Beam](https://github.com/fable-compiler/Fable) compiler.

```sh
just build    # Compile F# to Erlang via Fable.Beam
just check    # Type-check F# with dotnet build
just test     # Run all tests on BEAM
just format   # Format source with Fantomas
```

## Example

```fsharp
open Factor.Types
open Factor.Reactive

// Create a pipeline
let pipeline =
    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Reactive.filter (fun x -> x % 2 = 0)   // Keep even numbers
    |> Reactive.map (fun x -> x * 10)          // Multiply by 10
    |> Reactive.take 3                         // Take first 3

// Create a handler
let handler =
    Reactive.makeHandler
        (fun x -> printfn "Value: %d" x)
        (fun _err -> ())
        (fun () -> printfn "Done!")

// Subscribe
let _handle = Reactive.subscribe handler pipeline
// Output:
// Value: 20
// Value: 40
// Value: 60
// Done!
```

## Computation Expression

Factor supports F#'s computation expression syntax (`factor { ... }`) for actor orchestration. Each `let!` is a process boundary — it desugars to `flatMapSpawned`, which spawns a monitored child process.

```fsharp
open Factor.Reactive
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

Use `factor { }` for actor orchestration (process boundaries, supervision) and pipe operators for lightweight in-process data transformation:

```fsharp
// Process-based composition via CE (each let! spawns a child process)
let pipeline = factor {
    let! data = fetchSource           // spawns child process
    let! result = process data        // spawns child process
    return result
}

// Local composition via pipes (same process, no overhead)
let transformed =
    source
    |> Reactive.map transform
    |> Reactive.filter isValid
    |> Reactive.take 100
```

**CE constraint:** The body of `factor { }` runs in spawned child processes, so it must NOT reference parent process dictionary state (mutable variables, `Dictionary`). Only capture immutable values and actor-based streams.

## Error Handling

Errors are `string` typed throughout:

```fsharp
type Factor<'T> = { Subscribe: Handler<'T> -> Handle }
type Notification<'T> = OnNext of 'T | OnError of string | OnCompleted
```

Process crashes (Level 3) are caught by monitors and converted to `OnError` with a formatted reason string.

```fsharp
source
|> Reactive.catch (fun err -> Reactive.single fallbackValue)  // switch to fallback on error
|> Reactive.retry 3                                           // resubscribe on error
```

## Async Operators

Factor provides time-based operators for async scenarios. These use native Erlang `erlang:send_after` via the `factor_timer` module, ensuring callbacks execute in the subscriber's process context.

```fsharp
open Factor.Reactive

// Emit 0, 1, 2, ... every 100ms, take first 5
let pipeline =
    Reactive.interval 100
    |> Reactive.take 5
    |> Reactive.map (fun x -> x * 10)

let handler =
    Reactive.makeHandler
        (fun x -> printfn "%d" x)
        (fun _ -> ())
        (fun () -> printfn "Done!")

let handle = Reactive.subscribe handler pipeline
// Output over 500ms: 0, 10, 20, 30, 40, Done!

// Can dispose early to cancel
// handle.Dispose()
```

### Stream Example

Streams are both Handlers and Factors — push values in, subscribe to receive them. Each stream is backed by a separate BEAM actor process that manages subscribers via message passing.

```fsharp
open Factor.Reactive

let input, output = Reactive.stream ()

// Subscribe to output
let _handle = Reactive.subscribe myHandler output

// Push values through input
Reactive.onNext input 1
Reactive.onNext input 2
Reactive.onCompleted input
```

## Core Concepts

### Factor

A `Factor<'T>` represents a lazy push-based stream of values `'T` with string errors. Factors don't produce values until subscribed to.

### Handler

A `Handler<'T>` receives notifications from a Factor:

- `OnNext x` — Called for each value
- `OnError e` — Called on error (terminal), with string error message
- `OnCompleted` — Called when complete (terminal)

The Rx contract guarantees: `OnNext* (OnError | OnCompleted)?`

### Handle

A `Handle` represents a subscription that can be cancelled. Call `Dispose()` to unsubscribe and release resources.

## Available Operators

### Creation

|      Operator      |              Description              |
| ------------------ | ------------------------------------- |
| `single value`     | Emit single value, then complete      |
| `empty ()`         | Complete immediately                  |
| `never ()`         | Never emit, never complete            |
| `fail error`       | Error immediately with string error   |
| `ofList items`     | Emit all items from list              |
| `defer factory`    | Create factor lazily on subscribe     |

### Transform

|           Operator            |                              Description                              |
| ----------------------------- | --------------------------------------------------------------------- |
| `map fn source`               | Transform each element                                                |
| `mapi fn source`              | Transform with index: `fn a i -> b`                                   |
| `flatMap fn source`           | Map to factors, merge results (inline, no process spawning)           |
| `flatMapi fn source`          | Map with index to factors, merge (inline)                             |
| `concatMap fn source`         | Map to factors, concatenate in order (= map + concatInner)            |
| `concatMapi fn source`        | Map with index to factors, concatenate (= mapi + concatInner)         |
| `switchMap fn source`         | Map to factors, switch to latest (= map + switchInner)                |
| `switchMapi fn source`        | Map with index to factors, switch (= mapi + switchInner)              |
| `mergeInner max source`       | Flatten Factor of Factors with concurrency limit (inline)             |
| `concatInner source`          | Flatten Factor of Factors in order (= mergeInner with max=1)          |
| `switchInner source`          | Flatten Factor of Factors by switching to latest                      |
| `scan init fn source`         | Running accumulation, emit each step                                  |
| `reduce init fn source`       | Final accumulation, emit on completion                                |
| `groupBy keySelector source`  | Group elements into sub-factors by key                                |
| `tap fn source`               | Side effect for each emission, pass through unchanged                 |
| `startWith values source`     | Prepend values before source emissions                                |
| `pairwise source`             | Emit consecutive pairs: `[1;2;3]` -> `[(1,2); (2,3)]`                 |

### Filter

|             Operator              |               Description                |
| --------------------------------- | ---------------------------------------- |
| `filter predicate source`         | Keep elements matching predicate         |
| `take n source`                   | Take first N elements                    |
| `skip n source`                   | Skip first N elements                    |
| `takeWhile predicate source`      | Take while predicate is true             |
| `skipWhile predicate source`      | Skip while predicate is true             |
| `choose fn source`                | Filter + map via Option                  |
| `distinctUntilChanged source`     | Skip consecutive duplicates              |
| `distinct source`                 | Filter ALL duplicates (seen set)         |
| `takeUntil other source`          | Take until other factor emits            |
| `takeLast n source`               | Emit last N elements on completion       |
| `first source`                    | Take only first element (error if empty) |
| `last source`                     | Take only last element (error if empty)  |
| `defaultIfEmpty val source`       | Emit default if source is empty          |
| `sample sampler source`           | Sample source when sampler emits         |

### Timeshift (Async)

|          Operator          |                  Description                  |
| -------------------------- | --------------------------------------------- |
| `timer delayMs`            | Emit `0` after delay, then complete           |
| `interval periodMs`        | Emit 0, 1, 2, ... at regular intervals        |
| `delay ms source`          | Delay each emission by specified time         |
| `debounce ms source`       | Emit only after silence period                |
| `throttle ms source`       | Rate limit to at most one per period          |
| `timeout ms source`        | Error if no emission within timeout period    |

### Stream

|      Operator      |                       Description                       |
| ------------------ | ------------------------------------------------------- |
| `stream ()`        | Multicast stream actor, allows multiple subscribers     |
| `singleStream ()`  | Single-subscriber stream actor, buffers until subscribed|
| `publish source`   | Connectable hot factor, call connect() to start         |
| `share source`     | Auto-connecting multicast, refCount semantics           |

### Combine

|              Operator              |                    Description                     |
| ---------------------------------- | -------------------------------------------------- |
| `merge sources`                    | Merge multiple factors into one                    |
| `merge2 source1 source2`           | Merge two factors                                  |
| `concat sources`                   | Subscribe to sources sequentially                  |
| `concat2 source1 source2`          | Concatenate two factors                            |
| `amb sources`                      | Race: first source to emit wins, others disposed   |
| `race sources`                     | Alias for `amb`                                    |
| `forkJoin sources`                 | Wait for all to complete, emit list of last values |
| `combineLatest fn s1 s2`           | Combine latest values from two sources             |
| `withLatestFrom fn sampler source` | Sample source with latest from another             |
| `zip fn source1 source2`           | Pair elements by index                             |

### Error Handling

|        Operator        |                     Description                      |
| ---------------------- | ---------------------------------------------------- |
| `retry n source`       | Resubscribe on error, up to N retries                |
| `catch handler source` | On error, switch to fallback factor                  |

### Builder (`factor { ... }`)

|         Function          |                   Description                    |
| ------------------------- | ------------------------------------------------ |
| `let! x = source`         | Bind (flatMapSpawned — spawns child process)    |
| `return value`            | Lift value into factor                           |
| `return! source`          | Return from factor                               |
| `for x in list do`        | Iterate list, concat results                     |

## Design

### Why F# + BEAM?

Factor uses F# compiled to Erlang via Fable.Beam. The F# type system provides strong typing and inference, while the BEAM runtime provides lightweight processes, fault tolerance, and distribution.

### Two Composition Modes

Factor provides two ways to compose streams, reflecting the tension between mutable state and process isolation on BEAM:

**Pipe operators** (`flatMap`, `mergeInner`, etc.) subscribe inline in the same process. They can safely access mutable state (process dictionary, `Dictionary`, `HashSet`) but don't create supervision boundaries.

**Computation expression** (`factor { let! ... }`) spawns a linked child process for each `let!`. This creates supervision boundaries — child crashes are caught by monitors and converted to `OnError`. However, the CE body must NOT reference parent process dictionary state, since it runs in a different process.

```fsharp
// Inline — safe for stateful operators like groupBy
source |> Reactive.flatMap (fun x -> ...)

// Spawning — creates supervision tree
factor {
    let! x = source   // child process
    let! y = other     // child process
    return x + y
}
```

### Stream Actors

Each stream (`stream()`, `singleStream()`) is backed by a separate BEAM actor process that manages a subscriber map. This solves the cross-process mutable state problem: when a child process subscribes to a stream, the stream actor handles the subscriber registration via message passing rather than process dictionary access.

Subscribe is **synchronous** (send + wait for ack) to prevent races between subscribe and first notify. The stream actor is linked to its creator via `spawn_link`.

### Message Dispatch

Notifications from stream actors and timer callbacks are delivered as messages. Any process that subscribes to streams or uses time-based operators must handle these messages:

- `{factor_timer, Ref, Callback}` — Timer callbacks from `erlang:send_after`
- `{factor_child, Ref, Notification}` — Notifications forwarded from stream actors or child processes

The `process_timers` loop (for subscribers) and `child_loop` (for spawned children) handle both message types.

### Timer Architecture

Time-based operators use a native Erlang module (`factor_timer`) that schedules callbacks via `erlang:send_after`. Callbacks are delivered as messages to `self()` and executed by a unified message pump (`process_timers`), ensuring they run in the subscriber's process with access to the process dictionary state.

### Compositional Design

Higher-order operators are composed from primitives:

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

The `mergeInner` operator accepts a `maxConcurrency` parameter:

- `None` — unlimited concurrency (subscribe to all inner factors immediately)
- `Some 1` — sequential processing (equivalent to `concatInner`)
- `Some n` — at most n inner factors active, queue the rest

### Safe Handler

The `SafeHandler` module enforces the Rx grammar:

- Tracks "stopped" state
- Ignores events after terminal
- Calls disposal on terminal events

## Development

```sh
just build    # Compile F# to Erlang via Fable.Beam
just check    # Type-check with dotnet build
just test     # Run tests on BEAM
just format   # Format with Fantomas
just clean    # Clean build artifacts
just all      # Check and build
```

## License

MIT

## Related Projects

- [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx) — Original F# implementation
- [Fable.Beam](https://github.com/fable-compiler/Fable) — F# to Erlang compiler
- [RxPY](https://github.com/ReactiveX/RxPY) — ReactiveX for Python
- [Reaxive](https://github.com/alfert/reaxive) — ReactiveX for Elixir
