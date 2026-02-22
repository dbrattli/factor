# Design: Composable Actors Through Layered Restriction

## The Core Thesis

**Composability emerges from restriction.** The actor model is powerful but fundamentally non-composable — any process can send any message to any other process at any time. Factor makes actors composable by applying three nested constraints, each enabling a new level of composition.

## The Three Layers

```text
Layer 2: Factor<'T>         ← Observable-Actor (spawnable, composable, process-per-operator)
         ↑ wraps
Layer 1: Observer<'T>       ← Observer-Actor (Rx grammar, process endpoint)
         ↑ restricts
Layer 0: BEAM Process       ← Raw Actor (any message, any sender, any time)
```

### Layer 0: Raw Actor (BEAM Process)

The unconstrained foundation.

- **Messages**: Any Erlang term
- **Sending**: Any process with a Pid can send
- **Protocol**: None — messages in any order, forever
- **State**: Process dictionary or recursive loop
- **Composability**: None — manual wiring, no guarantees

```fsharp
// Layer 0: raw BEAM actor primitives (Process.fs, Actor.fs)
let pid = spawnLinked (fun () -> ...)
send pid anyMessage

// The actor { } CE lives here — CPS receive loop
actor {
    let! msg = ctx.Recv()
    // handle msg...
}
```

### Layer 1: Observer-Actor (Process Endpoint)

Restricts the message vocabulary and sequencing.

- **Messages**: Only `Msg<'T>` — `OnNext`, `OnError`, `OnCompleted`
- **Protocol**: Rx grammar `OnNext* (OnError | OnCompleted)?`
- **Enforcement**: Each operator process self-enforces the grammar by calling `Process.exitNormal()` after forwarding a terminal event. Process exit cascades via BEAM links — no wrapper needed.
- **Composability**: Partial — observers can be manually chained, but wiring is still imperative

```fsharp
// Layer 1: restricted message protocol (Types.fs)
type Msg<'T> = OnNext of 'T | OnError of exn | OnCompleted
type Observer<'T> = { Pid: obj; Ref: obj }  // process endpoint, not a callback

// Rx grammar enforced by process self-termination:
// On terminal event, forward to downstream and exit
| OnError e -> Process.onError downstream e; Process.exitNormal ()
| OnCompleted -> Process.onCompleted downstream; Process.exitNormal ()
// Process exit cascades via links — Dispose IS Kill
```

**What this gains**: Because the protocol is fixed and known, generic transformations become possible. A `map` function can exist because it knows there are exactly three message types. Because each operator is a process, terminal events naturally cascade through BEAM links — no centralized grammar enforcement is needed.

### Layer 2: Observable-Actor (Factor)

Wraps the observer-actor in a spawn function — controls WHO sends. Every operator spawns a BEAM process.

- **Sending**: Only the upstream source (via spawning), not arbitrary senders
- **Lifecycle**: Lazy — nothing happens until spawned. Handle controls lifetime.
- **Composability**: Full — operator algebra (`map`, `filter`, `flatMap`, `merge`, etc.)
- **Process-per-operator**: Each operator in the pipeline is its own BEAM process, linked to its downstream. The pipeline IS the supervision tree at every level.

```fsharp
// Layer 2: spawnable, composable wrapper (Types.fs)
type Factor<'T> = { Spawn: Observer<'T> -> Handle }

// Every operator spawns a BEAM process
let map (mapper: 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Spawn = fun downstream ->
        let pid = Process.spawnLinked (fun () ->
            let ref = Process.makeRef ()
            Process.registerChild ref (fun msg ->
                let n = unbox<Msg<'T>> msg
                match n with
                | OnNext x -> Process.onNext downstream (mapper x)
                | OnError e -> Process.onError downstream e; Process.exitNormal ()
                | OnCompleted -> Process.onCompleted downstream; Process.exitNormal ())
            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
            source.Spawn(self) |> ignore
            Process.childLoop ())
        { Dispose = fun () -> Process.killProcess pid } }
```

**What this gains**: Because every operator is a process, the pipeline topology IS the BEAM process tree. Killing any process cascades via links. There is no separate "supervision" concern — the pipeline structure provides it inherently.

## The Three Restrictions

Each layer constrains the raw actor model, and each constraint enables composition:

```text
Raw BEAM actor → any message, any sender, any time     → not composable
+ Msg<'T>      → three message types only               → operators can transform
+ Rx grammar   → strict sequencing protocol             → operators can rely on termination
+ Spawn wrap   → spawning controls the sender           → wiring is composable
= Fully composable pipeline
```

### Restriction 1: Constrain WHAT (Msg)

Raw actors accept arbitrary messages of any type. `Msg<'T>` restricts the vocabulary to three message types: `OnNext of 'T`, `OnError of exn`, `OnCompleted`. This makes generic operators possible — `map`, `filter`, `merge` all work because there are exactly three cases.

### Restriction 2: Constrain WHEN (Rx Grammar)

Raw actors accept messages in any order, forever. The Rx grammar prescribes `OnNext* (OnError | OnCompleted)?` — terminal events are final. Each operator process self-enforces this by calling `Process.exitNormal()` after forwarding a terminal event. Process exit cascades through BEAM links, ensuring no further messages are delivered. This makes resource management possible — operators can clean up on termination.

### Restriction 3: Constrain WHO (Spawn)

Raw actors receive from anyone with their Pid. `Factor<'T>` wraps the observer in a `Spawn` function — only the upstream source delivers messages. This makes wiring composable — operators build a chain by wrapping observers and spawning upstream.

## How the Layers Map to Code

| Layer |                   Types                    |                                       Files                                       |                 Purpose                  |
| ----- | ------------------------------------------ | --------------------------------------------------------------------------------- | ---------------------------------------- |
| 0     | BEAM process, Pid                          | `Process.fs`, `Actor.fs`, `factor_actor.erl`                                      | Raw actor primitives, CPS actor CE       |
| 1     | `Msg<'T>`, `Observer<'T>`                  | `Types.fs`                                                                        | Restricted protocol, process endpoints   |
| 2     | `Factor<'T>`, `Handle`, operators          | `Create.fs`, `Transform.fs`, `Filter.fs`, `Combine.fs`, `TimeShift.fs`, `Flow.fs` | Composable process-per-operator actors   |

## Bridges Between Layers

### channel() — Layer 0 → Layer 2

`channel()` creates a BEAM actor process (Layer 0) and exposes it as `Sender<'T> * Factor<'T>` (Layer 1 + 2). This is the primary bridge — it gives a raw actor a composable interface.

```fsharp
let (sender, messages) = channel<Command> ()
// sender = the input (Sender) — push messages here via Reactive.pushNext
// messages = the output (Factor) — spawn to process them
```

`Sender<'T>` is `{ ChannelPid: obj }` — the push-side handle to a channel actor. Messages are sent via `Reactive.pushNext sender value` (not `observer.Send`).

The channel actor (`factor_stream.erl`) manages a subscriber map and broadcasts messages. Spawn is synchronous to prevent races between spawning and first send.

### flatMap — Layer 2 → Layer 0 → Layer 2

Every `flatMap` (and every `flow { let! }`) descends to Layer 0 (spawns a BEAM process), then wraps the child back in Layer 2 (the child spawns an inner Factor and sends messages back to the parent via `{factor_child, Ref, Msg}` messages). There is no separate "spawned" variant — every flatMap spawns child processes, because every operator is a process. The supervision tree emerges from this Layer 2 → 0 → 2 round-trip.

```fsharp
flow {
    let! x = sourceA    // spawn child process for sourceA
    let! y = sourceB x  // spawn child process for sourceB
    return combine x y
}
// Parent supervises children — pipeline IS the supervision hierarchy
```

### actor { } — Escape Hatch to Layer 0

The Actor CE (`actor { let! msg = ctx.Recv() }`) is a direct Layer 0 primitive. It bypasses Layer 1 (arbitrary messages) and Layer 2 (no spawning model). Useful for patterns Rx can't express (blocking selective receive), but not composable with Factor pipelines without manual channel bridging.

```fsharp
let counterActor = spawn (fun ctx ->
    rec' (fun loop count -> actor {
        let! msg = ctx.Recv()
        match msg with
        | Increment -> return! loop (count + 1)
        | GetCount replyPid -> send replyPid count
    }) 0)
```

## The Actor-Observable Duality

|    Actor Concept     |            Factor Equivalent             |                 Notes                  |
| -------------------- | ---------------------------------------- | -------------------------------------- |
| Actor (unspawned)    | `Factor<'T>` (cold)                      | A behavior definition, not yet running |
| Actor (running)      | Spawned subscription / `channel()` (hot) | Running process, addressable           |
| Mailbox              | `channel()` / Sender input side          | Where messages arrive                  |
| Behavior             | Operator pipeline                        | How messages are processed             |
| State                | `scan` operator / mutable vars           | Accumulated over messages              |
| Spawn child          | `flatMap` / `mergeInner`                 | Creates linked child process           |
| Send message         | `Process.onNext observer x` or `Reactive.pushNext sender x` | Push into process or channel |
| Actor identity (Pid) | Channel process Pid                      | Addressable endpoint                   |
| Actor lifecycle      | Handle (`Dispose` = shutdown)            | Resource management                    |
| Supervision          | `SupervisionPolicy` on child processes   | `Terminate` / `Skip` / `Restart`       |

### What Each Concept Really Is

**Factor<'T> = An Unborn Actor.** A `Factor<'T>` is a blueprint — it describes what an actor will do when spawned. Nothing happens until `Spawn` is called. Spawning IS instantiation.

**channel() = A Running Actor's Mailbox.** `channel()` returns `Sender<'T> * Factor<'T>` — input and output. This is an actor that's already alive (a BEAM process), waiting for messages.

**Rx Operators = Actor Behavior.** Each operator IS a BEAM process. The pipeline between input and output is a tree of linked processes.

**flatMap = Spawn Child Actor.** Each `let!` in `flow { }` spawns a child process. The supervision tree emerges from the pipeline topology.

**subscribe = Actor Instantiation.** Spawning starts the pipeline running. The returned Handle controls the actor's lifetime.

## The Continuation Monad Unification

All three layers share the same underlying shape: the continuation monad.

```fsharp
// Continuation monad (textbook)
type Cont<'T>      = { Run:    ('T -> unit)     -> unit   }

// Actor CE (Actor.fs) — the raw continuation monad
type Actor<'Msg,'T> = { Run:    ('T -> unit)     -> unit   }

// Factor — continuation monad + protocol + lifetime
type Factor<'T>    = { Spawn:  (Observer<'T>)    -> Handle }
```

Factor enriches the continuation monad in exactly two ways:

1. **Richer callback**: `Observer<'T>` (process endpoint for three-case `Msg`) instead of plain `'T -> unit`
2. **Lifetime return**: `Handle` instead of `unit`

These two enrichments correspond precisely to the two sides of actor lifecycle.

### Spawn = Run = Spawn

These are the same operation — execute a deferred computation with a callback:

| Abstraction | Operation | Meaning |
|---|---|---|
| `Cont<'T>` | `Run(callback)` | Run the continuation |
| `Actor<'Msg,'T>` | `Run(callback)` | Start the CPS receive loop |
| `Factor<'T>` | `Spawn(observer)` | Start the pipeline, begin delivery |
| BEAM | `spawn(fun)` | Start a process |

A `Factor<'T>` is inert until `Spawn` is called — just as an actor behavior is inert until `spawn`. The continuation captures *what will happen*; calling it makes it happen. **Spawn IS Run.**

### Dispose = Kill = Supervise

The return side unifies lifetime management:

| Abstraction | Operation | Meaning |
|---|---|---|
| `Cont<'T>` | *(none — `unit` return)* | No lifetime control |
| `Actor<'Msg,'T>` | *(none — `unit` return)* | No structured shutdown |
| `Factor<'T>` | `Handle.Dispose()` | Stop delivery, clean up |
| BEAM | `exit(Pid, Reason)` | Kill a process |
| OTP | Supervisor | Manage child lifetimes |

The raw continuation monad returns `unit` — fire and forget. There is no way to cancel or manage what you started. The raw actor model has `exit/2` but it's external and unstructured — any process can kill any other.

Factor's `Handle` unifies these: `Spawn` atomically returns the means to end what it started. **Dispose IS Kill.** Because every operator is a process that exits on terminal events (`Process.exitNormal()`), and processes are linked, error propagation IS supervision — BEAM links handle what OTP supervisors handle.

```fsharp
// Spawn returns Handle — spawning and lifetime are one atomic operation
let handle = pipeline |> subscribe observer
// handle.Dispose() — structured shutdown, like supervisor:terminate_child

// Each operator process self-enforces the Rx grammar:
// On terminal event, forward to downstream and exit
| OnError e -> Process.onError downstream e; Process.exitNormal ()
| OnCompleted -> Process.onCompleted downstream; Process.exitNormal ()
// Process exit cascades via links — Dispose IS Kill
```

### What the Continuation Monad Lens Reveals

In the raw actor model, spawning and supervision are separate concerns — you spawn a process, then separately set up a supervisor to watch it. In Factor, they are unified through the continuation monad shape:

- **`Spawn`** = spawn (start the computation with a callback)
- **`Handle`** = supervision (the returned capability to manage lifetime)
- **Process exit on terminal** = supervision policy (exit on error/complete cascades via links)
- **`SupervisionPolicy`** = what `flatMap` does when a child process exits with an error

The pipeline IS the supervision tree because the continuation monad naturally nests: each operator wraps the downstream observer and spawns upstream, creating a chain of Spawn/Handle pairs — a chain of spawn/supervise pairs.

```text
Cont monad:           run(k)           → unit
                         ↓ enrich callback
Factor:              spawn(observer)   → handle
                         ↓ meaning
Actor model:          spawn(behavior)  → supervision
```

## One Composition Model: Process-Per-Operator

Every operator is a BEAM process. The pipeline IS the supervision tree at every level. There is no distinction between "composing behavior within a process" and "composing actors into a supervision tree" — they are the same thing.

### Pipe Operators — Building Process Trees

```fsharp
source |> map f |> filter g |> flatMap h
// Three linked processes: map → filter → flatMap (plus children from h)
```

Each pipe operator spawns a process linked to the downstream. Killing any process in the chain cascades via BEAM links. `flatMap` and `mergeInner` spawn additional child processes for inner subscriptions — the supervision tree emerges naturally from the pipeline topology.

### flow { } — Syntactic Sugar for flatMap

```fsharp
flow {
    let! x = sourceA    // flatMap: spawn child process for sourceA
    let! y = sourceB x  // flatMap: spawn child process for sourceB
    return combine x y
}
// Desugars to flatMap chains — same process-per-operator model
```

The `flow { }` CE is syntactic sugar for `flatMap` chains. Since every operator already spawns a process, `flow { }` does not introduce a fundamentally different composition mode — it provides monadic syntax for the same underlying process tree.

## The Composition Hierarchy

```text
        Raw Actor (Layer 0)
           │
           │ restrict messages to Msg<'T>
           │ enforce Rx grammar via process exit
           ▼
     Observer-Actor (Layer 1)
           │
           │ wrap in Spawn function
           │ control who sends via spawning
           ▼
   Observable-Actor (Layer 2) = Factor<'T>
           │
           │ compose with operators (each a process)
           ▼
      Pipeline = Supervision Tree
      (every operator is a linked BEAM process)
```

## Where the Duality Breaks Down

The Rx/Factor model captures most of what actors do, but some patterns don't map cleanly:

1. **Selective Receive**: Actors can pattern-match on mailbox messages, leaving non-matching ones for later. Rx processes ALL messages in order. You'd need `groupBy` or `partition` for selective behavior.

2. **Request-Response (Ask Pattern)**: Actors naturally support send-request → block-for-reply. Rx is fire-and-forget push. You'd need a channel pair and correlation.

3. **Blocking vs Push**: The `actor { let! msg = ctx.Recv() }` pattern blocks until a message arrives (pull-based). Rx is push-based. The CPS actor CE exists precisely because Rx can't express blocking receive.

4. **Identity and Addressing**: In the actor model, you send to a Pid — a first-class value you can pass around. Observers are ephemeral. `channel()` bridges this gap by providing an actor-backed endpoint.

## Prior Art

The actor model's composability problem is widely recognized. Every solution adds the same thing: **structured channels/streams between actors**.

### Prokopec & Odersky (2015) — "Reactors, Channels, and Event Streams"

Identified three obstacles for actor composition:

1. Single untyped channel — actors have one mailbox mixing all message types
2. Protocol conflation — implementing multiple protocols in one actor's receive is cumbersome
3. No first-class event handling — the receive statement can't be composed functionally

Their solution: **Reactors** — actors with multiple typed channels and event streams supporting functional combinators (map, filter, union). This is almost exactly what Factor does.

### Akka Streams

Created because "it has been found tedious and error-prone to implement all the proper measures to achieve stable streaming between actors." `Source → Flow → Sink` building blocks compose into `RunnableGraph`, materialized onto actors under the hood.

### Elixir GenStage / Broadway

Producer-consumer model with demand-based back-pressure on BEAM. Structured composition: `Producer → ProducerConsumer → Consumer`. Broadway adds higher-level pipeline composition with batching, telemetry, and fault tolerance.

### Process Calculi (CSP / Pi-calculus)

CSP constrains actors with fixed-topology channels — composable but inflexible. Pi-calculus allows dynamic channel passing — more flexible but channels still impose structure. Both are more composable than actors precisely because they constrain communication.

### The Common Pattern

| System | Composable Unit | Constraint Added |
|--------|-----------------|-----------------|
| Prokopec's Reactors | Typed channels + event streams | Multiple typed ports instead of one untyped mailbox |
| Akka Streams | Source / Flow / Sink | Directed graph topology with back-pressure |
| GenStage/Broadway | Producer / Consumer stages | Demand-based flow control |
| CSP | Channels | Fixed topology, synchronous rendezvous |
| **Factor** | `channel()` + Rx operators | Push-based spawning with Rx grammar |

Factor's approach is closest to Prokopec's Reactors: typed channels enable composition through functional combinators (Rx operators).
