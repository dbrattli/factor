# Actor Interop Design Document

This document describes the design for bridging BEAM actors/processes with reactive streams in Factor.

## Overview

Factor provides reactive streams on the BEAM, but many BEAM applications use OTP actors (processes) for state management and concurrency. This module bridges these two paradigms.

## Current Operators

| Operator | Purpose | Type |
| -------- | ------- | ---- |
| `tapSend(send, source)` | Send emissions via callback (passthrough) | Operator |

### `tapSend` - Send Emissions via Callback

Passthrough operator that calls a send function for each value while forwarding downstream.

```fsharp
let tapSend (send: 'a -> unit) (source: Observable<'a>) : Observable<'a>
```

**Characteristics:**

- **Passthrough**: Values flow to both the send function and downstream observer
- **Only sends OnNext**: Terminal events (OnError, OnCompleted) are forwarded downstream only
- **Synchronous**: Send is called before forwarding downstream

**Example usage:**

```fsharp
interval 100
|> take 10
|> map (fun n -> Increment n)
|> tapSend (fun msg -> sendToActor counterActor msg)
|> subscribe logObserver
```

## Future Operators

The following operators from the original Gleam version could be added when Fable.Beam provides the necessary process primitives:

| Operator | Purpose | Requires |
| -------- | ------- | -------- |
| `fromSubject()` | Create Subject/Observable bridge | BEAM process.Subject |
| `toSubject(source, subject)` | Send emissions to BEAM Subject | BEAM process.Subject |
| `callActor(subject, timeout, makeRequest)` | Request-response as Observable | BEAM process.Subject + receive |

These require typed BEAM process messaging (Subject/receive) which may need additional Fable.Beam FFI support.

## Usage Patterns

### Pattern 1: Stream Driving External System

```fsharp
// Process a stream and send to external system
ofList commands
|> delay 100
|> tapSend sendToExternalSystem
|> subscribe logObserver
```

### Pattern 2: Side Effects in Pipeline

```fsharp
// Log values while processing
source
|> tapSend (fun x -> printfn "Processing: %A" x)
|> map transform
|> subscribe observer
```

## Design Decisions

### Simplified Interop

The F# version simplifies interop compared to the Gleam version. Since Fable.Beam's process primitives are still evolving, we provide `tapSend` as a general-purpose side-effect operator. More specific BEAM interop (Subject bridging, call/reply) can be added as Fable.Beam matures.

### Backpressure Considerations

BEAM actor mailboxes are unbounded, matching Rx's push semantics. For high-volume streams, users should apply backpressure operators:

```fsharp
highVolumeStream
|> throttle 100      // At most 10 messages/second
|> tapSend sendToActor
```

## File Structure

```text
src/
├── Interop.fs       # Interop operators
└── ...
```

The main `src/Factor.fs` re-exports the interop operators.
