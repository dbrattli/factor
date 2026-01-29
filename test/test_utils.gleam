//// Shared test utilities for ActorX tests

import actorx/types.{
  type Notification, type Observer, Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process
import gleam/list

// ============================================================================
// Erlang process dictionary FFI for test state
// ============================================================================

@external(erlang, "erlang", "put")
pub fn erlang_put(key: a, value: b) -> c

@external(erlang, "erlang", "get")
pub fn erlang_get(key: a) -> b

@external(erlang, "erlang", "make_ref")
pub fn erlang_make_ref() -> a

// ============================================================================
// Reference helpers for collecting test results
// ============================================================================

/// Create a reference to an empty list for collecting values
pub fn make_list_ref() -> a {
  let ref = erlang_make_ref()
  erlang_put(ref, [])
  ref
}

/// Get the current list from a reference
pub fn get_list_ref(ref: a) -> List(b) {
  erlang_get(ref)
}

/// Append a value to a list reference
pub fn append_to_ref(ref: a, value: b) -> Nil {
  let current: List(b) = erlang_get(ref)
  erlang_put(ref, list.append(current, [value]))
  Nil
}

/// Create a reference to a boolean value
pub fn make_bool_ref(initial: Bool) -> a {
  let ref = erlang_make_ref()
  erlang_put(ref, initial)
  ref
}

/// Get the current boolean from a reference
pub fn get_bool_ref(ref: a) -> Bool {
  erlang_get(ref)
}

/// Set a boolean reference value
pub fn set_bool_ref(ref: a, value: Bool) -> Nil {
  erlang_put(ref, value)
  Nil
}

/// Create a reference to an integer value
pub fn make_int_ref(initial: Int) -> a {
  let ref = erlang_make_ref()
  erlang_put(ref, initial)
  ref
}

/// Get the current integer from a reference
pub fn get_int_ref(ref: a) -> Int {
  erlang_get(ref)
}

/// Set an integer reference value
pub fn set_int_ref(ref: a, value: Int) -> Nil {
  erlang_put(ref, value)
  Nil
}

/// Increment an integer reference
pub fn incr_int_ref(ref: a) -> Nil {
  let current: Int = erlang_get(ref)
  erlang_put(ref, current + 1)
  Nil
}

// ============================================================================
// Test observer factories
// ============================================================================

/// Create a test observer that collects Int results
pub fn test_observer(
  results_ref: a,
  completed_ref: b,
  errors_ref: c,
) -> Observer(Int) {
  Observer(notify: fn(n) {
    case n {
      OnNext(x) -> append_to_ref(results_ref, x)
      OnError(err) -> append_to_ref(errors_ref, err)
      OnCompleted -> set_bool_ref(completed_ref, True)
    }
  })
}

/// Create a test observer that collects String results
pub fn test_string_observer(
  results_ref: a,
  completed_ref: b,
  errors_ref: c,
) -> Observer(String) {
  Observer(notify: fn(n) {
    case n {
      OnNext(x) -> append_to_ref(results_ref, x)
      OnError(err) -> append_to_ref(errors_ref, err)
      OnCompleted -> set_bool_ref(completed_ref, True)
    }
  })
}

/// Create a test observer that collects notifications in order
pub fn notification_observer(notifications_ref: a) -> Observer(Int) {
  Observer(notify: fn(n) { append_to_ref(notifications_ref, n) })
}

/// Get collected notifications from a reference
pub fn get_notifications(ref: a) -> List(Notification(Int)) {
  erlang_get(ref)
}

/// Sleep for the specified number of milliseconds.
/// Useful for testing async operators.
pub fn wait_ms(ms: Int) -> Nil {
  process.sleep(ms)
}
