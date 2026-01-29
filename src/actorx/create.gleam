//// Creation operators for ActorX
////
//// These functions create new Observable sequences from various sources:
//// - single: Single value then complete
//// - empty: Complete immediately
//// - never: Never emit or complete
//// - fail: Error immediately
//// - from_list: Emit all items from a list
//// - defer: Factory function called on each subscription

import actorx/types.{
  type Disposable, type Observable, type Observer, Observable, Observer,
  OnCompleted, OnNext, empty_disposable,
}
import gleam/list

/// Create an observable from a subscribe function.
pub fn create(subscribe: fn(Observer(a)) -> Disposable) -> Observable(a) {
  Observable(subscribe: subscribe)
}

/// Returns an observable sequence containing a single element.
pub fn single(value: a) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(notify) = observer
    notify(OnNext(value))
    notify(OnCompleted)
    empty_disposable()
  })
}

/// Returns an observable sequence with no elements that completes immediately.
pub fn empty() -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(notify) = observer
    notify(OnCompleted)
    empty_disposable()
  })
}

/// Returns an observable sequence that never emits and never completes.
pub fn never() -> Observable(a) {
  Observable(subscribe: fn(_observer: Observer(a)) { empty_disposable() })
}

/// Returns an observable sequence that errors immediately.
pub fn fail(error: String) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(notify) = observer
    notify(types.OnError(error))
    empty_disposable()
  })
}

/// Returns an observable sequence from a list of values.
/// Emits each value in order, then completes.
pub fn from_list(items: List(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(notify) = observer
    list.each(items, fn(x) { notify(OnNext(x)) })
    notify(OnCompleted)
    empty_disposable()
  })
}

/// Returns an observable that invokes the factory function
/// whenever a new observer subscribes.
pub fn defer(factory: fn() -> Observable(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observable(subscribe) = factory()
    subscribe(observer)
  })
}
