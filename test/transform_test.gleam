//// Tests for transform operators (map, flat_map, concat_map)

import actorx
import actorx/types.{type Notification, Observer, OnCompleted, OnError, OnNext}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should
import test_utils.{
  get_bool_ref, get_list_ref, get_notifications, make_bool_ref, make_list_ref,
  notification_observer, test_observer,
}

// ============================================================================
// map tests
// ============================================================================

pub fn map_transforms_values_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.map(fn(x) { x * 10 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([10, 20, 30])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn map_chained_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.map(fn(x) { x * 10 })
    |> actorx.map(fn(x) { x + 1 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([11, 21, 31])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn map_identity_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.map(fn(x) { x })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn map_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.empty()
    |> actorx.map(fn(x) { x * 10 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn map_single_value_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.single(42)
    |> actorx.map(fn(x) { x * 10 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([420])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn map_notifications_test() {
  let notifications = make_list_ref()

  let observable =
    actorx.from_list([1, 2])
    |> actorx.map(fn(x) { x * 10 })

  let _ = actorx.subscribe(observable, notification_observer(notifications))

  get_notifications(notifications)
  |> should.equal([OnNext(10), OnNext(20), OnCompleted])
}

pub fn map_constant_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.map(fn(_) { 99 })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([99, 99, 99])
}

// ============================================================================
// flat_map tests
// ============================================================================

pub fn flat_map_flattens_observables_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.flat_map(fn(x) { actorx.single(x * 10) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([10, 20, 30])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn flat_map_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable: actorx.Observable(Int) =
    actorx.empty()
    |> actorx.flat_map(fn(x) { actorx.single(x) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn flat_map_to_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.flat_map(fn(_) { actorx.empty() })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn flat_map_expands_to_multiple_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2])
    |> actorx.flat_map(fn(x) { actorx.from_list([x, x * 10]) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  // Each element expands to [x, x*10]
  get_list_ref(results) |> should.equal([1, 10, 2, 20])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn flat_map_cartesian_product_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // [1, 2] x [10, 20] = [11, 21, 12, 22]
  let observable =
    actorx.from_list([1, 2])
    |> actorx.flat_map(fn(x) {
      actorx.from_list([10, 20])
      |> actorx.map(fn(y) { x + y })
    })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([11, 21, 12, 22])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// flat_map monad laws tests
// ============================================================================

/// Left identity: return x >>= f  ===  f x
pub fn flat_map_monad_law_left_identity_test() {
  let f = fn(x) { actorx.single(x * 10) }

  // return x >>= f
  let results1 = make_list_ref()
  let completed1 = make_bool_ref(False)
  let errors1 = make_list_ref()
  let observable1 =
    actorx.single(42)
    |> actorx.flat_map(f)
  let _ =
    actorx.subscribe(observable1, test_observer(results1, completed1, errors1))

  // f x
  let results2 = make_list_ref()
  let completed2 = make_bool_ref(False)
  let errors2 = make_list_ref()
  let observable2 = f(42)
  let _ =
    actorx.subscribe(observable2, test_observer(results2, completed2, errors2))

  get_list_ref(results1) |> should.equal(get_list_ref(results2))
  get_list_ref(results1) |> should.equal([420])
}

/// Right identity: m >>= return  ===  m
pub fn flat_map_monad_law_right_identity_test() {
  let m = actorx.single(42)

  // m
  let results1 = make_list_ref()
  let completed1 = make_bool_ref(False)
  let errors1 = make_list_ref()
  let _ = actorx.subscribe(m, test_observer(results1, completed1, errors1))

  // m >>= return
  let results2 = make_list_ref()
  let completed2 = make_bool_ref(False)
  let errors2 = make_list_ref()
  let observable2 =
    m
    |> actorx.flat_map(actorx.single)
  let _ =
    actorx.subscribe(observable2, test_observer(results2, completed2, errors2))

  get_list_ref(results1) |> should.equal(get_list_ref(results2))
  get_list_ref(results1) |> should.equal([42])
}

/// Associativity: (m >>= f) >>= g  ===  m >>= (\x -> f x >>= g)
pub fn flat_map_monad_law_associativity_test() {
  let m = actorx.single(42)
  let f = fn(x) { actorx.single(x * 1000) }
  let g = fn(x) { actorx.single(x * 42) }

  // (m >>= f) >>= g
  let results1 = make_list_ref()
  let completed1 = make_bool_ref(False)
  let errors1 = make_list_ref()
  let observable1 =
    m
    |> actorx.flat_map(f)
    |> actorx.flat_map(g)
  let _ =
    actorx.subscribe(observable1, test_observer(results1, completed1, errors1))

  // m >>= (\x -> f x >>= g)
  let results2 = make_list_ref()
  let completed2 = make_bool_ref(False)
  let errors2 = make_list_ref()
  let observable2 =
    m
    |> actorx.flat_map(fn(x) { f(x) |> actorx.flat_map(g) })
  let _ =
    actorx.subscribe(observable2, test_observer(results2, completed2, errors2))

  get_list_ref(results1) |> should.equal(get_list_ref(results2))
  get_list_ref(results1) |> should.equal([1_764_000])
}

// ============================================================================
// concat_map tests
// ============================================================================

pub fn concat_map_preserves_order_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.concat_map(fn(x) { actorx.from_list([x, x * 10]) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  // Sequential: [1, 10], then [2, 20], then [3, 30]
  get_list_ref(results) |> should.equal([1, 10, 2, 20, 3, 30])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn concat_map_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable: actorx.Observable(Int) =
    actorx.empty()
    |> actorx.concat_map(fn(x) { actorx.single(x) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn concat_map_to_single_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.concat_map(fn(x) { actorx.single(x * 100) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([100, 200, 300])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// flat_map_async tests (use message-based collection for async)
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

pub fn flat_map_async_flattens_observables_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.flat_map_async(fn(x) { actorx.single(x * 10) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([10, 20, 30])
  completed |> should.be_true()
}

pub fn flat_map_async_empty_source_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable: actorx.Observable(Int) =
    actorx.empty()
    |> actorx.flat_map_async(fn(x) { actorx.single(x) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn flat_map_async_expands_to_multiple_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  let observable =
    actorx.from_list([1, 2])
    |> actorx.flat_map_async(fn(x) { actorx.from_list([x, x * 10]) })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Each element expands to [x, x*10]
  values |> should.equal([1, 10, 2, 20])
  completed |> should.be_true()
}

pub fn flat_map_async_waits_for_all_inners_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Source completes immediately but inners have delays
  let observable =
    actorx.from_list([1, 2])
    |> actorx.flat_map_async(fn(x) {
      actorx.timer(30)
      |> actorx.map(fn(_) { x * 10 })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  // Collect - should wait for timers
  let #(values, completed, _errors) = collect_messages(result_subject, 200)

  values |> should.equal([10, 20])
  completed |> should.be_true()
}
