//// Safe Observer - Observer wrapper that enforces Rx grammar
////
//// The safe_observer wraps a downstream observer and:
//// 1. Enforces Rx grammar: OnNext* (OnError | OnCompleted)?
//// 2. Disposes resources on terminal events
//// 3. Ignores messages after terminal event
////
//// Note: For full actor-based implementation with message serialization,
//// use gleam_otp actors. This module provides a simpler synchronous version.

import actorx/types.{
  type Disposable, type Notification, type Observer, Disposable, Observer,
  OnCompleted, OnError, OnNext,
}

/// Wrap an observer with Rx grammar enforcement.
///
/// Returns an Observer that:
/// - Forwards OnNext until a terminal event
/// - Calls disposal on terminal events
/// - Ignores all events after terminal
pub fn wrap(observer: Observer(a), disposable: Disposable) -> Observer(a) {
  let Observer(downstream) = observer
  let Disposable(dispose) = disposable

  // Use Erlang process dictionary for stopped state
  let stopped_ref = make_ref(0)

  Observer(notify: fn(n) {
    case get_ref(stopped_ref) {
      0 ->
        case n {
          OnNext(x) -> downstream(OnNext(x))
          OnError(e) -> {
            set_ref(stopped_ref, 1)
            dispose()
            downstream(OnError(e))
          }
          OnCompleted -> {
            set_ref(stopped_ref, 1)
            dispose()
            downstream(OnCompleted)
          }
        }
      _ -> Nil
    }
  })
}

/// Create an observer from a notification handler function,
/// wrapped with Rx grammar enforcement.
pub fn from_notify(
  notify: fn(Notification(a)) -> Nil,
  disposable: Disposable,
) -> Observer(a) {
  let observer = Observer(notify: notify)
  wrap(observer, disposable)
}

// Mutable reference helpers using Erlang process dictionary

@external(erlang, "erlang", "put")
fn erlang_put(key: a, value: b) -> c

@external(erlang, "erlang", "get")
fn erlang_get(key: a) -> b

@external(erlang, "erlang", "make_ref")
fn erlang_make_ref() -> a

fn make_ref(initial: Int) -> a {
  let ref = erlang_make_ref()
  erlang_put(ref, initial)
  ref
}

fn get_ref(ref: a) -> Int {
  erlang_get(ref)
}

fn set_ref(ref: a, value: Int) -> Nil {
  erlang_put(ref, value)
  Nil
}
