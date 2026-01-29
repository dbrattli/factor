//// Tests for combining operators (merge, combine_latest, with_latest_from, zip)

import actorx
import actorx/types.{type Notification, Observer, OnCompleted, OnError, OnNext}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should

// ============================================================================
// Test utilities (message-based collection for actor-based operators)
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
// merge tests
// ============================================================================

pub fn merge_empty_list_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable = actorx.merge([])

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn merge_single_source_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable = actorx.merge([actorx.from_list([1, 2, 3])])

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn merge_two_sources_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let obs1 = actorx.from_list([1, 2])
  let obs2 = actorx.from_list([10, 20])
  let observable = actorx.merge([obs1, obs2])

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Order depends on subscription order - both complete synchronously
  values |> should.equal([1, 2, 10, 20])
  completed |> should.be_true()
}

pub fn merge2_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let obs1 = actorx.from_list([1, 2])
  let obs2 = actorx.from_list([10, 20])
  let observable = actorx.merge2(obs1, obs2)

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2, 10, 20])
  completed |> should.be_true()
}

pub fn merge_with_empty_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let obs1 = actorx.from_list([1, 2])
  let obs2 = actorx.empty()
  let observable = actorx.merge([obs1, obs2])

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([1, 2])
  completed |> should.be_true()
}

pub fn merge_async_sources_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Two timers that complete at different times
  let obs1 =
    actorx.timer(20)
    |> actorx.map(fn(_) { 1 })
  let obs2 =
    actorx.timer(10)
    |> actorx.map(fn(_) { 2 })
  let observable = actorx.merge([obs1, obs2])

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 200)

  // obs2 should emit first (10ms) then obs1 (20ms)
  values |> should.equal([2, 1])
  completed |> should.be_true()
}

// ============================================================================
// combine_latest tests
// ============================================================================

pub fn combine_latest_basic_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.from_list([1, 2])
  let obs2 = actorx.from_list(["a", "b"])
  let observable = actorx.combine_latest(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Sync sources: obs1 completes first with [1, 2], then obs2 emits
  // After obs1 emits 1: left=Some(1), right=None -> no emit
  // After obs1 emits 2: left=Some(2), right=None -> no emit
  // After obs2 emits "a": left=Some(2), right=Some("a") -> emit (2, "a")
  // After obs2 emits "b": left=Some(2), right=Some("b") -> emit (2, "b")
  values |> should.equal([#(2, "a"), #(2, "b")])
  completed |> should.be_true()
}

pub fn combine_latest_with_singles_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.single(42)
  let obs2 = actorx.single("hello")
  let observable = actorx.combine_latest(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([#(42, "hello")])
  completed |> should.be_true()
}

pub fn combine_latest_one_empty_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.from_list([1, 2])
  let obs2: actorx.Observable(String) = actorx.empty()
  let observable = actorx.combine_latest(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // One source never emits, so combine_latest never emits
  values |> should.equal([])
  completed |> should.be_true()
}

// ============================================================================
// with_latest_from tests
// ============================================================================

pub fn with_latest_from_basic_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let source = actorx.from_list([1, 2, 3])
  let sampler = actorx.single("x")
  let observable =
    actorx.with_latest_from(source, sampler, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Source emits 1, 2, 3 - sampler emits "x" at some point
  // Since sampler is single and emits immediately, it should have a value
  // But order matters - if source emits before sampler has a value, those are dropped
  // With sync sources: source [1,2,3] completes, then sampler ["x"] emits
  // So source emits 1, 2, 3 before sampler has any value -> all dropped
  values |> should.equal([])
  completed |> should.be_true()
}

pub fn with_latest_from_sampler_first_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  // Use timer to ensure sampler emits first
  let source =
    actorx.timer(30)
    |> actorx.map(fn(_) { 1 })
  let sampler = actorx.single("x")
  let observable =
    actorx.with_latest_from(source, sampler, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 200)

  values |> should.equal([#(1, "x")])
  completed |> should.be_true()
}

pub fn with_latest_from_multiple_samples_test() {
  let result_subject: Subject(Notification(#(Int, Int))) =
    process.new_subject()

  // Source emits after sampler has had time to emit multiple values
  let source =
    actorx.timer(50)
    |> actorx.map(fn(_) { 100 })
  let sampler =
    actorx.from_list([1, 2, 3])
    |> actorx.flat_map(fn(x) {
      actorx.timer(10)
      |> actorx.map(fn(_) { x })
    })
  let observable =
    actorx.with_latest_from(source, sampler, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 200)

  // Source emits at 50ms, sampler emits 1 at 10ms, 2 at 20ms, 3 at 30ms
  // At 50ms, latest sampler value is 3
  values |> should.equal([#(100, 3)])
  completed |> should.be_true()
}

// ============================================================================
// zip tests
// ============================================================================

pub fn zip_basic_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.from_list([1, 2, 3])
  let obs2 = actorx.from_list(["a", "b", "c"])
  let observable = actorx.zip(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([#(1, "a"), #(2, "b"), #(3, "c")])
  completed |> should.be_true()
}

pub fn zip_different_lengths_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.from_list([1, 2, 3, 4, 5])
  let obs2 = actorx.from_list(["a", "b"])
  let observable = actorx.zip(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Completes when shorter source completes
  values |> should.equal([#(1, "a"), #(2, "b")])
  completed |> should.be_true()
}

pub fn zip_one_empty_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.from_list([1, 2, 3])
  let obs2: actorx.Observable(String) = actorx.empty()
  let observable = actorx.zip(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn zip_singles_test() {
  let result_subject: Subject(Notification(#(Int, String))) =
    process.new_subject()

  let obs1 = actorx.single(42)
  let obs2 = actorx.single("hello")
  let observable = actorx.zip(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([#(42, "hello")])
  completed |> should.be_true()
}

pub fn zip_async_sources_test() {
  let result_subject: Subject(Notification(#(Int, Int))) =
    process.new_subject()

  // Two timer-based sources with different delays
  let obs1 =
    actorx.from_list([1, 2, 3])
    |> actorx.flat_map(fn(x) {
      actorx.timer(20 * x)
      |> actorx.map(fn(_) { x })
    })
  let obs2 =
    actorx.from_list([10, 20, 30])
    |> actorx.flat_map(fn(x) {
      actorx.timer(25 * { x / 10 })
      |> actorx.map(fn(_) { x })
    })
  let observable = actorx.zip(obs1, obs2, fn(a, b) { #(a, b) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 300)

  // Pairs by index regardless of timing
  values |> should.equal([#(1, 10), #(2, 20), #(3, 30)])
  completed |> should.be_true()
}
