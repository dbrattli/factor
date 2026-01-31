//// Transform operators for ActorX
////
//// These operators transform the elements of an observable sequence:
//// - map/mapi: Apply a function to each element (with optional index)
//// - flat_map/flat_mapi: Map to observables and flatten (= mapi + merge_inner)
//// - concat_map/concat_mapi: Map to observables and concatenate (= mapi + concat_inner)
//// - merge_inner: Flatten Observable(Observable(a)) by merging
//// - concat_inner: Flatten Observable(Observable(a)) by concatenating
//// - scan: Running accumulation
//// - reduce: Final accumulation on completion
//// - group_by: Group elements into sub-observables by key

import actorx/types.{
  type Disposable, type Notification, type Observable, type Observer, Disposable,
  Observable, Observer, OnCompleted, OnError, OnNext,
}
import gleam/dict.{type Dict}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleam/option.{type Option, None, Some}

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

// ============================================================================
// mapi - Map with index
// ============================================================================

/// Messages for the mapi actor
type MapiMsg(a) {
  MapiNext(a)
  MapiError(String)
  MapiCompleted
  MapiDispose
}

/// Returns an observable whose elements are the result of invoking
/// the mapper function on each element and its index.
///
/// ## Example
/// ```gleam
/// from_list(["a", "b", "c"])
/// |> mapi(fn(x, i) { #(i, x) })
/// // Emits: #(0, "a"), #(1, "b"), #(2, "c")
/// ```
pub fn mapi(source: Observable(a), mapper: fn(a, Int) -> b) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(MapiMsg(a))) = process.new_subject()

    // Spawn actor to track index
    process.spawn(fn() {
      let control: Subject(MapiMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      mapi_loop(control, downstream, mapper, 0)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create mapi actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, MapiNext(x))
          OnError(e) -> process.send(control, MapiError(e))
          OnCompleted -> process.send(control, MapiCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, MapiDispose)
      Nil
    })
  })
}

fn mapi_loop(
  control: Subject(MapiMsg(a)),
  downstream: fn(Notification(b)) -> Nil,
  mapper: fn(a, Int) -> b,
  index: Int,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    MapiNext(x) -> {
      downstream(OnNext(mapper(x, index)))
      mapi_loop(control, downstream, mapper, index + 1)
    }
    MapiError(e) -> {
      downstream(OnError(e))
      Nil
    }
    MapiCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    MapiDispose -> Nil
  }
}

// ============================================================================
// merge_inner - Flatten Observable(Observable(a)) by merging
// ============================================================================

/// Messages for the merge_inner coordinator actor
type MergeInnerMsg(a) {
  /// Outer emitted an inner observable to subscribe to
  MergeInnerSubscribe(Observable(a))
  /// Inner observable emitted a value
  MergeInnerNext(a)
  /// Inner observable completed
  MergeInnerCompleted
  /// Inner observable errored
  MergeInnerError(String)
  /// Outer completed
  MergeOuterCompleted
  /// Outer errored
  MergeOuterError(String)
  /// Dispose all subscriptions
  MergeInnerDispose
}

/// State for merge_inner actor
type MergeInnerState(a) {
  MergeInnerState(
    inner_count: Int,
    outer_stopped: Bool,
    queue: List(Observable(a)),
    max_concurrency: Option(Int),
  )
}

/// Flattens an Observable of Observables by merging inner emissions.
///
/// The `max_concurrency` parameter controls how many inner observables
/// can be subscribed to concurrently:
/// - `None` (default): Unlimited concurrency, subscribe to all immediately
/// - `Some(1)`: Sequential processing (equivalent to concat_inner)
/// - `Some(n)`: At most n inner observables active at once
///
/// Completes only when the outer source AND all inner observables
/// (including queued ones) have completed.
///
/// ## Example
/// ```gleam
/// // Unlimited concurrency (default)
/// source_of_sources
/// |> merge_inner(None)
///
/// // Limit to 3 concurrent inner subscriptions
/// source_of_sources
/// |> merge_inner(Some(3))
/// ```
pub fn merge_inner(
  source: Observable(Observable(a)),
  max_concurrency: Option(Int),
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(MergeInnerMsg(a))) =
      process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(MergeInnerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      let initial_state =
        MergeInnerState(
          inner_count: 0,
          outer_stopped: False,
          queue: [],
          max_concurrency: max_concurrency,
        )
      merge_inner_loop(control, downstream, initial_state)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create merge_inner"
    }

    // Subscribe to outer source
    let outer_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(inner) -> process.send(control, MergeInnerSubscribe(inner))
          OnError(e) -> process.send(control, MergeOuterError(e))
          OnCompleted -> process.send(control, MergeOuterCompleted)
        }
      })

    let Observable(subscribe) = source
    let outer_disp = subscribe(outer_observer)

    // Return disposable that cleans up everything
    Disposable(dispose: fn() {
      let Disposable(dispose_outer) = outer_disp
      dispose_outer()
      process.send(control, MergeInnerDispose)
      Nil
    })
  })
}

/// Check if we can subscribe to more inner observables
fn can_subscribe(state: MergeInnerState(a)) -> Bool {
  case state.max_concurrency {
    None -> True
    Some(max) -> state.inner_count < max
  }
}

/// Subscribe to an inner observable
fn subscribe_to_merge_inner(
  control: Subject(MergeInnerMsg(a)),
  inner: Observable(a),
) -> Nil {
  let inner_observer =
    Observer(notify: fn(n) {
      case n {
        OnNext(value) -> process.send(control, MergeInnerNext(value))
        OnError(e) -> process.send(control, MergeInnerError(e))
        OnCompleted -> process.send(control, MergeInnerCompleted)
      }
    })

  let Observable(inner_subscribe) = inner
  let _inner_disp = inner_subscribe(inner_observer)
  Nil
}

fn merge_inner_loop(
  control: Subject(MergeInnerMsg(a)),
  downstream: fn(Notification(a)) -> Nil,
  state: MergeInnerState(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    MergeInnerSubscribe(inner_observable) -> {
      case can_subscribe(state) {
        True -> {
          // Subscribe immediately
          subscribe_to_merge_inner(control, inner_observable)
          merge_inner_loop(
            control,
            downstream,
            MergeInnerState(..state, inner_count: state.inner_count + 1),
          )
        }
        False -> {
          // Queue for later (append to end to preserve order)
          let new_queue = list.append(state.queue, [inner_observable])
          merge_inner_loop(
            control,
            downstream,
            MergeInnerState(..state, queue: new_queue),
          )
        }
      }
    }

    MergeInnerNext(value) -> {
      downstream(OnNext(value))
      merge_inner_loop(control, downstream, state)
    }

    MergeInnerCompleted -> {
      let new_count = state.inner_count - 1
      // Try to subscribe to next queued observable
      case state.queue {
        [next_inner, ..remaining] -> {
          subscribe_to_merge_inner(control, next_inner)
          // inner_count stays the same (one finished, one started)
          merge_inner_loop(
            control,
            downstream,
            MergeInnerState(..state, inner_count: new_count + 1, queue: remaining),
          )
        }
        [] -> {
          // No queued observables
          case state.outer_stopped && new_count <= 0 {
            True -> {
              // Outer done, no active inners, no queued - complete
              downstream(OnCompleted)
              Nil
            }
            False -> {
              merge_inner_loop(
                control,
                downstream,
                MergeInnerState(..state, inner_count: new_count),
              )
            }
          }
        }
      }
    }

    MergeInnerError(e) -> {
      downstream(OnError(e))
      Nil
    }

    MergeOuterCompleted -> {
      case state.inner_count <= 0 && list.is_empty(state.queue) {
        True -> {
          // No active inners and no queued - complete immediately
          downstream(OnCompleted)
          Nil
        }
        False -> {
          // Wait for inners to complete (including queued ones)
          merge_inner_loop(
            control,
            downstream,
            MergeInnerState(..state, outer_stopped: True),
          )
        }
      }
    }

    MergeOuterError(e) -> {
      downstream(OnError(e))
      Nil
    }

    MergeInnerDispose -> Nil
  }
}

// ============================================================================
// concat_inner - Flatten Observable(Observable(a)) by concatenating
// ============================================================================

/// Flattens an Observable of Observables by concatenating in order.
///
/// Subscribes to each inner observable only after the previous one
/// completes. This is equivalent to `merge_inner` with `max_concurrency = 1`.
///
/// ## Example
/// ```gleam
/// // Flatten a stream of streams in order
/// source_of_sources
/// |> concat_inner()
/// ```
pub fn concat_inner(source: Observable(Observable(a))) -> Observable(a) {
  merge_inner(source, Some(1))
}

// ============================================================================
// flat_map - Composed from map + merge_inner
// ============================================================================

/// Projects each element of an observable sequence into an observable
/// sequence and merges the resulting observable sequences.
///
/// This is composed from `map` and `merge_inner`:
/// `flat_map(source, f) = source |> map(f) |> merge_inner(None)`
///
/// ## Example
/// ```gleam
/// interval(100)
/// |> take(3)
/// |> flat_map(fn(i) { timer(50) |> map(fn(_) { i }) })
/// ```
pub fn flat_map(
  source: Observable(a),
  mapper: fn(a) -> Observable(b),
) -> Observable(b) {
  source
  |> map(mapper)
  |> merge_inner(None)
}

// ============================================================================
// concat_map - Composed from map + concat_inner
// ============================================================================

/// Projects each element into an observable and concatenates them in order.
///
/// This is composed from `map` and `concat_inner`:
/// `concat_map(source, f) = source |> map(f) |> concat_inner()`
///
/// Unlike `flat_map`, this preserves the order of inner observables.
/// Each inner observable is fully processed before the next one starts.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3])
/// |> concat_map(fn(x) { from_list([x, x * 10]) })
/// // Emits: 1, 10, 2, 20, 3, 30
/// ```
pub fn concat_map(
  source: Observable(a),
  mapper: fn(a) -> Observable(b),
) -> Observable(b) {
  source
  |> map(mapper)
  |> concat_inner()
}

// ============================================================================
// flat_mapi - Composed from mapi + merge_inner
// ============================================================================

/// Projects each element and its index into an observable and merges results.
///
/// This is composed from `mapi` and `merge_inner`:
/// `flat_mapi(source, f) = source |> mapi(f) |> merge_inner(None)`
///
/// ## Example
/// ```gleam
/// from_list(["a", "b", "c"])
/// |> flat_mapi(fn(x, i) { from_list([#(i, x), #(i, x <> "!")]) })
/// ```
pub fn flat_mapi(
  source: Observable(a),
  mapper: fn(a, Int) -> Observable(b),
) -> Observable(b) {
  source
  |> mapi(mapper)
  |> merge_inner(None)
}

// ============================================================================
// concat_mapi - Composed from mapi + concat_inner
// ============================================================================

/// Projects each element and its index into an observable and concatenates in order.
///
/// This is composed from `mapi` and `concat_inner`:
/// `concat_mapi(source, f) = source |> mapi(f) |> concat_inner()`
///
/// ## Example
/// ```gleam
/// from_list(["a", "b"])
/// |> concat_mapi(fn(x, i) { from_list([#(i, x), #(i, x <> "!")]) })
/// // Emits: #(0, "a"), #(0, "a!"), #(1, "b"), #(1, "b!")
/// ```
pub fn concat_mapi(
  source: Observable(a),
  mapper: fn(a, Int) -> Observable(b),
) -> Observable(b) {
  source
  |> mapi(mapper)
  |> concat_inner()
}

// ============================================================================
// switch_inner - Flatten Observable(Observable(a)) by switching
// ============================================================================

/// Messages for the switch_inner coordinator actor
type SwitchInnerMsg(a) {
  /// Outer emitted a new inner observable (actor assigns ID)
  SwitchInnerNew(Observable(a))
  /// Inner observable emitted a value
  SwitchInnerNext(a, Int)
  /// Inner observable completed
  SwitchInnerCompleted(Int)
  /// Inner observable errored
  SwitchInnerError(String)
  /// Outer completed
  SwitchOuterCompleted
  /// Outer errored
  SwitchOuterError(String)
  /// Dispose all subscriptions
  SwitchInnerDispose
}

/// State for switch_inner actor
type SwitchInnerState {
  SwitchInnerState(
    next_id: Int,
    current_id: Int,
    outer_stopped: Bool,
    has_active: Bool,
  )
}

/// Flattens an Observable of Observables by switching to the latest.
///
/// When a new inner observable arrives, the previous inner is disposed
/// and the new one becomes active. Only emissions from the current
/// inner are forwarded.
///
/// This is useful for scenarios like search-as-you-type where you only
/// care about the latest request.
///
/// ## Example
/// ```gleam
/// // Cancel previous search when new query arrives
/// search_queries
/// |> switch_inner()
/// ```
pub fn switch_inner(source: Observable(Observable(a))) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(SwitchInnerMsg(a))) =
      process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(SwitchInnerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      let initial_state =
        SwitchInnerState(
          next_id: 1,
          current_id: 0,
          outer_stopped: False,
          has_active: False,
        )
      switch_inner_loop(control, downstream, initial_state)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create switch_inner"
    }

    // Subscribe to outer source
    let outer_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(inner) -> process.send(control, SwitchInnerNew(inner))
          OnError(e) -> process.send(control, SwitchOuterError(e))
          OnCompleted -> process.send(control, SwitchOuterCompleted)
        }
      })

    let Observable(subscribe) = source
    let outer_disp = subscribe(outer_observer)

    // Return disposable
    Disposable(dispose: fn() {
      let Disposable(dispose_outer) = outer_disp
      dispose_outer()
      process.send(control, SwitchInnerDispose)
      Nil
    })
  })
}

fn switch_inner_loop(
  control: Subject(SwitchInnerMsg(a)),
  downstream: fn(Notification(a)) -> Nil,
  state: SwitchInnerState,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    SwitchInnerNew(inner_observable) -> {
      // Assign ID to this inner
      let id = state.next_id

      // Subscribe to new inner with this ID
      let inner_observer =
        Observer(notify: fn(n) {
          case n {
            OnNext(value) -> process.send(control, SwitchInnerNext(value, id))
            OnError(e) -> process.send(control, SwitchInnerError(e))
            OnCompleted -> process.send(control, SwitchInnerCompleted(id))
          }
        })

      let Observable(inner_subscribe) = inner_observable
      let _inner_disp = inner_subscribe(inner_observer)

      // Update state - this ID is now current, increment next_id
      switch_inner_loop(
        control,
        downstream,
        SwitchInnerState(
          ..state,
          next_id: id + 1,
          current_id: id,
          has_active: True,
        ),
      )
    }

    SwitchInnerNext(value, id) -> {
      // Only forward if this is from the current inner
      case id == state.current_id {
        True -> downstream(OnNext(value))
        False -> Nil
      }
      switch_inner_loop(control, downstream, state)
    }

    SwitchInnerCompleted(id) -> {
      // Only matters if this is the current inner
      case id == state.current_id {
        True -> {
          case state.outer_stopped {
            True -> {
              // Outer done and current inner done - complete
              downstream(OnCompleted)
              Nil
            }
            False -> {
              // Wait for more inners or outer completion
              switch_inner_loop(
                control,
                downstream,
                SwitchInnerState(..state, has_active: False),
              )
            }
          }
        }
        False -> switch_inner_loop(control, downstream, state)
      }
    }

    SwitchInnerError(e) -> {
      downstream(OnError(e))
      Nil
    }

    SwitchOuterCompleted -> {
      case state.has_active {
        True -> {
          // Wait for current inner to complete
          switch_inner_loop(
            control,
            downstream,
            SwitchInnerState(..state, outer_stopped: True),
          )
        }
        False -> {
          // No active inner - complete immediately
          downstream(OnCompleted)
          Nil
        }
      }
    }

    SwitchOuterError(e) -> {
      downstream(OnError(e))
      Nil
    }

    SwitchInnerDispose -> Nil
  }
}

// ============================================================================
// switch_map - Composed from map + switch_inner
// ============================================================================

/// Projects each element into an observable and switches to the latest.
///
/// This is composed from `map` and `switch_inner`:
/// `switch_map(source, f) = source |> map(f) |> switch_inner()`
///
/// When a new element arrives, the previous inner observable is cancelled
/// and the mapper is called to create a new one.
///
/// ## Example
/// ```gleam
/// // Search as you type - cancel previous request on new keystroke
/// keystrokes
/// |> debounce(300)
/// |> switch_map(fn(query) { search_api(query) })
/// ```
pub fn switch_map(
  source: Observable(a),
  mapper: fn(a) -> Observable(b),
) -> Observable(b) {
  source
  |> map(mapper)
  |> switch_inner()
}

// ============================================================================
// switch_mapi - Composed from mapi + switch_inner
// ============================================================================

/// Projects each element and its index into an observable and switches to latest.
///
/// This is composed from `mapi` and `switch_inner`:
/// `switch_mapi(source, f) = source |> mapi(f) |> switch_inner()`
pub fn switch_mapi(
  source: Observable(a),
  mapper: fn(a, Int) -> Observable(b),
) -> Observable(b) {
  source
  |> mapi(mapper)
  |> switch_inner()
}

// ============================================================================
// tap - Side effects without transforming
// ============================================================================

/// Performs a side effect for each emission without transforming.
///
/// Useful for debugging, logging, or triggering external actions.
///
/// ## Example
/// ```gleam
/// source
/// |> tap(fn(x) { io.println("Got: " <> int.to_string(x)) })
/// |> map(fn(x) { x * 2 })
/// ```
pub fn tap(source: Observable(a), effect: fn(a) -> Nil) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            effect(x)
            downstream(OnNext(x))
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
// start_with - Prepend values
// ============================================================================

/// Prepends values before the source emissions.
///
/// ## Example
/// ```gleam
/// from_list([3, 4, 5])
/// |> start_with([1, 2])
/// // Emits: 1, 2, 3, 4, 5
/// ```
pub fn start_with(source: Observable(a), values: List(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Emit prepended values first
    list.each(values, fn(v) { downstream(OnNext(v)) })

    // Then subscribe to source
    let Observable(subscribe) = source
    subscribe(observer)
  })
}

// ============================================================================
// pairwise - Emit consecutive pairs
// ============================================================================

/// Messages for the pairwise actor
type PairwiseMsg(a) {
  PairwiseNext(a)
  PairwiseError(String)
  PairwiseCompleted
  PairwiseDispose
}

/// Emits consecutive pairs of values.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3, 4])
/// |> pairwise()
/// // Emits: #(1, 2), #(2, 3), #(3, 4)
/// ```
pub fn pairwise(source: Observable(a)) -> Observable(#(a, a)) {
  Observable(subscribe: fn(observer: Observer(#(a, a))) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(PairwiseMsg(a))) = process.new_subject()

    // Spawn actor
    process.spawn(fn() {
      let control: Subject(PairwiseMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      pairwise_loop(control, downstream, None)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create pairwise actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, PairwiseNext(x))
          OnError(e) -> process.send(control, PairwiseError(e))
          OnCompleted -> process.send(control, PairwiseCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, PairwiseDispose)
      Nil
    })
  })
}

fn pairwise_loop(
  control: Subject(PairwiseMsg(a)),
  downstream: fn(Notification(#(a, a))) -> Nil,
  prev: Option(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    PairwiseNext(x) -> {
      case prev {
        Some(p) -> {
          downstream(OnNext(#(p, x)))
          pairwise_loop(control, downstream, Some(x))
        }
        None -> pairwise_loop(control, downstream, Some(x))
      }
    }
    PairwiseError(e) -> {
      downstream(OnError(e))
      Nil
    }
    PairwiseCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    PairwiseDispose -> Nil
  }
}

// ============================================================================
// scan - Running accumulation
// ============================================================================

/// Messages for the scan actor
type ScanMsg(a) {
  ScanNext(a)
  ScanError(String)
  ScanCompleted
  ScanDispose
}

/// Applies an accumulator function over the source, emitting each
/// intermediate result.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3, 4, 5])
/// |> scan(0, fn(acc, x) { acc + x })
/// // Emits: 1, 3, 6, 10, 15
/// ```
pub fn scan(
  source: Observable(a),
  initial: b,
  accumulator: fn(b, a) -> b,
) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(ScanMsg(a))) = process.new_subject()

    // Spawn actor to manage state
    process.spawn(fn() {
      let control: Subject(ScanMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      scan_loop(control, downstream, initial, accumulator)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create scan actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, ScanNext(x))
          OnError(e) -> process.send(control, ScanError(e))
          OnCompleted -> process.send(control, ScanCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, ScanDispose)
      Nil
    })
  })
}

fn scan_loop(
  control: Subject(ScanMsg(a)),
  downstream: fn(types.Notification(b)) -> Nil,
  acc: b,
  accumulator: fn(b, a) -> b,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ScanNext(x) -> {
      let new_acc = accumulator(acc, x)
      downstream(OnNext(new_acc))
      scan_loop(control, downstream, new_acc, accumulator)
    }
    ScanError(e) -> {
      downstream(OnError(e))
      Nil
    }
    ScanCompleted -> {
      downstream(OnCompleted)
      Nil
    }
    ScanDispose -> Nil
  }
}

// ============================================================================
// reduce - Final accumulation on completion
// ============================================================================

/// Messages for the reduce actor
type ReduceMsg(a) {
  ReduceNext(a)
  ReduceError(String)
  ReduceCompleted
  ReduceDispose
}

/// Applies an accumulator function over the source, emitting only
/// the final accumulated value when the source completes.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3, 4, 5])
/// |> reduce(0, fn(acc, x) { acc + x })
/// // Emits: 15, then completes
/// ```
pub fn reduce(
  source: Observable(a),
  initial: b,
  accumulator: fn(b, a) -> b,
) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(ReduceMsg(a))) = process.new_subject()

    // Spawn actor to manage state
    process.spawn(fn() {
      let control: Subject(ReduceMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      reduce_loop(control, downstream, initial, accumulator)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create reduce actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> process.send(control, ReduceNext(x))
          OnError(e) -> process.send(control, ReduceError(e))
          OnCompleted -> process.send(control, ReduceCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, ReduceDispose)
      Nil
    })
  })
}

fn reduce_loop(
  control: Subject(ReduceMsg(a)),
  downstream: fn(types.Notification(b)) -> Nil,
  acc: b,
  accumulator: fn(b, a) -> b,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ReduceNext(x) -> {
      let new_acc = accumulator(acc, x)
      reduce_loop(control, downstream, new_acc, accumulator)
    }
    ReduceError(e) -> {
      downstream(OnError(e))
      Nil
    }
    ReduceCompleted -> {
      // Emit final accumulated value, then complete
      downstream(OnNext(acc))
      downstream(OnCompleted)
      Nil
    }
    ReduceDispose -> Nil
  }
}

// ============================================================================
// group_by - Group elements into sub-observables by key
// ============================================================================

/// Messages for the group_by actor
type GroupByMsg(k, a) {
  /// Element with its computed key
  GroupByNext(k, a)
  GroupByError(String)
  GroupByCompleted
  GroupByDispose
  /// Request to subscribe to a specific group
  GroupSubscribe(k, Observer(a), Subject(Disposable))
  /// Unsubscribe from a group
  GroupUnsubscribe(k, Int)
}

/// A group subscriber
type GroupSubscriber(a) {
  GroupSubscriber(id: Int, observer: Observer(a))
}

/// State for a single group
type GroupState(a) {
  GroupState(subscribers: List(GroupSubscriber(a)), buffer: List(a))
}

/// State for the group_by actor
type GroupByState(k, a) {
  GroupByState(
    groups: Dict(k, GroupState(a)),
    emitted_keys: List(k),
    terminated: Bool,
  )
}

/// Groups elements of an observable by key, returning an observable of
/// grouped observables.
///
/// Each time a new key is encountered, emits a tuple of (key, Observable).
/// All elements with that key are forwarded to the corresponding group's
/// observable.
///
/// ## Example
/// ```gleam
/// // Group numbers by even/odd
/// from_list([1, 2, 3, 4, 5, 6])
/// |> group_by(fn(x) { x % 2 })
/// |> flat_map(fn(group) {
///   let #(key, values) = group
///   values |> map(fn(v) { #(key, v) })
/// })
/// // Emits: #(1, 1), #(0, 2), #(1, 3), #(0, 4), #(1, 5), #(0, 6)
/// ```
pub fn group_by(
  source: Observable(a),
  key_selector: fn(a) -> k,
) -> Observable(#(k, Observable(a))) {
  Observable(subscribe: fn(observer: Observer(#(k, Observable(a)))) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(GroupByMsg(k, a))) =
      process.new_subject()

    // Spawn actor
    process.spawn(fn() {
      let control: Subject(GroupByMsg(k, a)) = process.new_subject()
      process.send(control_ready, control)
      let initial_state =
        GroupByState(groups: dict.new(), emitted_keys: [], terminated: False)
      group_by_loop(control, downstream, initial_state)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create group_by actor"
    }

    // Subscribe to source
    let source_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            let key = key_selector(x)
            process.send(control, GroupByNext(key, x))
          }
          OnError(e) -> process.send(control, GroupByError(e))
          OnCompleted -> process.send(control, GroupByCompleted)
        }
      })

    let Observable(subscribe) = source
    let source_disp = subscribe(source_observer)

    Disposable(dispose: fn() {
      let Disposable(dispose_source) = source_disp
      dispose_source()
      process.send(control, GroupByDispose)
      Nil
    })
  })
}

fn group_by_loop(
  control: Subject(GroupByMsg(k, a)),
  downstream: fn(Notification(#(k, Observable(a)))) -> Nil,
  state: GroupByState(k, a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    GroupByNext(key, value) -> {
      // Check if this is a new key
      let is_new_key = !list.contains(state.emitted_keys, key)

      // Create group observable if new key
      let new_state = case is_new_key {
        True -> {
          // Create the group observable
          let group_observable =
            Observable(subscribe: fn(group_observer: Observer(a)) {
              let reply: Subject(Disposable) = process.new_subject()
              process.send(control, GroupSubscribe(key, group_observer, reply))
              case process.receive(reply, 5000) {
                Ok(disp) -> disp
                Error(_) -> panic as "group subscribe timeout"
              }
            })

          // Emit the new group to downstream
          downstream(OnNext(#(key, group_observable)))

          // Update emitted keys
          GroupByState(..state, emitted_keys: [key, ..state.emitted_keys])
        }
        False -> state
      }

      // Forward value to group subscribers (or buffer if no subscribers yet)
      let current_group = case dict.get(new_state.groups, key) {
        Ok(g) -> g
        Error(_) -> GroupState(subscribers: [], buffer: [])
      }

      let updated_group = case current_group.subscribers {
        [] -> {
          // No subscribers yet, buffer the value
          GroupState(
            ..current_group,
            buffer: list.append(current_group.buffer, [value]),
          )
        }
        subs -> {
          // Forward to all subscribers
          list.each(subs, fn(sub) {
            let Observer(notify) = sub.observer
            notify(OnNext(value))
          })
          current_group
        }
      }

      let updated_groups = dict.insert(new_state.groups, key, updated_group)
      group_by_loop(
        control,
        downstream,
        GroupByState(..new_state, groups: updated_groups),
      )
    }
    GroupByError(e) -> {
      // Propagate error to downstream and all groups
      downstream(OnError(e))
      // Also error all group subscribers
      let _ =
        dict.each(state.groups, fn(_key, group_state) {
          list.each(group_state.subscribers, fn(sub) {
            let Observer(notify) = sub.observer
            notify(OnError(e))
          })
        })
      Nil
    }
    GroupByCompleted -> {
      // Complete all groups first, then complete downstream
      let _ =
        dict.each(state.groups, fn(_key, group_state) {
          list.each(group_state.subscribers, fn(sub) {
            let Observer(notify) = sub.observer
            notify(OnCompleted)
          })
        })
      downstream(OnCompleted)
      // Keep running to handle late subscribers who should get OnCompleted
      group_by_loop(
        control,
        downstream,
        GroupByState(..state, terminated: True),
      )
    }
    GroupByDispose -> Nil
    GroupSubscribe(key, observer, reply) -> {
      // Add subscriber to the group
      let id = erlang_unique_integer()
      let new_sub = GroupSubscriber(id: id, observer: observer)

      let current_group = case dict.get(state.groups, key) {
        Ok(g) -> g
        Error(_) -> GroupState(subscribers: [], buffer: [])
      }

      // Flush buffered values to new subscriber
      let Observer(notify) = observer
      list.each(current_group.buffer, fn(value) { notify(OnNext(value)) })

      // If already terminated, send completion
      case state.terminated {
        True -> notify(OnCompleted)
        False -> Nil
      }

      let new_group =
        GroupState(..current_group, subscribers: [
          new_sub,
          ..current_group.subscribers
        ])
      let new_groups = dict.insert(state.groups, key, new_group)

      let disp =
        Disposable(dispose: fn() {
          process.send(control, GroupUnsubscribe(key, id))
          Nil
        })
      process.send(reply, disp)

      group_by_loop(
        control,
        downstream,
        GroupByState(..state, groups: new_groups),
      )
    }
    GroupUnsubscribe(key, id) -> {
      let new_groups = case dict.get(state.groups, key) {
        Ok(group_state) -> {
          let new_subs =
            list.filter(group_state.subscribers, fn(s) { s.id != id })
          dict.insert(
            state.groups,
            key,
            GroupState(..group_state, subscribers: new_subs),
          )
        }
        Error(_) -> state.groups
      }
      group_by_loop(
        control,
        downstream,
        GroupByState(..state, groups: new_groups),
      )
    }
  }
}

@external(erlang, "erlang", "unique_integer")
fn erlang_unique_integer() -> Int
