//// Tests for group_by operator

import actorx
import actorx/types.{type Notification}
import gleam/dict
import gleam/erlang/process.{type Subject}
import gleam/list
import gleeunit/should
import test_utils.{collect_messages, message_observer}

// ============================================================================
// group_by tests
// ============================================================================

pub fn group_by_basic_test() {
  let result_subject: Subject(Notification(#(Int, List(Int)))) =
    process.new_subject()

  // Group numbers by even (0) / odd (1)
  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6])
    |> actorx.group_by(fn(x) { x % 2 })
    |> actorx.flat_map(fn(group) {
      let #(key, values) = group
      // Collect all values from this group
      values
      |> actorx.reduce([], fn(acc, v) { list.append(acc, [v]) })
      |> actorx.map(fn(collected) { #(key, collected) })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 200)

  // Should have two groups: even and odd
  // Order depends on which group completes first
  let sorted =
    list.sort(values, fn(a, b) {
      let #(k1, _) = a
      let #(k2, _) = b
      case k1 < k2 {
        True -> order.Lt
        False ->
          case k1 > k2 {
            True -> order.Gt
            False -> order.Eq
          }
      }
    })

  sorted
  |> should.equal([#(0, [2, 4, 6]), #(1, [1, 3, 5])])
  completed |> should.be_true()
}

import gleam/order

pub fn group_by_single_group_test() {
  let result_subject: Subject(Notification(#(String, Int))) =
    process.new_subject()

  // All elements have same key
  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.group_by(fn(_) { "all" })
    |> actorx.flat_map(fn(group) {
      let #(key, values) = group
      values |> actorx.map(fn(v) { #(key, v) })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([#("all", 1), #("all", 2), #("all", 3)])
  completed |> should.be_true()
}

pub fn group_by_each_unique_key_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Each element is its own group
  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.group_by(fn(x) { x })
    |> actorx.flat_map(fn(group) {
      let #(_key, values) = group
      values
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // Should get all values (order may vary due to async)
  list.length(values) |> should.equal(3)
  completed |> should.be_true()
}

pub fn group_by_empty_source_test() {
  let result_subject: Subject(Notification(#(Int, actorx.Observable(Int)))) =
    process.new_subject()

  let observable =
    actorx.empty()
    |> actorx.group_by(fn(x: Int) { x % 2 })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn group_by_count_groups_test() {
  let result_subject: Subject(Notification(Int)) = process.new_subject()

  // Count how many groups are created
  let observable =
    actorx.from_list([1, 2, 3, 4, 5, 6, 7, 8])
    |> actorx.group_by(fn(x) { x % 3 })
    |> actorx.reduce(0, fn(count, _group) { count + 1 })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  // 3 groups: 0, 1, 2
  values |> should.equal([3])
  completed |> should.be_true()
}

pub fn group_by_with_strings_test() {
  let result_subject: Subject(Notification(#(String, List(String)))) =
    process.new_subject()

  // Group strings by first letter
  let observable =
    actorx.from_list(["apple", "banana", "avocado", "blueberry", "cherry"])
    |> actorx.group_by(fn(s) {
      case s {
        "a" <> _ -> "a"
        "b" <> _ -> "b"
        "c" <> _ -> "c"
        _ -> "other"
      }
    })
    |> actorx.flat_map(fn(group) {
      let #(key, values) = group
      values
      |> actorx.reduce([], fn(acc, v) { list.append(acc, [v]) })
      |> actorx.map(fn(collected) { #(key, collected) })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 200)

  // Convert to dict for easier checking (order may vary)
  let groups =
    list.fold(values, dict.new(), fn(d, pair) {
      let #(k, v) = pair
      dict.insert(d, k, v)
    })

  dict.get(groups, "a") |> should.equal(Ok(["apple", "avocado"]))
  dict.get(groups, "b") |> should.equal(Ok(["banana", "blueberry"]))
  dict.get(groups, "c") |> should.equal(Ok(["cherry"]))
  completed |> should.be_true()
}

pub fn group_by_preserves_order_within_group_test() {
  let result_subject: Subject(Notification(List(Int))) = process.new_subject()

  // Group even numbers and verify order is preserved
  let observable =
    actorx.from_list([2, 4, 6, 8, 10])
    |> actorx.group_by(fn(_) { "evens" })
    |> actorx.flat_map(fn(group) {
      let #(_key, values) = group
      values |> actorx.reduce([], fn(acc, v) { list.append(acc, [v]) })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 100)

  values |> should.equal([[2, 4, 6, 8, 10]])
  completed |> should.be_true()
}

pub fn group_by_with_timer_test() {
  let result_subject: Subject(Notification(#(Int, Int))) = process.new_subject()

  // Group interval values by mod 2
  let observable =
    actorx.interval(20)
    |> actorx.take(6)
    |> actorx.group_by(fn(x) { x % 2 })
    |> actorx.flat_map(fn(group) {
      let #(key, values) = group
      values |> actorx.map(fn(v) { #(key, v) })
    })

  let _ = actorx.subscribe(observable, message_observer(result_subject))

  let #(values, completed, _errors) = collect_messages(result_subject, 300)

  // Should have 6 values total
  list.length(values) |> should.equal(6)
  completed |> should.be_true()
}
