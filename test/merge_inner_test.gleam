//// Tests for merge_inner and concat_inner operators

import actorx
import actorx/types.{
  type Notification, type Observable, type Observer, Disposable, Observable,
}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleam/option.{None, Some}
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
    |> actorx.merge_inner(None)

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
  let source = empty_outer |> actorx.merge_inner(None)

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn merge_inner_empty_inners_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([actorx.empty(), actorx.empty(), actorx.empty()])
    |> actorx.merge_inner(None)

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn merge_inner_single_inner_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.single(actorx.from_list([1, 2, 3]))
    |> actorx.merge_inner(None)

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
    |> actorx.merge_inner(None)

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
    |> actorx.merge_inner(None)

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
    |> actorx.merge_inner(None)
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

// ============================================================================
// mapi tests
// ============================================================================

pub fn mapi_basic_test() {
  let result: Subject(Notification(#(Int, String))) = process.new_subject()

  let _ =
    actorx.from_list(["a", "b", "c"])
    |> actorx.mapi(fn(x, i) { #(i, x) })
    |> actorx.subscribe(message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([#(0, "a"), #(1, "b"), #(2, "c")])
  completed |> should.be_true()
}

pub fn mapi_empty_test() {
  let result: Subject(Notification(#(Int, Int))) = process.new_subject()

  let _ =
    actorx.empty()
    |> actorx.mapi(fn(x: Int, i) { #(i, x) })
    |> actorx.subscribe(message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn mapi_index_starts_at_zero_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let _ =
    actorx.from_list([100, 200, 300])
    |> actorx.mapi(fn(_x, i) { i })
    |> actorx.subscribe(message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([0, 1, 2])
  completed |> should.be_true()
}

// ============================================================================
// flat_mapi tests
// ============================================================================

pub fn flat_mapi_basic_test() {
  let result: Subject(Notification(#(Int, String))) = process.new_subject()

  let _ =
    actorx.from_list(["a", "b"])
    |> actorx.flat_mapi(fn(x, i) { actorx.from_list([#(i, x), #(i, x <> "!")]) })
    |> actorx.subscribe(message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  // Should have 4 values total
  list.length(values) |> should.equal(4)
  completed |> should.be_true()
}

pub fn flat_mapi_is_mapi_plus_merge_inner_test() {
  let result1: Subject(Notification(#(Int, Int))) = process.new_subject()
  let result2: Subject(Notification(#(Int, Int))) = process.new_subject()

  let source = actorx.from_list([10, 20, 30])
  let mapper = fn(x, i) { actorx.from_list([#(i, x), #(i, x + 1)]) }

  // Using flat_mapi directly
  let _ =
    source
    |> actorx.flat_mapi(mapper)
    |> actorx.subscribe(message_observer(result1))

  // Using mapi + merge_inner (should be equivalent)
  let _ =
    source
    |> actorx.mapi(mapper)
    |> actorx.merge_inner(None)
    |> actorx.subscribe(message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  // Both should have same number of elements
  list.length(values1) |> should.equal(list.length(values2))
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

// ============================================================================
// concat_mapi tests
// ============================================================================

pub fn concat_mapi_basic_test() {
  let result: Subject(Notification(#(Int, String))) = process.new_subject()

  let _ =
    actorx.from_list(["a", "b"])
    |> actorx.concat_mapi(fn(x, i) {
      actorx.from_list([#(i, x), #(i, x <> "!")])
    })
    |> actorx.subscribe(message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  // concat_mapi preserves strict order
  values
  |> should.equal([#(0, "a"), #(0, "a!"), #(1, "b"), #(1, "b!")])
  completed |> should.be_true()
}

pub fn concat_mapi_is_mapi_plus_concat_inner_test() {
  let result1: Subject(Notification(#(Int, Int))) = process.new_subject()
  let result2: Subject(Notification(#(Int, Int))) = process.new_subject()

  let source = actorx.from_list([10, 20])
  let mapper = fn(x, i) { actorx.from_list([#(i, x), #(i, x + 1)]) }

  // Using concat_mapi directly
  let _ =
    source
    |> actorx.concat_mapi(mapper)
    |> actorx.subscribe(message_observer(result1))

  // Using mapi + concat_inner (should be equivalent)
  let _ =
    source
    |> actorx.mapi(mapper)
    |> actorx.concat_inner()
    |> actorx.subscribe(message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  // Both should produce same results in same order
  values1 |> should.equal([#(0, 10), #(0, 11), #(1, 20), #(1, 21)])
  values2 |> should.equal([#(0, 10), #(0, 11), #(1, 20), #(1, 21)])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

// ============================================================================
// max_concurrency tests
// ============================================================================

/// merge_inner with max_concurrency=1 should behave like concat_inner
pub fn merge_inner_max_concurrency_one_equals_concat_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([
      actorx.from_list([1, 2]),
      actorx.from_list([3, 4]),
      actorx.from_list([5, 6]),
    ])

  // Using merge_inner with max_concurrency=1
  let _ =
    source
    |> actorx.merge_inner(Some(1))
    |> actorx.subscribe(message_observer(result1))

  // Using concat_inner
  let _ =
    source
    |> actorx.concat_inner()
    |> actorx.subscribe(message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  // Both should produce same sequential results
  values1 |> should.equal([1, 2, 3, 4, 5, 6])
  values2 |> should.equal([1, 2, 3, 4, 5, 6])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

/// merge_inner with max_concurrency should process all values
pub fn merge_inner_max_concurrency_two_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([
      actorx.from_list([1, 2]),
      actorx.from_list([3, 4]),
      actorx.from_list([5, 6]),
      actorx.from_list([7, 8]),
    ])
    |> actorx.merge_inner(Some(2))

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  // All values should be present
  list.length(values) |> should.equal(8)
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
  |> should.equal([1, 2, 3, 4, 5, 6, 7, 8])
  completed |> should.be_true()
}

/// merge_inner with max_concurrency should handle empty queue correctly
pub fn merge_inner_max_concurrency_empty_inners_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let source =
    actorx.from_list([actorx.empty(), actorx.empty(), actorx.empty()])
    |> actorx.merge_inner(Some(2))

  let _ = actorx.subscribe(source, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

// ============================================================================
// Inner disposable tracking tests
// ============================================================================

/// Message type for tracking disposals
type DisposeMsg {
  Disposed
}

/// Count messages received on a subject within a timeout
fn count_messages(subj: Subject(DisposeMsg), timeout_ms: Int) -> Int {
  count_messages_loop(subj, timeout_ms, 0)
}

fn count_messages_loop(
  subj: Subject(DisposeMsg),
  timeout_ms: Int,
  count: Int,
) -> Int {
  let selector =
    process.new_selector()
    |> process.select(subj)

  case process.selector_receive(selector, timeout_ms) {
    Ok(Disposed) -> count_messages_loop(subj, timeout_ms, count + 1)
    Error(_) -> count
  }
}

/// Helper: create an observable that sends a message when disposed
fn tracked_interval(
  dispose_tracker: Subject(DisposeMsg),
  value: Int,
) -> Observable(Int) {
  Observable(subscribe: fn(observer: Observer(Int)) {
    // Start emitting values via interval
    let inner_source = actorx.interval(50) |> actorx.map(fn(_) { value })
    let Disposable(inner_dispose) = actorx.subscribe(inner_source, observer)

    // Return a disposable that sends a message when called
    Disposable(dispose: fn() {
      process.send(dispose_tracker, Disposed)
      inner_dispose()
      Nil
    })
  })
}

/// merge_inner should dispose all active inner subscriptions when disposed
pub fn merge_inner_dispose_cancels_all_inners_test() {
  let result: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Create inner observables that track disposal
  let inner1 = tracked_interval(dispose_tracker, 1)
  let inner2 = tracked_interval(dispose_tracker, 2)
  let inner3 = tracked_interval(dispose_tracker, 3)

  let source =
    actorx.from_list([inner1, inner2, inner3])
    |> actorx.merge_inner(None)

  let Disposable(dispose) = actorx.subscribe(source, message_observer(result))

  // Wait a bit for subscriptions to be active
  process.sleep(80)

  // Dispose the outer subscription
  dispose()

  // Count how many dispose messages we received
  let dispose_count = count_messages(dispose_tracker, 100)

  // All 3 inner subscriptions should have been disposed
  dispose_count |> should.equal(3)
}

/// merge_inner with max_concurrency should dispose active inners when disposed
pub fn merge_inner_max_concurrency_dispose_test() {
  let result: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Create 4 inner observables, but only 2 can be active at a time
  let inners =
    list.map([1, 2, 3, 4], fn(i) { tracked_interval(dispose_tracker, i) })

  let source =
    actorx.from_list(inners)
    |> actorx.merge_inner(Some(2))

  let Disposable(dispose) = actorx.subscribe(source, message_observer(result))

  // Wait a bit - only 2 should be subscribed due to max_concurrency
  process.sleep(80)

  // Dispose
  dispose()

  // Count dispose messages
  let dispose_count = count_messages(dispose_tracker, 100)

  // Only the 2 active subscriptions should be disposed (not the queued ones)
  dispose_count |> should.equal(2)
}

/// switch_inner should dispose previous inner when switching to new one
pub fn switch_inner_disposes_previous_on_switch_test() {
  let result: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Create a subject to control when inners are emitted
  let #(outer_input, outer_output) = actorx.subject()

  let source = outer_output |> actorx.switch_inner()

  let Disposable(dispose) = actorx.subscribe(source, message_observer(result))

  // Emit first inner
  let inner1 = tracked_interval(dispose_tracker, 1)
  actorx.on_next(outer_input, inner1)
  process.sleep(30)

  // Check dispose count - should still be 0 (inner1 is active)
  count_messages(dispose_tracker, 50) |> should.equal(0)

  // Emit second inner - should dispose first
  let inner2 = tracked_interval(dispose_tracker, 2)
  actorx.on_next(outer_input, inner2)
  process.sleep(30)

  // First inner should now be disposed
  count_messages(dispose_tracker, 50) |> should.equal(1)

  // Emit third inner - should dispose second
  let inner3 = tracked_interval(dispose_tracker, 3)
  actorx.on_next(outer_input, inner3)
  process.sleep(30)

  // Second inner should now be disposed (we should see 1 more message)
  count_messages(dispose_tracker, 50) |> should.equal(1)

  // Dispose outer - should dispose third inner
  dispose()

  // Third inner should now be disposed
  count_messages(dispose_tracker, 50) |> should.equal(1)
}

/// switch_inner should dispose current inner when outer subscription is disposed
pub fn switch_inner_dispose_cancels_current_inner_test() {
  let result: Subject(Notification(Int)) = process.new_subject()
  let dispose_tracker: Subject(DisposeMsg) = process.new_subject()

  // Create a subject to control when inners are emitted
  let #(outer_input, outer_output) = actorx.subject()

  let source = outer_output |> actorx.switch_inner()

  let Disposable(dispose) = actorx.subscribe(source, message_observer(result))

  // Emit an inner
  let inner1 = tracked_interval(dispose_tracker, 1)
  actorx.on_next(outer_input, inner1)
  process.sleep(30)

  // Inner should be active, not disposed yet
  count_messages(dispose_tracker, 50) |> should.equal(0)

  // Dispose outer subscription
  dispose()

  // Inner should now be disposed
  count_messages(dispose_tracker, 50) |> should.equal(1)
}
