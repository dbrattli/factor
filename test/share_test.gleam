//// Tests for share and publish operators

import actorx
import actorx/types.{type Notification, Disposable}
import gleam/erlang/process.{type Subject}
import gleeunit/should
import test_utils.{collect_messages, message_observer, wait_ms}

// ============================================================================
// publish tests
// ============================================================================

pub fn publish_no_emissions_before_connect_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let #(hot, connect) = actorx.publish(actorx.from_list([1, 2, 3]))

  // Subscribe before connecting
  let _ = actorx.subscribe(hot, message_observer(result1))
  let _ = actorx.subscribe(hot, message_observer(result2))

  // Nothing received yet - not connected
  wait_ms(50)
  let #(v1, _, _) = collect_messages(result1, 10)
  let #(v2, _, _) = collect_messages(result2, 10)

  v1 |> should.equal([])
  v2 |> should.equal([])

  // Now connect
  let _ = connect()

  // Both receive all values
  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  values1 |> should.equal([1, 2, 3])
  values2 |> should.equal([1, 2, 3])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn publish_multiple_subscribers_share_source_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  // Use interval to verify shared subscription
  let #(hot, connect) =
    actorx.publish(
      actorx.interval(30)
      |> actorx.take(3),
    )

  // Subscribe two observers
  let _ = actorx.subscribe(hot, message_observer(result1))
  let _ = actorx.subscribe(hot, message_observer(result2))

  // Connect
  let _ = connect()

  // Both should receive same values
  let #(values1, completed1, _) = collect_messages(result1, 200)
  let #(values2, completed2, _) = collect_messages(result2, 200)

  values1 |> should.equal([0, 1, 2])
  values2 |> should.equal([0, 1, 2])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn publish_late_subscriber_misses_earlier_values_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let #(hot, connect) =
    actorx.publish(
      actorx.interval(30)
      |> actorx.take(4),
    )

  // First subscriber before connect
  let _ = actorx.subscribe(hot, message_observer(result1))

  // Connect
  let _ = connect()

  // Wait for a couple values
  wait_ms(70)

  // Second subscriber joins late
  let _ = actorx.subscribe(hot, message_observer(result2))

  // Collect results
  let #(values1, _, _) = collect_messages(result1, 150)
  let #(values2, _, _) = collect_messages(result2, 150)

  // First subscriber gets all values
  values1 |> should.equal([0, 1, 2, 3])

  // Second subscriber misses early values (0, 1 already emitted)
  // Should get remaining values (2, 3) or subset thereof
  should.be_true(
    values2 == [2, 3]
    || values2 == [3]
    || values2 == [1, 2, 3]
    || values2 == [2],
  )
}

pub fn publish_connect_returns_disposable_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let #(hot, connect) =
    actorx.publish(
      actorx.interval(30)
      |> actorx.take(10),
    )

  let _ = actorx.subscribe(hot, message_observer(result))

  // Connect
  let connection = connect()

  // Wait for some values
  wait_ms(80)

  // Disconnect
  let Disposable(dispose) = connection
  dispose()

  // Wait a bit more
  wait_ms(80)

  // Should have stopped receiving after disconnect
  let #(values, completed, _) = collect_messages(result, 50)

  // Should have some values but not complete (disconnected early)
  should.be_true(list.length(values) < 5)
  completed |> should.be_false()
}

import gleam/list

pub fn publish_connect_idempotent_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let #(hot, connect) = actorx.publish(actorx.from_list([1, 2, 3]))

  let _ = actorx.subscribe(hot, message_observer(result))

  // Connect twice - should return same connection
  let conn1 = connect()
  let conn2 = connect()

  // Both should be the same disposable (or at least both work)
  let Disposable(_) = conn1
  let Disposable(_) = conn2

  let #(values, completed, _) = collect_messages(result, 100)

  // Values only emitted once
  values |> should.equal([1, 2, 3])
  completed |> should.be_true()
}

// ============================================================================
// share tests
// ============================================================================

pub fn share_connects_on_first_subscriber_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.interval(30)
    |> actorx.take(3)
    |> actorx.share()

  // Subscribe - should auto-connect
  let _ = actorx.subscribe(shared, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 200)

  values |> should.equal([0, 1, 2])
  completed |> should.be_true()
}

pub fn share_multiple_subscribers_share_source_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.interval(30)
    |> actorx.take(3)
    |> actorx.share()

  // Both subscribe - share same source
  let _ = actorx.subscribe(shared, message_observer(result1))
  let _ = actorx.subscribe(shared, message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 200)
  let #(values2, completed2, _) = collect_messages(result2, 200)

  values1 |> should.equal([0, 1, 2])
  values2 |> should.equal([0, 1, 2])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn share_disconnects_on_last_unsubscribe_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.interval(30)
    |> actorx.take(10)
    |> actorx.share()

  // Two subscribers
  let d1 = actorx.subscribe(shared, message_observer(result1))
  let d2 = actorx.subscribe(shared, message_observer(result2))

  // Wait for some values
  wait_ms(80)

  // Unsubscribe first - source should continue
  let Disposable(dispose1) = d1
  dispose1()

  wait_ms(50)

  // Unsubscribe last - source should stop
  let Disposable(dispose2) = d2
  dispose2()

  // Wait and check no more values after unsubscribe
  wait_ms(80)

  let #(values1, _, _) = collect_messages(result1, 50)
  let #(values2, _, _) = collect_messages(result2, 50)

  // Both got some values, but not the full 10
  let len1 = list.length(values1)
  let len2 = list.length(values2)
  should.be_true(len1 > 0 && len1 < 10)
  should.be_true(len2 > 0 && len2 < 10)
}

pub fn share_resubscribe_reconnects_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.from_list([1, 2, 3])
    |> actorx.share()

  // First subscription - connects and gets values
  let d1 = actorx.subscribe(shared, message_observer(result1))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  values1 |> should.equal([1, 2, 3])
  completed1 |> should.be_true()

  // Unsubscribe
  let Disposable(dispose1) = d1
  dispose1()

  // Second subscription - should reconnect to source
  let _ = actorx.subscribe(shared, message_observer(result2))

  let #(values2, completed2, _) = collect_messages(result2, 100)
  values2 |> should.equal([1, 2, 3])
  completed2 |> should.be_true()
}

pub fn share_with_sync_source_test() {
  let result1: Subject(Notification(Int)) = process.new_subject()
  let result2: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.from_list([1, 2, 3, 4, 5])
    |> actorx.share()

  let _ = actorx.subscribe(shared, message_observer(result1))
  let _ = actorx.subscribe(shared, message_observer(result2))

  let #(values1, completed1, _) = collect_messages(result1, 100)
  let #(values2, completed2, _) = collect_messages(result2, 100)

  values1 |> should.equal([1, 2, 3, 4, 5])
  values2 |> should.equal([1, 2, 3, 4, 5])
  completed1 |> should.be_true()
  completed2 |> should.be_true()
}

pub fn share_with_map_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.from_list([1, 2, 3])
    |> actorx.map(fn(x) { x * 10 })
    |> actorx.share()

  let _ = actorx.subscribe(shared, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([10, 20, 30])
  completed |> should.be_true()
}

pub fn share_empty_source_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.empty()
    |> actorx.share()

  let _ = actorx.subscribe(shared, message_observer(result))

  let #(values, completed, _) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_true()
}

pub fn share_error_propagates_test() {
  let result: Subject(Notification(Int)) = process.new_subject()

  let shared =
    actorx.fail("Test error")
    |> actorx.share()

  let _ = actorx.subscribe(shared, message_observer(result))

  let #(values, completed, errors) = collect_messages(result, 100)

  values |> should.equal([])
  completed |> should.be_false()
  errors |> should.equal(["Test error"])
}
