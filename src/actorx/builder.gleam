//// Builder module for ActorX
////
//// Provides `use`-friendly functions for composing observables in a
//// monadic style, similar to F#'s computation expressions.
////
//// ## Example
////
//// ```gleam
//// import actorx
//// import actorx/builder.{bind, return}
////
//// pub fn example() -> Observable(Int) {
////   use x <- bind(actorx.single(10))
////   use y <- bind(actorx.single(20))
////   use z <- bind(actorx.from_list([1, 2, 3]))
////   return(x + y + z)
//// }
//// // Emits: 31, 32, 33 then completes
//// ```
////
//// The `use` keyword in Gleam desugars to callback passing:
//// ```gleam
//// use x <- bind(obs)
//// rest...
//// ```
//// becomes:
//// ```gleam
//// bind(obs, fn(x) { rest... })
//// ```

import actorx/types.{
  type Observable, type Observer, Observable, Observer, OnCompleted, OnError,
  OnNext,
}

/// Bind an observable to a continuation function.
/// This is `flatMap` with arguments ordered for Gleam's `use` syntax.
///
/// ## Example
/// ```gleam
/// use value <- bind(actorx.single(42))
/// return(value * 2)
/// ```
pub fn bind(
  source: Observable(a),
  continuation: fn(a) -> Observable(b),
) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> {
            // For each value, subscribe to the continuation observable
            let inner = continuation(x)
            let Observable(inner_subscribe) = inner
            let inner_observer =
              Observer(notify: fn(inner_n) {
                case inner_n {
                  OnNext(value) -> downstream(OnNext(value))
                  OnError(e) -> downstream(OnError(e))
                  // Inner completion doesn't complete outer
                  OnCompleted -> Nil
                }
              })
            let _ = inner_subscribe(inner_observer)
            Nil
          }
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> downstream(OnCompleted)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Lift a pure value into an observable (like `return` or `pure`).
/// Alias for `single` but named for monadic style.
///
/// ## Example
/// ```gleam
/// use x <- bind(some_observable)
/// return(x * 2)
/// ```
pub fn return(value: a) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    downstream(OnNext(value))
    downstream(OnCompleted)
    types.empty_disposable()
  })
}

/// Alias for `return` - lifts a value into an observable.
pub fn pure(value: a) -> Observable(a) {
  return(value)
}

/// Yield from another observable (identity for observables).
/// Useful for yielding an existing observable in a `use` chain.
pub fn yield_from(source: Observable(a)) -> Observable(a) {
  source
}

/// Combine two observables sequentially (concat).
pub fn combine(first: Observable(a), second: Observable(a)) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let first_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> downstream(OnNext(x))
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> {
            // When first completes, subscribe to second
            let Observable(second_subscribe) = second
            let _ = second_subscribe(observer)
            Nil
          }
        }
      })

    let Observable(first_subscribe) = first
    first_subscribe(first_observer)
  })
}

/// Map over an observable (functor map).
/// Can also be used with `use` for transformations.
///
/// ## Example
/// ```gleam
/// use x <- map_over(actorx.from_list([1, 2, 3]))
/// x * 10
/// ```
pub fn map_over(source: Observable(a), mapper: fn(a) -> b) -> Observable(b) {
  Observable(subscribe: fn(observer: Observer(b)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) -> downstream(OnNext(mapper(x)))
          OnError(e) -> downstream(OnError(e))
          OnCompleted -> downstream(OnCompleted)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// Filter with `use` syntax.
///
/// ## Example
/// ```gleam
/// use x <- filter_with(actorx.from_list([1, 2, 3, 4, 5]))
/// x > 2
/// ```
/// Returns observable of values where the predicate returns True.
pub fn filter_with(
  source: Observable(a),
  predicate: fn(a) -> Bool,
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    let upstream_observer =
      Observer(notify: fn(n) {
        case n {
          OnNext(x) ->
            case predicate(x) {
              True -> downstream(OnNext(x))
              False -> Nil
            }
          _ -> downstream(n)
        }
      })

    let Observable(subscribe) = source
    subscribe(upstream_observer)
  })
}

/// For each item in a list, apply a function and concat results.
/// Similar to F#'s `for` in computation expressions.
///
/// ## Example
/// ```gleam
/// for_each([1, 2, 3], fn(x) {
///   actorx.single(x * 10)
/// })
/// // Emits: 10, 20, 30
/// ```
pub fn for_each(items: List(a), f: fn(a) -> Observable(b)) -> Observable(b) {
  case items {
    [] -> empty()
    [head, ..tail] -> combine(f(head), for_each(tail, f))
  }
}

/// Empty observable - completes immediately with no values.
pub fn empty() -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer
    downstream(OnCompleted)
    types.empty_disposable()
  })
}

/// Zero for the monoid - same as empty.
pub fn zero() -> Observable(a) {
  empty()
}
