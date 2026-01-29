//// Tests for merge_inner and concat_inner operators

import actorx
import actorx/types.{type Notification}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should
import test_utils.{collect_messages, message_observer}

// ============================================================================
// merge_inner tests
// ============================================================================

pub fn merge_inner_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Create observable of observables
  let source =
    actorx.from_list([
      actorx.from_list([1, 2]),
      actorx.from_list([3, 4]),
      actorx.from_list([5, 6]),
    ])
    |> actorx.merge_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  // All values should be present (order may vary due to merging)
  list.length(values) |> should.equal(6)
  list.sort(values, fn(a, b) {
    case a < b {
      True -> order.Lt
      False ->
        case a > b {
          True -> order.Gt
          False -> order.Eq
        }
    }
  })
  |> should.equal([1, 2, 3, 4, 5, 6])
  completed |> should.be_true()
}

import gleam/order

pub fn merge_inner_empty_outer_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Empty outer observable produces empty result
  let empty_outer: actorx.Observable(actorx.Observable(Int)) = actorx.empty()
  let source = empty_outer |> actorx.merge_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn merge_inner_empty_inners_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([actorx.empty(), actorx.empty(), actorx.empty()])
    |> actorx.merge_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn merge_inner_single_inner_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.single(actorx.from_list([1, 2, 3]))
    |> actorx.merge_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn merge_inner_async_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Create async sources using timer
  let source =
    actorx.from_list([
      actorx.timer(10) |> actorx.map(fn(_) { 1 }),
      actorx.timer(20) |> actorx.map(fn(_) { 2 }),
      actorx.timer(30) |> actorx.map(fn(_) { 3 }),
    ])
    |> actorx.merge_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 200)

  // Values should arrive in order of timer completion
  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

pub fn merge_inner_error_propagates_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([
      actorx.from_list([1, 2]),
      actorx.fail("inner error"),
      actorx.from_list([3, 4]),
    ])
    |> actorx.merge_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(_values, completed, errors) = collect_messages(result, 100)

  // Error should propagate
  completed |> should.be_false()
  list.length(errors) |> should.equal(1)
}

// ============================================================================
// concat_inner tests
// ============================================================================

pub fn concat_inner_basic_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([
      actorx.from_list([1, 2]),
      actorx.from_list([3, 4]),
      actorx.from_list([5, 6]),
    ])
    |> actorx.concat_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  // concat_inner preserves order - first inner completes fully before second starts
  values |> should.equal([1, 2, 3, 4, 5, 6])
  completed |> should.be_true()
}

pub fn concat_inner_empty_outer_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Empty outer observable produces empty result
  let empty_outer: actorx.Observable(actorx.Observable(Int)) = actorx.empty()
  let source = empty_outer |> actorx.concat_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn concat_inner_empty_inners_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([actorx.empty(), actorx.empty(), actorx.empty()])
    |> actorx.concat_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn concat_inner_preserves_order_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // Even with different inner lengths, order is preserved
  let source =
    actorx.from_list([
      actorx.from_list([1]),
      actorx.from_list([2, 3, 4]),
      actorx.from_list([5, 6]),
    ])
    |> actorx.concat_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([1, 2, 3, 4, 5, 6])
  completed |> should.be_true()
}

pub fn concat_inner_async_waits_for_previous_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  // First inner takes longer but concat_inner waits for it
  let source =
    actorx.from_list([
      actorx.timer(50) |> actorx.map(fn(_) { 1 }),
      actorx.timer(10) |> actorx.map(fn(_) { 2 }),
    ])
    |> actorx.concat_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 200)

  // Despite second being faster, concat preserves order
  values |> should.equal([1, 2])
  completed |> should.be_true()
}

pub fn concat_inner_error_stops_processing_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([
      actorx.from_list([1, 2]),
      actorx.fail("inner error"),
      actorx.from_list([3, 4]),
    ])
    |> actorx.concat_inner()

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, errors) = collect_messages(result, 100)

  // Should get values from first inner, then error
  values |> should.equal([1, 2])
  completed |> should.be_false()
  errors |> should.equal(["inner error"])
}

// ============================================================================
// flat_map composition verification
// ============================================================================

pub fn flat_map_is_map_plus_merge_inner_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let source = actorx.from_list([1, 2, 3])
  let mapper = fn(x) { actorx.from_list([x, x * 10]) }

  // Using flat_map directly
  let _ =
    source
    |> actorx.flat_map(mapper)
    |> actorx.subscribe(message_observer(result1))

  // Using map + merge_inner (should be equivalent)
  let _ =
    source
    |> actorx.map(mapper)
    |> actorx.merge_inner()
    |> actorx.subscribe(message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  // Both should produce same results
  list.sort(values1, fn(a, b) {
    case a < b {
      True -> order.Lt
      False ->
        case a > b {
          True -> order.Gt
          False -> order.Eq
        }
    }
  })
  |> should.equal(
    list.sort(values2, fn(a, b) {
      case a < b {
        True -> order.Lt
        False ->
          case a > b {
            True -> order.Gt
            False -> order.Eq
          }
      }
    }),
  )
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn concat_map_is_map_plus_concat_inner_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let source = actorx.from_list([1, 2, 3])
  let mapper = fn(x) { actorx.from_list([x, x * 10]) }

  // Using concat_map directly
  let _ =
    source
    |> actorx.concat_map(mapper)
    |> actorx.subscribe(message_observer(result1))

  // Using map + concat_inner (should be equivalent)
  let _ =
    source
    |> actorx.map(mapper)
    |> actorx.concat_inner()
    |> actorx.subscribe(message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  // Both should produce same results in same order
  values1 |> should.equal([1, 10, 2, 20, 3, 30])
  values2 |> should.equal([1, 10, 2, 20, 3, 30])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn concat_map_vs_flat_map_order_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let source = actorx.from_list([1, 2, 3])
  let mapper = fn(x) { actorx.from_list([x, x * 10]) }

  // flat_map may interleave
  let _ =
    source
    |> actorx.flat_map(mapper)
    |> actorx.subscribe(message_observer(result1))

  // concat_map preserves strict order
  let _ =
    source
    |> actorx.concat_map(mapper)
    |> actorx.subscribe(message_observer(result2))

  let #(values1, _, _) = collect_messages(result1, 100)
  let #(values2, _, _) = collect_messages(result2, 100)

  // concat_map always has strict order
  values2 |> should.equal([1, 10, 2, 20, 3, 30])

  // flat_map has same elements but order may differ
  list.length(values1) |> should.equal(6)
}
