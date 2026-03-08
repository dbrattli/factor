# Factor

> **Warning: Experimental / Work in Progress**

Factor is a composable actor framework for the Erlang/BEAM runtime, written in F# and compiled to Erlang via [Fable.Beam](https://github.com/fable-compiler/Fable). Every operator is a BEAM process — Rx-style lazy composition naturally creates OTP supervision trees where the pipeline IS the supervision hierarchy.

## Build

Requires .NET SDK 10+ and the [Fable.Beam](https://github.com/fable-compiler/Fable) compiler.

```sh
just build    # Compile F# to Erlang via Fable.Beam
just check    # Type-check F# with dotnet build
just test     # Run all tests on BEAM
just format   # Format source with Fantomas
```

## Architecture

Factor is organized in clean layers, each building on the previous:

```text
Process → Agent → Observer/Observable → Channel → Composed Operators
```

|     Layer      |                                 Module                                 |                                 Purpose                                 |
| -------------- | ---------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| **Process**    | `Process.fs`                                                           | BEAM primitives: spawn, link, kill, refs, observer message protocol     |
| **Agent**      | `Agent.fs`                                                             | Typed agent abstraction: `agent { }` CE, spawn, start, send, call       |
| **Observable** | `Types.fs`                                                             | `Observable<'T>`, `Observer<'T>`, `Msg<'T>`, `Handle`                   |
| **Channel**    | `Channel.fs`                                                           | Agent-parameterized channels: multicast, singleSubscriber, push helpers |
| **Operators**  | `Create.fs`, `Transform.fs`, `Filter.fs`, `Combine.fs`, `TimeShift.fs` | Composed operators                                                      |

Each layer restricts the one below to enable composition:

- **Process** — raw BEAM processes, any message, any sender
- **Agent** — typed message passing, structured send/call/reply
- **Observer/Observable** — Rx grammar (`OnNext* (OnError | OnCompleted)?`), lazy subscribe, process-per-operator
- **Channel** — agent-parameterized wormholes bridging push and subscribe
- **Operators** — composed from the above, each spawning a BEAM process

## Example

```fsharp
open Factor.Agent.Types
open Factor.Reactive

// Create a pipeline — each operator spawns a BEAM process
let pipeline =
    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Reactive.filter (fun x -> x % 2 = 0)   // Keep even numbers
    |> Reactive.map (fun x -> x * 10)          // Multiply by 10
    |> Reactive.take 3                         // Take first 3

// Subscribe with callbacks
let _handle =
    pipeline
    |> Reactive.subscribe
        (fun x -> printfn "Value: %d" x)
        (fun _err -> ())
        (fun () -> printfn "Done!")
// Output:
// Value: 20
// Value: 40
// Value: 60
// Done!
```

## Computation Expressions

Factor provides two computation expressions for different concurrency patterns.

### `observable { ... }` — Observable Composition

The `observable` CE composes reactive observables with supervision. Each `let!` desugars to `flatMap`, which spawns a linked child process per inner observable.

```fsharp
open Factor.Agent.Types
open Factor.Reactive
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

Since every operator is a BEAM process, both `observable { ... }` and pipe operators create process-based pipelines. The CE provides monadic syntax sugar for `flatMap`:

```fsharp
// Monadic composition via CE (desugars to flatMap)
let pipeline = observable {
    let! data = fetchSource           // spawns process
    let! result = process data        // spawns process
    return result
}

// Equivalent using pipe operators
let pipeline =
    fetchSource
    |> Reactive.flatMap (fun data ->
        process data)
```

**CE constraint:** All operator callbacks run in spawned processes. Capture only immutable values and agent-based channels — do not reference mutable state from outer scopes.

### `agent { ... }` — Typed Agents

The `agent` CE provides typed message-passing actors. `Agent.start` creates a stateful agent with a message handler (gen_server style). `Agent.spawn` creates a raw agent process.

```fsharp
open Factor.Agent.Types
open Factor.Beam.Agent

type CounterMsg =
    | Increment
    | GetCount of ReplyChannel<int>

let counter = Agent.start 0 (fun count msg ->
    match msg with
    | Increment -> Continue (count + 1)
    | GetCount rc ->
        rc.Reply count
        Continue count)

Agent.send counter Increment
Agent.send counter Increment
let count = Agent.call counter (fun rc -> GetCount rc)
// count = 2
```

Key features:

- **Typed Agents** — `Agent<'Msg>` ensures only correctly-typed messages can be sent
- **`Agent.start`** — stateful agent with message handler loop
- **`Agent.spawn`** — raw agent process
- **`Agent.send`** — fire-and-forget message send
- **`Agent.call`** — synchronous request-response with `ReplyChannel`

## Error Handling

Errors are `exn` (exception) typed, enabling pattern matching on specific error categories:

```fsharp
type Msg<'T> = OnNext of 'T | OnError of exn | OnCompleted

// Pre-defined exception types
exception FactorException of string           // general purpose
exception TimeoutException of string          // timeout operator
exception SequenceEmptyException              // first/last on empty sequence
exception ProcessExitException of string      // child process crash
exception ForkJoinException of string         // forkJoin: source completed without emitting
```

Process crashes are caught by monitors and converted to `OnError(ProcessExitException ...)`.

```fsharp
source
|> Reactive.catch (fun err ->
    match err with
    | :? TimeoutException -> Reactive.single fallbackValue
    | _ -> Reactive.fail err)                                 // re-raise other errors
|> Reactive.retry 3                                           // re-subscribe on error
```

## Async Operators

Factor provides time-based operators for async scenarios. These use native Erlang `erlang:send_after` via the `factor_timer` module, ensuring callbacks execute in the operator's process context.

```fsharp
open Factor.Agent.Types
open Factor.Reactive

// Emit 0, 1, 2, ... every 100ms, take first 5
let pipeline =
    Reactive.interval 100
    |> Reactive.take 5
    |> Reactive.map (fun x -> x * 10)

let handle =
    pipeline
    |> Reactive.subscribe
        (fun x -> printfn "%d" x)
        (fun _ -> ())
        (fun () -> printfn "Done!")
// Output over 500ms: 0, 10, 20, 30, 40, Done!

// Can dispose early to cancel
// handle.Dispose()
```

### Channel Example

Channels separate the push side (`Observer<'T>`) from the subscribe side (`Observable<'T>`). Each channel is backed by an `Agent<ChannelMsg<'T>>` that manages subscribers via message passing.

```fsharp
open Factor.Agent.Types
open Factor.Reactive

let observer, output = Reactive.multicast ()

// Subscribe to output
let _handle =
    output
    |> Reactive.subscribe
        (fun x -> printfn "Got: %d" x)
        (fun _err -> ())
        (fun () -> printfn "Done!")

// Push values through observer
Reactive.pushNext observer 1
Reactive.pushNext observer 2
Reactive.pushCompleted observer
```

## Core Concepts

### Observable

An `Observable<'T>` represents a lazy push-based stream blueprint. Observables don't produce values until subscribed to. Each operator spawns a BEAM process.

### Observer

An `Observer<'T>` is a process endpoint `{ Pid: obj; Ref: obj }` that receives messages. Messages are delivered as Erlang messages to the observer's process (`Pid`) tagged with the reference (`Ref`).

The Rx grammar is enforced: `OnNext* (OnError | OnCompleted)?`

### Channel

Channels bridge agents and observables. `multicast()` returns `Observer<'T> * Observable<'T>` — the `Observer` is the push side (send via `pushNext`/`pushError`/`pushCompleted`), the `Observable` is the subscribe side.

- **`multicast()`** — multiple subscribers, no buffering
- **`singleSubscriber()`** — single subscriber with buffering before subscribe

### Handle

A `Handle` represents a subscription that can be cancelled. Call `Dispose()` to unsubscribe and release resources.

## Available Operators

### Creation

|      Operator      |              Description              |
| ------------------ | ------------------------------------- |
| `single value`     | Emit single value, then complete      |
| `empty ()`         | Complete immediately                  |
| `never ()`         | Never emit, never complete            |
| `fail error`       | Error immediately with given exception |
| `ofList items`     | Emit all items from list              |
| `defer factory`    | Create observable lazily on subscribe |

### Transform

|           Operator            |                              Description                              |
| ----------------------------- | --------------------------------------------------------------------- |
| `map fn source`               | Transform each element                                                |
| `mapi fn source`              | Transform with index: `fn a i -> b`                                   |
| `flatMap fn source`           | Map to observables, merge results (spawns process per inner)          |
| `flatMapi fn source`          | Map with index to observables, merge (spawns process per inner)       |
| `concatMap fn source`         | Map to observables, concatenate in order (= map + concatInner)        |
| `concatMapi fn source`        | Map with index to observables, concatenate (= mapi + concatInner)     |
| `switchMap fn source`         | Map to observables, switch to latest (= map + switchInner)            |
| `switchMapi fn source`        | Map with index to observables, switch (= mapi + switchInner)          |
| `mergeInner policy max source` | Flatten Observable of Observables with merge policy and concurrency limit |
| `concatInner source`          | Flatten Observable of Observables in order (= mergeInner with max=1)  |
| `switchInner source`          | Flatten Observable of Observables by switching to latest              |
| `scan init fn source`         | Running accumulation, emit each step                                  |
| `reduce init fn source`       | Final accumulation, emit on completion                                |
| `groupBy keySelector source`  | Group elements into sub-observables by key                            |
| `tap fn source`               | Side effect for each emission, pass through unchanged                 |
| `startWith values source`     | Prepend values before source emissions                                |
| `pairwise source`             | Emit consecutive pairs: `[1;2;3]` -> `[(1,2); (2,3)]`                |

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

### Channel

|        Operator             |                       Description                       |
| --------------------------- | ------------------------------------------------------- |
| `multicast ()`              | Returns `Observer<'T> * Observable<'T>`, multicast channel agent |
| `singleSubscriber ()`       | Returns `Observer<'T> * Observable<'T>`, single-subscriber with buffering |
| `channel agent`             | Wrap any `Agent<ChannelMsg<'T>>` into Observer + Observable |
| `publish source`            | Connectable hot observable, call connect() to start     |
| `share source`              | Auto-connecting multicast, refCount semantics           |

### Combine

|              Operator              |                    Description                     |
| ---------------------------------- | -------------------------------------------------- |
| `merge sources`                    | Merge multiple observables into one                |
| `merge2 source1 source2`           | Merge two observables                              |
| `concat sources`                   | Subscribe to sources sequentially                  |
| `concat2 source1 source2`          | Concatenate two observables                        |
| `amb sources`                      | Race: first source to emit wins, others disposed   |
| `race sources`                     | Alias for `amb`                                    |
| `forkJoin sources`                 | Wait for all to complete, emit list of last values |
| `combineLatest fn s1 s2`           | Combine latest values from two sources             |
| `withLatestFrom fn sampler source` | Sample source with latest from another             |
| `zip fn source1 source2`           | Pair elements by index                             |

### Error Handling

|        Operator        |                     Description                      |
| ---------------------- | ---------------------------------------------------- |
| `retry n source`       | Re-subscribe on error, up to N retries               |
| `catch handler source` | On error, switch to fallback observable              |

### Observable (`observable { ... }`)

|         Function          |                   Description                    |
| ------------------------- | ------------------------------------------------ |
| `let! x = source`         | Bind (desugars to `flatMap`)                     |
| `return value`            | Lift value into observable                       |
| `return! source`          | Return from observable                           |
| `for x in list do`        | Iterate list, concat results                     |

### Agent (`Agent.start`, `Agent.spawn`)

|         Function                          |                   Description                    |
| ----------------------------------------- | ------------------------------------------------ |
| `Agent.start initialState handler`        | Start a stateful agent with message handler      |
| `Agent.spawn body`                        | Spawn a raw BEAM process as an agent             |
| `Agent.send agent msg`                    | Send a typed message (fire and forget)           |
| `Agent.call agent msgFactory`             | Synchronous request-response with ReplyChannel   |

## Design

### Why F# + BEAM?

Factor uses F# compiled to Erlang via Fable.Beam. The F# type system provides strong typing and inference, while the BEAM runtime provides lightweight processes, fault tolerance, and distribution.

### Process-Per-Operator Model

Every operator in Factor spawns its own BEAM process. This means every pipeline naturally forms a supervision tree: parent processes monitor their children, and crashes propagate as `OnError(ProcessExitException ...)`.

Both pipe operators and `observable { ... }` produce the same process-based pipelines. The CE simply provides monadic syntax sugar for `flatMap`:

```fsharp
// Pipe composition — each operator is a process
source
|> Reactive.map (fun x -> x * 2)      // spawns process
|> Reactive.filter (fun x -> x > 10)  // spawns process
|> Reactive.take 5                     // spawns process

// Equivalent using observable CE
observable {
    let! x = source        // desugars to flatMap
    let! y = transform x   // desugars to flatMap
    return y
}
```

### Operator Implementation

Operators use composable helpers built on the `agent { }` CE with selective receive. Most operators are one-liners using templates like `forNext`, `forNextStateful`, `ofMsgStateful`, and `ofMsg2`:

```fsharp
// map — stateless single-source (uses forNext template)
let map mapper source =
    Operator.forNext source (fun downstream x ->
        Process.onNext downstream (mapper x))

// scan — stateful with full Msg control (uses ofMsgStateful template)
let scan initial accumulator source =
    Operator.ofMsgStateful source initial (fun downstream acc msg ->
        match msg with
        | OnNext x ->
            let newAcc = accumulator acc x
            Process.onNext downstream newAcc
            Some newAcc
        | OnError e -> Process.onError downstream e; None
        | OnCompleted -> Process.onCompleted downstream; None)
```

Complex operators use `Operator.spawnOp` + `Operator.recvMsg`/`Operator.recvAnyMsg` directly with custom agent CE loops.

### Channel Agents

Each channel is backed by an `Agent<ChannelMsg<'T>>` — a stateful agent that manages subscribers. `multicast()` broadcasts to all subscribers. `singleSubscriber()` buffers messages until a subscriber connects.

Subscribe uses `Agent.call` for synchronous ack to prevent races between subscribing and first send. Push uses `Agent.send` with the `ChannelMsg.Notify` protocol.

### Compositional Design

Higher-order operators are composed from primitives:

```fsharp
// flatMap = map + mergeInner (spawns process per inner observable)
let flatMap mapper source =
    source |> map mapper |> mergeInner Terminate None

// concatMap = map + concatInner (sequential)
let concatMap mapper source =
    source |> map mapper |> concatInner

// concatInner = mergeInner with maxConcurrency=1
let concatInner source =
    mergeInner Terminate (Some 1) source
```

The `mergeInner` operator accepts a `SupervisionPolicy` and `maxConcurrency` parameter:

- **Policy**: `Terminate` (error propagates), `Skip` (skip crashed inner), `Restart n` (retry crashed inner up to n times)
- `None` — unlimited concurrency (spawn all inner observables immediately)
- `Some 1` — sequential processing (equivalent to `concatInner`)
- `Some n` — at most n inner observables active, queue the rest

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
