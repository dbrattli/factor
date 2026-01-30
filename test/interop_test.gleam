//// Tests for actor interop operators

import actorx
import actorx/types.{Disposable}
import gleam/erlang/process.{type Subject}
import gleeunit/should
import test_utils.{collect_messages, make_test_subject, wait_ms}

// ============================================================================
// from_subject tests
// ============================================================================

pub fn from_subject_emits_values_test() {
  // Create a Subject/Observable pair
  let #(source, observable) = actorx.from_subject()

  // Subscribe
  let #(subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  // Give time for subscription to register
  wait_ms(10)

  // Send values to the source Subject
  process.send(source, 1)
  process.send(source, 2)
  process.send(source, 3)

  // Collect with short timeout (no completion expected)
  let #(values, completed, errors) = collect_messages(subj, 100)

  values |> should.equal([1, 2, 3])
  completed |> should.equal(False)
  errors |> should.equal([])
}

pub fn from_subject_multiple_subscribers_test() {
  let #(source, observable) = actorx.from_subject()

  // Two subscribers
  let #(subj1, observer1) = make_test_subject()
  let #(subj2, observer2) = make_test_subject()
  let _disp1 = actorx.subscribe(observable, observer1)
  let _disp2 = actorx.subscribe(observable, observer2)

  wait_ms(10)

  // Send values
  process.send(source, 42)
  process.send(source, 99)

  // Both should receive same values
  let #(values1, _, _) = collect_messages(subj1, 100)
  let #(values2, _, _) = collect_messages(subj2, 100)

  values1 |> should.equal([42, 99])
  values2 |> should.equal([42, 99])
}

pub fn from_subject_disposal_stops_subscription_test() {
  let #(source, observable) = actorx.from_subject()

  let #(subj, observer) = make_test_subject()
  let disp = actorx.subscribe(observable, observer)

  wait_ms(10)

  // Send first value
  process.send(source, 1)
  wait_ms(10)

  // Dispose
  let Disposable(dispose) = disp
  dispose()
  wait_ms(10)

  // Send more values after disposal
  process.send(source, 2)
  process.send(source, 3)

  // Should only have received first value
  let #(values, _, _) = collect_messages(subj, 100)
  values |> should.equal([1])
}

pub fn from_subject_with_take_until_test() {
  let #(source, observable) = actorx.from_subject()
  let #(stopper, stopper_observable) = actorx.from_subject()

  let combined =
    observable
    |> actorx.take_until(stopper_observable)

  let #(subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(combined, observer)

  wait_ms(10)

  process.send(source, 1)
  process.send(source, 2)
  wait_ms(10)

  // Send stop signal
  process.send(stopper, Nil)
  wait_ms(10)

  // These should not be received
  process.send(source, 3)
  process.send(source, 4)

  let #(values, completed, _) = collect_messages(subj, 100)
  values |> should.equal([1, 2])
  completed |> should.equal(True)
}

// ============================================================================
// to_subject tests
// ============================================================================

pub fn to_subject_sends_to_subject_and_downstream_test() {
  let target: Subject(Int) = process.new_subject()

  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.to_subject(target)

  // Subscribe to the observable
  let #(downstream_subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  // Collect from downstream
  let #(downstream_values, completed, _) =
    collect_messages(downstream_subj, 100)

  // Collect from the target Subject
  let target_values = collect_subject_values(target, 3, 100)

  // Both should have received the values
  downstream_values |> should.equal([1, 2, 3])
  target_values |> should.equal([1, 2, 3])
  completed |> should.equal(True)
}

pub fn to_subject_does_not_send_terminal_to_subject_test() {
  let target: Subject(Int) = process.new_subject()

  // Observable that completes
  let observable =
    actorx.from_list([1, 2])
    |> actorx.to_subject(target)

  let #(downstream_subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  let #(_, completed, _) = collect_messages(downstream_subj, 100)

  // Downstream should complete
  completed |> should.equal(True)

  // Target Subject should only have received OnNext values (not completion)
  let target_values = collect_subject_values(target, 2, 100)
  target_values |> should.equal([1, 2])
}

pub fn to_subject_chaining_test() {
  let target1: Subject(Int) = process.new_subject()
  let target2: Subject(Int) = process.new_subject()

  let observable =
    actorx.from_list([10, 20, 30])
    |> actorx.to_subject(target1)
    |> actorx.map(fn(x) { x * 2 })
    |> actorx.to_subject(target2)

  let #(downstream_subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  let #(downstream_values, _, _) = collect_messages(downstream_subj, 100)

  // target1 receives original values
  let target1_values = collect_subject_values(target1, 3, 100)
  // target2 receives mapped values
  let target2_values = collect_subject_values(target2, 3, 100)

  target1_values |> should.equal([10, 20, 30])
  target2_values |> should.equal([20, 40, 60])
  downstream_values |> should.equal([20, 40, 60])
}

// ============================================================================
// call_actor tests
// ============================================================================

/// Message type for test actor
pub type TestActorMsg {
  GetValue(Subject(Int))
  SetValue(Int)
  AddAndGet(Int, Subject(Int))
}

/// Start a simple test actor that responds to GetValue
fn start_test_actor(initial_value: Int) -> Subject(TestActorMsg) {
  // Create a ready signal Subject in the test process
  let inbox_ready: Subject(Subject(TestActorMsg)) = process.new_subject()

  process.spawn(fn() {
    // Create inbox in the spawned process so it can receive from it
    let inbox: Subject(TestActorMsg) = process.new_subject()
    process.send(inbox_ready, inbox)
    test_actor_loop(inbox, initial_value)
  })

  // Wait for the actor to send its inbox
  case process.receive(inbox_ready, 1000) {
    Ok(inbox) -> inbox
    Error(_) -> panic as "Failed to start test actor"
  }
}

fn test_actor_loop(inbox: Subject(TestActorMsg), value: Int) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(inbox)

  case process.selector_receive_forever(selector) {
    GetValue(reply) -> {
      process.send(reply, value)
      test_actor_loop(inbox, value)
    }
    SetValue(new_value) -> {
      test_actor_loop(inbox, new_value)
    }
    AddAndGet(n, reply) -> {
      let new_value = value + n
      process.send(reply, new_value)
      test_actor_loop(inbox, new_value)
    }
  }
}

pub fn call_actor_receives_response_test() {
  let actor = start_test_actor(42)

  let observable = actorx.call_actor(actor, 1000, GetValue)

  let #(subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  let #(values, completed, errors) = collect_messages(subj, 500)

  values |> should.equal([42])
  completed |> should.equal(True)
  errors |> should.equal([])
}

pub fn call_actor_timeout_produces_error_test() {
  // Create a Subject but don't start an actor (no one will respond)
  let fake_actor: Subject(TestActorMsg) = process.new_subject()

  let observable = actorx.call_actor(fake_actor, 50, GetValue)

  let #(subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  let #(values, completed, errors) = collect_messages(subj, 200)

  values |> should.equal([])
  completed |> should.equal(False)
  // Should have a timeout error
  errors |> should.not_equal([])
}

pub fn call_actor_each_subscription_independent_test() {
  let actor = start_test_actor(0)

  let observable = actorx.call_actor(actor, 1000, GetValue)

  // First subscription
  let #(subj1, observer1) = make_test_subject()
  let _disp1 = actorx.subscribe(observable, observer1)
  let #(values1, _, _) = collect_messages(subj1, 500)

  // Update actor state
  process.send(actor, SetValue(100))
  wait_ms(10)

  // Second subscription sees updated value
  let #(subj2, observer2) = make_test_subject()
  let _disp2 = actorx.subscribe(observable, observer2)
  let #(values2, _, _) = collect_messages(subj2, 500)

  values1 |> should.equal([0])
  values2 |> should.equal([100])
}

pub fn call_actor_with_flat_map_orchestration_test() {
  let actor = start_test_actor(0)

  // Use flat_map to make multiple calls - each adds to the accumulator
  let observable =
    actorx.from_list([1, 2, 3])
    |> actorx.flat_map(fn(n) {
      // Each emission triggers a call to the actor
      actorx.call_actor(actor, 1000, fn(reply) { AddAndGet(n, reply) })
    })

  let #(subj, observer) = make_test_subject()
  let _disp = actorx.subscribe(observable, observer)

  let #(values, completed, _) = collect_messages(subj, 1000)

  // Should have 3 responses (order may vary due to concurrent flat_map)
  // The actor accumulates: 0+1=1, 1+2=3, 3+3=6
  should.equal(3, length(values))
  completed |> should.equal(True)
}

// ============================================================================
// Helper functions
// ============================================================================

/// Collect N values from a Subject with timeout
fn collect_subject_values(
  subj: Subject(a),
  count: Int,
  timeout_ms: Int,
) -> List(a) {
  collect_subject_values_loop(subj, count, timeout_ms, [])
}

fn collect_subject_values_loop(
  subj: Subject(a),
  remaining: Int,
  timeout_ms: Int,
  acc: List(a),
) -> List(a) {
  case remaining {
    0 -> acc
    _ -> {
      case process.receive(subj, timeout_ms) {
        Ok(value) ->
          collect_subject_values_loop(
            subj,
            remaining - 1,
            timeout_ms,
            append(acc, [value]),
          )
        Error(_) -> acc
      }
    }
  }
}

fn append(list: List(a), items: List(a)) -> List(a) {
  case list {
    [] -> items
    [head, ..tail] -> [head, ..append(tail, items)]
  }
}

fn length(list: List(a)) -> Int {
  case list {
    [] -> 0
    [_, ..tail] -> 1 + length(tail)
  }
}
