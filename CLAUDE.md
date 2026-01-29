# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ActorX is a Reactive Extensions (Rx) library for Gleam targeting the Erlang/BEAM runtime. It's a port of [FSharp.Control.AsyncRx](https://github.com/dbrattli/AsyncRx) to Gleam.

## Build Commands

Using justfile (preferred):
```sh
just build    # Build the project
just test     # Run all tests
just check    # Check code formatting
just format   # Format source and test files
just all      # Build and test
just clean    # Clean build artifacts
```

Or directly with gleam:
```sh
gleam build                      # Build the project
gleam test                       # Run all tests
gleam format --check src test    # Check code formatting
```

## Architecture

```text
Observable (source) → Operator (transform) → Observer (sink)
                            ↓
                     State Management
                  (process dictionary)
```

### Core Types (src/actorx/types.gleam)

- **Notification(a)**: Rx grammar atoms (`OnNext(a)`, `OnError(String)`, `OnCompleted`)
- **Disposable**: Resource cleanup handle with `dispose: fn() -> Nil`
- **Observer(a)**: Receives notifications via `on_next`, `on_error`, `on_completed` callbacks
- **Observable(a)**: Lazy push-based stream with `subscribe: fn(Observer(a)) -> Disposable`

### Module Structure

- **src/actorx.gleam**: Main API facade, re-exports all public types and operators
- **src/actorx/create.gleam**: Creation operators (`single`, `empty`, `never`, `fail`, `from_list`, `defer`)
- **src/actorx/transform.gleam**: Transform operators (`map`, `flat_map`, `concat_map`)
- **src/actorx/filter.gleam**: Filter operators (`filter`, `take`, `skip`, `take_while`, `skip_while`, `choose`, `distinct_until_changed`, `take_until`, `take_last`)
- **src/actorx/builder.gleam**: Monadic composition for Gleam's `use` syntax (`bind`, `return`, `map_over`, `filter_with`, `for_each`)
- **src/actorx/safe_observer.gleam**: Enforces Rx grammar (OnNext*, then optionally OnError or OnCompleted)

### State Management

The current implementation uses Erlang's process dictionary for mutable state via FFI (`erlang:put`, `erlang:get`, `erlang:make_ref`). This works for synchronous observables. Actor-based async implementation using `gleam_otp` is planned for Phase 2.

### Rx Contract

The library enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- After a terminal event (OnError or OnCompleted), no further events are delivered
- `safe_observer.wrap()` handles this enforcement

## Testing

Tests use gleeunit and are organized by operator category:

- `test/create_test.gleam` - Creation operators
- `test/transform_test.gleam` - Transform operators and monad laws
- `test/filter_test.gleam` - Filter operators
- `test/builder_test.gleam` - Builder module for `use` syntax
- `test/test_utils.gleam` - Shared test utilities

Tests use the same process dictionary pattern for state management as the implementation.

## Dependencies

- gleam_stdlib >= 0.52.0
- gleam_otp >= 0.16.0
- gleam_erlang >= 0.35.0
- gleeunit >= 1.0.0 (dev)
