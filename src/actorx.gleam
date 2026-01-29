//// ActorX - Reactive Extensions for Gleam using BEAM actors
////
//// A reactive programming library that composes BEAM actors for
//// building asynchronous, event-driven applications.
////
//// ## Example
////
//// ```gleam
//// import actorx
//// import gleam/io
//// import gleam/int
////
//// pub fn main() {
////   let observable =
////     actorx.from_list([1, 2, 3, 4, 5])
////     |> actorx.map(fn(x) { x * 2 })
////     |> actorx.filter(fn(x) { x > 4 })
////
////   let observer = actorx.make_observer(
////     on_next: fn(x) { io.println(int.to_string(x)) },
////     on_error: fn(err) { io.println("Error: " <> err) },
////     on_completed: fn() { io.println("Done!") },
////   )
////
////   actorx.subscribe(observable, observer)
//// }
//// ```

import actorx/create
import actorx/filter
import actorx/subject
import actorx/timeshift
import actorx/transform
import actorx/types
import gleam/option

// ============================================================================
// Re-export types
// ============================================================================

pub type Observable(a) =
  types.Observable(a)

pub type Observer(a) =
  types.Observer(a)

pub type Disposable =
  types.Disposable

pub type Notification(a) =
  types.Notification(a)

// ============================================================================
// Observer helpers
// ============================================================================

/// Create an observer from three callback functions.
pub fn make_observer(
  on_next on_next: fn(a) -> Nil,
  on_error on_error: fn(String) -> Nil,
  on_completed on_completed: fn() -> Nil,
) -> types.Observer(a) {
  types.make_observer(on_next, on_error, on_completed)
}

/// Create an observer that only handles OnNext events.
pub fn make_next_observer(on_next: fn(a) -> Nil) -> types.Observer(a) {
  types.make_next_observer(on_next)
}

/// Send an OnNext notification to an observer.
pub fn on_next(observer: types.Observer(a), value: a) -> Nil {
  types.on_next(observer, value)
}

/// Send an OnError notification to an observer.
pub fn on_error(observer: types.Observer(a), error: String) -> Nil {
  types.on_error(observer, error)
}

/// Send an OnCompleted notification to an observer.
pub fn on_completed(observer: types.Observer(a)) -> Nil {
  types.on_completed(observer)
}

/// Forward a notification to an observer.
pub fn notify(
  observer: types.Observer(a),
  notification: types.Notification(a),
) -> Nil {
  types.notify(observer, notification)
}

// ============================================================================
// Subscribe helper
// ============================================================================

/// Subscribe an observer to an observable.
pub fn subscribe(
  observable: types.Observable(a),
  observer: types.Observer(a),
) -> types.Disposable {
  let types.Observable(subscribe_fn) = observable
  subscribe_fn(observer)
}

// ============================================================================
// Creation operators
// ============================================================================

/// Create an observable from a subscribe function.
pub fn create(
  subscribe_fn: fn(types.Observer(a)) -> types.Disposable,
) -> types.Observable(a) {
  create.create(subscribe_fn)
}

/// Create an observable that emits a single value then completes.
pub fn single(value: a) -> types.Observable(a) {
  create.single(value)
}

/// Create an observable that completes immediately without emitting.
pub fn empty() -> types.Observable(a) {
  create.empty()
}

/// Create an observable that never emits and never completes.
pub fn never() -> types.Observable(a) {
  create.never()
}

/// Create an observable that errors immediately.
pub fn fail(error: String) -> types.Observable(a) {
  create.fail(error)
}

/// Create an observable from a list of values.
pub fn from_list(items: List(a)) -> types.Observable(a) {
  create.from_list(items)
}

/// Create an observable that calls a factory function on each subscription.
pub fn defer(factory: fn() -> types.Observable(a)) -> types.Observable(a) {
  create.defer(factory)
}

// ============================================================================
// Transform operators
// ============================================================================

/// Transform each element using a mapper function.
pub fn map(
  source: types.Observable(a),
  mapper: fn(a) -> b,
) -> types.Observable(b) {
  transform.map(source, mapper)
}

/// Project each element to an observable and flatten.
pub fn flat_map(
  source: types.Observable(a),
  mapper: fn(a) -> types.Observable(b),
) -> types.Observable(b) {
  transform.flat_map(source, mapper)
}

/// Project each element to an observable and concatenate in order.
pub fn concat_map(
  source: types.Observable(a),
  mapper: fn(a) -> types.Observable(b),
) -> types.Observable(b) {
  transform.concat_map(source, mapper)
}

/// Actor-based flat_map for async sources.
///
/// Unlike `flat_map`, this version uses an actor to coordinate inner
/// subscriptions, making it safe for async sources that emit over time.
/// It properly tracks all inner subscriptions and only completes when
/// both the source AND all inner observables have completed.
pub fn flat_map_async(
  source: types.Observable(a),
  mapper: fn(a) -> types.Observable(b),
) -> types.Observable(b) {
  transform.flat_map_async(source, mapper)
}

// ============================================================================
// Filter operators
// ============================================================================

/// Filter elements based on a predicate.
pub fn filter(
  source: types.Observable(a),
  predicate: fn(a) -> Bool,
) -> types.Observable(a) {
  filter.filter(source, predicate)
}

/// Take the first N elements.
pub fn take(source: types.Observable(a), count: Int) -> types.Observable(a) {
  filter.take(source, count)
}

/// Skip the first N elements.
pub fn skip(source: types.Observable(a), count: Int) -> types.Observable(a) {
  filter.skip(source, count)
}

/// Take elements while predicate returns True.
pub fn take_while(
  source: types.Observable(a),
  predicate: fn(a) -> Bool,
) -> types.Observable(a) {
  filter.take_while(source, predicate)
}

/// Skip elements while predicate returns True.
pub fn skip_while(
  source: types.Observable(a),
  predicate: fn(a) -> Bool,
) -> types.Observable(a) {
  filter.skip_while(source, predicate)
}

/// Filter and map in one operation.
pub fn choose(
  source: types.Observable(a),
  chooser: fn(a) -> option.Option(b),
) -> types.Observable(b) {
  filter.choose(source, chooser)
}

/// Emit only when value changes from previous.
pub fn distinct_until_changed(
  source: types.Observable(a),
) -> types.Observable(a) {
  filter.distinct_until_changed(source)
}

/// Take elements until another observable emits.
pub fn take_until(
  source: types.Observable(a),
  other: types.Observable(b),
) -> types.Observable(a) {
  filter.take_until(source, other)
}

/// Take the last N elements (emitted on completion).
pub fn take_last(source: types.Observable(a), count: Int) -> types.Observable(a) {
  filter.take_last(source, count)
}

// ============================================================================
// Utility
// ============================================================================

/// Create an empty disposable.
pub fn empty_disposable() -> types.Disposable {
  types.empty_disposable()
}

// ============================================================================
// Timeshift operators
// ============================================================================

/// Creates an observable that emits `0` after the specified delay, then completes.
pub fn timer(delay_ms: Int) -> types.Observable(Int) {
  timeshift.timer(delay_ms)
}

/// Creates an observable that emits incrementing integers (0, 1, 2, ...)
/// at regular intervals.
pub fn interval(period_ms: Int) -> types.Observable(Int) {
  timeshift.interval(period_ms)
}

/// Delays each emission from the source observable by the specified time.
pub fn delay(source: types.Observable(a), ms: Int) -> types.Observable(a) {
  timeshift.delay(source, ms)
}

/// Emits a value only after the specified time has passed without
/// another value being emitted.
pub fn debounce(source: types.Observable(a), ms: Int) -> types.Observable(a) {
  timeshift.debounce(source, ms)
}

/// Rate limits emissions to at most one per specified period.
pub fn throttle(source: types.Observable(a), ms: Int) -> types.Observable(a) {
  timeshift.throttle(source, ms)
}

// ============================================================================
// Subject operators
// ============================================================================

/// Creates a multicast subject that allows multiple subscribers.
///
/// Returns a tuple of (Observer, Observable) where:
/// - The Observer side is used to push notifications
/// - The Observable side can be subscribed to by multiple observers
///
/// Unlike single_subject, notifications are NOT buffered.
pub fn subject() -> #(types.Observer(a), types.Observable(a)) {
  subject.subject()
}

/// Creates a single-subscriber subject.
///
/// Returns a tuple of (Observer, Observable) where:
/// - The Observer side is used to push notifications
/// - The Observable side can be subscribed to (once only!)
pub fn single_subject() -> #(types.Observer(a), types.Observable(a)) {
  subject.single_subject()
}
