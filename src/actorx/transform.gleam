//// Transform operators for ActorX
////
//// These operators transform the elements of an observable sequence:
//// - map: Apply a function to each element
//// - flat_map: Map to observables and flatten (= map + merge_inner)
//// - concat_map: Map to observables and concatenate (= map + concat_inner)
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

/// Flattens an Observable of Observables by merging inner emissions.
///
/// Subscribes to each inner observable as it arrives and forwards all
/// emissions. Completes only when the outer source AND all inner
/// observables have completed.
///
/// ## Example
/// ```gleam
/// // Flatten a stream of streams
/// source_of_sources
/// |> merge_inner()
/// ```
pub fn merge_inner(source: Observable(Observable(a))) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(MergeInnerMsg(a))) =
      process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(MergeInnerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      merge_inner_loop(control, downstream, 0, False)
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

fn merge_inner_loop(
  control: Subject(MergeInnerMsg(a)),
  downstream: fn(Notification(a)) -> Nil,
  inner_count: Int,
  outer_stopped: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    MergeInnerSubscribe(inner_observable) -> {
      // Subscribe to inner observable
      let inner_observer =
        Observer(notify: fn(n) {
          case n {
            OnNext(value) -> process.send(control, MergeInnerNext(value))
            OnError(e) -> process.send(control, MergeInnerError(e))
            OnCompleted -> process.send(control, MergeInnerCompleted)
          }
        })

      let Observable(inner_subscribe) = inner_observable
      let _inner_disp = inner_subscribe(inner_observer)

      merge_inner_loop(control, downstream, inner_count + 1, outer_stopped)
    }

    MergeInnerNext(value) -> {
      downstream(OnNext(value))
      merge_inner_loop(control, downstream, inner_count, outer_stopped)
    }

    MergeInnerCompleted -> {
      let new_count = inner_count - 1
      case outer_stopped && new_count <= 0 {
        True -> {
          // Outer done and all inners done - complete
          downstream(OnCompleted)
          Nil
        }
        False -> merge_inner_loop(control, downstream, new_count, outer_stopped)
      }
    }

    MergeInnerError(e) -> {
      downstream(OnError(e))
      Nil
    }

    MergeOuterCompleted -> {
      case inner_count <= 0 {
        True -> {
          // No active inners - complete immediately
          downstream(OnCompleted)
          Nil
        }
        False -> {
          // Wait for inners to complete
          merge_inner_loop(control, downstream, inner_count, True)
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

/// Messages for the concat_inner coordinator actor
type ConcatInnerMsg(a) {
  /// Outer emitted an inner observable to queue
  ConcatInnerEnqueue(Observable(a))
  /// Inner observable emitted a value
  ConcatInnerNext(a)
  /// Inner observable completed
  ConcatInnerCompleted
  /// Inner observable errored
  ConcatInnerError(String)
  /// Outer completed
  ConcatOuterCompleted
  /// Outer errored
  ConcatOuterError(String)
  /// Dispose all subscriptions
  ConcatInnerDispose
}

/// State for concat_inner actor
/// Uses a list as a queue (append to end, take from front)
type ConcatInnerState(a) {
  ConcatInnerState(
    queue: List(Observable(a)),
    active: Bool,
    outer_stopped: Bool,
  )
}

/// Flattens an Observable of Observables by concatenating in order.
///
/// Subscribes to each inner observable only after the previous one
/// completes. Queues inner observables and processes them sequentially.
///
/// ## Example
/// ```gleam
/// // Flatten a stream of streams in order
/// source_of_sources
/// |> concat_inner()
/// ```
pub fn concat_inner(source: Observable(Observable(a))) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(ConcatInnerMsg(a))) =
      process.new_subject()

    // Spawn coordinator actor
    process.spawn(fn() {
      let control: Subject(ConcatInnerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      let initial_state =
        ConcatInnerState(queue: [], active: False, outer_stopped: False)
      concat_inner_loop(control, downstream, initial_state)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create concat_inner"
    }

    // Subscribe to outer source
    let outer_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(inner) -> process.send(control, ConcatInnerEnqueue(inner))
          OnError(e) -> process.send(control, ConcatOuterError(e))
          OnCompleted -> process.send(control, ConcatOuterCompleted)
        }
      })

    let Observable(subscribe) = source
    let outer_disp = subscribe(outer_observer)

    // Return disposable
    Disposable(dispose: fn() {
      let Disposable(dispose_outer) = outer_disp
      dispose_outer()
      process.send(control, ConcatInnerDispose)
      Nil
    })
  })
}

fn concat_inner_loop(
  control: Subject(ConcatInnerMsg(a)),
  downstream: fn(Notification(a)) -> Nil,
  state: ConcatInnerState(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ConcatInnerEnqueue(inner) -> {
      case state.active {
        True -> {
          // Already processing an inner, queue this one (append to end)
          let new_queue = list.append(state.queue, [inner])
          concat_inner_loop(
            control,
            downstream,
            ConcatInnerState(..state, queue: new_queue),
          )
        }
        False -> {
          // No active inner, subscribe immediately
          subscribe_to_inner(control, inner)
          concat_inner_loop(
            control,
            downstream,
            ConcatInnerState(..state, active: True),
          )
        }
      }
    }

    ConcatInnerNext(value) -> {
      downstream(OnNext(value))
      concat_inner_loop(control, downstream, state)
    }

    ConcatInnerCompleted -> {
      // Current inner completed, try to process next in queue
      case state.queue {
        [next_inner, ..remaining] -> {
          subscribe_to_inner(control, next_inner)
          concat_inner_loop(
            control,
            downstream,
            ConcatInnerState(..state, queue: remaining, active: True),
          )
        }
        [] -> {
          // Queue empty
          case state.outer_stopped {
            True -> {
              // Outer done and no more inners - complete
              downstream(OnCompleted)
              Nil
            }
            False -> {
              // Wait for more inners
              concat_inner_loop(
                control,
                downstream,
                ConcatInnerState(..state, active: False),
              )
            }
          }
        }
      }
    }

    ConcatInnerError(e) -> {
      downstream(OnError(e))
      Nil
    }

    ConcatOuterCompleted -> {
      case state.active || !list.is_empty(state.queue) {
        True -> {
          // Still processing or have queued inners
          concat_inner_loop(
            control,
            downstream,
            ConcatInnerState(..state, outer_stopped: True),
          )
        }
        False -> {
          // No active inner and queue empty - complete
          downstream(OnCompleted)
          Nil
        }
      }
    }

    ConcatOuterError(e) -> {
      downstream(OnError(e))
      Nil
    }

    ConcatInnerDispose -> Nil
  }
}

fn subscribe_to_inner(
  control: Subject(ConcatInnerMsg(a)),
  inner: Observable(a),
) -> Nil {
  let inner_observer =
    Observer(notify: fn(n) {
      case n {
        OnNext(value) -> process.send(control, ConcatInnerNext(value))
        OnError(e) -> process.send(control, ConcatInnerError(e))
        OnCompleted -> process.send(control, ConcatInnerCompleted)
      }
    })

  let Observable(inner_subscribe) = inner
  let _inner_disp = inner_subscribe(inner_observer)
  Nil
}

// ============================================================================
// flat_map - Composed from map + merge_inner
// ============================================================================

/// Projects each element of an observable sequence into an observable
/// sequence and merges the resulting observable sequences.
///
/// This is composed from `map` and `merge_inner`:
/// `flat_map(source, f) = source |> map(f) |> merge_inner()`
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
  |> merge_inner()
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
