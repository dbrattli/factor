//// Subject types for ActorX
////
//// Subjects are both Observers and Observables - they can receive
//// notifications and forward them to subscribers.
////
//// - subject: Multicast subject, allows multiple subscribers
//// - single_subject: Single subscriber only, buffers until subscribed

import actorx/types.{
  type Disposable, type Notification, type Observable, type Observer, Disposable,
  Observable, Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/list
import gleam/option.{type Option, None, Some}

// ============================================================================
// Multicast Subject
// ============================================================================

/// Messages for the multicast subject actor
type SubjectMsg(a) {
  /// Subscribe a downstream observer
  SubjectSubscribe(Int, Observer(a), Subject(Disposable))
  /// Unsubscribe by id
  SubjectUnsubscribe(Int)
  /// Send a notification
  SubjectNotify(Notification(a))
}

/// Subscriber entry with unique id
type Subscriber(a) {
  Subscriber(id: Int, observer: Observer(a))
}

/// Creates a multicast subject that allows multiple subscribers.
///
/// Returns a tuple of (Observer, Observable) where:
/// - The Observer side is used to push notifications
/// - The Observable side can be subscribed to by multiple observers
///
/// Unlike single_subject, notifications are NOT buffered - they are only
/// delivered to currently subscribed observers.
///
/// ## Example
/// ```gleam
/// let #(input, output) = subject()
///
/// // Multiple subscribers
/// let _disp1 = output |> actorx.subscribe(observer1)
/// let _disp2 = output |> actorx.subscribe(observer2)
///
/// // Both receive this value
/// actorx.on_next(input, 42)
/// ```
pub fn subject() -> #(Observer(a), Observable(a)) {
  let control_ready: Subject(Subject(SubjectMsg(a))) = process.new_subject()

  // Spawn the coordinator actor
  process.spawn(fn() {
    let control: Subject(SubjectMsg(a)) = process.new_subject()
    process.send(control_ready, control)
    subject_loop(control, [])
  })

  // Get the actor's control subject
  let control = case process.receive(control_ready, 1000) {
    Ok(s) -> s
    Error(_) -> panic as "Failed to create subject"
  }

  // Observer side - sends notifications to the actor
  let observer =
    Observer(notify: fn(n) { process.send(control, SubjectNotify(n)) })

  // Observable side - subscribes to receive notifications
  let observable =
    Observable(subscribe: fn(downstream) {
      let reply: Subject(Disposable) = process.new_subject()
      // Generate a unique id for this subscription
      let id = erlang_unique_integer()
      process.send(control, SubjectSubscribe(id, downstream, reply))
      case process.receive(reply, 5000) {
        Ok(disp) -> disp
        Error(_) -> panic as "subject subscribe timeout"
      }
    })

  #(observer, observable)
}

@external(erlang, "erlang", "unique_integer")
fn erlang_unique_integer() -> Int

fn subject_loop(
  control: Subject(SubjectMsg(a)),
  subscribers: List(Subscriber(a)),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    SubjectSubscribe(id, observer, reply) -> {
      let subscriber = Subscriber(id: id, observer: observer)
      let disp =
        Disposable(dispose: fn() {
          process.send(control, SubjectUnsubscribe(id))
          Nil
        })
      process.send(reply, disp)
      subject_loop(control, [subscriber, ..subscribers])
    }
    SubjectUnsubscribe(id) -> {
      let new_subscribers =
        list.filter(subscribers, fn(s) { s.id != id })
      subject_loop(control, new_subscribers)
    }
    SubjectNotify(n) -> {
      // Broadcast to all subscribers
      list.each(subscribers, fn(s) {
        let Observer(notify) = s.observer
        notify(n)
      })
      // Stop looping on terminal events
      case n {
        OnCompleted -> Nil
        OnError(_) -> Nil
        OnNext(_) -> subject_loop(control, subscribers)
      }
    }
  }
}

// ============================================================================
// Single Subject
// ============================================================================

/// Messages for the single_subject actor
type SingleSubjectMsg(a) {
  /// Subscribe a downstream observer (only one allowed)
  Subscribe(Observer(a), Subject(Result(Disposable, String)))
  /// Send a notification
  Notify(Notification(a))
  /// Dispose the subject
  Dispose
}

/// Creates a single-subscriber subject.
///
/// Returns a tuple of (Observer, Observable) where:
/// - The Observer side is used to push notifications
/// - The Observable side can be subscribed to (once only!)
///
/// Notifications sent before subscription are buffered and delivered
/// when a subscriber connects.
///
/// ## Example
/// ```gleam
/// let #(observer, observable) = single_subject()
///
/// // Push values through the observer side
/// types.on_next(observer, 1)
/// types.on_next(observer, 2)
///
/// // Subscribe to receive them
/// observable |> actorx.subscribe(my_observer)
///
/// // More values flow through
/// types.on_next(observer, 3)
/// types.on_completed(observer)
/// ```
pub fn single_subject() -> #(Observer(a), Observable(a)) {
  // Channel for receiving the actor's control subject
  let control_ready: Subject(Subject(SingleSubjectMsg(a))) =
    process.new_subject()

  // Spawn the coordinator actor
  process.spawn(fn() {
    let control: Subject(SingleSubjectMsg(a)) = process.new_subject()
    process.send(control_ready, control)
    single_subject_loop(control, None, [])
  })

  // Get the actor's control subject
  let control = case process.receive(control_ready, 1000) {
    Ok(s) -> s
    Error(_) -> panic as "Failed to create single_subject"
  }

  // Observer side - sends notifications to the actor
  let observer = Observer(notify: fn(n) { process.send(control, Notify(n)) })

  // Observable side - subscribes to receive notifications
  let observable =
    Observable(subscribe: fn(downstream) {
      let reply: Subject(Result(Disposable, String)) = process.new_subject()
      process.send(control, Subscribe(downstream, reply))
      case process.receive(reply, 5000) {
        Ok(Ok(disp)) -> disp
        Ok(Error(msg)) -> panic as msg
        Error(_) -> panic as "single_subject subscribe timeout"
      }
    })

  #(observer, observable)
}

fn single_subject_loop(
  control: Subject(SingleSubjectMsg(a)),
  subscriber: Option(Observer(a)),
  pending: List(Notification(a)),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    Subscribe(observer, reply) -> {
      case subscriber {
        Some(_) -> {
          // Already have a subscriber - reject
          process.send(reply, Error("single_subject: Already subscribed"))
          single_subject_loop(control, subscriber, pending)
        }
        None -> {
          // First subscriber - flush pending and accept
          flush_pending(observer, pending)
          let disp =
            Disposable(dispose: fn() {
              process.send(control, Dispose)
              Nil
            })
          process.send(reply, Ok(disp))
          single_subject_loop(control, Some(observer), [])
        }
      }
    }
    Notify(n) -> {
      case subscriber {
        Some(Observer(notify)) -> {
          // Forward to subscriber
          notify(n)
          // Stop looping on terminal events
          case n {
            OnCompleted -> Nil
            OnError(_) -> Nil
            OnNext(_) -> single_subject_loop(control, subscriber, pending)
          }
        }
        None -> {
          // No subscriber yet - buffer the notification
          single_subject_loop(control, subscriber, list.append(pending, [n]))
        }
      }
    }
    Dispose -> Nil
  }
}

fn flush_pending(observer: Observer(a), pending: List(Notification(a))) -> Nil {
  let Observer(notify) = observer
  list.each(pending, notify)
}
