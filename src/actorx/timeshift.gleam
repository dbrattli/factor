//// Timeshift operators for ActorX
////
//// These operators work with time-based asynchronous streams:
//// - timer: Emit a single value after a delay
//// - interval: Emit incrementing values at regular intervals
//// - delay: Delay each emission by a specified time
//// - debounce: Emit only after silence (no new values) for a period
//// - throttle: Rate limit emissions to at most one per period
////
//// IMPORTANT: These operators are asynchronous. After subscribing, use
//// `process.sleep` or a proper async wait mechanism to receive values.

import actorx/types.{
  type Notification, type Observable, type Observer, Disposable, Observable,
  Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process.{type Subject, type Timer}
import gleam/option.{type Option, None, Some}

// ============================================================================
// Internal message types
// ============================================================================

type TimerWorkerMsg {
  TimerStart
  TimerDispose
}

type IntervalWorkerMsg {
  IntervalStart
  IntervalDispose
}

type DelayWorkerMsg(a) {
  DelayStart
  DelaySchedule(a)
  DelayEmit(a)
  DelayComplete
  DelayError(String)
  DelayDispose
}

type DebounceWorkerMsg(a) {
  DebounceStart
  DebounceValue(a)
  DebounceTimerFired
  DebounceComplete
  DebounceError(String)
  DebounceDispose
}

type ThrottleWorkerMsg(a) {
  ThrottleStart
  ThrottleValue(a)
  ThrottleWindowEnd
  ThrottleComplete
  ThrottleError(String)
  ThrottleDispose
}

// ============================================================================
// Timer operator
// ============================================================================

/// Creates an observable that emits `0` after the specified delay, then completes.
///
/// ## Example
/// ```gleam
/// // Emit 0 after 100ms
/// timer(100)
/// |> actorx.subscribe(observer)
///
/// // Wait for async completion
/// process.sleep(150)
/// ```
pub fn timer(delay_ms: Int) -> Observable(Int) {
  Observable(subscribe: fn(observer: Observer(Int)) {
    let Observer(downstream) = observer

    // Create subject for receiving worker's control subject
    let control_ready: Subject(Subject(TimerWorkerMsg)) = process.new_subject()

    // Spawn the timer worker
    process.spawn(fn() {
      let control: Subject(TimerWorkerMsg) = process.new_subject()
      process.send(control_ready, control)
      timer_worker(control, delay_ms, downstream)
    })

    // Get the worker's control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to get timer control subject"
    }

    // Tell worker to start
    process.send(control, TimerStart)

    Disposable(dispose: fn() {
      process.send(control, TimerDispose)
      Nil
    })
  })
}

fn timer_worker(
  control: Subject(TimerWorkerMsg),
  delay_ms: Int,
  downstream: fn(Notification(Int)) -> Nil,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    TimerStart -> {
      // Sleep for the delay
      process.sleep(delay_ms)
      // Check if disposed during sleep
      case process.selector_receive(selector, 0) {
        Ok(TimerDispose) -> Nil
        Ok(TimerStart) -> {
          downstream(OnNext(0))
          downstream(OnCompleted)
        }
        Error(_) -> {
          downstream(OnNext(0))
          downstream(OnCompleted)
        }
      }
    }
    TimerDispose -> Nil
  }
}

// ============================================================================
// Interval operator
// ============================================================================

/// Creates an observable that emits incrementing integers (0, 1, 2, ...)
/// at regular intervals.
///
/// ## Example
/// ```gleam
/// // Emit every 100ms
/// interval(100)
/// |> actorx.take(5)  // Take first 5 values
/// |> actorx.subscribe(observer)
/// ```
pub fn interval(period_ms: Int) -> Observable(Int) {
  Observable(subscribe: fn(observer: Observer(Int)) {
    let Observer(downstream) = observer

    // Create subject for receiving worker's control subject
    let control_ready: Subject(Subject(IntervalWorkerMsg)) =
      process.new_subject()

    // Spawn the interval worker
    process.spawn(fn() {
      let control: Subject(IntervalWorkerMsg) = process.new_subject()
      process.send(control_ready, control)
      interval_worker_init(control, period_ms, downstream)
    })

    // Get the worker's control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to get interval control subject"
    }

    // Tell worker to start
    process.send(control, IntervalStart)

    Disposable(dispose: fn() {
      process.send(control, IntervalDispose)
      Nil
    })
  })
}

fn interval_worker_init(
  control: Subject(IntervalWorkerMsg),
  period_ms: Int,
  downstream: fn(Notification(Int)) -> Nil,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    IntervalStart -> interval_worker_loop(control, period_ms, downstream, 0)
    IntervalDispose -> Nil
  }
}

fn interval_worker_loop(
  control: Subject(IntervalWorkerMsg),
  period_ms: Int,
  downstream: fn(Notification(Int)) -> Nil,
  count: Int,
) -> Nil {
  // Check for dispose before sleeping
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive(selector, 0) {
    Ok(IntervalDispose) -> Nil
    Ok(IntervalStart) ->
      interval_worker_loop(control, period_ms, downstream, count)
    Error(_) -> {
      // No dispose message, sleep and emit
      process.sleep(period_ms)

      // Check again after sleep
      case process.selector_receive(selector, 0) {
        Ok(IntervalDispose) -> Nil
        Ok(IntervalStart) ->
          interval_worker_loop(control, period_ms, downstream, count)
        Error(_) -> {
          downstream(OnNext(count))
          interval_worker_loop(control, period_ms, downstream, count + 1)
        }
      }
    }
  }
}

// ============================================================================
// Delay operator
// ============================================================================

/// Delays each emission from the source observable by the specified time.
///
/// ## Example
/// ```gleam
/// source
/// |> delay(100)  // Delay each value by 100ms
/// |> actorx.subscribe(observer)
/// ```
pub fn delay(source: Observable(a), ms: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create subject for receiving worker's control subject
    let control_ready: Subject(Subject(DelayWorkerMsg(a))) =
      process.new_subject()

    // Spawn the delay worker
    process.spawn(fn() {
      let control: Subject(DelayWorkerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      delay_worker_init(control, ms, downstream)
    })

    // Get the worker's control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to get delay control subject"
    }

    // Tell worker to start
    process.send(control, DelayStart)

    // Subscribe to source
    let Observable(subscribe) = source
    let source_disp =
      subscribe(
        Observer(notify: fn(n) {
          case n {
            OnNext(x) -> process.send(control, DelaySchedule(x))
            OnError(e) -> process.send(control, DelayError(e))
            OnCompleted -> process.send(control, DelayComplete)
          }
        }),
      )

    Disposable(dispose: fn() {
      process.send(control, DelayDispose)
      let Disposable(dispose_source) = source_disp
      dispose_source()
    })
  })
}

fn delay_worker_init(
  control: Subject(DelayWorkerMsg(a)),
  ms: Int,
  downstream: fn(Notification(a)) -> Nil,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    DelayStart -> delay_worker_loop(control, ms, downstream, 0, False)
    DelaySchedule(_) -> delay_worker_init(control, ms, downstream)
    DelayEmit(_) -> delay_worker_init(control, ms, downstream)
    DelayComplete -> Nil
    DelayError(_) -> Nil
    DelayDispose -> Nil
  }
}

fn delay_worker_loop(
  control: Subject(DelayWorkerMsg(a)),
  ms: Int,
  downstream: fn(Notification(a)) -> Nil,
  pending: Int,
  source_completed: Bool,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    DelayStart ->
      delay_worker_loop(control, ms, downstream, pending, source_completed)
    DelaySchedule(x) -> {
      // Schedule delayed emission
      process.send_after(control, ms, DelayEmit(x))
      delay_worker_loop(control, ms, downstream, pending + 1, source_completed)
    }
    DelayEmit(x) -> {
      downstream(OnNext(x))
      let new_pending = pending - 1
      case source_completed, new_pending {
        True, 0 -> downstream(OnCompleted)
        _, _ ->
          delay_worker_loop(
            control,
            ms,
            downstream,
            new_pending,
            source_completed,
          )
      }
    }
    DelayComplete -> {
      case pending {
        0 -> downstream(OnCompleted)
        _ -> delay_worker_loop(control, ms, downstream, pending, True)
      }
    }
    DelayError(e) -> downstream(OnError(e))
    DelayDispose -> Nil
  }
}

// ============================================================================
// Debounce operator
// ============================================================================

/// Emits a value only after the specified time has passed without
/// another value being emitted.
///
/// ## Example
/// ```gleam
/// search_input
/// |> debounce(300)  // Wait 300ms after last keystroke
/// |> actorx.subscribe(observer)
/// ```
pub fn debounce(source: Observable(a), ms: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create subject for receiving worker's control subject
    let control_ready: Subject(Subject(DebounceWorkerMsg(a))) =
      process.new_subject()

    // Spawn the debounce worker
    process.spawn(fn() {
      let control: Subject(DebounceWorkerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      debounce_worker_init(control, ms, downstream)
    })

    // Get the worker's control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to get debounce control subject"
    }

    // Tell worker to start
    process.send(control, DebounceStart)

    // Subscribe to source
    let Observable(subscribe) = source
    let source_disp =
      subscribe(
        Observer(notify: fn(n) {
          case n {
            OnNext(x) -> process.send(control, DebounceValue(x))
            OnError(e) -> process.send(control, DebounceError(e))
            OnCompleted -> process.send(control, DebounceComplete)
          }
        }),
      )

    Disposable(dispose: fn() {
      process.send(control, DebounceDispose)
      let Disposable(dispose_source) = source_disp
      dispose_source()
    })
  })
}

fn debounce_worker_init(
  control: Subject(DebounceWorkerMsg(a)),
  ms: Int,
  downstream: fn(Notification(a)) -> Nil,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    DebounceStart -> debounce_worker_loop(control, ms, downstream, None, None)
    DebounceValue(_) -> debounce_worker_init(control, ms, downstream)
    DebounceTimerFired -> debounce_worker_init(control, ms, downstream)
    DebounceComplete -> Nil
    DebounceError(_) -> Nil
    DebounceDispose -> Nil
  }
}

fn debounce_worker_loop(
  control: Subject(DebounceWorkerMsg(a)),
  ms: Int,
  downstream: fn(Notification(a)) -> Nil,
  latest: Option(a),
  current_timer: Option(Timer),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    DebounceStart ->
      debounce_worker_loop(control, ms, downstream, latest, current_timer)
    DebounceValue(x) -> {
      // Cancel previous timer if any
      case current_timer {
        Some(t) -> {
          process.cancel_timer(t)
          Nil
        }
        None -> Nil
      }
      // Schedule new timer
      let t = process.send_after(control, ms, DebounceTimerFired)
      debounce_worker_loop(control, ms, downstream, Some(x), Some(t))
    }
    DebounceTimerFired -> {
      // Timer fired, emit the latest value
      case latest {
        Some(x) -> {
          downstream(OnNext(x))
          debounce_worker_loop(control, ms, downstream, None, None)
        }
        None -> debounce_worker_loop(control, ms, downstream, None, None)
      }
    }
    DebounceComplete -> {
      // Emit pending value if any
      case latest {
        Some(x) -> downstream(OnNext(x))
        None -> Nil
      }
      downstream(OnCompleted)
    }
    DebounceError(e) -> downstream(OnError(e))
    DebounceDispose -> Nil
  }
}

// ============================================================================
// Throttle operator
// ============================================================================

/// Rate limits emissions to at most one per specified period.
/// Emits the first value immediately, then samples the latest value
/// at the end of each window.
///
/// ## Example
/// ```gleam
/// mouse_moves
/// |> throttle(100)  // At most one emission per 100ms
/// |> actorx.subscribe(observer)
/// ```
pub fn throttle(source: Observable(a), ms: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create subject for receiving worker's control subject
    let control_ready: Subject(Subject(ThrottleWorkerMsg(a))) =
      process.new_subject()

    // Spawn the throttle worker
    process.spawn(fn() {
      let control: Subject(ThrottleWorkerMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      throttle_worker_init(control, ms, downstream)
    })

    // Get the worker's control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to get throttle control subject"
    }

    // Tell worker to start
    process.send(control, ThrottleStart)

    // Subscribe to source
    let Observable(subscribe) = source
    let source_disp =
      subscribe(
        Observer(notify: fn(n) {
          case n {
            OnNext(x) -> process.send(control, ThrottleValue(x))
            OnError(e) -> process.send(control, ThrottleError(e))
            OnCompleted -> process.send(control, ThrottleComplete)
          }
        }),
      )

    Disposable(dispose: fn() {
      process.send(control, ThrottleDispose)
      let Disposable(dispose_source) = source_disp
      dispose_source()
    })
  })
}

fn throttle_worker_init(
  control: Subject(ThrottleWorkerMsg(a)),
  ms: Int,
  downstream: fn(Notification(a)) -> Nil,
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ThrottleStart -> throttle_worker_loop(control, ms, downstream, False, None)
    ThrottleValue(_) -> throttle_worker_init(control, ms, downstream)
    ThrottleWindowEnd -> throttle_worker_init(control, ms, downstream)
    ThrottleComplete -> Nil
    ThrottleError(_) -> Nil
    ThrottleDispose -> Nil
  }
}

fn throttle_worker_loop(
  control: Subject(ThrottleWorkerMsg(a)),
  ms: Int,
  downstream: fn(Notification(a)) -> Nil,
  in_window: Bool,
  latest: Option(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ThrottleStart ->
      throttle_worker_loop(control, ms, downstream, in_window, latest)
    ThrottleValue(x) -> {
      case in_window {
        False -> {
          // Not in window: emit immediately and start window
          downstream(OnNext(x))
          process.send_after(control, ms, ThrottleWindowEnd)
          throttle_worker_loop(control, ms, downstream, True, None)
        }
        True -> {
          // In window: store as latest
          throttle_worker_loop(control, ms, downstream, True, Some(x))
        }
      }
    }
    ThrottleWindowEnd -> {
      // Window ended
      case latest {
        Some(x) -> {
          // Emit stored value and start new window
          downstream(OnNext(x))
          process.send_after(control, ms, ThrottleWindowEnd)
          throttle_worker_loop(control, ms, downstream, True, None)
        }
        None -> {
          // No value during window, end throttle window
          throttle_worker_loop(control, ms, downstream, False, None)
        }
      }
    }
    ThrottleComplete -> {
      // Emit any pending value before completing
      case latest {
        Some(x) -> downstream(OnNext(x))
        None -> Nil
      }
      downstream(OnCompleted)
    }
    ThrottleError(e) -> downstream(OnError(e))
    ThrottleDispose -> Nil
  }
}
