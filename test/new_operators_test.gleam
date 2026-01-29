//// Tests for new operators: switch_map, tap, start_with, first, last, etc.

import actorx
import actorx/types.{type Notification}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should
import test_utils.{collect_messages, message_observer}

// ============================================================================
// switch_inner / switch_map tests
// ============================================================================

pub fn switch_inner_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Create observable of observables, switch should only emit from latest
  // With sync sources, each inner completes before the next arrives
  // so all values come through
  let source =
    actorx.from_list([
      actorx.from_list([1, 2, 3]),
      actorx.from_list([4, 5, 6]),
    ])
    |> actorx.switch_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  // With sync sources, each inner completes before next arrives
  // So we get values from both (but only last inner's values count
  // if they arrive after the switch)
  should.be_true(list.length(values) >= 3)
  completed |> should.be_true()
}

pub fn switch_map_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.switch_map(fn(x) { actorx.from_list([x, x * 10]) })

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  // With sync sources, each inner completes synchronously
  should.be_true(list.length(values) >= 2)
  completed |> should.be_true()
}

pub fn switch_map_async_cancels_previous_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // With async, later inners should cancel earlier ones
  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.switch_map(fn(x) {
      actorx.timer(x * 30) |> actorx.map(fn(_) { x })
    })

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 200)

  // Only the last one should complete (others cancelled)
  values |> should.equal([3])
  completed |> should.be_true()
}

pub fn switch_inner_empty_outer_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let empty_outer: actorx.Observable(actorx.Observable(Int)) = actorx.empty()
  let source = empty_outer |> actorx.switch_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

// ============================================================================
// tap tests
// ============================================================================

pub fn tap_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()
  let side_effect: Subject(Int) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.tap(fn(x) { process.send(side_effect, x) })

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  // Values should pass through unchanged
  values |> should.equal([1, 2, 3])
  completed |> should.be_true()

  // Side effects should have occurred
  let se1 = process.receive(side_effect, 10)
  let se2 = process.receive(side_effect, 10)
  let se3 = process.receive(side_effect, 10)
  se1 |> should.equal(Ok(1))
  se2 |> should.equal(Ok(2))
  se3 |> should.equal(Ok(3))
}

pub fn tap_does_not_modify_values_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([10, 20, 30])
    |> actorx.tap(fn(_x) { Nil })
    |> actorx.map(fn(x) { x * 2 })

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([20, 40, 60])
  completed |> should.be_true()
}

// ============================================================================
// start_with tests
// ============================================================================

pub fn start_with_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([3, 4, 5])
    |> actorx.start_with([1, 2])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3, 4, 5])
  completed |> should.be_true()
}

pub fn start_with_empty_prefix_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.start_with([])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn start_with_empty_source_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.empty()
    |> actorx.start_with([1, 2])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2])
  completed |> should.be_true()
}

// ============================================================================
// pairwise tests
// ============================================================================

pub fn pairwise_basic_test() {
  let result: Subject(Notification(#(Int, Int))) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3, 4])
    |> actorx.pairwise()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([#(1, 2), #(2, 3), #(3, 4)])
  completed |> should.be_true()
}

pub fn pairwise_single_element_test() {
  let result: Subject(Notification(#(Int, Int))) = process.new_subject()

  let source =
    actorx.single(1)
    |> actorx.pairwise()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn pairwise_empty_test() {
  let result: Subject(Notification(#(Int, Int))) = process.new_subject()

  let source =
    actorx.empty()
    |> actorx.pairwise()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

// ============================================================================
// first tests
// ============================================================================

pub fn first_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.first()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1])
  completed |> should.be_true()
}

pub fn first_single_element_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.single(42)
    |> actorx.first()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([42])
  completed |> should.be_true()
}

pub fn first_empty_errors_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.empty()
    |> actorx.first()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_false()
  list.length(errors) |> should.equal(1)
}

// ============================================================================
// last tests
// ============================================================================

pub fn last_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.last()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([3])
  completed |> should.be_true()
}

pub fn last_single_element_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.single(42)
    |> actorx.last()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([42])
  completed |> should.be_true()
}

pub fn last_empty_errors_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.empty()
    |> actorx.last()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_false()
  list.length(errors) |> should.equal(1)
}

// ============================================================================
// default_if_empty tests
// ============================================================================

pub fn default_if_empty_with_values_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([1, 2, 3])
    |> actorx.default_if_empty(99)

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn default_if_empty_empty_source_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.empty()
    |> actorx.default_if_empty(42)

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([42])
  completed |> should.be_true()
}

// ============================================================================
// sample tests
// ============================================================================

pub fn sample_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Sample interval values with a slower interval
  let source =
    actorx.interval(20)
    |> actorx.take(10)
    |> actorx.sample(actorx.interval(50) |> actorx.take(3))

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 300)

  // Should have some sampled values
  should.be_true(values != [])
  should.be_true(list.length(values) <= 3)
  completed |> should.be_true()
}

// ============================================================================
// concat tests
// ============================================================================

pub fn concat_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.concat([
      actorx.from_list([1, 2]),
      actorx.from_list([3, 4]),
      actorx.from_list([5, 6]),
    ])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3, 4, 5, 6])
  completed |> should.be_true()
}

pub fn concat2_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.concat2(actorx.from_list([1, 2]), actorx.from_list([3, 4]))

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3, 4])
  completed |> should.be_true()
}

pub fn concat_empty_list_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source = actorx.concat([])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn concat_with_empty_sources_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.concat([
      actorx.empty(),
      actorx.from_list([1, 2]),
      actorx.empty(),
      actorx.from_list([3]),
    ])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn concat_sequential_async_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Second source should wait for first to complete
  let source =
    actorx.concat([
      actorx.timer(50) |> actorx.map(fn(_) { 1 }),
      actorx.timer(10) |> actorx.map(fn(_) { 2 }),
    ])

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 200)

  // Despite second being faster, order is preserved
  values |> should.equal([1, 2])
  completed |> should.be_true()
}
