//// Filter operators for ActorX
////
//// These operators filter elements from an observable sequence.
//// All stateful operators use actors for proper state management
//// across async boundaries.

import actorx/types.{
  type Observable, type Observer, Disposable, Observable, Observer, OnCompleted,
  OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleam/option.{type Option, None, Some}

// ============================================================================
// Stateless operators (no actor needed)
// ============================================================================

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

// ============================================================================
// take - Actor-based
// ============================================================================

type TakeMsg(a) {
  TakeNext(a)
  TakeError(String)
  TakeCompleted
  TakeDispose
}

/// Returns the first N elements from the source.
pub fn take(source: Observable(a), count: Int) -> Observable(a) {
  case count <= 0 {
    True ->
      Observable(subscribe: fn(observer: Observer(a)) {
        let Observer(downstream) = observer
        downstream(OnCompleted)
        Disposable(dispose: fn() { Nil })
      })
    False ->
      Observable(subscribe: fn(observer: Observer(a)) {
        let Observer(downstream) = observer

        let control_ready: Subject(Subject(TakeMsg(a))) = process.new_subject()

        process.spawn(fn() {
          let control: Subject(TakeMsg(a)) = process.new_subject()
          process.send(control_ready, control)
          take_loop(control, downstream, count)
        })

        let control = case process.receive(control_ready, 1000) {
          Ok(s) -> s
          Error(_) -> panic as "Failed to create take"
        }

        let upstream_observer =
          Observer(notify: fn(n) {
            case n {
              OnNext(x) -> process.send(control, TakeNext(x))
              OnError(e) -> process.send(control, TakeError(e))
              OnCompleted -> process.send(control, TakeCompleted)
            }
          })

        let Observable(subscribe) = source
        let source_disp = subscribe(upstream_observer)

        Disposable(dispose: fn() {
          let Disposable(d) = source_disp
          d()
          process.send(control, TakeDispose)
          Nil
        })
      })
  }
}

fn take_loop(
  control: Subject(TakeMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  remaining: Int,
) -> Nil {
  case remaining <= 0 {
    True -> Nil
    False -> {
      let selector =
        process.new_selector()
        |> process.select(control)

      case process.selector_receive_forever(selector) {
        TakeNext(x) -> {
          downstream(OnNext(x))
          case remaining - 1 {
            0 -> {
              downstream(OnCompleted)
              Nil
            }
            n -> take_loop(control, downstream, n)
          }
        }
        TakeError(e) -> {
          downstream(OnError(e))
          Nil
        }
        TakeCompleted -> {
          downstream(OnCompleted)
          Nil
        }
        TakeDispose -> Nil
      }
    }
  }
}

// ============================================================================
// skip - Actor-based
// ============================================================================

type SkipMsg(a) {
  SkipNext(a)
  SkipError(String)
  SkipCompleted
  SkipDispose
}

/// Skips the first N elements from the source.
pub fn skip(source: Observable(a), count: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let control_ready: Subject(Subject(SkipMsg(a))) = process.new_subject()

    process.spawn(fn() {
      let control: Subject(SkipMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      skip_loop(control, downstream, count)
    })

    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create skip"
    }

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, SkipNext(x))
          OnError(e) -> process.send(control, SkipError(e))
          OnCompleted -> process.send(control, SkipCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(upstream_observer)

    Disposable(dispose: fn() {
      let Disposable(d) = source_disp
      d()
      process.send(control, SkipDispose)
      Nil
    })
  })
}

fn skip_loop(
  control: Subject(SkipMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  remaining: Int,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    SkipNext(x) -> {
      case remaining > 0 {
        True -> skip_loop(control, downstream, remaining - 1)
        False -> {
          downstream(OnNext(x))
          skip_loop(control, downstream, 0)
        }
      }
    }
    SkipError(e) -> {
      downstream(OnError(e))
      Nil
    }
    SkipCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    SkipDispose -> Nil
  }
}

// ============================================================================
// take_while - Actor-based
// ============================================================================

type TakeWhileMsg(a) {
  TakeWhileNext(a)
  TakeWhileError(String)
  TakeWhileCompleted
  TakeWhileDispose
}

/// Takes elements while predicate returns True.
pub fn take_while(
  source: Observable(a),
  predicate: fn(a) -> Bool,
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let control_ready: Subject(Subject(TakeWhileMsg(a))) = process.new_subject()

    process.spawn(fn() {
      let control: Subject(TakeWhileMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      take_while_loop(control, downstream, predicate)
    })

    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create take_while"
    }

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, TakeWhileNext(x))
          OnError(e) -> process.send(control, TakeWhileError(e))
          OnCompleted -> process.send(control, TakeWhileCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(upstream_observer)

    Disposable(dispose: fn() {
      let Disposable(d) = source_disp
      d()
      process.send(control, TakeWhileDispose)
      Nil
    })
  })
}

fn take_while_loop(
  control: Subject(TakeWhileMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  predicate: fn(a) -> Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    TakeWhileNext(x) -> {
      case predicate(x) {
        True -> {
          downstream(OnNext(x))
          take_while_loop(control, downstream, predicate)
        }
        False -> {
          downstream(OnCompleted)
          Nil
        }
      }
    }
    TakeWhileError(e) -> {
      downstream(OnError(e))
      Nil
    }
    TakeWhileCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    TakeWhileDispose -> Nil
  }
}

// ============================================================================
// skip_while - Actor-based
// ============================================================================

type SkipWhileMsg(a) {
  SkipWhileNext(a)
  SkipWhileError(String)
  SkipWhileCompleted
  SkipWhileDispose
}

/// Skips elements while predicate returns True.
pub fn skip_while(
  source: Observable(a),
  predicate: fn(a) -> Bool,
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let control_ready: Subject(Subject(SkipWhileMsg(a))) = process.new_subject()

    process.spawn(fn() {
      let control: Subject(SkipWhileMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      skip_while_loop(control, downstream, predicate, False)
    })

    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create skip_while"
    }

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, SkipWhileNext(x))
          OnError(e) -> process.send(control, SkipWhileError(e))
          OnCompleted -> process.send(control, SkipWhileCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(upstream_observer)

    Disposable(dispose: fn() {
      let Disposable(d) = source_disp
      d()
      process.send(control, SkipWhileDispose)
      Nil
    })
  })
}

fn skip_while_loop(
  control: Subject(SkipWhileMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  predicate: fn(a) -> Bool,
  emitting: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    SkipWhileNext(x) -> {
      case emitting {
        True -> {
          downstream(OnNext(x))
          skip_while_loop(control, downstream, predicate, True)
        }
        False -> {
          case predicate(x) {
            True -> skip_while_loop(control, downstream, predicate, False)
            False -> {
              downstream(OnNext(x))
              skip_while_loop(control, downstream, predicate, True)
            }
          }
        }
      }
    }
    SkipWhileError(e) -> {
      downstream(OnError(e))
      Nil
    }
    SkipWhileCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    SkipWhileDispose -> Nil
  }
}

// ============================================================================
// distinct_until_changed - Actor-based
// ============================================================================

type DistinctMsg(a) {
  DistinctNext(a)
  DistinctError(String)
  DistinctCompleted
  DistinctDispose
}

/// Emits elements that are different from the previous element.
pub fn distinct_until_changed(source: Observable(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let control_ready: Subject(Subject(DistinctMsg(a))) = process.new_subject()

    process.spawn(fn() {
      let control: Subject(DistinctMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      distinct_loop(control, downstream, None)
    })

    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create distinct_until_changed"
    }

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, DistinctNext(x))
          OnError(e) -> process.send(control, DistinctError(e))
          OnCompleted -> process.send(control, DistinctCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(upstream_observer)

    Disposable(dispose: fn() {
      let Disposable(d) = source_disp
      d()
      process.send(control, DistinctDispose)
      Nil
    })
  })
}

fn distinct_loop(
  control: Subject(DistinctMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  last: Option(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    DistinctNext(x) -> {
      case last {
        None -> {
          downstream(OnNext(x))
          distinct_loop(control, downstream, Some(x))
        }
        Some(prev) -> {
          case prev == x {
            True -> distinct_loop(control, downstream, last)
            False -> {
              downstream(OnNext(x))
              distinct_loop(control, downstream, Some(x))
            }
          }
        }
      }
    }
    DistinctError(e) -> {
      downstream(OnError(e))
      Nil
    }
    DistinctCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    DistinctDispose -> Nil
  }
}

// ============================================================================
// take_until - Actor-based
// ============================================================================

type TakeUntilMsg(a) {
  TakeUntilSourceNext(a)
  TakeUntilSourceError(String)
  TakeUntilSourceCompleted
  TakeUntilOtherEmit
  TakeUntilOtherError(String)
  TakeUntilDispose
}

/// Returns elements until the other observable emits.
pub fn take_until(source: Observable(a), other: Observable(b)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let control_ready: Subject(Subject(TakeUntilMsg(a))) = process.new_subject()

    process.spawn(fn() {
      let control: Subject(TakeUntilMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      take_until_loop(control, downstream)
    })

    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create take_until"
    }

    // Subscribe to other
    let other_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(_) -> process.send(control, TakeUntilOtherEmit)
          OnError(e) -> process.send(control, TakeUntilOtherError(e))
          OnCompleted -> Nil
        }
      })
    let Observable(other_subscribe) = other
    let other_disp = other_subscribe(other_observer)

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, TakeUntilSourceNext(x))
          OnError(e) -> process.send(control, TakeUntilSourceError(e))
          OnCompleted -> process.send(control, TakeUntilSourceCompleted)
        }
      })
    let Observable(source_subscribe) = source
    let source_disp = source_subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(d1) = source_disp
      let Disposable(d2) = other_disp
      d1()
      d2()
      process.send(control, TakeUntilDispose)
      Nil
    })
  })
}

fn take_until_loop(
  control: Subject(TakeUntilMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    TakeUntilSourceNext(x) -> {
      downstream(OnNext(x))
      take_until_loop(control, downstream)
    }
    TakeUntilSourceError(e) -> {
      downstream(OnError(e))
      Nil
    }
    TakeUntilSourceCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    TakeUntilOtherEmit -> {
      downstream(OnCompleted)
      Nil
    }
    TakeUntilOtherError(e) -> {
      downstream(OnError(e))
      Nil
    }
    TakeUntilDispose -> Nil
  }
}

// ============================================================================
// take_last - Actor-based
// ============================================================================

type TakeLastMsg(a) {
  TakeLastNext(a)
  TakeLastError(String)
  TakeLastCompleted
  TakeLastDispose
}

/// Returns the last N elements from the source.
pub fn take_last(source: Observable(a), count: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let control_ready: Subject(Subject(TakeLastMsg(a))) = process.new_subject()

    process.spawn(fn() {
      let control: Subject(TakeLastMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      take_last_loop(control, downstream, [], count)
    })

    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create take_last"
    }

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, TakeLastNext(x))
          OnError(e) -> process.send(control, TakeLastError(e))
          OnCompleted -> process.send(control, TakeLastCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(upstream_observer)

    Disposable(dispose: fn() {
      let Disposable(d) = source_disp
      d()
      process.send(control, TakeLastDispose)
      Nil
    })
  })
}

fn take_last_loop(
  control: Subject(TakeLastMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  buffer: List(a),
  max_count: Int,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    TakeLastNext(x) -> {
      let new_buffer = append_and_limit(buffer, x, max_count)
      take_last_loop(control, downstream, new_buffer, max_count)
    }
    TakeLastError(e) -> {
      downstream(OnError(e))
      Nil
    }
    TakeLastCompleted -> {
      emit_all(buffer, fn(x) { downstream(OnNext(x)) })
      downstream(OnCompleted)
      Nil
    }
    TakeLastDispose -> Nil
  }
}

// ============================================================================
// first - Take only the first element
// ============================================================================

/// Messages for the first actor
type FirstMsg(a) {
  FirstNext(a)
  FirstError(String)
  FirstCompleted
  FirstDispose
}

/// Takes only the first element from the source.
///
/// Errors if the source completes without emitting.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3])
/// |> first()
/// // Emits: 1, then completes
/// ```
pub fn first(source: Observable(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(FirstMsg(a))) = process.new_subject()

    // Spawn actor
    process.spawn(fn() {
      let control: Subject(FirstMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      first_loop(control, downstream, False)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create first actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, FirstNext(x))
          OnError(e) -> process.send(control, FirstError(e))
          OnCompleted -> process.send(control, FirstCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, FirstDispose)
      Nil
    })
  })
}

fn first_loop(
  control: Subject(FirstMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  got_value: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    FirstNext(x) -> {
      case got_value {
        True -> first_loop(control, downstream, True)
        False -> {
          downstream(OnNext(x))
          downstream(OnCompleted)
          Nil
        }
      }
    }
    FirstError(e) -> {
      downstream(OnError(e))
      Nil
    }
    FirstCompleted -> {
      case got_value {
        True -> Nil
        False -> {
          downstream(OnError("Sequence contains no elements"))
          Nil
        }
      }
    }
    FirstDispose -> Nil
  }
}

// ============================================================================
// last - Take only the last element
// ============================================================================

/// Messages for the last actor
type LastMsg(a) {
  LastNext(a)
  LastError(String)
  LastCompleted
  LastDispose
}

/// Takes only the last element from the source.
///
/// Errors if the source completes without emitting.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3])
/// |> last()
/// // Emits: 3, then completes
/// ```
pub fn last(source: Observable(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(LastMsg(a))) = process.new_subject()

    // Spawn actor
    process.spawn(fn() {
      let control: Subject(LastMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      last_loop(control, downstream, None)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create last actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, LastNext(x))
          OnError(e) -> process.send(control, LastError(e))
          OnCompleted -> process.send(control, LastCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, LastDispose)
      Nil
    })
  })
}

fn last_loop(
  control: Subject(LastMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  latest: Option(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    LastNext(x) -> last_loop(control, downstream, Some(x))
    LastError(e) -> {
      downstream(OnError(e))
      Nil
    }
    LastCompleted -> {
      case latest {
        Some(x) -> {
          downstream(OnNext(x))
          downstream(OnCompleted)
          Nil
        }
        None -> {
          downstream(OnError("Sequence contains no elements"))
          Nil
        }
      }
    }
    LastDispose -> Nil
  }
}

// ============================================================================
// default_if_empty - Emit default value if source is empty
// ============================================================================

/// Messages for the default_if_empty actor
type DefaultIfEmptyMsg(a) {
  DefaultIfEmptyNext(a)
  DefaultIfEmptyError(String)
  DefaultIfEmptyCompleted
  DefaultIfEmptyDispose
}

/// Emits a default value if the source completes without emitting.
///
/// ## Example
/// ```gleam
/// empty()
/// |> default_if_empty(42)
/// // Emits: 42, then completes
/// ```
pub fn default_if_empty(source: Observable(a), default: a) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(DefaultIfEmptyMsg(a))) =
      process.new_subject()

    // Spawn actor
    process.spawn(fn() {
      let control: Subject(DefaultIfEmptyMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      default_if_empty_loop(control, downstream, default, False)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create default_if_empty actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, DefaultIfEmptyNext(x))
          OnError(e) -> process.send(control, DefaultIfEmptyError(e))
          OnCompleted -> process.send(control, DefaultIfEmptyCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, DefaultIfEmptyDispose)
      Nil
    })
  })
}

fn default_if_empty_loop(
  control: Subject(DefaultIfEmptyMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  default: a,
  has_value: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    DefaultIfEmptyNext(x) -> {
      downstream(OnNext(x))
      default_if_empty_loop(control, downstream, default, True)
    }
    DefaultIfEmptyError(e) -> {
      downstream(OnError(e))
      Nil
    }
    DefaultIfEmptyCompleted -> {
      case has_value {
        True -> {
          downstream(OnCompleted)
          Nil
        }
        False -> {
          downstream(OnNext(default))
          downstream(OnCompleted)
          Nil
        }
      }
    }
    DefaultIfEmptyDispose -> Nil
  }
}

// ============================================================================
// sample - Sample source when another observable emits
// ============================================================================

/// Messages for the sample actor
type SampleMsg(a) {
  SampleValue(a)
  SampleTrigger
  SampleSourceError(String)
  SampleSourceCompleted
  SampleSamplerCompleted
  SampleDispose
}

/// Samples the source observable when the sampler observable emits.
///
/// Each time the sampler emits, the most recent value from the source
/// is emitted (if any new value has arrived since last sample).
///
/// ## Example
/// ```gleam
/// // Emit mouse position every 100ms
/// mouse_moves
/// |> sample(interval(100))
/// ```
pub fn sample(source: Observable(a), sampler: Observable(b)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(SampleMsg(a))) = process.new_subject()

    // Spawn actor
    process.spawn(fn() {
      let control: Subject(SampleMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      sample_loop(control, downstream, None, False, False)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create sample actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, SampleValue(x))
          OnError(e) -> process.send(control, SampleSourceError(e))
          OnCompleted -> process.send(control, SampleSourceCompleted)
        }
      })

    let Observable(subscribe_source) = source
    let source_disp = subscribe_source(source_observer)

    // Subscribe to sampler
    let sampler_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(_) -> process.send(control, SampleTrigger)
          OnError(e) -> process.send(control, SampleSourceError(e))
          OnCompleted -> process.send(control, SampleSamplerCompleted)
        }
      })

    let Observable(subscribe_sampler) = sampler
    let sampler_disp = subscribe_sampler(sampler_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      let Disposable(dispose_sampler) = sampler_disp
      dispose_source()
      dispose_sampler()
      process.send(control, SampleDispose)
      Nil
    })
  })
}

fn sample_loop(
  control: Subject(SampleMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  latest: Option(a),
  source_done: Bool,
  sampler_done: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    SampleValue(x) -> {
      sample_loop(control, downstream, Some(x), source_done, sampler_done)
    }
    SampleTrigger -> {
      case latest {
        Some(x) -> {
          downstream(OnNext(x))
          // Clear the value so we don't emit same value twice
          sample_loop(control, downstream, None, source_done, sampler_done)
        }
        None ->
          sample_loop(control, downstream, None, source_done, sampler_done)
      }
    }
    SampleSourceError(e) -> {
      downstream(OnError(e))
      Nil
    }
    SampleSourceCompleted -> {
      case sampler_done {
        True -> {
          downstream(OnCompleted)
          Nil
        }
        False -> sample_loop(control, downstream, latest, True, sampler_done)
      }
    }
    SampleSamplerCompleted -> {
      case source_done {
        True -> {
          downstream(OnCompleted)
          Nil
        }
        False -> sample_loop(control, downstream, latest, source_done, True)
      }
    }
    SampleDispose -> Nil
  }
}

// ============================================================================
// Helper functions
// ============================================================================

fn append_and_limit(buffer: List(a), item: a, max: Int) -> List(a) {
  let new_buffer = list.append(buffer, [item])
  case list.length(new_buffer) > max {
    True -> list.drop(new_buffer, 1)
    False -> new_buffer
  }
}

fn emit_all(items: List(a), emit: fn(a) -> Nil) -> Nil {
  list.each(items, emit)
}
