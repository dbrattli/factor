//// Filter operators for ActorX
////
//// These operators filter elements from an observable sequence:
//// - filter: Keep elements matching predicate
//// - choose: Filter and map in one operation
////
//// Note: Stateful operators (take, skip, etc.) require actors for proper
//// state management. For synchronous observables, simplified versions are provided.

import actorx/types.{
  type Observable, type Observer, Observable, Observer, OnCompleted, OnError,
  OnNext, composite_disposable, empty_disposable,
}
import gleam/option.{type Option, None, Some}

/// Filters elements based on a predicate.
/// Only elements for which predicate returns True are emitted.
pub fn filter(source: Observable(a), predicate: fn(a) -> Bool) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case predicate(x) {
              True -> downstream(n)
              False -> Nil
            }
          _ -> downstream(n)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Applies a function to each element that returns Option.
/// Emits Some values, skips None values.
pub fn choose(
  source: Observable(a),
  chooser: fn(a) -> Option(b),
) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case chooser(x) {
              Some(value) -> downstream(OnNext(value))
              None -> Nil
            }
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> downstream(OnCompleted)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Returns the first N elements from the source (synchronous version).
/// For async sources, use the actor-based version.
pub fn take(source: Observable(a), count: Int) -> Observable(a) {
  // Handle edge case: take(0) completes immediately
  case count <= 0 {
    True ->
      Observable(subscribe: fn(observer: Observer(a)) {
        let Observer(downstream) = observer
        downstream(OnCompleted)
        empty_disposable()
      })
    False -> {
      Observable(subscribe: fn(observer: Observer(a)) {
        let Observer(downstream) = observer
        let counter_ref = make_ref(count)

        let upstream_observer =
          Observer(notify: fn(n) {
            case n {
              OnNext(x) -> {
                let remaining = get_ref(counter_ref)
                case remaining > 0 {
                  True -> {
                    downstream(OnNext(x))
                    set_ref(counter_ref, remaining - 1)
                    case remaining - 1 {
                      0 -> downstream(OnCompleted)
                      _ -> Nil
                    }
                  }
                  False -> Nil
                }
              }
              OnError(e) -> downstream(OnError(e))
              OnCompleted -> {
                // Only complete if take didn't complete us already
                case get_ref(counter_ref) > 0 {
                  True -> downstream(OnCompleted)
                  False -> Nil
                }
              }
            }
          })

        let Observable(subscribe) = source
        subscribe(upstream_observer)
      })
    }
  }
}

/// Skips the first N elements from the source.
pub fn skip(source: Observable(a), count: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    let counter_ref = make_ref(count)

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            let remaining = get_ref(counter_ref)
            case remaining > 0 {
              True -> set_ref(counter_ref, remaining - 1)
              False -> downstream(OnNext(x))
            }
          }
          _ -> downstream(n)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Takes elements while predicate returns True.
pub fn take_while(
  source: Observable(a),
  predicate: fn(a) -> Bool,
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    let stopped_ref = make_ref(0)

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case get_ref(stopped_ref) {
              0 ->
                case predicate(x) {
                  True -> downstream(OnNext(x))
                  False -> {
                    set_ref(stopped_ref, 1)
                    downstream(OnCompleted)
                  }
                }
              _ -> Nil
            }
          OnError(e) -> downstream(OnError(e))
          OnCompleted ->
            case get_ref(stopped_ref) {
              0 -> downstream(OnCompleted)
              _ -> Nil
            }
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Skips elements while predicate returns True.
pub fn skip_while(
  source: Observable(a),
  predicate: fn(a) -> Bool,
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    let emitting_ref = make_ref(0)

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case get_ref(emitting_ref) {
              1 -> downstream(OnNext(x))
              _ ->
                case predicate(x) {
                  True -> Nil
                  False -> {
                    set_ref(emitting_ref, 1)
                    downstream(OnNext(x))
                  }
                }
            }
          _ -> downstream(n)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Emits elements that are different from the previous element.
pub fn distinct_until_changed(source: Observable(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    let latest_ref = make_option_ref()

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case get_option_ref(latest_ref) {
              None -> {
                downstream(OnNext(x))
                set_option_ref(latest_ref, Some(x))
              }
              Some(prev) ->
                case prev == x {
                  True -> Nil
                  False -> {
                    downstream(OnNext(x))
                    set_option_ref(latest_ref, Some(x))
                  }
                }
            }
          _ -> downstream(n)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Returns elements until the other observable emits.
pub fn take_until(source: Observable(a), other: Observable(b)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    let stopped_ref = make_ref(0)

    // Subscribe to 'other'
    let Observable(other_subscribe) = other
    let other_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(_) ->
            case get_ref(stopped_ref) {
              0 -> {
                set_ref(stopped_ref, 1)
                downstream(OnCompleted)
              }
              _ -> Nil
            }
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> Nil
        }
      })
    let other_disp = other_subscribe(other_observer)

    // Subscribe to source
    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case get_ref(stopped_ref) {
              0 -> downstream(OnNext(x))
              _ -> Nil
            }
          OnError(e) -> downstream(OnError(e))
          OnCompleted ->
            case get_ref(stopped_ref) {
              0 -> {
                set_ref(stopped_ref, 1)
                downstream(OnCompleted)
              }
              _ -> Nil
            }
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(upstream_observer)

    composite_disposable([source_disp, other_disp])
  })
}

/// Returns the last N elements from the source.
pub fn take_last(source: Observable(a), count: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    let buffer_ref = make_list_ref()

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            let buffer = get_list_ref(buffer_ref)
            let new_buffer = append_and_limit(buffer, x, count)
            set_list_ref(buffer_ref, new_buffer)
          }
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> {
            let buffer = get_list_ref(buffer_ref)
            emit_all(buffer, fn(x) { downstream(OnNext(x)) })
            downstream(OnCompleted)
          }
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

// Mutable reference helpers using Erlang process dictionary
// These are used for state management in synchronous observables

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

fn make_option_ref() -> a {
  let ref = erlang_make_ref()
  erlang_put(ref, None)
  ref
}

fn get_option_ref(ref: a) -> Option(b) {
  erlang_get(ref)
}

fn set_option_ref(ref: a, value: Option(b)) -> Nil {
  erlang_put(ref, value)
  Nil
}

fn make_list_ref() -> a {
  let ref = erlang_make_ref()
  erlang_put(ref, [])
  ref
}

fn get_list_ref(ref: a) -> List(b) {
  erlang_get(ref)
}

fn set_list_ref(ref: a, value: List(b)) -> Nil {
  erlang_put(ref, value)
  Nil
}

fn append_and_limit(list: List(a), item: a, max: Int) -> List(a) {
  let new_list = append_to_end(list, item)
  case length(new_list) > max {
    True -> drop_first(new_list)
    False -> new_list
  }
}

fn append_to_end(list: List(a), item: a) -> List(a) {
  case list {
    [] -> [item]
    [head, ..tail] -> [head, ..append_to_end(tail, item)]
  }
}

fn length(list: List(a)) -> Int {
  case list {
    [] -> 0
    [_, ..tail] -> 1 + length(tail)
  }
}

fn drop_first(list: List(a)) -> List(a) {
  case list {
    [] -> []
    [_, ..tail] -> tail
  }
}

fn emit_all(list: List(a), emit: fn(a) -> Nil) -> Nil {
  case list {
    [] -> Nil
    [head, ..tail] -> {
      emit(head)
      emit_all(tail, emit)
    }
  }
}
