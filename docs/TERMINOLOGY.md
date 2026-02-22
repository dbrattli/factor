# Factor Terminology

Quick reference for concepts used throughout Factor. Organized by the three-layer architecture described in [DESIGN.md](DESIGN.md).

## Layer 0: Raw Actor (BEAM Process)

- **Pid** — An Erlang process identifier. In Factor, `Pid<'Msg>` is a typed wrapper for type-safe message sending.
- **process dictionary** — Per-process mutable key-value store in BEAM. Factor uses it for `factor_children` (child handler registry) and `factor_exits` (exit handler registry). Operators using `mutable` variables compile to process dictionary access.
- **spawnLinked** — Spawn a new BEAM process linked to the caller. If either process crashes, the other receives an EXIT signal. Foundation for supervision.
- **trapExits** — Enable `trap_exit` on a process so EXIT signals become messages instead of causing termination. Used by supervisory processes.
- **registerChild / unregisterChild** — Add or remove a callback in the process dictionary's `factor_children` registry. Used to dispatch `{factor_child, Ref, Msg}` messages to the correct handler.
- **registerExit / unregisterExit** — Add or remove a callback in the `factor_exits` registry. Used to handle `{'EXIT', Pid, Reason}` messages from linked processes.
- **actor { }** — Computation expression for CPS-based message-passing actors. Provides `Recv` to wait for messages. Runs inside a spawned BEAM process. This is a direct Layer 0 primitive — the raw continuation monad with no protocol or lifetime restrictions.

### Message Types

- `{factor_child, Ref, Msg}` — Delivers a message from a channel actor to a subscribing process. Dispatched via `factor_children` registry.
- `{factor_timer, Ref, Callback}` — Delivers a timer callback. Dispatched via `factor_timer:schedule`.
- `{'EXIT', Pid, Reason}` — Delivered when a linked process terminates (requires `trap_exit`). Dispatched via `factor_exits` registry.

## Layer 1: Observer-Actor (Process Endpoint)

- **Msg\<'T\>** — The atoms of the Rx grammar: `OnNext of 'T`, `OnError of exn`, or `OnCompleted`. Every event in a Factor pipeline is one of these three. This is the protocol restriction — constraining WHAT messages are allowed.
- **Observer\<'T\>** — A process endpoint. Contains `Pid` and `Ref` identifying the target process. Messages are sent via `Process.sendChildMsg`. This replaces the old callback-based observer; the process itself IS the observer.

### Rx Contract

- **Rx grammar** — The legal sequence of messages: `OnNext* (OnError | OnCompleted)?` — zero or more `OnNext`, followed by at most one terminal event.
- **terminal event** — Either `OnError` or `OnCompleted`. After a terminal event, the operator process exits normally via `Process.exitNormal()`, which cascades through process links to tear down the pipeline. Rx grammar is self-enforced by each operator process — no separate wrapper is needed.

## Layer 2: Observable-Actor (Factor)

- **Factor\<'T\>** — A lazy push-based stream. Wraps a `Spawn: Observer<'T> -> Handle` function. Nothing happens until spawned. Spawn creates a BEAM process (linked to the caller) that subscribes to upstream. The chain of spawned processes IS the supervision tree. Spawn IS Run.
- **Observer\<'T\>** — (input side) The process endpoint `{ Pid; Ref }` passed to `Spawn` — identifies which process receives the messages.
- **Handle** — A resource cleanup token. Wraps a `Dispose: unit -> unit` function. Calling `Dispose` cancels a subscription or releases resources. Dispose IS Kill — the returned capability to manage the lifetime of what was spawned.
- **Sender\<'T\>** — Push-side handle for channel actors. Contains `ChannelPid`. Used with `pushNext`/`pushError`/`pushCompleted` to send messages into a channel.

### Cold vs Hot

- **cold** — A Factor that does work per subscriber. Each `Spawn` call starts a fresh sequence (spawns a fresh computation). Most operators produce cold Factors (e.g. `ofList`, `map`, `filter`).
- **hot** — A Factor that shares a single execution among subscribers. Channels (`channel`, `singleChannel`) and the output of `share`/`publish` are hot.

### Creation Operators

- **create** — Build a Factor from a raw spawn function.
- **single** — A Factor that emits one value then completes.
- **empty** — A Factor that completes immediately with no values.
- **never** — A Factor that never emits and never completes.
- **fail** — A Factor that immediately errors with a given exception.
- **ofList** — A Factor that synchronously emits all items from a list then completes.
- **defer** — Delays Factor creation until spawn time by calling a factory function.

### Transform Operators

- **map / mapi** — Project each element through a function. `mapi` also passes the element index.
- **flatMap** — Map each element to an inner Factor and merge results. Spawns a linked child process per inner Factor.
- **mergeInner** — Flatten a `Factor<Factor<'T>>` with supervision policy and optional `maxConcurrency`. Each inner Factor is spawned as a linked child process.
- **concatMap / concatInner** — Like flatMap/mergeInner but spawns inners one at a time, in order.
- **switchMap / switchInner** — Like flatMap/mergeInner but disposes the previous inner when a new one arrives.
- **scan** — Accumulate state over elements, emitting each intermediate value.
- **reduce** — Accumulate state over elements, emitting only the final value on completion.
- **groupBy** — Partition elements by key. Each unique key produces an inner Factor. Uses an actor per group.
- **tap** — Side-effect on each element without modifying the sequence.
- **startWith** — Prepend values before the source elements.
- **pairwise** — Emit `(previous, current)` pairs.

### Filter Operators

- **filter** — Keep only elements matching a predicate.
- **take / skip** — Take or skip the first N elements.
- **takeWhile / skipWhile** — Take or skip elements while a predicate holds.
- **choose** — Apply a function returning `Option`; keep `Some` values.
- **distinctUntilChanged** — Suppress consecutive duplicates.
- **distinct** — Suppress all duplicates (uses a `HashSet`).
- **takeUntil** — Emit elements until a notifier Factor produces a value.
- **takeLast** — Buffer all elements, emit only the last N on completion.
- **first / last** — Emit only the first or last element.
- **defaultIfEmpty** — Emit a default value if the source completes without emitting.
- **sample** — Emit the latest source value each time a sampler Factor fires.

### Combine Operators

- **merge / merge2** — Spawn all sources concurrently, interleave their values.
- **combineLatest** — When either source emits, combine with the latest value from the other.
- **withLatestFrom** — When the primary source emits, combine with the latest from a sampler.
- **zip** — Pair elements from two sources by index.
- **concat / concat2** — Spawn sources one after another, in order.
- **amb / race** — Spawn all sources; forward only the one that emits first, dispose the rest.
- **forkJoin** — Wait for all sources to complete, emit a list of their last values.

### Time-shift Operators

- **timer** — Emit a single `0` after a delay (milliseconds).
- **interval** — Emit incrementing integers at a fixed period.
- **delay** — Shift each element forward in time by a fixed duration.
- **debounce** — Emit an element only after a quiet period with no new elements.
- **throttle** — After emitting, suppress further elements for a duration.
- **timeout** — Error if no element arrives within a duration.

### Error Handling

- **retry** — Re-spawn the source up to N times on error.
- **catch** — On error, switch to an alternative Factor produced by an error handler function.

### Multicast

- **publish** — Convert a cold Factor into a connectable hot Factor. Returns `Factor<'T> * (unit -> Handle)`. Subscribers attach first, then `connect()` starts the source.
- **share** — Automatically connect on first subscriber, disconnect when the last unsubscribes. Refcounted convenience over `publish`.
- **connect** — The function returned by `publish` that starts the underlying source spawning.
- **refcount** — The pattern used by `share` — track subscriber count and auto-connect/disconnect.

## Bridges Between Layers

- **channel** — A multicast channel backed by a BEAM actor process. Returns `Sender<'T> * Factor<'T>` — push messages via the Sender side using `pushNext`/`pushError`/`pushCompleted`, subscribers receive them from the Factor side. Bridges Layer 0 (actor process) to Layer 2 (composable Factor). Supports multiple concurrent subscribers.
- **singleChannel** — A single-subscriber channel with buffering. Returns `Sender<'T> * Factor<'T>`. Messages sent before any subscriber connects are buffered and replayed on subscription. Also backed by a BEAM actor.

## Composition

- **flow { }** — Computation expression for composing Factors monadically. `let!` desugars to `flatMap`, spawning a linked child process per binding. Each `let!` is a Spawn, and the returned Handle = Supervision. Creates supervision boundaries. Operator callbacks run in spawned processes; mutable variables captured in closures will not work cross-process.
- **pipe operators** — Using `|>` with operators like `map`, `filter`, `flatMap`. Every operator spawns a BEAM process. The pipeline builds a linked process tree that IS the supervision hierarchy.

## Supervision

- **SupervisionPolicy** — Controls behavior when a spawned child process crashes (when a child's Handle is disposed by error).
- **Terminate** — (Default) Propagate the child's error as `OnError` to the pipeline, killing the entire subscription.
- **Skip** — Ignore the crash and continue processing other inner subscriptions.
- **Restart of int** — Re-spawn the failed inner, up to N times before giving up.
- **process boundary** — Every operator is a process boundary. Each operator in a pipeline spawns its own BEAM process, linked to upstream and downstream. State cannot be shared across process boundaries via mutable variables.

## Interop

- **tapSend** — Side-effect operator that sends each element to an Erlang process via message passing. Bridges Factor pipelines to plain BEAM processes.
- **Emit** — Fable attribute for Erlang FFI. `[<Emit("module:function($0)")>]` generates a direct Erlang function call.
