//// Subject types for ActorX
////
//// Subjects are both Observers and Observables - they can receive
//// notifications and forward them to subscribers.
////
//// - subject: Multicast subject, allows multiple subscribers
//// - single_subject: Single subscriber only, buffers until subscribed
//// - publish: Convert cold observable to connectable hot observable
//// - share: Auto-connecting multicast (publish + refCount)

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
      let new_subscribers = list.filter(subscribers, fn(s) { s.id != id })
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

// ============================================================================
// Publish - Connectable Observable
// ============================================================================

/// Messages for the publish actor
type PublishMsg(a) {
  /// Subscribe a downstream observer
  PublishSubscribe(Int, Observer(a), Subject(Disposable))
  /// Unsubscribe by id
  PublishUnsubscribe(Int)
  /// Connect to source
  PublishConnect(Subject(Disposable))
  /// Notification from source
  PublishNotify(Notification(a))
  /// Dispose connection
  PublishDisposeConnection
}

/// State for publish actor
type PublishState(a) {
  PublishState(
    subscribers: List(Subscriber(a)),
    connection: Option(Disposable),
    terminal: Option(Notification(a)),
  )
}

/// Converts a cold observable into a connectable hot observable.
///
/// Returns a tuple of (Observable, connect_fn) where:
/// - The Observable can be subscribed to by multiple observers
/// - The connect function starts the source subscription
///
/// Values are only emitted after connect() is called. Multiple subscribers
/// share the same source subscription.
///
/// ## Example
/// ```gleam
/// let #(hot, connect) = publish(cold_source)
///
/// // Subscribe multiple observers (source not started yet)
/// let _d1 = hot |> actorx.subscribe(observer1)
/// let _d2 = hot |> actorx.subscribe(observer2)
///
/// // Now connect - source starts, both observers receive values
/// let connection = connect()
///
/// // Disconnect when done
/// let Disposable(dispose) = connection
/// dispose()
/// ```
pub fn publish(source: Observable(a)) -> #(Observable(a), fn() -> Disposable) {
  let control_ready: Subject(Subject(PublishMsg(a))) = process.new_subject()

  // Spawn the coordinator actor
  process.spawn(fn() {
    let control: Subject(PublishMsg(a)) = process.new_subject()
    process.send(control_ready, control)
    let initial_state =
      PublishState(subscribers: [], connection: None, terminal: None)
    publish_loop(control, source, initial_state)
  })

  // Get the actor's control subject
  let control = case process.receive(control_ready, 1000) {
    Ok(s) -> s
    Error(_) -> panic as "Failed to create publish"
  }

  // Observable side - subscribes to receive notifications
  let observable =
    Observable(subscribe: fn(downstream) {
      let reply: Subject(Disposable) = process.new_subject()
      let id = erlang_unique_integer()
      process.send(control, PublishSubscribe(id, downstream, reply))
      case process.receive(reply, 5000) {
        Ok(disp) -> disp
        Error(_) -> panic as "publish subscribe timeout"
      }
    })

  // Connect function - starts the source subscription
  let connect = fn() {
    let reply: Subject(Disposable) = process.new_subject()
    process.send(control, PublishConnect(reply))
    case process.receive(reply, 5000) {
      Ok(disp) -> disp
      Error(_) -> panic as "publish connect timeout"
    }
  }

  #(observable, connect)
}

fn publish_loop(
  control: Subject(PublishMsg(a)),
  source: Observable(a),
  state: PublishState(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    PublishSubscribe(id, observer, reply) -> {
      // If already terminated, immediately send terminal and return no-op disposable
      case state.terminal {
        Some(terminal_notification) -> {
          let Observer(notify) = observer
          notify(terminal_notification)
          let disp = Disposable(dispose: fn() { Nil })
          process.send(reply, disp)
          publish_loop(control, source, state)
        }
        None -> {
          let subscriber = Subscriber(id: id, observer: observer)
          let disp =
            Disposable(dispose: fn() {
              process.send(control, PublishUnsubscribe(id))
              Nil
            })
          process.send(reply, disp)
          let new_state =
            PublishState(..state, subscribers: [subscriber, ..state.subscribers])
          publish_loop(control, source, new_state)
        }
      }
    }
    PublishUnsubscribe(id) -> {
      let new_subscribers = list.filter(state.subscribers, fn(s) { s.id != id })
      let new_state = PublishState(..state, subscribers: new_subscribers)
      publish_loop(control, source, new_state)
    }
    PublishConnect(reply) -> {
      case state.connection {
        Some(existing) -> {
          // Already connected - return existing connection (or no-op if terminated)
          process.send(reply, existing)
          publish_loop(control, source, state)
        }
        None -> {
          // If already terminated, just return a no-op disposable
          case state.terminal {
            Some(_) -> {
              let disp = Disposable(dispose: fn() { Nil })
              process.send(reply, disp)
              publish_loop(control, source, state)
            }
            None -> {
              // Subscribe to source
              let source_observer =
                Observer(notify: fn(n) {
                  process.send(control, PublishNotify(n))
                })
              let Observable(subscribe) = source
              let source_disp = subscribe(source_observer)

              let conn_disp =
                Disposable(dispose: fn() {
                  let Disposable(dispose_source) = source_disp
                  dispose_source()
                  process.send(control, PublishDisposeConnection)
                  Nil
                })
              process.send(reply, conn_disp)
              let new_state = PublishState(..state, connection: Some(conn_disp))
              publish_loop(control, source, new_state)
            }
          }
        }
      }
    }
    PublishNotify(n) -> {
      // Broadcast to all subscribers
      list.each(state.subscribers, fn(s) {
        let Observer(notify) = s.observer
        notify(n)
      })
      // On terminal events, store terminal but keep looping
      case n {
        OnCompleted -> {
          let new_state = PublishState(..state, terminal: Some(n))
          publish_loop(control, source, new_state)
        }
        OnError(_) -> {
          let new_state = PublishState(..state, terminal: Some(n))
          publish_loop(control, source, new_state)
        }
        OnNext(_) -> publish_loop(control, source, state)
      }
    }
    PublishDisposeConnection -> {
      let new_state = PublishState(..state, connection: None)
      publish_loop(control, source, new_state)
    }
  }
}

// ============================================================================
// Share - Auto-connecting Multicast
// ============================================================================

/// Messages for the share actor
type ShareMsg(a) {
  /// Subscribe a downstream observer
  ShareSubscribe(Int, Observer(a), Subject(Disposable))
  /// Unsubscribe by id
  ShareUnsubscribe(Int)
  /// Notification from source
  ShareNotify(Notification(a))
}

/// Share state
type ShareState(a) {
  ShareState(
    subscribers: List(Subscriber(a)),
    source_disposable: Option(Disposable),
    terminal: Option(Notification(a)),
  )
}

/// Shares a single subscription to the source among multiple subscribers.
///
/// Automatically connects to the source when the first subscriber subscribes,
/// and disconnects when the last subscriber unsubscribes.
///
/// This is equivalent to `publish(source)` with automatic reference counting.
///
/// ## Example
/// ```gleam
/// let shared =
///   interval(100)
///   |> share()
///
/// // First subscriber - source starts
/// let d1 = shared |> actorx.subscribe(observer1)
///
/// // Second subscriber - shares same source
/// let d2 = shared |> actorx.subscribe(observer2)
///
/// // Unsubscribe first
/// let Disposable(dispose1) = d1
/// dispose1()
///
/// // Unsubscribe last - source stops
/// let Disposable(dispose2) = d2
/// dispose2()
/// ```
pub fn share(source: Observable(a)) -> Observable(a) {
  let control_ready: Subject(Subject(ShareMsg(a))) = process.new_subject()

  // Spawn the coordinator actor
  process.spawn(fn() {
    let control: Subject(ShareMsg(a)) = process.new_subject()
    process.send(control_ready, control)
    let initial_state =
      ShareState(subscribers: [], source_disposable: None, terminal: None)
    share_loop(control, source, initial_state)
  })

  // Get the actor's control subject
  let control = case process.receive(control_ready, 1000) {
    Ok(s) -> s
    Error(_) -> panic as "Failed to create share"
  }

  // Observable side
  Observable(subscribe: fn(downstream) {
    let reply: Subject(Disposable) = process.new_subject()
    let id = erlang_unique_integer()
    process.send(control, ShareSubscribe(id, downstream, reply))
    case process.receive(reply, 5000) {
      Ok(disp) -> disp
      Error(_) -> panic as "share subscribe timeout"
    }
  })
}

fn share_loop(
  control: Subject(ShareMsg(a)),
  source: Observable(a),
  state: ShareState(a),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    ShareSubscribe(id, observer, reply) -> {
      // If previously terminated and no active connection, reconnect fresh
      let #(new_source_disp, reset_terminal) = case state.terminal {
        Some(_) -> {
          case state.source_disposable {
            None -> {
              // Previous source completed, start fresh
              let source_observer =
                Observer(notify: fn(n) { process.send(control, ShareNotify(n)) })
              let Observable(subscribe) = source
              #(Some(subscribe(source_observer)), True)
            }
            Some(d) -> #(Some(d), False)
          }
        }
        None -> {
          case state.source_disposable {
            Some(d) -> #(Some(d), False)
            None -> {
              // First subscriber, connect
              let source_observer =
                Observer(notify: fn(n) { process.send(control, ShareNotify(n)) })
              let Observable(subscribe) = source
              #(Some(subscribe(source_observer)), False)
            }
          }
        }
      }

      let subscriber = Subscriber(id: id, observer: observer)
      let new_subscribers = [subscriber, ..state.subscribers]

      let disp =
        Disposable(dispose: fn() {
          process.send(control, ShareUnsubscribe(id))
          Nil
        })
      process.send(reply, disp)

      let new_terminal = case reset_terminal {
        True -> None
        False -> state.terminal
      }

      share_loop(
        control,
        source,
        ShareState(
          subscribers: new_subscribers,
          source_disposable: new_source_disp,
          terminal: new_terminal,
        ),
      )
    }
    ShareUnsubscribe(id) -> {
      let new_subscribers = list.filter(state.subscribers, fn(s) { s.id != id })

      // Disconnect from source if this was the last subscriber
      let new_source_disp = case new_subscribers {
        [] -> {
          case state.source_disposable {
            Some(Disposable(dispose)) -> {
              dispose()
              None
            }
            None -> None
          }
        }
        _ -> state.source_disposable
      }

      share_loop(
        control,
        source,
        ShareState(
          ..state,
          subscribers: new_subscribers,
          source_disposable: new_source_disp,
        ),
      )
    }
    ShareNotify(n) -> {
      // Broadcast to all subscribers
      list.each(state.subscribers, fn(s) {
        let Observer(notify) = s.observer
        notify(n)
      })
      // On terminal, keep looping but mark as terminated
      case n {
        OnCompleted -> {
          let new_state =
            ShareState(..state, terminal: Some(n), source_disposable: None)
          share_loop(control, source, new_state)
        }
        OnError(_) -> {
          let new_state =
            ShareState(..state, terminal: Some(n), source_disposable: None)
          share_loop(control, source, new_state)
        }
        OnNext(_) -> share_loop(control, source, state)
      }
    }
  }
}
