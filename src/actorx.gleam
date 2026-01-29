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

import actorx/combine
import actorx/create
import actorx/error
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

/// Transform each element using a mapper function that also receives the index.
///
/// ## Example
/// ```gleam
/// from_list(["a", "b", "c"])
/// |> mapi(fn(x, i) { #(i, x) })
/// // Emits: #(0, "a"), #(1, "b"), #(2, "c")
/// ```
pub fn mapi(
  source: types.Observable(a),
  mapper: fn(a, Int) -> b,
) -> types.Observable(b) {
  transform.mapi(source, mapper)
}

/// Project each element to an observable and flatten.
///
/// Composed from `map` and `merge_inner`:
/// `flat_map(source, f) = source |> map(f) |> merge_inner()`
pub fn flat_map(
  source: types.Observable(a),
  mapper: fn(a) -> types.Observable(b),
) -> types.Observable(b) {
  transform.flat_map(source, mapper)
}

/// Project each element and its index to an observable and flatten.
///
/// Composed from `mapi` and `merge_inner`:
/// `flat_mapi(source, f) = source |> mapi(f) |> merge_inner()`
pub fn flat_mapi(
  source: types.Observable(a),
  mapper: fn(a, Int) -> types.Observable(b),
) -> types.Observable(b) {
  transform.flat_mapi(source, mapper)
}

/// Project each element to an observable and concatenate in order.
///
/// Composed from `map` and `concat_inner`:
/// `concat_map(source, f) = source |> map(f) |> concat_inner()`
///
/// Unlike `flat_map`, this preserves the order of inner observables.
pub fn concat_map(
  source: types.Observable(a),
  mapper: fn(a) -> types.Observable(b),
) -> types.Observable(b) {
  transform.concat_map(source, mapper)
}

/// Project each element and its index to an observable and concatenate in order.
///
/// Composed from `mapi` and `concat_inner`:
/// `concat_mapi(source, f) = source |> mapi(f) |> concat_inner()`
pub fn concat_mapi(
  source: types.Observable(a),
  mapper: fn(a, Int) -> types.Observable(b),
) -> types.Observable(b) {
  transform.concat_mapi(source, mapper)
}

/// Flattens an Observable of Observables by merging inner emissions.
///
/// Subscribes to each inner observable as it arrives and forwards all
/// emissions. Completes when outer AND all inners complete.
pub fn merge_inner(
  source: types.Observable(types.Observable(a)),
) -> types.Observable(a) {
  transform.merge_inner(source)
}

/// Flattens an Observable of Observables by concatenating in order.
///
/// Subscribes to each inner observable only after the previous one
/// completes. Queues inner observables and processes them sequentially.
pub fn concat_inner(
  source: types.Observable(types.Observable(a)),
) -> types.Observable(a) {
  transform.concat_inner(source)
}

/// Flattens an Observable of Observables by switching to the latest.
///
/// When a new inner arrives, the previous inner is cancelled.
/// Useful for search-as-you-type patterns.
pub fn switch_inner(
  source: types.Observable(types.Observable(a)),
) -> types.Observable(a) {
  transform.switch_inner(source)
}

/// Project each element to an observable and switch to the latest.
///
/// Composed from `map` and `switch_inner`:
/// `switch_map(source, f) = source |> map(f) |> switch_inner()`
pub fn switch_map(
  source: types.Observable(a),
  mapper: fn(a) -> types.Observable(b),
) -> types.Observable(b) {
  transform.switch_map(source, mapper)
}

/// Project each element and its index to an observable and switch to latest.
pub fn switch_mapi(
  source: types.Observable(a),
  mapper: fn(a, Int) -> types.Observable(b),
) -> types.Observable(b) {
  transform.switch_mapi(source, mapper)
}

/// Performs a side effect for each emission without transforming.
pub fn tap(
  source: types.Observable(a),
  effect: fn(a) -> Nil,
) -> types.Observable(a) {
  transform.tap(source, effect)
}

/// Prepends values before the source emissions.
pub fn start_with(
  source: types.Observable(a),
  values: List(a),
) -> types.Observable(a) {
  transform.start_with(source, values)
}

/// Emits consecutive pairs of values.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3, 4])
/// |> pairwise()
/// // Emits: #(1, 2), #(2, 3), #(3, 4)
/// ```
pub fn pairwise(source: types.Observable(a)) -> types.Observable(#(a, a)) {
  transform.pairwise(source)
}

/// Applies an accumulator function over the source, emitting each
/// intermediate result.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3, 4, 5])
/// |> scan(0, fn(acc, x) { acc + x })
/// // Emits: 1, 3, 6, 10, 15
/// ```
pub fn scan(
  source: types.Observable(a),
  initial: b,
  accumulator: fn(b, a) -> b,
) -> types.Observable(b) {
  transform.scan(source, initial, accumulator)
}

/// Applies an accumulator function over the source, emitting only
/// the final accumulated value when the source completes.
///
/// ## Example
/// ```gleam
/// from_list([1, 2, 3, 4, 5])
/// |> reduce(0, fn(acc, x) { acc + x })
/// // Emits: 15, then completes
/// ```
pub fn reduce(
  source: types.Observable(a),
  initial: b,
  accumulator: fn(b, a) -> b,
) -> types.Observable(b) {
  transform.reduce(source, initial, accumulator)
}

/// Groups elements by key, returning an observable of grouped observables.
///
/// Each time a new key is encountered, emits a tuple of (key, Observable).
/// All elements with that key are forwarded to the corresponding group's
/// observable.
///
/// ## Example
/// ```gleam
/// // Group numbers by even/odd
/// from_list([1, 2, 3, 4, 5, 6])
/// |> group_by(fn(x) { x % 2 })
/// |> flat_map(fn(group) {
///   let #(key, values) = group
///   values |> map(fn(v) { #(key, v) })
/// })
/// ```
pub fn group_by(
  source: types.Observable(a),
  key_selector: fn(a) -> k,
) -> types.Observable(#(k, types.Observable(a))) {
  transform.group_by(source, key_selector)
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

/// Take only the first element. Errors if source is empty.
pub fn first(source: types.Observable(a)) -> types.Observable(a) {
  filter.first(source)
}

/// Take only the last element. Errors if source is empty.
pub fn last(source: types.Observable(a)) -> types.Observable(a) {
  filter.last(source)
}

/// Emit default value if source completes empty.
pub fn default_if_empty(
  source: types.Observable(a),
  default: a,
) -> types.Observable(a) {
  filter.default_if_empty(source, default)
}

/// Sample source when sampler emits.
pub fn sample(
  source: types.Observable(a),
  sampler: types.Observable(b),
) -> types.Observable(a) {
  filter.sample(source, sampler)
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
/// ```
pub fn publish(
  source: types.Observable(a),
) -> #(types.Observable(a), fn() -> types.Disposable) {
  subject.publish(source)
}

/// Shares a single subscription to the source among multiple subscribers.
///
/// Automatically connects when the first subscriber subscribes,
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
/// ```
pub fn share(source: types.Observable(a)) -> types.Observable(a) {
  subject.share(source)
}

// ============================================================================
// Combining operators
// ============================================================================

/// Merges multiple observable sequences into a single observable sequence.
/// Values are emitted as they arrive from any source.
/// Completes when all sources complete.
pub fn merge(sources: List(types.Observable(a))) -> types.Observable(a) {
  combine.merge(sources)
}

/// Merge two observables.
pub fn merge2(
  source1: types.Observable(a),
  source2: types.Observable(a),
) -> types.Observable(a) {
  combine.merge2(source1, source2)
}

/// Combines the latest values from two observable sequences.
/// Emits whenever either source emits, after both have emitted at least once.
/// Completes when both sources complete.
pub fn combine_latest(
  source1: types.Observable(a),
  source2: types.Observable(b),
  combiner: fn(a, b) -> c,
) -> types.Observable(c) {
  combine.combine_latest(source1, source2, combiner)
}

/// Combines the source observable with the latest value from another observable.
/// Emits only when the source emits, pairing with the latest value from sampler.
/// Completes when source completes.
pub fn with_latest_from(
  source: types.Observable(a),
  sampler: types.Observable(b),
  combiner: fn(a, b) -> c,
) -> types.Observable(c) {
  combine.with_latest_from(source, sampler, combiner)
}

/// Combines two observable sequences by pairing their elements by index.
/// Emits when both sources have emitted a value at the same index.
/// Completes when either source completes.
pub fn zip(
  source1: types.Observable(a),
  source2: types.Observable(b),
  combiner: fn(a, b) -> c,
) -> types.Observable(c) {
  combine.zip(source1, source2, combiner)
}

/// Concatenates multiple observables sequentially.
/// Subscribes to each source only after the previous completes.
pub fn concat(sources: List(types.Observable(a))) -> types.Observable(a) {
  combine.concat(sources)
}

/// Concatenates two observables sequentially.
pub fn concat2(
  source1: types.Observable(a),
  source2: types.Observable(a),
) -> types.Observable(a) {
  combine.concat2(source1, source2)
}

// ============================================================================
// Error handling operators
// ============================================================================

/// Resubscribes to the source observable when an error occurs,
/// up to the specified number of retries.
///
/// ## Example
/// ```gleam
/// // Retry up to 3 times on error
/// flaky_observable
/// |> retry(3)
/// ```
pub fn retry(
  source: types.Observable(a),
  max_retries: Int,
) -> types.Observable(a) {
  error.retry(source, max_retries)
}

/// On error, switches to a fallback observable returned by the handler.
/// Also known as `catch_error` or `on_error_resume_next`.
///
/// ## Example
/// ```gleam
/// // On error, emit a default value
/// risky_observable
/// |> catch(fn(_error) { single(default_value) })
/// ```
pub fn catch(
  source: types.Observable(a),
  handler: fn(String) -> types.Observable(a),
) -> types.Observable(a) {
  error.catch(source, handler)
}
