# Timeflies - Factor Demo

A classic Reactive Extensions demo using Factor (F#/Fable.Beam) and Cowboy WebSockets.

## What it does

The letters of "TIME FLIES LIKE AN ARROW" follow your mouse cursor, with each successive letter delayed by an increasing amount (80ms per letter). This creates a trailing snake-like effect where the first letter follows immediately, while later letters lag behind.

## Architecture

```
Browser                              BEAM Server (Cowboy + Factor)
┌─────────────────┐                  ┌────────────────────────────────────┐
│  mousemove      │ ──WebSocket──>   │  subject (input)                   │
│  events (x,y)   │                  │         │                          │
│                 │                  │         ▼                          │
│                 │                  │  ofList(letters)                   │
│                 │                  │  |> flatMap (for each letter)      │
│                 │                  │       mouseMoves                   │
│                 │                  │       |> delay(80ms * index)       │
│                 │                  │       |> map(add letter offset)    │
│                 │                  │         │                          │
│  render letters │ <──WebSocket──   │         ▼                          │
│  at positions   │                  │  send JSON to client               │
└─────────────────┘                  └────────────────────────────────────┘
```

## Key Factor features demonstrated

- **`subject`** - Creates a hot observable that can receive mouse events pushed from WebSocket
- **`flatMap`** - For each letter, creates a stream that subscribes to mouse moves
- **`delay`** - Each letter's stream is delayed by `index * 80ms`
- **`map`** - Transforms mouse positions to letter positions with horizontal offset

## Prerequisites

- .NET SDK 8+
- Fable.Beam compiler (at `../../../fable/fable-beam/src/Fable.Cli`)
- Erlang/OTP
- rebar3
- Factor library built (`just build` from project root)

## Running

```sh
cd examples/timeflies

# Build Factor library first (from project root)
cd ../.. && just build && cd examples/timeflies

# Build and run
just run
```

Then open http://localhost:3000 in your browser and move your mouse around.

## Original

This is a port of the classic Rx Timeflies example. See the original F# version in [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx).
