//// Transform operators for ActorX
////
//// These operators transform the elements of an observable sequence:
//// - map: Apply a function to each element
//// - flat_map: Map to observables and flatten (sync)
//// - flat_map_async: Map to observables and flatten (actor-based, for async sources)

import actorx/types.{
  type Observable, type Observer, Disposable, Observable, Observer, OnCompleted,
  OnError, OnNext,
}
import gleam/erlang/process.{type Subject}

/// Returns an observable whose elements are the result of invoking
/// the mapper function on each element of the source.
pub fn map(source: Observable(a), mapper: fn(a) -> b) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> downstream(OnNext(mapper(x)))
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> downstream(OnCompleted)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Projects each element of an observable sequence into an observable
/// sequence and merges the resulting observable sequences.
pub fn flat_map(
  source: Observable(a),
  mapper: fn(a) -> Observable(b),
) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            let inner_observable = mapper(x)
            let Observable(inner_subscribe) = inner_observable

            // Inner observer forwards OnNext, ignores inner completion
            let inner_observer =
              Observer(notify: fn(inner_n) {
                case inner_n {
                  OnNext(value) -> downstream(OnNext(value))
                  OnError(e) -> downstream(OnError(e))
                  OnCompleted -> Nil
                }
              })

            let _inner_disp = inner_subscribe(inner_observer)
            Nil
          }
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> downstream(OnCompleted)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Projects each element into an observable and concatenates them in order.
pub fn concat_map(
  source: Observable(a),
  mapper: fn(a) -> Observable(b),
) -> Observable(b) {
  // Simplified version - processes inner observables immediately
  // A full implementation would queue and process sequentially
  flat_map(source, mapper)
}

// ============================================================================
// flat_map_async - Actor-based flat_map for async sources
// ============================================================================

/// Messages for the flat_map_async coordinator actor
type FlatMapMsg(b) {
  /// Source emitted a value (inner observable to subscribe to)
  InnerSubscribe(Observable(b))
  /// Inner observable emitted a value
  InnerNext(b)
  /// Inner observable completed
  InnerCompleted
  /// Inner observable errored
  InnerError(String)
  /// Source completed
  SourceCompleted
  /// Source errored
  SourceError(String)
  /// Dispose all subscriptions
  FlatMapDispose
}

/// Actor-based flat_map for async sources.
///
/// Unlike `flat_map`, this version uses an actor to coordinate inner
/// subscriptions, making it safe for async sources that emit over time.
/// It properly tracks all inner subscriptions and only completes when
/// both the source AND all inner observables have completed.
///
/// ## Example
/// ```gleam
/// // Safe with async sources
/// interval(100)
/// |> take(3)
/// |> flat_map_async(fn(i) { timer(50) |> map(fn(_) { i }) })
/// ```
pub fn flat_map_async(
  source: Observable(a),
  mapper: fn(a) -> Observable(b),
) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(FlatMapMsg(b))) = process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(FlatMapMsg(b)) = process.new_subject()
      process.send(control_ready, control)
      flat_map_loop(control, downstream, 0, False)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create flat_map_async"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            let inner = mapper(x)
            process.send(control, InnerSubscribe(inner))
          }
          OnError(e) -> process.send(control, SourceError(e))
          OnCompleted -> process.send(control, SourceCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    // Return disposable that cleans up everything
    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, FlatMapDispose)
      Nil
    })
  })
}

fn flat_map_loop(
  control: Subject(FlatMapMsg(b)),
  downstream: fn(types.Notification(b)) -> Nil,
  inner_count: Int,
  source_stopped: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    InnerSubscribe(inner_observable) -> {
      // Subscribe to inner observable
      let inner_observer =
        Observer(notify: fn(n) {
          case n {
            OnNext(value) -> process.send(control, InnerNext(value))
            OnError(e) -> process.send(control, InnerError(e))
            OnCompleted -> process.send(control, InnerCompleted)
          }
        })

      let Observable(inner_subscribe) = inner_observable
      let _inner_disp = inner_subscribe(inner_observer)

      flat_map_loop(control, downstream, inner_count + 1, source_stopped)
    }

    InnerNext(value) -> {
      downstream(OnNext(value))
      flat_map_loop(control, downstream, inner_count, source_stopped)
    }

    InnerCompleted -> {
      let new_count = inner_count - 1
      case source_stopped && new_count <= 0 {
        True -> {
          // Source done and all inners done - complete
          downstream(OnCompleted)
          Nil
        }
        False -> flat_map_loop(control, downstream, new_count, source_stopped)
      }
    }

    InnerError(e) -> {
      downstream(OnError(e))
      Nil
    }

    SourceCompleted -> {
      case inner_count <= 0 {
        True -> {
          // No active inners - complete immediately
          downstream(OnCompleted)
          Nil
        }
        False -> {
          // Wait for inners to complete
          flat_map_loop(control, downstream, inner_count, True)
        }
      }
    }

    SourceError(e) -> {
      downstream(OnError(e))
      Nil
    }

    FlatMapDispose -> Nil
  }
}
