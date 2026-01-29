//// Tests for filter operators

import actorx
import actorx/types.{OnCompleted, OnNext}
import gleam/option.{None, Some}
import gleeunit/should
import test_utils.{
  get_bool_ref, get_list_ref, get_notifications, make_bool_ref, make_list_ref,
  notification_observer, test_observer,
}

// ============================================================================
// filter tests
// ============================================================================

pub fn filter_keeps_matching_elements_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6])
    |> actorx.filter(fn(x) { x > 3 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([4, 5, 6])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn filter_all_pass_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.filter(fn(_) { True })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn filter_none_pass_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.filter(fn(_) { False })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn filter_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.filter(fn(x) { x > 0 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn filter_even_numbers_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    |> actorx.filter(fn(x) { x % 2 == 0 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([2, 4, 6, 8, 10])
}

pub fn filter_chained_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    |> actorx.filter(fn(x) { x > 3 })
    |> actorx.filter(fn(x) { x < 8 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([4, 5, 6, 7])
}

// ============================================================================
// take tests
// ============================================================================

pub fn take_first_n_elements_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take(3)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_zero_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take(0)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_more_than_available_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take(10)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_exact_count_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take(3)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_from_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.take(5)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_one_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take(1)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_notifications_test() {
  let notifications = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take(2)

  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications)
  |> should.equal([OnNext(1), OnNext(2), OnCompleted])
}

// ============================================================================
// skip tests
// ============================================================================

pub fn skip_first_n_elements_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.skip(2)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([3, 4, 5])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_zero_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.skip(0)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn skip_more_than_available_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.skip(10)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_exact_count_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.skip(3)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_from_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.skip(5)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_all_but_one_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.skip(4)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([5])
}

// ============================================================================
// take_while tests
// ============================================================================

pub fn take_while_condition_true_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take_while(fn(x) { x < 4 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_while_always_true_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take_while(fn(_) { True })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn take_while_always_false_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take_while(fn(_) { False })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_while_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.take_while(fn(x) { x > 0 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_while_first_fails_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([5, 4, 3, 2, 1])
    |> actorx.take_while(fn(x) { x < 5 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// skip_while tests
// ============================================================================

pub fn skip_while_condition_true_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.skip_while(fn(x) { x < 3 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([3, 4, 5])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_while_always_true_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.skip_while(fn(_) { True })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_while_always_false_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.skip_while(fn(_) { False })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn skip_while_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.skip_while(fn(x) { x > 0 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_while_first_fails_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([5, 4, 3, 2, 1])
    |> actorx.skip_while(fn(x) { x < 5 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  // First element (5) doesn't satisfy < 5, so we start emitting immediately
  get_list_ref(results) |> should.equal([5, 4, 3, 2, 1])
}

// ============================================================================
// distinct_until_changed tests
// ============================================================================

pub fn distinct_until_changed_removes_consecutive_dupes_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 1, 2, 2, 2, 3, 1, 1])
    |> actorx.distinct_until_changed()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3, 1])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn distinct_until_changed_all_different_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.distinct_until_changed()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3, 4, 5])
}

pub fn distinct_until_changed_all_same_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([2, 2, 2, 2, 2])
    |> actorx.distinct_until_changed()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([2])
}

pub fn distinct_until_changed_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.distinct_until_changed()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn distinct_until_changed_single_value_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.single(42)
    |> actorx.distinct_until_changed()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([42])
}

pub fn distinct_until_changed_alternating_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 1, 2, 1, 2])
    |> actorx.distinct_until_changed()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  // All values are distinct from their predecessor
  get_list_ref(results) |> should.equal([1, 2, 1, 2, 1, 2])
}

// ============================================================================
// choose tests
// ============================================================================

pub fn choose_filters_and_maps_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.choose(fn(x) {
      case x % 2 == 0 {
        True -> Some(x * 10)
        False -> None
      }
    })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([20, 40])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn choose_all_some_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.choose(fn(x) { Some(x * 100) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([100, 200, 300])
}

pub fn choose_all_none_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.choose(fn(_) { None })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn choose_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.choose(fn(x) { Some(x) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// take_last tests
// ============================================================================

pub fn take_last_returns_last_n_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take_last(2)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([4, 5])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_last_zero_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take_last(0)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_last_more_than_available_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take_last(10)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_last_exact_count_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.take_last(3)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_last_from_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.take_last(5)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn take_last_one_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take_last(1)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([5])
}

// ============================================================================
// Combined operator tests
// ============================================================================

pub fn map_and_filter_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.map(fn(x) { x * 2 })
    |> actorx.filter(fn(x) { x > 4 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([6, 8, 10])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn filter_map_take_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    |> actorx.filter(fn(x) { x % 2 == 0 })
    |> actorx.map(fn(x) { x * 10 })
    |> actorx.take(3)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([20, 40, 60])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn skip_then_take_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    |> actorx.skip(3)
    |> actorx.take(4)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([4, 5, 6, 7])
}

pub fn take_while_then_map_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.take_while(fn(x) { x < 4 })
    |> actorx.map(fn(x) { x * 10 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([10, 20, 30])
}

pub fn distinct_then_take_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 1, 2, 2, 3, 3, 4, 4, 5, 5])
    |> actorx.distinct_until_changed()
    |> actorx.take(3)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}
