# Factor Terminology

Quick reference for concepts used throughout Factor. Organized by the layered architecture described in [DESIGN.md](DESIGN.md).

## Layer 0: Raw Process (BEAM Process)

- **Pid** — An Erlang process identifier. In Factor, wrapped as `Agent<'Msg>` for type-safe message sending.
- **process dictionary** — Per-process mutable key-value store in BEAM. Factor uses it for `factor_children` (child handler registry) and `factor_exits` (exit handler registry). Operators using `mutable` variables compile to process dictionary access.
- **spawnLinked** — Spawn a new BEAM process linked to the caller. If either process crashes, the other receives an EXIT signal. Foundation for supervision.
- **trapExits** — Enable `trap_exit` on a process so EXIT signals become messages instead of causing termination. Used by supervisory processes.
- **registerChild / unregisterChild** — Add or remove a callback in the process dictionary's `factor_children` registry. Used to dispatch `{factor_child, Ref, Msg}` messages to the correct handler.
- **registerExit / unregisterExit** — Add or remove a callback in the `factor_exits` registry. Used to handle `{'EXIT', Pid, Reason}` messages from linked processes.

### Message Types

- `{factor_child, Ref, Msg}` — Delivers a message from an operator or channel agent to a subscribing process. Dispatched via `factor_children` registry.
- `{factor_msg, Msg}` — Delivers a message to an agent process. Used by `Agent.send` and channel push helpers.
- `{factor_timer, Ref, Callback}` — Delivers a timer callback. Dispatched via `factor_timer:schedule`.
- `{'EXIT', Pid, Reason}` — Delivered when a linked process terminates (requires `trap_exit`). Dispatched via `factor_exits` registry.

## Agent Layer

- **Agent\<'Msg\>** — A typed wrapper around a BEAM process Pid. Ensures only correctly-typed messages can be sent via `Agent.send`.
- **agent { }** — Computation expression for CPS-based selective receive. Provides `let!` to wait for messages. Used internally by operators (via `Operator.recvMsg` and `Operator.recvAnyMsg`) and can be used directly for custom actors.
- **Agent.start** — Start a stateful agent with a message handler (gen_server style). The handler returns `Continue(newState)` or `Stop`.
- **Agent.spawn** — Spawn a raw agent process running a body function.
- **Agent.send** — Fire-and-forget message send to an agent.
- **Agent.call** — Synchronous request-response. Sends a message with a `ReplyChannel`, blocks until the agent replies.
- **ReplyChannel\<'Reply\>** — A callback that the receiver uses to send a response back to the caller in `Agent.call`.
- **Next\<'State\>** — What an agent handler returns: `Continue of 'State` or `Stop`.

## Layer 1: Observer (Process Endpoint)

- **Msg\<'T\>** — The atoms of the Rx grammar: `OnNext of 'T`, `OnError of exn`, or `OnCompleted`. Every event in a pipeline is one of these three. This is the protocol restriction — constraining WHAT messages are allowed.
- **Observer\<'T\>** — A process endpoint. Contains `Pid` and `Ref` identifying the target process. Messages are sent via `Process.sendChildMsg`. The process itself IS the observer.

### Rx Contract

- **Rx grammar** — The legal sequence of messages: `OnNext* (OnError | OnCompleted)?` — zero or more `OnNext`, followed by at most one terminal event.
- **terminal event** — Either `OnError` or `OnCompleted`. After a terminal event, the operator process exits naturally (the agent CE loop ends without `return! loop`), which cascades through process links to tear down the pipeline. Rx grammar is self-enforced by each operator process — no separate wrapper is needed.

## Layer 2: Observable (Composable Stream)

- **Observable\<'T\>** — A lazy push-based stream. Wraps a `Subscribe: Observer<'T> -> Handle` function. Nothing happens until subscribed. Subscribe creates a BEAM process (linked to the caller) that subscribes to upstream. The chain of spawned processes IS the supervision tree. Subscribe IS Run.
- **Handle** — A resource cleanup token. Wraps a `Dispose: unit -> unit` function. Calling `Dispose` cancels a subscription or releases resources. Dispose IS Kill — the returned capability to manage the lifetime of what was subscribed.

### Cold vs Hot

- **cold** — An Observable that does work per subscriber. Each `Subscribe` call starts a fresh sequence (spawns a fresh computation). Most operators produce cold Observables (e.g. `ofList`, `map`, `filter`).
- **hot** — An Observable that shares a single execution among subscribers. Channels (`multicast`, `singleSubscriber`) and the output of `share`/`publish` are hot.

### Creation Operators

- **create** — Build an Observable from a raw subscribe function.
- **single** — An Observable that emits one value then completes.
- **empty** — An Observable that completes immediately with no values.
- **never** — An Observable that never emits and never completes.
- **fail** — An Observable that immediately errors with a given exception.
- **ofList** — An Observable that synchronously emits all items from a list then completes.
- **defer** — Delays Observable creation until subscribe time by calling a factory function.

### Transform Operators

- **map / mapi** — Project each element through a function. `mapi` also passes the element index.
- **flatMap** — Map each element to an inner Observable and merge results. Spawns a linked child process per inner Observable.
- **mergeInner** — Flatten an `Observable<Observable<'T>>` with supervision policy and optional `maxConcurrency`. Each inner Observable is spawned as a linked child process.
- **concatMap / concatInner** — Like flatMap/mergeInner but subscribes to inners one at a time, in order.
- **switchMap / switchInner** — Like flatMap/mergeInner but disposes the previous inner when a new one arrives.
- **scan** — Accumulate state over elements, emitting each intermediate value.
- **reduce** — Accumulate state over elements, emitting only the final value on completion.
- **groupBy** — Partition elements by key. Each unique key produces an inner Observable backed by a `singleSubscriber` channel.
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
- **takeUntil** — Emit elements until a notifier Observable produces a value.
- **takeLast** — Buffer all elements, emit only the last N on completion.
- **first / last** — Emit only the first or last element.
- **defaultIfEmpty** — Emit a default value if the source completes without emitting.
- **sample** — Emit the latest source value each time a sampler Observable fires.

### Combine Operators

- **merge / merge2** — Subscribe to all sources concurrently, interleave their values.
- **combineLatest** — When either source emits, combine with the latest value from the other.
- **withLatestFrom** — When the primary source emits, combine with the latest from a sampler.
- **zip** — Pair elements from two sources by index.
- **concat / concat2** — Subscribe to sources one after another, in order.
- **amb / race** — Subscribe to all sources; forward only the one that emits first, dispose the rest.
- **forkJoin** — Wait for all sources to complete, emit a list of their last values.

### Time-shift Operators

- **timer** — Emit a single `0` after a delay (milliseconds).
- **interval** — Emit incrementing integers at a fixed period.
- **delay** — Shift each element forward in time by a fixed duration.
- **debounce** — Emit an element only after a quiet period with no new elements.
- **throttle** — After emitting, suppress further elements for a duration.
- **timeout** — Error if no element arrives within a duration.

### Error Handling

- **retry** — Re-subscribe to the source up to N times on error.
- **catch** — On error, switch to an alternative Observable produced by an error handler function.

### Multicast

- **publish** — Convert a cold Observable into a connectable hot Observable. Returns `Observable<'T> * (unit -> Handle)`. Subscribers attach first, then `connect()` starts the source.
- **share** — Automatically connect on first subscriber, disconnect when the last unsubscribes. Refcounted convenience over `publish`.
- **connect** — The function returned by `publish` that starts the underlying source subscription.
- **refcount** — The pattern used by `share` — track subscriber count and auto-connect/disconnect.

## Bridges Between Layers

- **ChannelMsg\<'T\>** — Protocol messages for channel agents: `Notify of Msg<'T>`, `Subscribe of Observer<'T> * ReplyChannel<unit>`, `Unsubscribe of obj`. Parameterizes channel agent behavior.
- **channel** — Wraps any `Agent<ChannelMsg<'T>>` into an `Observer<'T> * Observable<'T>` pair. The Observer is the push side, the Observable is the subscribe side. Bridges the Agent layer to the Observable layer.
- **multicast** — Pre-composed multicast channel backed by a stateful agent. Returns `Observer<'T> * Observable<'T>`. Broadcasts to all subscribers, no buffering.
- **singleSubscriber** — Pre-composed single-subscriber channel with buffering. Returns `Observer<'T> * Observable<'T>`. Messages sent before subscription are buffered and replayed.
- **pushNext / pushError / pushCompleted** — Send messages into a channel via its Observer push side. Uses `Agent.send` with `ChannelMsg.Notify`.

## Composition

- **observable { }** — Computation expression for composing Observables monadically. `let!` desugars to `flatMap`, spawning a linked child process per binding. Creates supervision boundaries. Operator callbacks run in spawned processes; mutable variables captured in closures will not work cross-process.
- **pipe operators** — Using `|>` with operators like `map`, `filter`, `flatMap`. Every operator spawns a BEAM process. The pipeline builds a linked process tree that IS the supervision hierarchy.

## Operator Infrastructure

- **Operator.forNext** — Stateless single-source operator template (used by map, filter, tap, choose).
- **Operator.forNextStateful** — Stateful single-source template (used by mapi, skip, skipWhile, distinctUntilChanged, distinct).
- **Operator.ofMsgStateful** — Full Msg control with state (used by scan, reduce, take, takeWhile, pairwise, first, last, defaultIfEmpty, takeLast).
- **Operator.ofMsg2** — Dual-source with state (used by combineLatest, withLatestFrom, zip, takeUntil, sample).
- **Operator.spawnOp** — Spawn a linked operator process from an agent CE computation, return a dispose Handle.
- **Operator.recvMsg** — Selective receive for a single source. Blocks for `{factor_child, Ref, Msg}` while dispatching timers and EXIT signals.
- **Operator.recvAnyMsg** — Selective receive for any source (multi-source operators).
- **Operator.childLoop** — Generic message pump for operators without a source to receive from (timer, interval, mergeInner parent).

## Supervision

- **SupervisionPolicy** — Controls behavior when a spawned child process crashes.
- **Terminate** — (Default) Propagate the child's error as `OnError` to the pipeline, killing the entire subscription.
- **Skip** — Ignore the crash and continue processing other inner subscriptions.
- **Restart of int** — Re-subscribe to the failed inner, up to N times before giving up.
- **process boundary** — Every operator is a process boundary. Each operator in a pipeline spawns its own BEAM process, linked to upstream and downstream. State cannot be shared across process boundaries via mutable variables.

## Interop

- **tapSend** — Side-effect operator that sends each element to an external function. Bridges Observable pipelines to plain BEAM processes.
- **Emit** — Fable attribute for Erlang FFI. `[<Emit("module:function($0)")>]` generates a direct Erlang function call.
