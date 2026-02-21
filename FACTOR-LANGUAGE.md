# Factor Language

## Vision

Factor is a composable actor language with Rx-style operator vocabulary, targeting the BEAM runtime. It composes actors the way SQL composes tables — a declarative algebra over a runtime primitive.

```text
SQL:    table  → select/where/join/group → table
Factor: actor  → map/filter/merge/scan   → actor
```

Every operator is an actor. The pipeline is an actor topology. An observable is just an observed actor — a view, not the primitive.

## Why BEAM

- **Fault isolation** — one actor crashes, only that node restarts
- **Backpressure** — actor mailboxes are natural buffers
- **Location transparency** — any actor in the pipeline could be on another node
- **Hot rewiring** — replace one actor in the graph, rest keeps running
- **Introspection** — observe mailbox depth, message rates, per-stage

## Architecture

```text
F# combinators (typed API)
        ↓ compile
Erlang terms (AST as data)
        ↓ interpret
Actor topology (pid → pid → pid)
```

### Layer 1: Typed AST (F# combinators)

The primary language. Type-safe at construction time, compiles to Erlang terms.

```fsharp
let pipeline =
    sensor "temperature"
    |> filter (fun t -> t > 100.0)
    |> scan 0.0 (fun acc t -> acc + t)
    |> merge [alerts; warnings]
```

### Layer 2: AST as Erlang Terms

The compiled output is maps, tuples, atoms — BEAM's universal data format. Any Erlang/Elixir process can inspect, rewrite, optimize, and forward these natively. No serialization layer needed.

### Layer 3: Actor Topology

The interpreter walks the AST and spawns a supervision tree of actors. Each operator becomes a BEAM process.

```text
source actor → map actor → filter actor → merge actor → sink actor
     pid          pid           pid           pid          pid
```

### Layer 4: Text DSL (future)

Optional string-based front-end for external systems (CLI, dashboards, remote APIs, non-F# clients).

```text
sensor 'temp' | filter (> 100) | scan (+) 0
```

Covers common cases (simple predicates, projections, aggregations). Complex logic stays in F#.

## Standing Queries

SQL is request-response: query arrives, runs against data at rest, returns results, done. Factor inverts this — a query arrives, **stays alive**, and data flows through it continuously.

```text
SQL:    query → data (at rest) → result → done
Factor: query → stays alive → data (in motion) → results stream → ...forever
```

A standing query is a **deployed actor topology**. The Factor server doesn't just execute and return — it spawns a living pipeline that persists, receives events, and pushes results indefinitely. The query *becomes infrastructure*.

### Factor Server

The Factor server manages standing queries the way a SQL server manages connections — but queries are long-lived, not ephemeral.

```text
Client                          Factor Server
  |                                  |
  |-- Deploy(query_1) ------------->| → spawns topology, stays alive
  |-- Deploy(query_2) ------------->| → spawns topology, stays alive
  |                                  |
  |          events arrive continuously...
  |                                  |
  |<---- query_1 results stream ----|
  |<---- query_2 results stream ----|
  |                                  |
  |-- Undeploy(query_1) ----------->| → tears down topology
  |-- Replace(query_2, new) ------->| → hot-swap, no downtime
```

Server responsibilities:

- **Lifecycle** — deploy, undeploy, hot-replace standing queries
- **Supervision** — restart crashed stages, monitor health
- **Routing** — connect event sources to the right standing queries
- **Multiplexing** — multiple standing queries share the same event sources
- **Backpressure** — slow consumers don't kill producers

### Two entry points

Same node (local client): send `FactorNode` with closures directly as Erlang terms. Full expressiveness — any F# lambda works.

Cross node / external client: send text DSL, server parses into `FactorNode`. Limited to serializable expressions — named functions, simple predicates, arithmetic. This is Factor's wire protocol.

```text
Local:     FactorNode (Erlang terms + closures) → server → topology
Remote:    "interval 1000 | filter (> 5) | map (* 2)" → parse → FactorNode → server → topology
```

## AST Design

Untyped internal AST with typed wrapper. Functions are opaque closures — the AST captures structure (topology), not the lambda bodies.

```fsharp
type FactorNode =
    // --- Sources ---
    | Empty
    | Never
    | Single of obj
    | OfList of obj list
    | Timer of int
    | Interval of int
    | Defer of (unit -> FactorNode)
    // --- Transforms ---
    | Map of FactorNode * (obj -> obj)
    | Mapi of FactorNode * (int -> obj -> obj)
    | Scan of FactorNode * obj * (obj -> obj -> obj)
    | GroupBy of FactorNode * (obj -> obj)
    | FlatMap of FactorNode * (obj -> FactorNode)
    | ConcatMap of FactorNode * (obj -> FactorNode)
    | SwitchMap of FactorNode * (obj -> FactorNode)
    | Tap of FactorNode * (obj -> unit)
    | StartWith of FactorNode * obj list
    | Pairwise of FactorNode
    // --- Filters ---
    | Filter of FactorNode * (obj -> bool)
    | Take of FactorNode * int
    | Skip of FactorNode * int
    | TakeWhile of FactorNode * (obj -> bool)
    | SkipWhile of FactorNode * (obj -> bool)
    | Choose of FactorNode * (obj -> obj option)
    | DistinctUntilChanged of FactorNode
    | Distinct of FactorNode
    | First of FactorNode
    | Last of FactorNode
    | TakeUntil of FactorNode * FactorNode
    | Sample of FactorNode * FactorNode
    // --- Combine ---
    | Merge of FactorNode list
    | Concat of FactorNode list
    | CombineLatest of FactorNode * FactorNode * (obj -> obj -> obj)
    | WithLatestFrom of FactorNode * FactorNode * (obj -> obj -> obj)
    | Zip of FactorNode * FactorNode * (obj -> obj -> obj)
    | Race of FactorNode list
    | ForkJoin of FactorNode list
    // --- Time ---
    | Delay of FactorNode * int
    | Debounce of FactorNode * int
    | Throttle of FactorNode * int
    | Timeout of FactorNode * int
    // --- Error ---
    | Retry of FactorNode * int
    | Catch of FactorNode * (string -> FactorNode)

// Typed wrapper — type safety at the API boundary
type Factor<'T> = { Node: FactorNode }
```

Smart constructors (e.g. `Factor.map`, `Factor.filter`) ensure type safety at construction. The `obj` boxing is the cost of F# not having GADTs — one allocation per event at interpretation boundaries.

## Interpreter

Walks `FactorNode`, spawns an actor per operator, wires them via message passing.

```fsharp
let rec interpret (node: FactorNode) (downstream: Pid<obj>) : Pid<obj> =
    match node with
    | Map(source, f) ->
        let me = spawnMapActor f downstream
        interpret source me
    | Filter(source, pred) ->
        let me = spawnFilterActor pred downstream
        interpret source me
    | Merge(sources) ->
        let me = spawnMergeActor downstream
        sources |> List.iter (fun s -> interpret s me |> ignore)
        me
    // ... one arm per node
```

Each spawned actor:

- Receives messages from upstream
- Applies its operation
- Sends results downstream
- Is supervised — crash restarts just this node

## Implementation Phases

### Phase 1: Actor CE (done)

- `actor { }` computation expression
- `spawn`, `send`, `self`, `rec'`
- Erlang FFI (`factor_actor.erl`)

### Phase 2: Core AST + Interpreter

- `FactorNode` DU and `Factor<'T>` wrapper
- Smart constructors for all operators
- Basic interpreter: walks AST, spawns one actor per operator
- Simple source actors (single, ofList, interval)
- Simple transform actors (map, filter, take, scan)

### Phase 3: Supervision

- Supervisor actor that owns the topology
- Restart strategies (one-for-one, rest-for-one)
- Link actors in pipeline so crashes propagate correctly
- Health monitoring (mailbox depth, throughput)

### Phase 4: Optimization

- AST rewriting before interpretation
  - Fuse adjacent maps: `map f |> map g` → `map (f >> g)` (one actor instead of two)
  - Push filters upstream (reduce message volume early)
  - Colocate hot paths
- Cost model for deciding when to fuse vs. keep separate actors

### Phase 5: Factor Server

- Server actor that accepts Deploy/Undeploy/Replace messages
- Standing query registry — track deployed topologies by name/id
- Lifecycle management — spawn, tear down, hot-swap topologies
- Source routing — connect named event sources to standing queries
- Multiplexing — multiple queries share the same event sources

### Phase 6: Text DSL + Wire Protocol

- Parser for serializable expressions (the remote entry point)
- Named functions (atoms) for predicates/projections that cross node boundaries
- Simple expression language: comparisons, arithmetic, field access
- Entry point for dashboards, CLI, non-F# clients, remote nodes

### Phase 7: Distribution

- Spawn actors on remote nodes
- `Factor.on(node, pipeline)` placement directives
- Cross-node standing query deployment

## Design Principles

1. **Actors are the primitive** — not observables. An observable is a view over an actor.
2. **Operators are topology** — each operator describes a node in an actor graph.
3. **AST is data** — Erlang terms, inspectable and rewritable.
4. **Typed at the edges, dynamic inside** — F# ensures correctness at construction, BEAM handles the runtime.
5. **Supervision is structural** — the pipeline shape determines the supervision tree.
6. **Same operators, different semantics** — Rx vocabulary, actor execution model.
