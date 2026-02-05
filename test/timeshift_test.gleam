//// Tests for timeshift operators
////
//// Note: These tests use a message-based approach rather than process dictionary
//// refs because the async operators run callbacks from spawned processes.

import actorx
import actorx/timeshift
import actorx/types.{
  type Notification, type Observer, Disposable, Observer, OnCompleted, OnError,
  OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should
import test_utils.{wait_ms}

// ============================================================================
// Test helper: Message-based observer
// ============================================================================

/// Create an observer that sends notifications to a subject
fn message_observer(subject: Subject(Notification(a))) -> Observer(a) {
  Observer(notify: fn(n) { process.send(subject, n) })
}

/// Collect messages from a subject with timeout
fn collect_messages(
  subject: Subject(Notification(a)),
  timeout_ms: Int,
) -> #(List(a), Bool, List(String)) {
  collect_messages_loop(subject, timeout_ms, [], False, [])
}

fn collect_messages_loop(
  subject: Subject(Notification(a)),
  timeout_ms: Int,
  values: List(a),
  completed: Bool,
  errors: List(String),
) -> #(List(a), Bool, List(String)) {
  let selector =
    process.new_selector()
    |> process.select(subject)

  case process.selector_receive(selector, timeout_ms) {
    Ok(OnNext(x)) ->
      collect_messages_loop(
        subject,
        timeout_ms,
        list.append(values, [x]),
        completed,
        errors,
      )
    Ok(OnCompleted) -> #(values, True, errors)
    Ok(OnError(e)) -> #(values, completed, list.append(errors, [e]))
    Error(_) -> #(values, completed, errors)
  }
}

// ============================================================================
// Timer tests
// ============================================================================

pub fn timer_emits_zero_after_delay_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let _disp = actorx.subscribe(timeshift.timer(50), observer)

  // Collect with generous timeout
  let #(values, completed, errors) = collect_messages(subject, 200)

  values |> should.equal([0])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn timer_completes_after_emission_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let _disp = actorx.subscribe(timeshift.timer(30), observer)

  let #(_values, completed, _errors) = collect_messages(subject, 150)

  completed |> should.be_true()
}

pub fn timer_disposal_prevents_emission_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let Disposable(dispose) = actorx.subscribe(timeshift.timer(100), observer)

  // Dispose before timer fires
  wait_ms(20)
  dispose()

  // Wait and collect - should get nothing
  let #(values, completed, _errors) = collect_messages(subject, 200)

  values |> should.equal([])
  completed |> should.be_false()
}

// ============================================================================
// Interval tests
// ============================================================================

pub fn interval_emits_incrementing_values_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let Disposable(dispose) = actorx.subscribe(timeshift.interval(30), observer)

  // Wait for a few intervals
  wait_ms(130)
  dispose()

  // Collect any remaining messages with short timeout
  let #(values, _completed, _errors) = collect_messages(subject, 50)

  // Should have emitted several values
  { list.length(values) >= 3 } |> should.be_true()

  // Values should start at 0 and increment
  case values {
    [first, second, third, ..] -> {
      first |> should.equal(0)
      second |> should.equal(1)
      third |> should.equal(2)
    }
    _ -> should.fail()
  }
}

pub fn interval_disposal_stops_emissions_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let Disposable(dispose) = actorx.subscribe(timeshift.interval(30), observer)

  // Wait for a couple emissions
  wait_ms(80)
  dispose()

  // Drain any messages that arrived
  let #(values_at_disposal, _, _) = collect_messages(subject, 50)
  let count_at_disposal = list.length(values_at_disposal)

  // Wait some more and collect
  wait_ms(100)
  let #(more_values, _, _) = collect_messages(subject, 50)

  // Should not have received more values after disposal
  more_values |> should.equal([])
  { count_at_disposal >= 2 } |> should.be_true()
}

pub fn interval_multiple_subscribers_independent_test() {
  let subject1: Subject(Notification(Int)) = process.new_subject()
  let subject2: Subject(Notification(Int)) = process.new_subject()

  let observer1 = message_observer(subject1)
  let observer2 = message_observer(subject2)

  let Disposable(dispose1) = actorx.subscribe(timeshift.interval(30), observer1)
  wait_ms(50)
  let Disposable(dispose2) = actorx.subscribe(timeshift.interval(30), observer2)

  wait_ms(100)
  dispose1()
  dispose2()

  let #(values1, _, _) = collect_messages(subject1, 50)
  let #(values2, _, _) = collect_messages(subject2, 50)

  // Both should have their own sequence starting from 0
  case values1, values2 {
    [first1, ..], [first2, ..] -> {
      first1 |> should.equal(0)
      first2 |> should.equal(0)
    }
    _, _ -> should.fail()
  }
}

// ============================================================================
// Delay tests
// ============================================================================

pub fn delay_shifts_emissions_in_time_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let source = actorx.from_list([1, 2, 3])

  let _disp =
    source
    |> timeshift.delay(50)
    |> actorx.subscribe(observer)

  // Immediately after subscribe, should have no values yet
  let #(immediate_values, _, _) = collect_messages(subject, 10)
  immediate_values |> should.equal([])

  // Wait for delay and collect all
  let #(values, completed, _errors) = collect_messages(subject, 150)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn delay_preserves_order_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let source = actorx.from_list([5, 4, 3, 2, 1])

  let _disp =
    source
    |> timeshift.delay(30)
    |> actorx.subscribe(observer)

  let #(values, _completed, _errors) = collect_messages(subject, 150)

  values |> should.equal([5, 4, 3, 2, 1])
}

pub fn delay_completes_after_all_emitted_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let source = actorx.from_list([1, 2])

  let _disp =
    source
    |> timeshift.delay(50)
    |> actorx.subscribe(observer)

  // Immediately after, should not be complete
  let #(_, immediate_complete, _) = collect_messages(subject, 10)
  immediate_complete |> should.be_false()

  // After delay, should be complete
  let #(_values, completed, _errors) = collect_messages(subject, 150)
  completed |> should.be_true()
}

// ============================================================================
// Debounce tests
// ============================================================================

pub fn debounce_waits_for_silence_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  // Create a source that emits 1, then completes
  let source = actorx.from_list([1])

  let _disp =
    source
    |> timeshift.debounce(50)
    |> actorx.subscribe(observer)

  // After debounce, the single value should be emitted on complete
  let #(values, completed, _errors) = collect_messages(subject, 150)

  values |> should.equal([1])
  completed |> should.be_true()
}

pub fn debounce_emits_latest_value_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  // Source that emits multiple values at once
  let source = actorx.from_list([1, 2, 3])

  let _disp =
    source
    |> timeshift.debounce(50)
    |> actorx.subscribe(observer)

  let #(values, completed, _errors) = collect_messages(subject, 150)

  // Should emit the latest value (3)
  values |> should.equal([3])
  completed |> should.be_true()
}

// ============================================================================
// Throttle tests
// ============================================================================

pub fn throttle_emits_first_immediately_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let source = actorx.from_list([1, 2, 3, 4, 5])

  let _disp =
    source
    |> timeshift.throttle(100)
    |> actorx.subscribe(observer)

  // Give a bit of time for processing
  wait_ms(50)
  let #(values, _, _) = collect_messages(subject, 50)

  // First value should be emitted immediately
  case values {
    [first, ..] -> first |> should.equal(1)
    _ -> should.fail()
  }
}

pub fn throttle_rate_limits_emissions_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let source = actorx.from_list([1, 2, 3, 4, 5])

  let _disp =
    source
    |> timeshift.throttle(50)
    |> actorx.subscribe(observer)

  // Wait for throttle windows to process
  let #(values, _completed, _errors) = collect_messages(subject, 200)

  // Should have emitted first value immediately, then latest at window end
  // First should be 1 (immediate)
  case values {
    [first, ..rest] -> {
      first |> should.equal(1)
      // Should have at least one more value (the latest: 5)
      { list.length(rest) >= 1 } |> should.be_true()
    }
    _ -> should.fail()
  }
}

pub fn throttle_completes_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let source = actorx.from_list([1, 2, 3])

  let _disp =
    source
    |> timeshift.throttle(30)
    |> actorx.subscribe(observer)

  let #(_values, completed, _errors) = collect_messages(subject, 150)

  completed |> should.be_true()
}

// ============================================================================
// Integration with actorx facade
// ============================================================================

pub fn timer_via_actorx_facade_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let _disp = actorx.subscribe(actorx.timer(50), observer)

  let #(values, completed, _errors) = collect_messages(subject, 150)

  values |> should.equal([0])
  completed |> should.be_true()
}

pub fn interval_via_actorx_facade_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)
  let Disposable(dispose) = actorx.subscribe(actorx.interval(30), observer)

  wait_ms(100)
  dispose()

  let #(values, _completed, _errors) = collect_messages(subject, 50)
  { list.length(values) >= 2 } |> should.be_true()
}

pub fn delay_via_actorx_facade_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  let _disp =
    actorx.from_list([1, 2, 3])
    |> actorx.delay(50)
    |> actorx.subscribe(observer)

  let #(values, _completed, _errors) = collect_messages(subject, 150)

  values |> should.equal([1, 2, 3])
}

pub fn debounce_via_actorx_facade_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  let _disp =
    actorx.from_list([1, 2, 3])
    |> actorx.debounce(50)
    |> actorx.subscribe(observer)

  let #(values, _completed, _errors) = collect_messages(subject, 150)

  values |> should.equal([3])
}

pub fn throttle_via_actorx_facade_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  let _disp =
    actorx.from_list([1, 2, 3])
    |> actorx.throttle(50)
    |> actorx.subscribe(observer)

  let #(values, _completed, _errors) = collect_messages(subject, 150)

  // First value emitted immediately
  case values {
    [first, ..] -> first |> should.equal(1)
    _ -> should.fail()
  }
}

// ============================================================================
// Timer cancellation on dispose tests
// ============================================================================

pub fn debounce_dispose_cancels_timer_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  // Use a subject to control when values are emitted
  let #(input, output) = actorx.subject()

  let Disposable(dispose) =
    output
    |> timeshift.debounce(100)
    |> actorx.subscribe(observer)

  // Send a value - this starts a debounce timer
  actorx.on_next(input, 42)

  // Dispose before timer fires
  wait_ms(30)
  dispose()

  // Wait past when timer would have fired
  wait_ms(150)

  // Should not have received the debounced value
  let #(values, _completed, _errors) = collect_messages(subject, 50)
  values |> should.equal([])
}

pub fn throttle_dispose_cancels_timer_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  // Use a subject to control when values are emitted
  let #(input, output) = actorx.subject()

  let Disposable(dispose) =
    output
    |> timeshift.throttle(100)
    |> actorx.subscribe(observer)

  // Send first value - emitted immediately, starts window timer
  actorx.on_next(input, 1)
  wait_ms(10)

  // Drain the immediate emission
  let #(immediate, _, _) = collect_messages(subject, 20)
  immediate |> should.equal([1])

  // Send second value during window - stored as "latest"
  actorx.on_next(input, 2)

  // Dispose before window ends
  wait_ms(30)
  dispose()

  // Wait past when window timer would have fired
  wait_ms(150)

  // Should not have received the "latest" value from window end
  let #(values, _completed, _errors) = collect_messages(subject, 50)
  values |> should.equal([])
}

pub fn delay_dispose_cancels_pending_timers_test() {
  let subject: Subject(Notification(Int)) = process.new_subject()
  let observer = message_observer(subject)

  // Use a subject to control when values are emitted
  let #(input, output) = actorx.subject()

  let Disposable(dispose) =
    output
    |> timeshift.delay(100)
    |> actorx.subscribe(observer)

  // Send multiple values - each schedules a delayed emission
  actorx.on_next(input, 1)
  actorx.on_next(input, 2)
  actorx.on_next(input, 3)

  // Dispose before any timers fire
  wait_ms(30)
  dispose()

  // Wait past when timers would have fired
  wait_ms(150)

  // Should not have received any delayed values
  let #(values, _completed, _errors) = collect_messages(subject, 50)
  values |> should.equal([])
}
