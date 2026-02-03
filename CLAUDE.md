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
                   (actors / process dict)
```

### Core Types (src/actorx/types.gleam)

- **Notification(a)**: Rx grammar atoms (`OnNext(a)`, `OnError(String)`, `OnCompleted`)
- **Disposable**: Resource cleanup handle with `dispose: fn() -> Nil`
- **Observer(a)**: Receives notifications via notify callback
- **Observable(a)**: Lazy push-based stream with `subscribe: fn(Observer(a)) -> Disposable`

### Module Structure

- **src/actorx.gleam**: Main API facade, re-exports all public types and operators
- **src/actorx/types.gleam**: Core types (Observable, Observer, Notification, Disposable)
- **src/actorx/create.gleam**: Creation operators (`create`, `single`, `empty`, `never`, `fail`, `from_list`, `defer`)
- **src/actorx/transform.gleam**: Transform operators (`map`, `mapi`, `flat_map`, `flat_mapi`, `concat_map`, `concat_mapi`, `merge_inner`, `concat_inner`, `switch_inner`, `switch_map`, `switch_mapi`, `tap`, `start_with`, `pairwise`, `scan`, `reduce`, `group_by`)
- **src/actorx/filter.gleam**: Filter operators (`filter`, `take`, `skip`, `take_while`, `skip_while`, `choose`, `distinct_until_changed`, `distinct`, `take_until`, `take_last`, `first`, `last`, `default_if_empty`, `sample`)
- **src/actorx/combine.gleam**: Combining operators (`merge`, `merge2`, `combine_latest`, `with_latest_from`, `zip`, `concat`, `concat2`, `amb`, `race`, `fork_join`)
- **src/actorx/timeshift.gleam**: Time-based operators (`timer`, `interval`, `delay`, `debounce`, `throttle`, `timeout`)
- **src/actorx/subject.gleam**: Subjects (`subject`, `single_subject`, `publish`, `share`)
- **src/actorx/error.gleam**: Error handling (`retry`, `catch`)
- **src/actorx/interop.gleam**: Actor interop (`from_subject`, `to_subject`, `call_actor`)
- **src/actorx/safe_observer.gleam**: Enforces Rx grammar (OnNext*, then optionally OnError or OnCompleted)
- **src/actorx/builder.gleam**: Monadic composition for Gleam's `use` syntax (`bind`, `return`, `map_over`, `filter_with`, `for_each`)

### State Management

The library uses two patterns for mutable state:

**1. Actor-based (primary)**: Most stateful operators spawn a coordinator actor using `gleam/erlang/process.Subject` for typed message passing. The actor maintains state through recursive loop functions. Used by: `take`, `skip`, `merge`, `combine_latest`, `zip`, `debounce`, `throttle`, `subject`, `retry`, and many others.

Pattern:

```gleam
let control_ready: Subject(Subject(OperatorMsg(a))) = process.new_subject()
process.spawn(fn() {
  let control: Subject(OperatorMsg(a)) = process.new_subject()
  process.send(control_ready, control)
  operator_loop(control, downstream, initial_state)
})
let control = process.receive(control_ready, 1000)
```

**2. Process dictionary (Rx grammar enforcement only)**: Used exclusively in `safe_observer.gleam` via FFI to `erlang:put`/`erlang:get`/`erlang:make_ref` for tracking the "stopped" flag.

### Rx Contract

The library enforces the Rx grammar: `OnNext* (OnError | OnCompleted)?`

- After a terminal event (OnError or OnCompleted), no further events are delivered
- `safe_observer.wrap()` handles this enforcement

## Testing

Tests use gleeunit and are organized by operator category:

- `test/create_test.gleam` - Creation operators
- `test/transform_test.gleam` - Transform operators and monad laws
- `test/filter_test.gleam` - Filter operators
- `test/combine_test.gleam` - Combining operators
- `test/subject_test.gleam` - Subject types
- `test/timeshift_test.gleam` - Time-based operators
- `test/error_test.gleam` - Error handling
- `test/interop_test.gleam` - Actor interop
- `test/builder_test.gleam` - Builder module for `use` syntax
- `test/group_by_test.gleam` - Group by operator
- `test/share_test.gleam` - Share operator
- `test/merge_inner_test.gleam` - Merge inner with concurrency
- `test/new_operators_test.gleam` - Newer operators (amb, race, fork_join, timeout)
- `test/amb_forkjoin_test.gleam` - Amb and fork join tests
- `test/actorx_test.gleam` - Main test suite
- `test/test_utils.gleam` - Shared test utilities

## Dependencies

- gleam_stdlib >= 0.52.0
- gleam_otp >= 0.16.0
- gleam_erlang >= 0.35.0
- gleeunit >= 1.0.0 (dev)
