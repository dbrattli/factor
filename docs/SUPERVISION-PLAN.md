# ActorX Supervision Plan

This document explores how OTP supervision patterns relate to Reactive Extensions pipelines and outlines possible architectural approaches for ActorX.

## Conceptual Overlap

There's a natural parallel between Rx orchestration and OTP supervision:

|                   Rx Pipeline                    |              OTP Supervision              |
| ------------------------------------------------ | ----------------------------------------- |
| Operators create/manage downstream subscriptions | Supervisors create/manage child processes |
| Errors propagate and terminate streams           | Crashes propagate up the supervision tree |
| Disposable handles cleanup                       | Process exit triggers cleanup             |
| Hierarchical: source → operators → observer      | Hierarchical: supervisor → children       |

Both patterns involve:

- Hierarchical resource management
- Propagation of failure signals
- Cleanup on termination
- Orchestration of child components

## Key Tension: Termination Semantics

The fundamental difference lies in what happens on failure:

- **Rx**: Error terminates the stream. The contract says no more events after `OnError`. Streams are *finite by design*.
- **OTP**: Crash triggers restart. The supervisor brings the child back to life. Actors are *perpetual by design*.

This creates a conceptual mismatch that must be resolved when combining the two models.

## Possible Approaches

### 1. Supervision at the Subscription Level

Each subscription becomes a supervised actor:

```text
Supervisor
├── Subscription1 (cold observable → observer)
├── Subscription2 (cold observable → observer)
└── Subscription3 (cold observable → observer)
```

**Restart semantics**: If a subscription actor crashes, restart means "resubscribe from scratch" - essentially `retry` semantics baked into the supervision strategy.

**Pros**:

- Natural OTP integration
- Fault isolation per subscription
- Automatic retry via supervision

**Cons**:

- Overhead for simple synchronous operators
- May not match user expectations (implicit retry)

### 2. Supervision for Hot Observables Only

Cold observables stay lightweight (process dictionary for state). Hot observables (subjects, shared streams) get supervision:

```text
Supervisor
├── Subject1 (long-lived, multicasts to subscribers)
├── SharedStream (ref-counted hot observable)
└── IntervalTimer (infinite stream)
```

**Rationale**: Hot observables are inherently long-lived and benefit from restart semantics. Cold observables are ephemeral by nature.

**Pros**:

- Matches the natural lifetime of each observable type
- Minimal overhead for cold streams
- Subjects recover from crashes

**Cons**:

- Two different execution models to maintain
- Subscribers to a restarted subject may miss events

### 3. Operator Actors Under Pipeline Supervision

Each stateful operator in a pipeline is a supervised child, with the pipeline itself as a supervision unit:

```text
PipelineSupervisor (one_for_all)
├── SourceActor
├── MapOperator
├── FilterOperator
└── MergeInnerCoordinator
```

**Restart strategy**: `one_for_all` makes sense - if any operator crashes, restart the whole pipeline (since operator state is interconnected).

**Pros**:

- Full pipeline fault tolerance
- Clear restart semantics

**Cons**:

- High overhead (actor per operator)
- Pipeline restart loses all in-flight state
- Complexity in wiring up the supervision tree dynamically

### 4. Layered Approach

Separate concerns into distinct layers:

```text
┌─────────────────────────────────────────────┐
│  Application Layer                          │
│  (user decides what to supervise)           │
├─────────────────────────────────────────────┤
│  Supervision Layer                          │
│  (subjects, intervals, shared streams)      │
├─────────────────────────────────────────────┤
│  Actor Layer                                │
│  (async operators: merge, combine, etc.)    │
├─────────────────────────────────────────────┤
│  Core Rx Layer                              │
│  (pure operators, process dict state)       │
└─────────────────────────────────────────────┘
```

**Pros**:

- Clean separation of concerns
- Users get control over fault tolerance
- Each layer uses appropriate mechanisms

**Cons**:

- More complex to understand
- Cross-layer interactions need careful design

## Recommended Approach: Hybrid Model

Based on ActorX's current architecture, a hybrid approach is recommended:

### Keep Sync Operators Lightweight

The process dictionary pattern works well for simple state. No need for actor overhead on every `map` or `filter`. These operators:

- Execute in the subscriber's process
- Use process dictionary for minimal state (stopped flags, counters)
- Have no supervision overhead

### Actor-Based Async Operators

Already the pattern for `merge_inner`, `combine_latest`, etc. These operators:

- Spawn coordinator actors for complex state
- Handle concurrent subscriptions
- Naturally fit as isolated processes
- Could optionally be supervised

### Supervised Subjects and Shared Streams

These are the "persistent" parts of an Rx system:

- `subject()` - should survive crashes and continue accepting values
- `share()` - manages subscriber refcount, benefits from supervision
- `interval()` - long-running, should restart on failure

### Supervision via Existing Operators

Rather than introducing a new `supervise` operator, we can extend existing error-handling operators to also handle BEAM-level crashes. The operators `retry` and `catch` already implement resubscription logic for `OnError` - they just need to also monitor for process crashes.

**Key insight**: From downstream's perspective, it doesn't matter *why* upstream failed (explicit `OnError` vs process crash). What matters is the recovery strategy. Unifying these simplifies the mental model.

| Trigger         | Current Behavior       | Extended Behavior      |
| --------------- | ---------------------- | ---------------------- |
| `OnError`       | retry/catch handle it  | same                   |
| Process crash   | unhandled (propagates) | retry/catch handle it  |

**Extended `retry`**:

```gleam
// Current: resubscribes on OnError
// Extended: also resubscribes on upstream crash
observable
|> actorx.map(risky_operation)
|> actorx.retry(3)  // handles both OnError AND crashes
|> actorx.subscribe(observer)
```

Implementation:

1. `retry` spawns an actor that subscribes to upstream
2. Actor monitors the upstream subscription process
3. On `OnError` OR monitored process crash → resubscribe (up to n times)
4. Exhausted retries → send `OnError` downstream

**Extended `catch`**:

```gleam
// Current: switches to fallback on OnError
// Extended: also switches on upstream crash
observable
|> actorx.map(risky_operation)
|> actorx.catch(fn(_error) { fallback_observable })  // handles both
|> actorx.subscribe(observer)
```

Implementation:

1. `catch` spawns an actor that subscribes to upstream
2. Actor monitors the upstream subscription process
3. On `OnError` OR crash → switch to fallback observable

**Benefits**:

- **No new operators** - users already know `retry` and `catch`
- **Unified error model** - crash and OnError handled the same way
- **Composable** - place error handling anywhere in the pipeline
- **Simpler mental model** - "errors are errors" regardless of source

**Example with both**:

```gleam
http_requests()
|> actorx.retry(3)                                    // retry network failures (OnError or crash)
|> actorx.flat_map(parse_response)
|> actorx.catch(fn(_) { actorx.single(default) })    // fallback on parse failure
|> actorx.subscribe(observer)
```

**Trade-off**: This unification means you can't distinguish between "the upstream chose to error" vs "the upstream crashed unexpectedly". If that distinction matters, a separate `supervise` operator would be needed. In practice, the recovery action is usually the same.

## Philosophical View

The Rx pipeline is a **dataflow graph**, not a supervision tree. The "orchestration" is about *what happens to data*, not *how to recover from failures*.

However, the *execution* of that dataflow on BEAM naturally maps to actors. This creates a separation:

|       Concern       |            Defined By             |
| ------------------- | --------------------------------- |
| Dataflow semantics  | Rx operators (pure, composable)   |
| Execution semantics | Actor implementation of operators |
| Fault tolerance     | Supervision of those actors       |

This separation lets you:

1. Reason about pipelines declaratively
2. Get BEAM's fault tolerance for the runtime
3. Choose supervision granularity per use case

## Open Questions

1. **Subject restart behavior**: When a subject restarts, should it:
   - Notify existing subscribers of the restart?
   - Require re-subscription?
   - Buffer and replay missed values?

2. **Pipeline identity**: If a pipeline restarts, is it "the same" pipeline?
   - Important for stateful operators (scan, reduce)
   - Affects how subscribers perceive the stream

3. **Backpressure interaction**: How does supervision interact with backpressure?
   - Crashed slow consumer → restart → catch-up flood?

4. **Supervision strategy selection**: Which OTP strategies map to Rx semantics?
   - `one_for_one`: Independent subscriptions
   - `one_for_all`: Pipeline as unit
   - `rest_for_one`: Upstream/downstream dependency

## Next Steps

1. Implement supervised subjects as a proof of concept
2. Explore `subscribe_supervised` API design
3. Document which operators benefit from supervision
4. Consider integration with `gleam_otp` supervisor module
