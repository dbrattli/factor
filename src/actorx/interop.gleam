//// Actor interop for ActorX
////
//// Bridges BEAM actors (OTP Subjects) with reactive streams, enabling
//// actor orchestration via Rx operators.
////
//// - make_subject: Create a Subject/Observable pair for bridging actors
//// - to_subject: Send emissions to Subject (passthrough operator)
//// - call_actor: Request-response pattern as Observable (cold)

import actorx/types.{
  type Disposable, type Notification, type Observable, type Observer, Disposable,
  Observable, Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/list

// ============================================================================
// make_subject - Create Subject/Observable bridge
// ============================================================================

/// Messages for the from_subject coordinator
type FromSubjectMsg(a) {
  /// Subscribe a downstream observer
  FSSubscribe(Int, Observer(a), Subject(Disposable))
  /// Unsubscribe by id
  FSUnsubscribe(Int)
  /// Value received from source Subject
  FSValue(a)
}

/// Startup response containing both subjects
type FSStartup(a) {
  FSStartup(control: Subject(FromSubjectMsg(a)), source: Subject(a))
}

/// Subscriber entry with unique id
type FSSubscriber(a) {
  FSSubscriber(id: Int, observer: Observer(a))
}

/// Creates a Subject and Observable pair for bridging actors.
///
/// Returns a tuple of (Subject, Observable) where:
/// - Values sent to the Subject are emitted to all Observable subscribers
/// - The Observable never completes on its own - use `take_until` for completion
///
/// This is useful for having an actor publish events that can be consumed
/// reactively via the Observable.
///
/// ## Example
/// ```gleam
/// let #(events_subject, events_observable) = actorx.from_subject()
///
/// // Pass subject to a producer actor
/// let _producer = start_event_producer(events_subject)
///
/// // Subscribe to the observable
/// events_observable
/// |> actorx.filter(is_high_priority)
/// |> actorx.take_until(shutdown_signal)
/// |> actorx.subscribe(alert_observer)
/// ```
pub fn from_subject() -> #(Subject(a), Observable(a)) {
  let startup_ready: Subject(FSStartup(a)) = process.new_subject()

  // Spawn the coordinator actor
  process.spawn(fn() {
    let control: Subject(FromSubjectMsg(a)) = process.new_subject()
    let source: Subject(a) = process.new_subject()
    process.send(startup_ready, FSStartup(control: control, source: source))
    from_subject_loop(control, source, [])
  })

  // Get the actor's subjects
  let FSStartup(control, source) = case process.receive(startup_ready, 1000) {
    Ok(s) -> s
    Error(_) -> panic as "Failed to create from_subject"
  }

  // Observable side - subscribes to receive notifications
  let observable =
    Observable(subscribe: fn(downstream) {
      let reply: Subject(Disposable) = process.new_subject()
      let id = erlang_unique_integer()
      process.send(control, FSSubscribe(id, downstream, reply))
      case process.receive(reply, 5000) {
        Ok(disp) -> disp
        Error(_) -> panic as "from_subject subscribe timeout"
      }
    })

  #(source, observable)
}

@external(erlang, "erlang", "unique_integer")
fn erlang_unique_integer() -> Int

fn from_subject_loop(
  control: Subject(FromSubjectMsg(a)),
  source: Subject(a),
  subscribers: List(FSSubscriber(a)),
) -> Nil {
  // Select from both control messages and the source Subject
  let selector =
    process.new_selector()
    |> process.select(control)
    |> process.select_map(source, FSValue)

  case process.selector_receive_forever(selector) {
    FSSubscribe(id, observer, reply) -> {
      let subscriber = FSSubscriber(id: id, observer: observer)
      let disp =
        Disposable(dispose: fn() {
          process.send(control, FSUnsubscribe(id))
          Nil
        })
      process.send(reply, disp)
      from_subject_loop(control, source, [subscriber, ..subscribers])
    }
    FSUnsubscribe(id) -> {
      let new_subscribers = list.filter(subscribers, fn(s) { s.id != id })
      from_subject_loop(control, source, new_subscribers)
    }
    FSValue(value) -> {
      // Broadcast to all subscribers
      list.each(subscribers, fn(s) {
        let Observer(notify) = s.observer
        notify(OnNext(value))
      })
      from_subject_loop(control, source, subscribers)
    }
  }
}

// ============================================================================
// to_subject - Send emissions to Subject (passthrough)
// ============================================================================

/// Sends each emitted value to a Subject while passing through to downstream.
///
/// Only OnNext values are sent to the Subject. OnError and OnCompleted
/// are forwarded downstream but not sent to the Subject.
///
/// ## Example
/// ```gleam
/// actorx.interval(100)
/// |> actorx.take(10)
/// |> actorx.map(fn(n) { Increment(n) })
/// |> actorx.to_subject(counter_inbox)
/// |> actorx.subscribe(log_observer)
/// ```
pub fn to_subject(source: Observable(a), target: Subject(a)) -> Observable(a) {
  Observable(subscribe: fn(observer) {
    let Observer(downstream) = observer
    let upstream_observer =
      Observer(notify: fn(n: Notification(a)) {
        case n {
          OnNext(value) -> {
            process.send(target, value)
            downstream(OnNext(value))
          }
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> downstream(OnCompleted)
        }
      })
    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

// ============================================================================
// call_actor - Request-Response Pattern
// ============================================================================

/// Creates a cold Observable that performs a request-response call to an actor.
///
/// Each subscription triggers a new request. The `make_request` function
/// receives a reply Subject and should return the message to send to the actor.
///
/// ## Example
/// ```gleam
/// type CounterMsg {
///   Increment(Int)
///   GetValue(Subject(Int))
/// }
///
/// // Single call
/// actorx.call_actor(counter, 1000, GetValue)
/// |> actorx.subscribe(observer)
///
/// // Periodic polling
/// actorx.interval(1000)
/// |> actorx.flat_map(fn(_) {
///   actorx.call_actor(counter, 1000, GetValue)
/// })
/// |> actorx.subscribe(value_observer)
/// ```
pub fn call_actor(
  actor_subject: Subject(msg),
  timeout_ms: Int,
  make_request: fn(Subject(response)) -> msg,
) -> Observable(response) {
  Observable(subscribe: fn(observer) {
    let Observer(notify) = observer

    // Create reply Subject for this call
    let reply: Subject(response) = process.new_subject()

    // Build and send the request message
    let request_msg = make_request(reply)
    process.send(actor_subject, request_msg)

    // Wait for response with timeout
    case process.receive(reply, timeout_ms) {
      Ok(response) -> {
        notify(OnNext(response))
        notify(OnCompleted)
      }
      Error(_) -> {
        notify(OnError(
          "call_actor timeout after " <> int_to_string(timeout_ms) <> "ms",
        ))
      }
    }

    // Return empty disposable (call already completed)
    Disposable(dispose: fn() { Nil })
  })
}

@external(erlang, "erlang", "integer_to_binary")
fn int_to_string(n: Int) -> String
