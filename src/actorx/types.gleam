//// Core types for ActorX - Reactive Extensions for Gleam
////
//// This module defines the fundamental types for reactive programming:
//// - Notification: The atoms of the Rx grammar (OnNext, OnError, OnCompleted)
//// - Disposable: Resource cleanup handle
//// - Observer: Receives notifications from an observable
//// - Observable: Source of asynchronous events

/// Notification represents the three types of events in the Rx grammar:
/// OnNext* (OnError | OnCompleted)?
pub type Notification(a) {
  OnNext(a)
  OnError(String)
  OnCompleted
}

/// Disposable represents a resource that can be cleaned up.
/// Call the dispose function to release resources and unsubscribe.
pub type Disposable {
  Disposable(dispose: fn() -> Nil)
}

/// Create an empty disposable that does nothing when disposed.
pub fn empty_disposable() -> Disposable {
  Disposable(dispose: fn() { Nil })
}

/// Combine multiple disposables into one.
/// When disposed, all inner disposables are disposed.
pub fn composite_disposable(disposables: List(Disposable)) -> Disposable {
  Disposable(dispose: fn() { dispose_all(disposables) })
}

fn dispose_all(disposables: List(Disposable)) -> Nil {
  case disposables {
    [] -> Nil
    [Disposable(dispose), ..rest] -> {
      dispose()
      dispose_all(rest)
    }
  }
}

/// Observer receives notifications from an Observable.
///
/// The Rx contract guarantees:
/// - OnNext may be called zero or more times
/// - OnError or OnCompleted is called at most once (terminal)
/// - No calls occur after a terminal event
pub type Observer(a) {
  Observer(notify: fn(Notification(a)) -> Nil)
}

// ============================================================================
// Observer creation helpers
// ============================================================================

/// Create an observer from three callback functions.
/// This is convenient for subscribers who want to handle each event type separately.
pub fn make_observer(
  on_next: fn(a) -> Nil,
  on_error: fn(String) -> Nil,
  on_completed: fn() -> Nil,
) -> Observer(a) {
  Observer(notify: fn(n) {
    case n {
      OnNext(x) -> on_next(x)
      OnError(e) -> on_error(e)
      OnCompleted -> on_completed()
    }
  })
}

/// Create an observer that only handles OnNext events.
/// OnError and OnCompleted are ignored.
pub fn make_next_observer(on_next: fn(a) -> Nil) -> Observer(a) {
  Observer(notify: fn(n) {
    case n {
      OnNext(x) -> on_next(x)
      _ -> Nil
    }
  })
}

// ============================================================================
// Observer calling helpers
// ============================================================================

/// Send an OnNext notification to an observer.
pub fn on_next(observer: Observer(a), value: a) -> Nil {
  let Observer(notify) = observer
  notify(OnNext(value))
}

/// Send an OnError notification to an observer.
pub fn on_error(observer: Observer(a), error: String) -> Nil {
  let Observer(notify) = observer
  notify(OnError(error))
}

/// Send an OnCompleted notification to an observer.
pub fn on_completed(observer: Observer(a)) -> Nil {
  let Observer(notify) = observer
  notify(OnCompleted)
}

/// Forward a notification to an observer.
pub fn notify(observer: Observer(a), notification: Notification(a)) -> Nil {
  let Observer(notify_fn) = observer
  notify_fn(notification)
}

/// Observable is a source of asynchronous events.
/// Subscribe with an Observer to receive notifications.
pub type Observable(a) {
  Observable(subscribe: fn(Observer(a)) -> Disposable)
}
