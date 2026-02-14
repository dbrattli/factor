# Factor

> **⚠️ Experimental / Work in Progress**
>
> This library is a learning project exploring Rx patterns on the BEAM. It is **not production-ready** and has known limitations with OTP integration (no process monitoring, no supervision trees).

Factor is a Reactive Extensions (Rx) library for the Erlang/BEAM runtime, written in F# and compiled to Erlang via [Fable.Beam](https://github.com/nicklaskno/fable-beam). It is a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx).

## Build

Requires .NET SDK 8+ and the [Fable.Beam](https://github.com/nicklaskno/fable-beam) compiler.

```sh
just build    # Compile F# to Erlang via Fable.Beam
just check    # Type-check F# with dotnet build
just test     # Run all 273 tests on BEAM
just format   # Format source with Fantomas
```

## Example

```fsharp
open Factor.Types
open Factor.Rx

// Create an observable pipeline
let observable =
    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Rx.filter (fun x -> x % 2 = 0)   // Keep even numbers
    |> Rx.map (fun x -> x * 10)          // Multiply by 10
    |> Rx.take 3                         // Take first 3

// Create an observer
let observer =
    Rx.makeObserver
        (fun x -> printfn "Value: %d" x)
        (fun _err -> ())
        (fun () -> printfn "Done!")

// Subscribe
let _disposable = Rx.subscribe observer observable
// Output:
// Value: 20
// Value: 40
// Value: 60
// Done!
```

## Computation Expression

Factor supports F#'s computation expression syntax (`rx { ... }`) for monadic composition:

```fsharp
open Factor.Rx
open Factor.Builder

let example =
    rx {
        let! x = Rx.single 10
        let! y = Rx.single 20
        let! z = Rx.ofList [ 1; 2; 3 ]
        return x + y + z
    }
// Emits: 31, 32, 33 then completes
```

## Async Operators

Factor provides time-based operators for async scenarios. These use native Erlang `erlang:send_after` via the `factor_timer` module, ensuring callbacks execute in the subscriber's process context.

```fsharp
open Factor.Rx

// Emit 0, 1, 2, ... every 100ms, take first 5
let observable =
    Rx.interval 100
    |> Rx.take 5
    |> Rx.map (fun x -> x * 10)

let observer =
    Rx.makeObserver
        (fun x -> printfn "%d" x)
        (fun _ -> ())
        (fun () -> printfn "Done!")

let disposable = Rx.subscribe observer observable
// Output over 500ms: 0, 10, 20, 30, 40, Done!

// Can dispose early to cancel
// disposable.Dispose()
```

### Subject Example

Subjects are both Observers and Observables — push values in, subscribe to receive them:

```fsharp
open Factor.Rx

let input, output = Rx.singleSubject ()

// Subscribe to output
let _disp = Rx.subscribe myObserver output

// Push values through input
Rx.onNext input 1
Rx.onNext input 2
Rx.onCompleted input
```

## Core Concepts

### Observable

An `Observable<'a>` represents a push-based stream of values of type `'a`. Observables are lazy — they don't produce values until subscribed to.

### Observer

An `Observer<'a>` receives notifications from an Observable:

- `OnNext x` — Called for each value
- `OnError msg` — Called on error (terminal)
- `OnCompleted` — Called when complete (terminal)

The Rx contract guarantees: `OnNext* (OnError | OnCompleted)?`

### Disposable

A `Disposable` represents a subscription that can be cancelled. Call `Dispose()` to unsubscribe and release resources.

## Available Operators

### Creation

|      Operator      |              Description              |
| ------------------ | ------------------------------------- |
| `single value`     | Emit single value, then complete      |
| `empty ()`         | Complete immediately                  |
| `never ()`         | Never emit, never complete            |
| `fail error`       | Error immediately                     |
| `ofList items`     | Emit all items from list              |
| `defer factory`    | Create observable lazily on subscribe |

### Transform

|           Operator            |                              Description                              |
| ----------------------------- | --------------------------------------------------------------------- |
| `map fn source`               | Transform each element                                                |
| `mapi fn source`              | Transform with index: `fn i a -> b`                                   |
| `flatMap fn source`           | Map to observables, merge results (= map + mergeInner)                |
| `flatMapi fn source`          | Map with index to observables, merge (= mapi + mergeInner)            |
| `concatMap fn source`         | Map to observables, concatenate in order (= map + concatInner)        |
| `concatMapi fn source`        | Map with index to observables, concatenate (= mapi + concatInner)     |
| `switchMap fn source`         | Map to observables, switch to latest (= map + switchInner)            |
| `switchMapi fn source`        | Map with index to observables, switch (= mapi + switchInner)          |
| `mergeInner max source`       | Flatten Observable of Observables with concurrency limit              |
| `concatInner source`          | Flatten Observable of Observables in order (= mergeInner with max=1)  |
| `switchInner source`          | Flatten Observable of Observables by switching to latest              |
| `scan init fn source`         | Running accumulation, emit each step                                  |
| `reduce init fn source`       | Final accumulation, emit on completion                                |
| `groupBy keySelector source`  | Group elements into sub-observables by key                            |
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
| `takeUntil other source`          | Take until other observable emits        |
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
| `publish source`   | Connectable hot observable, call connect() to start  |
| `share source`     | Auto-connecting multicast, refCount semantics        |

### Combine

|              Operator               |                    Description                     |
| ----------------------------------- | -------------------------------------------------- |
| `merge sources`                     | Merge multiple observables into one                |
| `merge2 source1 source2`           | Merge two observables                              |
| `concat sources`                    | Subscribe to sources sequentially                  |
| `concat2 source1 source2`          | Concatenate two observables                        |
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
| `catch handler source`  | On error, switch to fallback from handler      |

### Builder (`rx { ... }`)

|         Function          |         Description          |
| ------------------------- | ---------------------------- |
| `let! x = source`         | Bind (flatMap)               |
| `return value`            | Lift value into observable   |
| `return! source`          | Return from observable       |
| `for x in list do`        | Iterate list, concat results |

## Design

### Why F# + BEAM?

Factor uses F# compiled to Erlang via Fable.Beam. The F# type system provides strong typing and inference, while the BEAM runtime provides lightweight processes, fault tolerance, and distribution.

State management uses **mutable variables** which Fable.Beam backs with the Erlang process dictionary. This keeps the implementation simple — all stateful operators run synchronously in the subscriber's process context.

### Timer Architecture

Time-based operators use a native Erlang module (`factor_timer`) that schedules callbacks via `erlang:send_after`. Callbacks are delivered as messages to `self()` and executed by a timer pump (`process_timers`), ensuring they run in the subscriber's process with access to the process dictionary state.

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

- `None` — unlimited concurrency (subscribe to all inner observables immediately)
- `Some 1` — sequential processing (equivalent to `concatInner`)
- `Some n` — at most n inner observables active, queue the rest

### Safe Observer

The `SafeObserver` module enforces the Rx grammar:

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
- [Fable.Beam](https://github.com/nicklaskno/fable-beam) — F# to Erlang compiler
- [RxPY](https://github.com/ReactiveX/RxPY) — ReactiveX for Python
- [Reaxive](https://github.com/alfert/reaxive) — ReactiveX for Elixir
