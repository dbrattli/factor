//// Tests for error handling operators (retry, catch)
////
//// Based on RxPY test patterns for comprehensive coverage.

import actorx
import actorx/types.{
  type Notification, type Observable, type Observer, Disposable, Observable,
  Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleeunit/should
import test_utils.{collect_messages, collect_notifications, message_observer}

// ============================================================================
// retry tests
// ============================================================================

pub fn retry_no_error_completes_normally_test() {
  // RxPY: test_retry_observable_basic
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.retry(3)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn retry_max_retries_then_error_test() {
  // RxPY: test_retry_observable_retry_count_basic
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Always fails - should retry twice then propagate error
  let observable =
    actorx.fail("Always fails")
    |> actorx.retry(2)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_false()
  errors |> should.equal(["Always fails"])
}

pub fn retry_zero_retries_propagates_immediately_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Immediate fail")
    |> actorx.retry(0)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_false()
  errors |> should.equal(["Immediate fail"])
}

pub fn retry_empty_source_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.empty()
    |> actorx.retry(3)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn retry_single_value_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.single(42)
    |> actorx.retry(3)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([42])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn retry_with_timer_test() {
  // Test retry with async source
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.timer(20)
    |> actorx.retry(2)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([0])
  completed |> should.be_true()
}

pub fn retry_partial_then_error_resubscribes_test() {
  // RxPY: test_retry_observable_error - values emitted, then error, then retry
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Track subscription count
  let count_subject: Subject(Int) = process.new_subject()

  let observable =
    actorx.defer(fn() {
      process.send(count_subject, 1)
      // First subscription: emit 1, 2 then error
      // Second subscription: succeed
      actorx.create(fn(observer) {
        actorx.on_next(observer, 1)
        actorx.on_next(observer, 2)
        // Check how many times we've subscribed
        let selector =
          process.new_selector()
          |> process.select(count_subject)
        case drain_subject(selector, 0) {
          1 -> actorx.on_error(observer, "First try fails")
          _ -> actorx.on_completed(observer)
        }
        actorx.empty_disposable()
      })
    })
    |> actorx.retry(2)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // First attempt: 1, 2, error -> retry
  // Second attempt: 1, 2, complete
  values |> should.equal([1, 2, 1, 2])
  completed |> should.be_true()
}

fn drain_subject(selector, count: Int) -> Int {
  case process.selector_receive(selector, 1) {
    Ok(_) -> drain_subject(selector, count + 1)
    Error(_) -> count
  }
}

// ============================================================================
// catch tests
// ============================================================================

pub fn catch_no_error_passes_through_test() {
  // RxPY: test_catch_no_errors
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.catch(fn(_) { actorx.single(99) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_error_switches_to_fallback_test() {
  // RxPY: test_catch_error
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Oops")
    |> actorx.catch(fn(_) { actorx.single(42) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([42])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_error_with_fallback_list_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Error")
    |> actorx.catch(fn(_) { actorx.from_list([10, 20, 30]) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([10, 20, 30])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_partial_emission_then_error_test() {
  // RxPY: test_catch_error - source emits some values before error
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.create(fn(observer) {
      actorx.on_next(observer, 1)
      actorx.on_next(observer, 2)
      actorx.on_error(observer, "Midway error")
      actorx.empty_disposable()
    })
    |> actorx.catch(fn(_) { actorx.from_list([100, 200]) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 100, 200])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_handler_receives_error_message_test() {
  // RxPY: test_catch_error_specific_caught
  let result_subject: Subject(Notification(String)) = process.new_subject()

  let observable =
    actorx.fail("Custom error")
    |> actorx.catch(fn(err) { actorx.single("Caught: " <> err) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal(["Caught: Custom error"])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_fallback_empty_test() {
  // RxPY: test_catch_empty
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Error")
    |> actorx.catch(fn(_) { actorx.empty() })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_fallback_also_errors_propagates_test() {
  // RxPY: test_catch_error_error
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Error 1")
    |> actorx.catch(fn(_) { actorx.fail("Error 2") })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_false()
  errors |> should.equal(["Error 2"])
}

pub fn catch_chained_catches_both_errors_test() {
  // RxPY: test_catch_throw_from_nested_catch
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Error 1")
    |> actorx.catch(fn(_) { actorx.fail("Error 2") })
    |> actorx.catch(fn(_) { actorx.single(999) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([999])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_chained_first_succeeds_test() {
  // RxPY: test_catch_nested_outer_catches
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // First catch handles the error, second catch is not invoked
  let observable =
    actorx.fail("Error 1")
    |> actorx.catch(fn(_) { actorx.from_list([1, 2, 3]) })
    |> actorx.catch(fn(_) { actorx.single(999) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_with_timer_fallback_test() {
  // Test catch with async fallback
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.fail("Error")
    |> actorx.catch(fn(_) {
      actorx.timer(20)
      |> actorx.map(fn(_) { 42 })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([42])
  completed |> should.be_true()
}

pub fn catch_preserves_notification_sequence_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.create(fn(observer) {
      actorx.on_next(observer, 1)
      actorx.on_error(observer, "Error")
      actorx.empty_disposable()
    })
    |> actorx.catch(fn(_) {
      actorx.create(fn(observer) {
        actorx.on_next(observer, 2)
        actorx.on_completed(observer)
        actorx.empty_disposable()
      })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let notifications = collect_notifications(result_subject, 100)

  notifications |> should.equal([OnNext(1), OnNext(2), OnCompleted])
}

// ============================================================================
// Combined retry + catch tests
// ============================================================================

pub fn retry_then_catch_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Retry twice, then fall back to default
  let observable =
    actorx.fail("Error")
    |> actorx.retry(2)
    |> actorx.catch(fn(_) { actorx.single(0) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([0])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn catch_then_retry_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // catch converts error to success, retry sees success
  let observable =
    actorx.fail("Error")
    |> actorx.catch(fn(_) { actorx.single(42) })
    |> actorx.retry(2)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([42])
  completed |> should.be_true()
  errors |> should.equal([])
}

// ============================================================================
// Subscription disposal tracking tests
// ============================================================================

/// Message type for tracking disposals
type DisposeMsg {
  Disposed
}

/// Count messages received on a subject within a timeout
fn count_dispose_messages(subj: Subject(DisposeMsg), timeout_ms: Int) -> Int {
  count_dispose_loop(subj, timeout_ms, 0)
}

fn count_dispose_loop(
  subj: Subject(DisposeMsg),
  timeout_ms: Int,
  count: Int,
) -> Int {
  let selector =
    process.new_selector()
    |> process.select(subj)

  case process.selector_receive(selector, timeout_ms) {
    Ok(Disposed) -> count_dispose_loop(subj, timeout_ms, count + 1)
    Error(_) -> count
  }
}

/// Create an observable that tracks when it's disposed (async version)
/// The delay allows SetDisposable message to be processed before emissions
fn tracked_source(
  dispose_tracker: Subject(DisposeMsg),
  values: List(a),
  should_error: Bool,
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(notify) = observer

    // Create a subject to signal when we should dispose the timer
    let disposed_subject: Subject(Bool) = process.new_subject()

    // Spawn a process to emit after a small delay
    // This ensures SetDisposable is processed before emissions
    process.spawn(fn() {
      // Small delay to allow SetDisposable to be processed
      process.sleep(5)

      // Check if already disposed
      let selector =
        process.new_selector()
        |> process.select(disposed_subject)

      case process.selector_receive(selector, 0) {
        Ok(True) -> Nil
        _ -> {
          // Emit values
          emit_values(notify, values)

          // Either error or complete
          case should_error {
            True -> notify(OnError("test error"))
            False -> notify(OnCompleted)
          }
        }
      }
    })

    // Return disposable that tracks disposal
    Disposable(dispose: fn() {
      process.send(disposed_subject, True)
      process.send(dispose_tracker, Disposed)
      Nil
    })
  })
}

fn emit_values(notify: fn(Notification(a)) -> Nil, values: List(a)) -> Nil {
  case values {
    [] -> Nil
    [x, ..rest] -> {
      notify(OnNext(x))
      emit_values(notify, rest)
    }
  }
}

/// retry should dispose previous subscription when retrying
pub fn retry_disposes_previous_on_retry_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Track subscription attempts
  let attempt_subject: Subject(Int) = process.new_subject()

  let observable =
    actorx.defer(fn() {
      process.send(attempt_subject, 1)
      // Check attempt count
      let selector =
        process.new_selector()
        |> process.select(attempt_subject)
      let attempt_count = drain_attempts(selector, 0)

      case attempt_count {
        1 ->
          // First attempt: error
          tracked_source(dispose_tracker, [1, 2], True)
        _ ->
          // Second attempt: succeed
          tracked_source(dispose_tracker, [3, 4], False)
      }
    })
    |> actorx.retry(2)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Should get values from both attempts
  values |> should.equal([1, 2, 3, 4])
  completed |> should.be_true()

  // First subscription should have been disposed when retry happened
  // Second subscription disposed on completion
  let dispose_count = count_dispose_messages(dispose_tracker, 50)
  dispose_count |> should.equal(2)
}

fn drain_attempts(selector, count: Int) -> Int {
  case process.selector_receive(selector, 1) {
    Ok(_) -> drain_attempts(selector, count + 1)
    Error(_) -> count
  }
}

/// retry should dispose current subscription when outer is disposed
pub fn retry_disposes_current_on_outer_dispose_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Create a long-running source
  let source =
    Observable(subscribe: fn(observer: Observer(Int)) {
      // Use interval to keep emitting
      let inner = actorx.interval(50)
      let Disposable(inner_dispose) = actorx.subscribe(inner, observer)

      Disposable(dispose: fn() {
        process.send(dispose_tracker, Disposed)
        inner_dispose()
        Nil
      })
    })

  let observable = source |> actorx.retry(2)

  let Disposable(dispose) =
    actorx.subscribe(observable, message_observer(result_subject))

  // Wait for some emissions
  process.sleep(80)

  // Dispose outer subscription
  dispose()

  // Inner subscription should have been disposed
  let dispose_count = count_dispose_messages(dispose_tracker, 100)
  dispose_count |> should.equal(1)
}

/// catch should dispose source when switching to fallback
pub fn catch_disposes_source_on_switch_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Source that errors and tracks disposal
  let source = tracked_source(dispose_tracker, [1, 2], True)

  // Fallback that also tracks disposal
  let fallback = tracked_source(dispose_tracker, [10, 20], False)

  let observable = source |> actorx.catch(fn(_) { fallback })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Should get source values then fallback values
  values |> should.equal([1, 2, 10, 20])
  completed |> should.be_true()

  // Both source and fallback should be disposed
  let dispose_count = count_dispose_messages(dispose_tracker, 50)
  dispose_count |> should.equal(2)
}

/// catch should dispose fallback when outer is disposed
pub fn catch_disposes_fallback_on_outer_dispose_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Source that errors after a small delay (allows SetDisposable to be processed)
  let source =
    Observable(subscribe: fn(observer: Observer(Int)) {
      let Observer(notify) = observer
      let disposed_subject: Subject(Bool) = process.new_subject()

      process.spawn(fn() {
        process.sleep(5)
        let selector =
          process.new_selector()
          |> process.select(disposed_subject)
        case process.selector_receive(selector, 0) {
          Ok(True) -> Nil
          _ -> notify(OnError("delayed error"))
        }
      })

      Disposable(dispose: fn() {
        process.send(disposed_subject, True)
        process.send(dispose_tracker, Disposed)
        Nil
      })
    })

  // Long-running fallback
  let fallback =
    Observable(subscribe: fn(observer: Observer(Int)) {
      let inner = actorx.interval(50)
      let Disposable(inner_dispose) = actorx.subscribe(inner, observer)

      Disposable(dispose: fn() {
        process.send(dispose_tracker, Disposed)
        inner_dispose()
        Nil
      })
    })

  let observable = source |> actorx.catch(fn(_) { fallback })

  let Disposable(dispose) =
    actorx.subscribe(observable, message_observer(result_subject))

  // Wait for source to error (5ms delay) and fallback to start emitting
  process.sleep(100)

  // Dispose outer
  dispose()

  // Both source (disposed on switch) and fallback (disposed on outer dispose) should be tracked
  let dispose_count = count_dispose_messages(dispose_tracker, 100)
  dispose_count |> should.equal(2)
}
