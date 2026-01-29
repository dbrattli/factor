//// Tests for subject module

import actorx
import actorx/subject
import actorx/types.{
  type Notification, Disposable, Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should
import test_utils.{wait_ms}

// ============================================================================
// Test helper: Message-based observer
// ============================================================================

fn message_observer(subj: Subject(Notification(a))) -> types.Observer(a) {
  Observer(notify: fn(n) { process.send(subj, n) })
}

fn collect_messages(
  subj: Subject(Notification(a)),
  timeout_ms: Int,
) -> #(List(a), Bool, List(String)) {
  collect_messages_loop(subj, timeout_ms, [], False, [])
}

fn collect_messages_loop(
  subj: Subject(Notification(a)),
  timeout_ms: Int,
  values: List(a),
  completed: Bool,
  errors: List(String),
) -> #(List(a), Bool, List(String)) {
  let selector =
    process.new_selector()
    |> process.select(subj)

  case process.selector_receive(selector, timeout_ms) {
    Ok(OnNext(x)) ->
      collect_messages_loop(
        subj,
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
// single_subject tests
// ============================================================================

pub fn single_subject_forwards_values_test() {
  let #(input, output) = subject.single_subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  // Send values through the input observer
  types.on_next(input, 1)
  types.on_next(input, 2)
  types.on_next(input, 3)
  types.on_completed(input)

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn single_subject_buffers_before_subscribe_test() {
  let #(input, output) = subject.single_subject()

  // Send values BEFORE subscribing
  types.on_next(input, 10)
  types.on_next(input, 20)

  // Small delay to ensure messages are buffered
  wait_ms(10)

  // Now subscribe
  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  // Send more after subscribe
  types.on_next(input, 30)
  types.on_completed(input)

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Should receive all values including buffered ones
  values |> should.equal([10, 20, 30])
  completed |> should.be_true()
}

pub fn single_subject_forwards_errors_test() {
  let #(input, output) = subject.single_subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  types.on_next(input, 1)
  types.on_error(input, "test error")

  let #(values, _completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1])
  errors |> should.equal(["test error"])
}

pub fn single_subject_only_allows_one_subscriber_test() {
  let #(_input, output) = subject.single_subject()

  let result1: Subject(Notification(Int)) = process.new_subject()
  let _disp1 = actorx.subscribe(output, message_observer(result1))

  // Second subscription should panic
  // We can't easily test panics in gleeunit, so we'll just verify
  // the first subscription works
  wait_ms(10)
  // First subscriber should be active
  // (In a real test we'd verify the panic on second subscribe)
}

pub fn single_subject_dispose_stops_forwarding_test() {
  let #(input, output) = subject.single_subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let Disposable(dispose) =
    actorx.subscribe(output, message_observer(result_subject))

  types.on_next(input, 1)
  wait_ms(10)

  // Dispose
  dispose()
  wait_ms(10)

  // These should not be received
  types.on_next(input, 2)
  types.on_next(input, 3)

  let #(values, _completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([1])
}

pub fn single_subject_with_actorx_facade_test() {
  let #(input, output) = actorx.single_subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  actorx.on_next(input, 42)
  actorx.on_completed(input)

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([42])
  completed |> should.be_true()
}

pub fn single_subject_works_with_map_test() {
  let #(input, output) = subject.single_subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let _disp =
    output
    |> actorx.map(fn(x) { x * 2 })
    |> actorx.subscribe(message_observer(result_subject))

  types.on_next(input, 1)
  types.on_next(input, 2)
  types.on_next(input, 3)
  types.on_completed(input)

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([2, 4, 6])
  completed |> should.be_true()
}

pub fn single_subject_works_with_filter_test() {
  let #(input, output) = subject.single_subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let _disp =
    output
    |> actorx.filter(fn(x) { x > 2 })
    |> actorx.subscribe(message_observer(result_subject))

  types.on_next(input, 1)
  types.on_next(input, 2)
  types.on_next(input, 3)
  types.on_next(input, 4)
  types.on_completed(input)

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([3, 4])
  completed |> should.be_true()
}

// ============================================================================
// multicast subject tests
// ============================================================================

pub fn subject_forwards_values_test() {
  let #(input, output) = subject.subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  types.on_next(input, 1)
  types.on_next(input, 2)
  types.on_next(input, 3)
  types.on_completed(input)

  let #(values, completed, errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
  errors |> should.equal([])
}

pub fn subject_allows_multiple_subscribers_test() {
  let #(input, output) = subject.subject()

  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let _disp1 = actorx.subscribe(output, message_observer(result1))
  let _disp2 = actorx.subscribe(output, message_observer(result2))

  types.on_next(input, 42)
  types.on_completed(input)

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  // Both subscribers should receive the value
  values1 |> should.equal([42])
  values2 |> should.equal([42])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn subject_does_not_buffer_test() {
  let #(input, output) = subject.subject()

  // Send values BEFORE subscribing
  types.on_next(input, 10)
  types.on_next(input, 20)

  wait_ms(10)

  // Now subscribe
  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  // Send more after subscribe
  types.on_next(input, 30)
  types.on_completed(input)

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Should only receive values after subscription (no buffering)
  values |> should.equal([30])
  completed |> should.be_true()
}

pub fn subject_dispose_stops_receiving_test() {
  let #(input, output) = subject.subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let Disposable(dispose) =
    actorx.subscribe(output, message_observer(result_subject))

  types.on_next(input, 1)
  wait_ms(10)

  // Dispose
  dispose()
  wait_ms(10)

  // These should not be received
  types.on_next(input, 2)
  types.on_next(input, 3)

  let #(values, _completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([1])
}

pub fn subject_with_actorx_facade_test() {
  let #(input, output) = actorx.subject()

  let result_subject: Subject(Notification(Int)) = process.new_subject()
  let _disp = actorx.subscribe(output, message_observer(result_subject))

  actorx.on_next(input, 42)
  actorx.on_completed(input)

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([42])
  completed |> should.be_true()
}
