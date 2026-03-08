# Actor Interop Design Document

This document describes the design for bridging BEAM agents/processes with reactive streams in Factor.

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
|> tapSend (fun msg -> Actor.send counterAgent msg)
|> subscribe logObserver
```

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

### Pattern 3: Channel as Agent Mailbox

Channels bridge agents and observables — use `multicast()` to create an agent-backed endpoint that can be pushed to from any process and subscribed to from a pipeline:

```fsharp
let observer, messages = Reactive.multicast ()

// Push from anywhere
Reactive.pushNext observer command

// Process in a pipeline
messages
|> Reactive.map handleCommand
|> Reactive.subscribe onResult onError onComplete
```

## Design Decisions

### Agent-Based Channels

Channels are parameterized by `Actor<ChannelMsg<'T>>`, making them composable and extensible. Pre-composed channels (`multicast`, `singleSubscriber`) handle common patterns. Custom channel behavior can be implemented by providing a custom agent handler.

### Backpressure Considerations

BEAM actor mailboxes are unbounded, matching Rx's push semantics. For high-volume streams, users should apply backpressure operators:

```fsharp
highVolumeStream
|> throttle 100      // At most 10 messages/second
|> tapSend (fun msg -> Actor.send target msg)
```

## File Structure

```text
src/Factor.Reactive/
├── Interop.fs       # Interop operators (tapSend)
├── Channel.fs       # Channel constructors (multicast, singleSubscriber)
└── ...
```

The main `Reactive.fs` re-exports the interop and channel operators.
