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

### `flow { ... }` — Factor Composition

The `flow` CE composes reactive factors with supervision. Each `let!` desugars to `flatMap`, which spawns a linked child process per inner factor.

```fsharp
open Factor.Reactive
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

Since every operator is a BEAM process, both `flow { ... }` and pipe operators create process-based pipelines. The CE provides monadic syntax sugar for `flatMap`:

```fsharp
// Monadic composition via CE (desugars to flatMap)
let pipeline = flow {
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

**CE constraint:** All operator callbacks run in spawned processes. Capture only immutable values and actor-based channels — do not reference mutable state from outer scopes.

### `actor { ... }` — Message-Passing Actors

The `actor` CE provides traditional actor-style concurrency with typed message passing. Each actor is a BEAM process that receives messages via `ctx.Recv()`.

```fsharp
open Factor.Actor

type Msg =
    | Greet of string
    | Stop

let pid =
    spawn (fun ctx ->
        let rec loop () =
            actor {
                let! msg = ctx.Recv()
                match msg with
                | Greet name ->
                    printfn "Hello, %s!" name
                    return! loop ()
                | Stop -> return ()
            }
        loop ())

send pid (Greet "world")   // prints: Hello, world!
send pid Stop              // actor exits
```

Key features:

- **Typed PIDs** — `Pid<'Msg>` ensures only correctly-typed messages can be sent
- **`ctx.Recv()`** — suspends the actor until a message arrives
- **`let rec`** — idiomatic F# recursive loops for actor state machines
- **`spawn`** — creates a new BEAM process running the actor body
- **`send`** — sends a typed message to an actor

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
|> Reactive.retry 3                                           // re-spawn on error
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

Channels separate the push side (`Sender<'T>`) from the subscribe side (`Factor<'T>`). Each channel is backed by a separate BEAM actor process that manages subscribers via message passing.

```fsharp
open Factor.Reactive

let sender, output = Reactive.channel ()

// Subscribe to output
let _handle =
    output
    |> Reactive.subscribe
        (fun x -> printfn "Got: %d" x)
        (fun _err -> ())
        (fun () -> printfn "Done!")

// Push values through sender
Reactive.pushNext sender 1
Reactive.pushNext sender 2
Reactive.pushCompleted sender
```

## Core Concepts

### Factor

A `Factor<'T>` represents a lazy push-based actor blueprint. Factors don't produce values until spawned (subscribed to).

### Observer

An `Observer<'T>` is a process endpoint `{ Pid: obj; Ref: obj }` that receives messages from a Factor. Messages are delivered as Erlang messages to the observer's process (`Pid`) tagged with the monitor reference (`Ref`).

The Rx grammar is enforced: `OnNext* (OnError | OnCompleted)?`

### Sender

A `Sender<'T>` = `{ ChannelPid: obj }` is the push side of a channel. Use `pushNext`, `pushError`, and `pushCompleted` to send values into a channel.

### Handle

A `Handle` represents a spawned subscription that can be cancelled. Call `Dispose()` to unsubscribe and release resources.

## Available Operators

### Creation

|      Operator      |              Description              |
| ------------------ | ------------------------------------- |
| `single value`     | Emit single value, then complete      |
| `empty ()`         | Complete immediately                  |
| `never ()`         | Never emit, never complete            |
| `fail error`       | Error immediately with given exception |
| `ofList items`     | Emit all items from list              |
| `defer factory`    | Create factor lazily on spawn         |

### Transform

|           Operator            |                              Description                              |
| ----------------------------- | --------------------------------------------------------------------- |
| `map fn source`               | Transform each element                                                |
| `mapi fn source`              | Transform with index: `fn a i -> b`                                   |
| `flatMap fn source`           | Map to factors, merge results (spawns process per inner)              |
| `flatMapi fn source`          | Map with index to factors, merge (spawns process per inner)           |
| `concatMap fn source`         | Map to factors, concatenate in order (= map + concatInner)            |
| `concatMapi fn source`        | Map with index to factors, concatenate (= mapi + concatInner)         |
| `switchMap fn source`         | Map to factors, switch to latest (= map + switchInner)                |
| `switchMapi fn source`        | Map with index to factors, switch (= mapi + switchInner)              |
| `mergeInner policy max source` | Flatten Factor of Factors with merge policy and concurrency limit    |
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

### Channel

|        Operator        |                       Description                       |
| ---------------------- | ------------------------------------------------------- |
| `channel ()`           | Returns `Sender<'T> * Factor<'T>`, multicast channel actor |
| `singleChannel ()`     | Returns `Sender<'T> * Factor<'T>`, single-subscriber with buffering |
| `publish source`       | Connectable hot factor, call connect() to start         |
| `share source`         | Auto-connecting multicast, refCount semantics           |

### Combine

|              Operator              |                    Description                     |
| ---------------------------------- | -------------------------------------------------- |
| `merge sources`                    | Merge multiple factors into one                    |
| `merge2 source1 source2`           | Merge two factors                                  |
| `concat sources`                   | Spawn sources sequentially                         |
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
| `retry n source`       | Re-spawn on error, up to N retries                   |
| `catch handler source` | On error, switch to fallback factor                  |

### Flow (`flow { ... }`)

|         Function          |                   Description                    |
| ------------------------- | ------------------------------------------------ |
| `let! x = source`         | Bind (desugars to `flatMap`)                     |
| `return value`            | Lift value into flow                             |
| `return! source`          | Return from flow                                 |
| `for x in list do`        | Iterate list, concat results                     |

### Actor (`actor { ... }`)

|         Function          |                   Description                    |
| ------------------------- | ------------------------------------------------ |
| `spawn body`              | Spawn a new BEAM process running the actor       |
| `send pid msg`            | Send a typed message to an actor                 |
| `let! msg = ctx.Recv()`   | Suspend until a message arrives                  |
| `let rec loop`            | Recursive loop for actor state machines          |
| `return value`            | Complete the actor computation                   |
| `return! computation`     | Tail-call into another actor computation         |

## Design

### Why F# + BEAM?

Factor uses F# compiled to Erlang via Fable.Beam. The F# type system provides strong typing and inference, while the BEAM runtime provides lightweight processes, fault tolerance, and distribution.

### Process-Per-Operator Model

Every operator in Factor spawns its own BEAM process. This means every pipeline naturally forms a supervision tree: parent processes monitor their children, and crashes propagate as `OnError(ProcessExitException ...)`.

Both pipe operators and `flow { ... }` produce the same process-based pipelines. The CE simply provides monadic syntax sugar for `flatMap`:

```fsharp
// Pipe composition — each operator is a process
source
|> Reactive.map (fun x -> x * 2)      // spawns process
|> Reactive.filter (fun x -> x > 10)  // spawns process
|> Reactive.take 5                     // spawns process

// Equivalent using flow CE
flow {
    let! x = source        // desugars to flatMap
    let! y = transform x   // desugars to flatMap
    return y
}
```

### Operator Implementation

Operators use the `actor { }` computation expression with selective receive for message processing. Each operator spawns a linked process that loops receiving messages from upstream:

```fsharp
let map mapper source =
    { Spawn = fun downstream ->
        let ref = Process.makeRef ()
        Process.spawnOp (fun () ->
            let upstream = { Pid = Process.selfPid (); Ref = ref }
            source.Spawn upstream |> ignore

            let rec loop () = actor {
                let! msg = Process.recvMsg<'T> ref
                match msg with
                | OnNext x ->
                    Process.onNext downstream (mapper x)
                    return! loop ()
                | OnError e -> Process.onError downstream e
                | OnCompleted -> Process.onCompleted downstream
            }
            loop ()) }
```

Multi-source operators use `recvAnyMsg` to receive from any upstream and match on the source ref:

```fsharp
let rec loop state = actor {
    let! (ref, rawMsg) = Process.recvAnyMsg ()
    if ref = ref1 then
        match unbox<Msg<'T>> rawMsg with
        // handle source 1 messages...
    else
        match unbox<Msg<'U>> rawMsg with
        // handle source 2 messages...
}
```

### Channel Actors

Each channel (`channel()`, `singleChannel()`) is backed by a separate BEAM actor process that manages a subscriber map. `channel()` returns a `Sender<'T> * Factor<'T>` pair: the `Sender` is used to push values via `pushNext`/`pushError`/`pushCompleted`, while the `Factor` side is subscribed to by downstream operators.

Spawn is **synchronous** (send + wait for ack) to prevent races between spawning and first send. The channel actor is linked to its creator via `spawn_link`.

### Message Dispatch

Messages from channel actors and timer callbacks are delivered as messages. Any process that subscribes to channels or uses time-based operators must handle these messages:

- `{factor_timer, Ref, Callback}` — Timer callbacks from `erlang:send_after`
- `{factor_child, Ref, Msg}` — Messages forwarded from channel actors or child processes

The `process_timers` loop (for subscribers) and `child_loop` (for spawned children) handle both message types.

### Timer Architecture

Time-based operators use a native Erlang module (`factor_timer`) that schedules callbacks via `erlang:send_after`. Callbacks are delivered as messages to `self()` and executed by a unified message pump (`process_timers`), ensuring they run in the subscriber's process with access to the process dictionary state.

### Compositional Design

Higher-order operators are composed from primitives:

```fsharp
// flatMap = map + mergeInner (spawns process per inner factor)
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
- `None` — unlimited concurrency (spawn all inner factors immediately)
- `Some 1` — sequential processing (equivalent to `concatInner`)
- `Some n` — at most n inner factors active, queue the rest

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
