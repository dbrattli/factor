//// Tests for creation operators

import actorx
import actorx/types.{OnCompleted, OnError, OnNext}
import gleeunit/should
import test_utils.{
  get_bool_ref, get_list_ref, get_notifications, make_bool_ref, make_list_ref,
  notification_observer, test_observer,
}

// ============================================================================
// single tests
// ============================================================================

pub fn single_emits_value_and_completes_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.single(42)
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([42])
  get_bool_ref(completed) |> should.equal(True)
  get_list_ref(errors) |> should.equal([])
}

pub fn single_notifications_in_order_test() {
  let notifications = make_list_ref()

  let observable = actorx.single(42)
  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications) |> should.equal([OnNext(42), OnCompleted])
}

pub fn single_with_zero_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.single(0)
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([0])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn single_with_negative_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.single(-42)
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([-42])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// empty tests
// ============================================================================

pub fn empty_completes_immediately_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.empty()
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
  get_list_ref(errors) |> should.equal([])
}

pub fn empty_notifications_test() {
  let notifications = make_list_ref()

  let observable = actorx.empty()
  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications) |> should.equal([OnCompleted])
}

// ============================================================================
// never tests
// ============================================================================

pub fn never_does_not_emit_or_complete_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.never()
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(False)
  get_list_ref(errors) |> should.equal([])
}

pub fn never_notifications_test() {
  let notifications = make_list_ref()

  let observable = actorx.never()
  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications) |> should.equal([])
}

// ============================================================================
// fail tests
// ============================================================================

pub fn fail_emits_error_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors: a = make_list_ref()

  let observable = actorx.fail("test error")
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(False)
  let errs: List(String) = get_list_ref(errors)
  errs |> should.equal(["test error"])
}

pub fn fail_notifications_test() {
  let notifications = make_list_ref()

  let observable = actorx.fail("error message")
  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications) |> should.equal([OnError("error message")])
}

pub fn fail_with_empty_message_test() {
  let errors: a = make_list_ref()
  let completed = make_bool_ref(False)
  let results = make_list_ref()

  let observable = actorx.fail("")
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  let errs: List(String) = get_list_ref(errors)
  errs |> should.equal([""])
}

// ============================================================================
// from_list tests
// ============================================================================

pub fn from_list_emits_all_items_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.from_list([1, 2, 3, 4, 5])
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3, 4, 5])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn from_list_empty_completes_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.from_list([])
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn from_list_single_item_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.from_list([42])
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([42])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn from_list_notifications_test() {
  let notifications = make_list_ref()

  let observable = actorx.from_list([1, 2, 3])
  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications)
  |> should.equal([OnNext(1), OnNext(2), OnNext(3), OnCompleted])
}

pub fn from_list_preserves_order_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.from_list([5, 4, 3, 2, 1])
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([5, 4, 3, 2, 1])
}

// ============================================================================
// defer tests
// ============================================================================

pub fn defer_creates_new_observable_per_subscribe_test() {
  let call_count = test_utils.make_int_ref(0)

  let observable =
    actorx.defer(fn() {
      test_utils.incr_int_ref(call_count)
      actorx.single(test_utils.get_int_ref(call_count))
    })

  let results1 = make_list_ref()
  let completed1 = make_bool_ref(False)
  let errors1 = make_list_ref()
  let _ =
    actorx.subscribe(observable, test_observer(results1, completed1, errors1))

  let results2 = make_list_ref()
  let completed2 = make_bool_ref(False)
  let errors2 = make_list_ref()
  let _ =
    actorx.subscribe(observable, test_observer(results2, completed2, errors2))

  // Factory was called twice
  test_utils.get_int_ref(call_count) |> should.equal(2)
  // Each subscription got its own value
  get_list_ref(results1) |> should.equal([1])
  get_list_ref(results2) |> should.equal([2])
}

pub fn defer_is_lazy_test() {
  let was_called = make_bool_ref(False)

  let _observable =
    actorx.defer(fn() {
      test_utils.set_bool_ref(was_called, True)
      actorx.single(42)
    })

  // Factory should not be called until subscribe
  get_bool_ref(was_called) |> should.equal(False)
}

pub fn defer_with_from_list_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = actorx.defer(fn() { actorx.from_list([10, 20, 30]) })
  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([10, 20, 30])
  get_bool_ref(completed) |> should.equal(True)
}
