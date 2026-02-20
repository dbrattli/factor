# Factor

> **⚠️ Experimental / Work in Progress**

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
open Factor.Rx

// Create a pipeline
let pipeline =
    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Rx.filter (fun x -> x % 2 = 0)   // Keep even numbers
    |> Rx.map (fun x -> x * 10)          // Multiply by 10
    |> Rx.take 3                         // Take first 3

// Create a handler
let handler =
    Rx.makeHandler
        (fun x -> printfn "Value: %d" x)
        (fun _err -> ())
        (fun () -> printfn "Done!")

// Subscribe
let _handle = Rx.subscribe handler pipeline
// Output:
// Value: 20
// Value: 40
// Value: 60
// Done!
```

## Computation Expression

Factor supports F#'s computation expression syntax (`factor { ... }`) for actor orchestration. Each `let!` is a process boundary — it desugars to `flatMap`, which spawns a monitored child process.

```fsharp
open Factor.Rx
open Factor.Builder

let example =
    factor {
        let! x = Rx.single 10
        let! y = Rx.single 20
        let! z = Rx.ofList [ 1; 2; 3 ]
        return x + y + z
    }
// Emits: 31, 32, 33 then completes
```

Use `factor { }` for actor orchestration (process boundaries, supervision) and pipe operators for lightweight in-process data transformation:

```fsharp
// Process-based composition via CE
let pipeline = factor {
    let! data = fetchSource           // spawns child process
    let! result = process data        // spawns child process
    return result
}

// Local composition via pipes (same process, no overhead)
let transformed =
    source
    |> Rx.map transform
    |> Rx.filter isValid
    |> Rx.take 100
```

## Typed Errors

Factor uses a three-level error hierarchy:

```
Level 1: Result<'T, 'E>     — Within expressions, handled locally
Level 2: OnError of 'E      — Stream error, handled by catch/retry/mapError
Level 3: Process crash       — Untyped, caught by monitor, converted to OnError
```

All types carry a typed error parameter `'E`:

```fsharp
type Factor<'T, 'E> = { Subscribe: Handler<'T, 'E> -> Handle }
```

Transform error types with `mapError`, switch to fallbacks with `catch`:

```fsharp
source
|> Rx.catch (fun err -> Rx.single fallbackValue)  // can change error type
|> Rx.mapError (fun e -> CustomError e)            // transform error type
|> Rx.retry 3                                      // resubscribe on error
```

## Async Operators

Factor provides time-based operators for async scenarios. These use native Erlang `erlang:send_after` via the `factor_timer` module, ensuring callbacks execute in the subscriber's process context.

```fsharp
open Factor.Rx

// Emit 0, 1, 2, ... every 100ms, take first 5
let pipeline =
    Rx.interval 100
    |> Rx.take 5
    |> Rx.map (fun x -> x * 10)

let handler =
    Rx.makeHandler
        (fun x -> printfn "%d" x)
        (fun _ -> ())
        (fun () -> printfn "Done!")

let handle = Rx.subscribe handler pipeline
// Output over 500ms: 0, 10, 20, 30, 40, Done!

// Can dispose early to cancel
// handle.Dispose()
```

### Subject Example

Subjects are both Handlers and Factors — push values in, subscribe to receive them:

```fsharp
open Factor.Rx

let input, output = Rx.singleSubject ()

// Subscribe to output
let _handle = Rx.subscribe myHandler output

// Push values through input
Rx.onNext input 1
Rx.onNext input 2
Rx.onCompleted input
```

## Core Concepts

### Factor

A `Factor<'T, 'E>` represents a lazy push-based stream of values `'T` with typed errors `'E`. Factors don't produce values until subscribed to.

### Handler

A `Handler<'T, 'E>` receives notifications from a Factor:

- `OnNext x` — Called for each value
- `OnError e` — Called on error (terminal), with typed error `'E`
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
| `fail error`       | Error immediately with typed error    |
| `ofList items`     | Emit all items from list              |
| `defer factory`    | Create factor lazily on subscribe     |

### Transform

|           Operator            |                              Description                              |
| ----------------------------- | --------------------------------------------------------------------- |
| `map fn source`               | Transform each element                                                |
| `mapi fn source`              | Transform with index: `fn a i -> b`                                   |
| `flatMap fn source`           | Map to factors, merge results (= map + mergeInner)                    |
| `flatMapi fn source`          | Map with index to factors, merge (= mapi + mergeInner)                |
| `concatMap fn source`         | Map to factors, concatenate in order (= map + concatInner)            |
| `concatMapi fn source`        | Map with index to factors, concatenate (= mapi + concatInner)         |
| `switchMap fn source`         | Map to factors, switch to latest (= map + switchInner)                |
| `switchMapi fn source`        | Map with index to factors, switch (= mapi + switchInner)              |
| `mergeInner max source`       | Flatten Factor of Factors with concurrency limit                      |
| `concatInner source`          | Flatten Factor of Factors in order (= mergeInner with max=1)          |
| `switchInner source`          | Flatten Factor of Factors by switching to latest                      |
| `scan init fn source`         | Running accumulation, emit each step                                  |
| `reduce init fn source`       | Final accumulation, emit on completion                                |
| `groupBy keySelector source`  | Group elements into sub-factors by key                                |
| `tap fn source`               | Side effect for each emission, pass through unchanged                 |
| `startWith values source`     | Prepend values before source emissions                                |
| `pairwise source`             | Emit consecutive pairs: `[1;2;3]` → `[(1,2); (2,3)]`                 |

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

### Subject

|      Operator      |                     Description                     |
| ------------------ | --------------------------------------------------- |
| `subject ()`       | Multicast subject, allows multiple subscribers      |
| `singleSubject ()` | Single-subscriber subject, buffers until subscribed  |
| `publish source`   | Connectable hot factor, call connect() to start      |
| `share source`     | Auto-connecting multicast, refCount semantics        |

### Combine

|              Operator               |                    Description                     |
| ----------------------------------- | -------------------------------------------------- |
| `merge sources`                     | Merge multiple factors into one                    |
| `merge2 source1 source2`           | Merge two factors                                   |
| `concat sources`                    | Subscribe to sources sequentially                  |
| `concat2 source1 source2`          | Concatenate two factors                             |
| `amb sources`                       | Race: first source to emit wins, others disposed   |
| `race sources`                      | Alias for `amb`                                    |
| `forkJoin sources`                  | Wait for all to complete, emit list of last values |
| `combineLatest fn s1 s2`           | Combine latest values from two sources             |
| `withLatestFrom fn sampler source` | Sample source with latest from another             |
| `zip fn source1 source2`           | Pair elements by index                             |

### Error Handling

|        Operator         |                  Description                   |
| ----------------------- | ---------------------------------------------- |
| `retry n source`        | Resubscribe on error, up to N retries          |
| `catch handler source`  | On error, switch to fallback (can change error type) |
| `mapError fn source`    | Transform the error type                       |

### Builder (`factor { ... }`)

|         Function          |         Description          |
| ------------------------- | ---------------------------- |
| `let! x = source`         | Bind (flatMap)               |
| `return value`            | Lift value into factor       |
| `return! source`          | Return from factor           |
| `for x in list do`        | Iterate list, concat results |

## Design

### Why F# + BEAM?

Factor uses F# compiled to Erlang via Fable.Beam. The F# type system provides strong typing and inference, while the BEAM runtime provides lightweight processes, fault tolerance, and distribution.

State management uses **mutable variables** which Fable.Beam backs with the Erlang process dictionary. This keeps the implementation simple — all stateful operators run synchronously in the subscriber's process context.

### Timer Architecture

Time-based operators use a native Erlang module (`factor_timer`) that schedules callbacks via `erlang:send_after`. Callbacks are delivered as messages to `self()` and executed by a unified message pump (`process_timers`), ensuring they run in the subscriber's process with access to the process dictionary state.

### Compositional Design

Higher-order operators are composed from primitives:

```fsharp
// flatMap = map + mergeInner (unlimited concurrency)
let flatMap mapper source =
    source |> map mapper |> mergeInner None

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
