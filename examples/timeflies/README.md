# Timeflies - ActorX Demo

A classic Reactive Extensions demo ported to Gleam using ActorX and WebSockets.

## What it does

The letters of "TIME FLIES LIKE AN ARROW" follow your mouse cursor, with each successive letter delayed by an increasing amount (80ms per letter). This creates a trailing snake-like effect where the first letter follows immediately, while later letters lag behind.

## Architecture

```
Browser                              Gleam Server (Mist + ActorX)
┌─────────────────┐                  ┌────────────────────────────────────┐
│  mousemove      │ ──WebSocket──>   │  single_subject (input)            │
│  events (x,y)   │                  │         │                          │
│                 │                  │         ▼                          │
│                 │                  │  from_list(letters)                │
│                 │                  │  |> flat_map (for each letter)     │
│                 │                  │       mouse_moves                  │
│                 │                  │       |> delay(80ms * index)       │
│                 │                  │       |> map(add letter offset)    │
│                 │                  │         │                          │
│  render letters │ <──WebSocket──   │         ▼                          │
│  at positions   │                  │  send JSON to client               │
└─────────────────┘                  └────────────────────────────────────┘
```

## Key ActorX features demonstrated

- **`single_subject`** - Creates a hot observable that can receive mouse events pushed from WebSocket
- **`flat_map`** - For each letter, creates a stream that subscribes to mouse moves
- **`delay`** - Each letter's stream is delayed by `index * 80ms`
- **`map`** - Transforms mouse positions to letter positions with horizontal offset

## Running

```sh
cd examples/timeflies
gleam run
```

Then open http://localhost:3000 in your browser and move your mouse around.

## Original

This is a port of the classic Rx Timeflies example. See the original F# version in [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx).
