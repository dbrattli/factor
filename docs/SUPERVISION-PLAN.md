# Factor Supervision Plan

This document explores how OTP supervision patterns relate to Reactive Extensions pipelines and outlines possible architectural approaches for Factor.

## Conceptual Overlap

There's a natural parallel between Rx orchestration and OTP supervision:

| Rx Pipeline | OTP Supervision |
| ----------- | --------------- |
| Operators create/manage downstream subscriptions | Supervisors create/manage child processes |
| Errors propagate and terminate streams | Crashes propagate up the supervision tree |
| Disposable handles cleanup | Process exit triggers cleanup |
| Hierarchical: source → operators → observer | Hierarchical: supervisor → children |

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

## Current Architecture (Post-Migration)

Factor (F#/Fable.Beam) uses a simpler state management model than the original Gleam version:

- **Mutable variables** for all stateful operators (backed by process dictionary on BEAM)
- **No dedicated actor processes** per operator
- **Erlang FFI** (`Fable.Core.Emit`) for time-based operations

This means the supervision concerns are different from the original Gleam version:

| Concern | Gleam Version | F# Version |
| ------- | ------------- | ---------- |
| Operator state | Actor per operator | Mutable vars in subscriber process |
| Crash isolation | Actor boundaries | Single process (subscriber) |
| Timer management | `process.send_after` | `timer:apply_after` FFI |

## Possible Approaches

### 1. Supervision at the Subscription Level

Each subscription runs in its own process. A supervisor manages these subscription processes:

```text
Supervisor
├── Subscription1 (cold observable → observer)
├── Subscription2 (cold observable → observer)
└── Subscription3 (cold observable → observer)
```

**Restart semantics**: If a subscription process crashes, restart means "resubscribe from scratch" — essentially `retry` semantics baked into the supervision strategy.

### 2. Supervision via Existing Operators

Rather than introducing OTP supervisors, extend `retry` and `catch` to also handle BEAM-level crashes:

```fsharp
// Current: resubscribes on OnError
// Extended: also resubscribes on upstream crash
observable
|> map riskyOperation
|> retry 3  // handles both OnError AND crashes
|> subscribe observer
```

**Key insight**: From downstream's perspective, it doesn't matter *why* upstream failed (explicit `OnError` vs process crash). What matters is the recovery strategy.

### 3. Layered Approach

Separate concerns into distinct layers:

```text
┌─────────────────────────────────────────────┐
│  Application Layer                          │
│  (user decides what to supervise)           │
├─────────────────────────────────────────────┤
│  Supervision Layer                          │
│  (subjects, intervals, shared streams)      │
├─────────────────────────────────────────────┤
│  Core Rx Layer                              │
│  (pure operators, mutable state)            │
└─────────────────────────────────────────────┘
```

## Recommended Approach

Given the F# migration to mutable variables (no per-operator actors), the supervision model simplifies:

### Keep Operators Lightweight

All operators use mutable variables in the subscriber's process. No actor overhead. These operators:

- Execute in the subscriber's process
- Use mutable variables for state
- Have no supervision overhead
- Crash if the subscriber process crashes (expected behavior)

### Supervised Subjects and Shared Streams

These are the "persistent" parts of an Rx system that could benefit from supervision:

- `subject()` - should survive crashes and continue accepting values
- `share()` - manages subscriber refcount, benefits from supervision
- Time-based operators - long-running, could benefit from restart

### Error Recovery via Operators

The `retry` and `catch` operators provide the primary error recovery mechanism:

```fsharp
httpRequests ()
|> retry 3                                    // retry failures
|> flatMap parseResponse
|> catch (fun _ -> single defaultValue)       // fallback on error
|> subscribe observer
```

## Implementation Considerations for Fable.Beam

When Fable.Beam provides process spawning and monitoring primitives:

1. **Process monitoring** via `Fable.Core.Emit` for `erlang:monitor/2`
2. **Supervised subjects** that spawn their coordinator in a supervised process
3. **Crash-aware retry/catch** that monitor upstream processes

## Open Questions

1. **Process identity**: How does Fable.Beam expose process identities (PIDs)?
2. **Monitor support**: Can `erlang:monitor/2` be called via Emit?
3. **Supervision trees**: Can OTP supervisors be created from Fable.Beam code?
4. **MailboxProcessor**: Does Fable.Beam's MailboxProcessor map to an Erlang process?

These questions will be resolved as Fable.Beam matures and provides more BEAM interop primitives.

## Design Principles

1. **Unified error model** — Crashes and `OnError` handled identically by `retry`/`catch`
2. **Opt-in supervision** — Keep the simple path lightweight, add supervision as needed
3. **Composable recovery** — Place error handling anywhere in the pipeline
4. **Leverage BEAM** — Use OTP supervision where it fits naturally (long-lived processes)
