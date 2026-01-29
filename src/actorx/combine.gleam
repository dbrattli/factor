//// Combining operators for ActorX
////
//// These operators combine multiple observable sequences:
//// - merge: Merge multiple observables into one
//// - combine_latest: Combine latest values from multiple observables
//// - with_latest_from: Sample source with latest from another
//// - zip: Pair elements by index

import actorx/types.{
  type Observable, type Observer, Disposable, Observable, Observer, OnCompleted,
  OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleam/option.{type Option, None, Some}

// ============================================================================
// merge - Merge multiple observables
// ============================================================================

type MergeMsg(a) {
  MergeNext(a)
  MergeError(String)
  MergeCompleted
  MergeDispose
}

/// Merges multiple observable sequences into a single observable sequence.
/// Values are emitted as they arrive from any source.
/// Completes when all sources complete.
///
/// ## Example
/// ```gleam
/// let obs1 = interval(100) |> take(3)
/// let obs2 = interval(150) |> take(2)
/// merge([obs1, obs2])
/// // Emits values from both as they arrive
/// ```
pub fn merge(sources: List(Observable(a))) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    case sources {
      [] -> {
        // Empty list - complete immediately
        downstream(OnCompleted)
        Disposable(dispose: fn() { Nil })
      }
      _ -> {
        // Create control channel
        let control_ready: Subject(Subject(MergeMsg(a))) = process.new_subject()

        // Spawn coordinator actor
        process.spawn(fn() {
          let control: Subject(MergeMsg(a)) = process.new_subject()
          process.send(control_ready, control)
          merge_loop(control, downstream, list.length(sources))
        })

        // Get control subject
        let control = case process.receive(control_ready, 1000) {
          Ok(s) -> s
          Error(_) -> panic as "Failed to create merge"
        }

        // Subscribe to all sources
        let disposables =
          list.map(sources, fn(source) {
            let source_observer =
              Observer(notify: fn(n) {
                case n {
                  OnNext(x) -> process.send(control, MergeNext(x))
                  OnError(e) -> process.send(control, MergeError(e))
                  OnCompleted -> process.send(control, MergeCompleted)
                }
              })
            let Observable(subscribe) = source
            subscribe(source_observer)
          })

        // Return disposable that cleans up everything
        Disposable(dispose: fn() {
          list.each(disposables, fn(d) {
            let Disposable(dispose) = d
            dispose()
          })
          process.send(control, MergeDispose)
          Nil
        })
      }
    }
  })
}

fn merge_loop(
  control: Subject(MergeMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  remaining: Int,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    MergeNext(value) -> {
      downstream(OnNext(value))
      merge_loop(control, downstream, remaining)
    }
    MergeCompleted -> {
      case remaining <= 1 {
        True -> {
          downstream(OnCompleted)
          Nil
        }
        False -> merge_loop(control, downstream, remaining - 1)
      }
    }
    MergeError(e) -> {
      downstream(OnError(e))
      Nil
    }
    MergeDispose -> Nil
  }
}

/// Merge two observables.
pub fn merge2(
  source1: Observable(a),
  source2: Observable(a),
) -> Observable(a) {
  merge([source1, source2])
}

// ============================================================================
// combine_latest - Combine latest values
// ============================================================================

type CombineLatestMsg(a, b) {
  CombineLeft(a)
  CombineRight(b)
  CombineLeftCompleted
  CombineRightCompleted
  CombineLeftError(String)
  CombineRightError(String)
  CombineLatestDispose
}

/// Combines the latest values from two observable sequences using a combiner function.
/// Emits whenever either source emits, after both have emitted at least once.
/// Completes when both sources complete.
///
/// ## Example
/// ```gleam
/// let obs1 = interval(100)
/// let obs2 = interval(150)
/// combine_latest(obs1, obs2, fn(a, b) { #(a, b) })
/// ```
pub fn combine_latest(
  source1: Observable(a),
  source2: Observable(b),
  combiner: fn(a, b) -> c,
) -> Observable(c) {
  Observable(subscribe: fn(observer: Observer(c)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(CombineLatestMsg(a, b))) =
      process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(CombineLatestMsg(a, b)) = process.new_subject()
      process.send(control_ready, control)
      combine_latest_loop(control, downstream, combiner, None, None, False, False)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create combine_latest"
    }

    // Subscribe to source1
    let obs1 =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, CombineLeft(x))
          OnError(e) -> process.send(control, CombineLeftError(e))
          OnCompleted -> process.send(control, CombineLeftCompleted)
        }
      })
    let Observable(subscribe1) = source1
    let disp1 = subscribe1(obs1)

    // Subscribe to source2
    let obs2 =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, CombineRight(x))
          OnError(e) -> process.send(control, CombineRightError(e))
          OnCompleted -> process.send(control, CombineRightCompleted)
        }
      })
    let Observable(subscribe2) = source2
    let disp2 = subscribe2(obs2)

    Disposable(dispose: fn() {
      let Disposable(d1) = disp1
      let Disposable(d2) = disp2
      d1()
      d2()
      process.send(control, CombineLatestDispose)
      Nil
    })
  })
}

fn combine_latest_loop(
  control: Subject(CombineLatestMsg(a, b)),
  downstream: fn(types.Notification(c)) -> Nil,
  combiner: fn(a, b) -> c,
  left: Option(a),
  right: Option(b),
  left_done: Bool,
  right_done: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    CombineLeft(a) -> {
      let new_left = Some(a)
      case right {
        Some(b) -> downstream(OnNext(combiner(a, b)))
        None -> Nil
      }
      combine_latest_loop(
        control,
        downstream,
        combiner,
        new_left,
        right,
        left_done,
        right_done,
      )
    }
    CombineRight(b) -> {
      let new_right = Some(b)
      case left {
        Some(a) -> downstream(OnNext(combiner(a, b)))
        None -> Nil
      }
      combine_latest_loop(
        control,
        downstream,
        combiner,
        left,
        new_right,
        left_done,
        right_done,
      )
    }
    CombineLeftCompleted -> {
      case right_done {
        True -> {
          downstream(OnCompleted)
          Nil
        }
        False ->
          combine_latest_loop(
            control,
            downstream,
            combiner,
            left,
            right,
            True,
            right_done,
          )
      }
    }
    CombineRightCompleted -> {
      case left_done {
        True -> {
          downstream(OnCompleted)
          Nil
        }
        False ->
          combine_latest_loop(
            control,
            downstream,
            combiner,
            left,
            right,
            left_done,
            True,
          )
      }
    }
    CombineLeftError(e) | CombineRightError(e) -> {
      downstream(OnError(e))
      Nil
    }
    CombineLatestDispose -> Nil
  }
}

// ============================================================================
// with_latest_from - Sample with latest
// ============================================================================

type WithLatestMsg(a, b) {
  WithLatestSource(a)
  WithLatestSample(b)
  WithLatestSourceCompleted
  WithLatestSampleCompleted
  WithLatestSourceError(String)
  WithLatestSampleError(String)
  WithLatestDispose
}

/// Combines the source observable with the latest value from another observable.
/// Emits only when the source emits, pairing with the latest value from sampler.
/// Values from sampler before the first source emission are ignored.
/// Completes when source completes.
///
/// ## Example
/// ```gleam
/// let clicks = from_clicks()
/// let mouse_pos = from_mouse_moves()
/// with_latest_from(clicks, mouse_pos, fn(click, pos) { #(click, pos) })
/// // Emits position at each click
/// ```
pub fn with_latest_from(
  source: Observable(a),
  sampler: Observable(b),
  combiner: fn(a, b) -> c,
) -> Observable(c) {
  Observable(subscribe: fn(observer: Observer(c)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(WithLatestMsg(a, b))) =
      process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(WithLatestMsg(a, b)) = process.new_subject()
      process.send(control_ready, control)
      with_latest_loop(control, downstream, combiner, None)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create with_latest_from"
    }

    // Subscribe to source
    let source_obs =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, WithLatestSource(x))
          OnError(e) -> process.send(control, WithLatestSourceError(e))
          OnCompleted -> process.send(control, WithLatestSourceCompleted)
        }
      })
    let Observable(subscribe_source) = source
    let disp_source = subscribe_source(source_obs)

    // Subscribe to sampler
    let sampler_obs =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, WithLatestSample(x))
          OnError(e) -> process.send(control, WithLatestSampleError(e))
          OnCompleted -> process.send(control, WithLatestSampleCompleted)
        }
      })
    let Observable(subscribe_sampler) = sampler
    let disp_sampler = subscribe_sampler(sampler_obs)

    Disposable(dispose: fn() {
      let Disposable(d1) = disp_source
      let Disposable(d2) = disp_sampler
      d1()
      d2()
      process.send(control, WithLatestDispose)
      Nil
    })
  })
}

fn with_latest_loop(
  control: Subject(WithLatestMsg(a, b)),
  downstream: fn(types.Notification(c)) -> Nil,
  combiner: fn(a, b) -> c,
  latest: Option(b),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    WithLatestSource(a) -> {
      case latest {
        Some(b) -> downstream(OnNext(combiner(a, b)))
        None -> Nil
      }
      with_latest_loop(control, downstream, combiner, latest)
    }
    WithLatestSample(b) -> {
      with_latest_loop(control, downstream, combiner, Some(b))
    }
    WithLatestSourceCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    WithLatestSampleCompleted -> {
      // Sampler completing doesn't complete the stream
      with_latest_loop(control, downstream, combiner, latest)
    }
    WithLatestSourceError(e) | WithLatestSampleError(e) -> {
      downstream(OnError(e))
      Nil
    }
    WithLatestDispose -> Nil
  }
}

// ============================================================================
// zip - Pair elements by index
// ============================================================================

type ZipMsg(a, b) {
  ZipLeft(a)
  ZipRight(b)
  ZipLeftCompleted
  ZipRightCompleted
  ZipLeftError(String)
  ZipRightError(String)
  ZipDispose
}

/// Combines two observable sequences by pairing their elements by index.
/// Emits when both sources have emitted a value at the same index.
/// Completes when either source completes.
///
/// ## Example
/// ```gleam
/// let obs1 = from_list([1, 2, 3])
/// let obs2 = from_list(["a", "b", "c"])
/// zip(obs1, obs2, fn(n, s) { #(n, s) })
/// // Emits: #(1, "a"), #(2, "b"), #(3, "c")
/// ```
pub fn zip(
  source1: Observable(a),
  source2: Observable(b),
  combiner: fn(a, b) -> c,
) -> Observable(c) {
  Observable(subscribe: fn(observer: Observer(c)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(ZipMsg(a, b))) = process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(ZipMsg(a, b)) = process.new_subject()
      process.send(control_ready, control)
      zip_loop(control, downstream, combiner, [], [], False, False)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create zip"
    }

    // Subscribe to source1
    let obs1 =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, ZipLeft(x))
          OnError(e) -> process.send(control, ZipLeftError(e))
          OnCompleted -> process.send(control, ZipLeftCompleted)
        }
      })
    let Observable(subscribe1) = source1
    let disp1 = subscribe1(obs1)

    // Subscribe to source2
    let obs2 =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, ZipRight(x))
          OnError(e) -> process.send(control, ZipRightError(e))
          OnCompleted -> process.send(control, ZipRightCompleted)
        }
      })
    let Observable(subscribe2) = source2
    let disp2 = subscribe2(obs2)

    Disposable(dispose: fn() {
      let Disposable(d1) = disp1
      let Disposable(d2) = disp2
      d1()
      d2()
      process.send(control, ZipDispose)
      Nil
    })
  })
}

fn zip_loop(
  control: Subject(ZipMsg(a, b)),
  downstream: fn(types.Notification(c)) -> Nil,
  combiner: fn(a, b) -> c,
  left_queue: List(a),
  right_queue: List(b),
  left_done: Bool,
  right_done: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ZipLeft(a) -> {
      case right_queue {
        [b, ..rest] -> {
          // Have a matching right value
          downstream(OnNext(combiner(a, b)))
          case right_done && list.is_empty(rest) {
            True -> {
              downstream(OnCompleted)
              Nil
            }
            False ->
              zip_loop(
                control,
                downstream,
                combiner,
                left_queue,
                rest,
                left_done,
                right_done,
              )
          }
        }
        [] -> {
          // Queue the left value
          zip_loop(
            control,
            downstream,
            combiner,
            list.append(left_queue, [a]),
            right_queue,
            left_done,
            right_done,
          )
        }
      }
    }
    ZipRight(b) -> {
      case left_queue {
        [a, ..rest] -> {
          // Have a matching left value
          downstream(OnNext(combiner(a, b)))
          case left_done && list.is_empty(rest) {
            True -> {
              downstream(OnCompleted)
              Nil
            }
            False ->
              zip_loop(
                control,
                downstream,
                combiner,
                rest,
                right_queue,
                left_done,
                right_done,
              )
          }
        }
        [] -> {
          // Queue the right value
          zip_loop(
            control,
            downstream,
            combiner,
            left_queue,
            list.append(right_queue, [b]),
            left_done,
            right_done,
          )
        }
      }
    }
    ZipLeftCompleted -> {
      case list.is_empty(left_queue) {
        True -> {
          // No pending left values, complete
          downstream(OnCompleted)
          Nil
        }
        False -> {
          // Still have pending left values, wait for right
          zip_loop(
            control,
            downstream,
            combiner,
            left_queue,
            right_queue,
            True,
            right_done,
          )
        }
      }
    }
    ZipRightCompleted -> {
      case list.is_empty(right_queue) {
        True -> {
          // No pending right values, complete
          downstream(OnCompleted)
          Nil
        }
        False -> {
          // Still have pending right values, wait for left
          zip_loop(
            control,
            downstream,
            combiner,
            left_queue,
            right_queue,
            left_done,
            True,
          )
        }
      }
    }
    ZipLeftError(e) | ZipRightError(e) -> {
      downstream(OnError(e))
      Nil
    }
    ZipDispose -> Nil
  }
}
