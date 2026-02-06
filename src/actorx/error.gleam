//// Error handling operators for ActorX
////
//// These operators handle errors in observable sequences:
//// - retry: Resubscribe on error up to N times
//// - catch: Switch to fallback observable on error

import actorx/types.{
  type Disposable, type Observable, type Observer, Disposable, Observable,
  Observer, OnCompleted, OnError, OnNext,
}
import gleam/erlang/process.{type Subject}
import gleam/option.{type Option, None, Some}

// ============================================================================
// retry - Resubscribe on error
// ============================================================================

/// Messages for the retry actor
type RetryMsg(a) {
  RetryNext(a)
  RetryError(String)
  RetryCompleted
  RetryDispose
  /// Set the current subscription disposable
  RetrySetDisposable(Disposable)
}

/// Resubscribes to the source observable when an error occurs,
/// up to the specified number of retries.
///
/// ## Example
/// ```gleam
/// // Retry up to 3 times on error
/// flaky_observable
/// |> retry(3)
/// ```
pub fn retry(source: Observable(a), max_retries: Int) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(RetryMsg(a))) = process.new_subject()

    // Spawn actor to manage retry state
    process.spawn(fn() {
      let control: Subject(RetryMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      retry_loop(control, downstream, source, max_retries, 0, None)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create retry actor"
    }

    // Initial subscription from parent process (preserves original behavior)
    let initial_disp = subscribe_with_retry(source, control)
    // Tell actor about the disposable
    process.send(control, RetrySetDisposable(initial_disp))

    Disposable(dispose: fn() {
      process.send(control, RetryDispose)
      Nil
    })
  })
}

/// Subscribe to source and return the disposable
fn subscribe_with_retry(
  source: Observable(a),
  control: Subject(RetryMsg(a)),
) -> Disposable {
  let source_observer =
    Observer(notify: fn(n) {
      case n {
        OnNext(x) -> process.send(control, RetryNext(x))
        OnError(e) -> process.send(control, RetryError(e))
        OnCompleted -> process.send(control, RetryCompleted)
      }
    })

  let Observable(subscribe) = source
  subscribe(source_observer)
}

fn retry_loop(
  control: Subject(RetryMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  source: Observable(a),
  max_retries: Int,
  retry_count: Int,
  current_disp: Option(Disposable),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    RetrySetDisposable(disp) -> {
      // Store the disposable from initial subscription
      retry_loop(
        control,
        downstream,
        source,
        max_retries,
        retry_count,
        Some(disp),
      )
    }
    RetryNext(x) -> {
      downstream(OnNext(x))
      retry_loop(
        control,
        downstream,
        source,
        max_retries,
        retry_count,
        current_disp,
      )
    }
    RetryError(e) -> {
      case retry_count < max_retries {
        True -> {
          // Dispose previous subscription before retrying
          case current_disp {
            Some(Disposable(dispose)) -> dispose()
            None -> Nil
          }
          // Retry: resubscribe to source
          let new_disp = subscribe_with_retry(source, control)
          retry_loop(
            control,
            downstream,
            source,
            max_retries,
            retry_count + 1,
            Some(new_disp),
          )
        }
        False -> {
          // Max retries reached, dispose and propagate error
          case current_disp {
            Some(Disposable(dispose)) -> dispose()
            None -> Nil
          }
          downstream(OnError(e))
          Nil
        }
      }
    }
    RetryCompleted -> {
      // Dispose current subscription on completion
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }
      downstream(OnCompleted)
      Nil
    }
    RetryDispose -> {
      // Dispose current subscription
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }
      Nil
    }
  }
}

// ============================================================================
// catch - Switch to fallback on error
// ============================================================================

/// Messages for the catch actor
type CatchMsg(a) {
  CatchNext(a)
  CatchError(String)
  CatchCompleted
  CatchDispose
  /// Set the current subscription disposable
  CatchSetDisposable(Disposable)
  // Fallback messages - errors from fallback propagate, not caught again
  FallbackNext(a)
  FallbackError(String)
  FallbackCompleted
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
  source: Observable(a),
  handler: fn(String) -> Observable(a),
) -> Observable(a) {
  Observable(subscribe: fn(observer: Observer(a)) {
    let Observer(downstream) = observer

    // Create control channel
    let control_ready: Subject(Subject(CatchMsg(a))) = process.new_subject()

    // Spawn actor to manage state
    process.spawn(fn() {
      let control: Subject(CatchMsg(a)) = process.new_subject()
      process.send(control_ready, control)
      catch_loop(control, downstream, handler, None)
    })

    // Get control subject
    let control = case process.receive(control_ready, 1000) {
      Ok(s) -> s
      Error(_) -> panic as "Failed to create catch actor"
    }

    // Initial subscription from parent process (preserves original behavior)
    let source_disp = subscribe_to_source(source, control)
    // Tell actor about the disposable
    process.send(control, CatchSetDisposable(source_disp))

    Disposable(dispose: fn() {
      process.send(control, CatchDispose)
      Nil
    })
  })
}

/// Subscribe to source and return disposable
fn subscribe_to_source(
  source: Observable(a),
  control: Subject(CatchMsg(a)),
) -> Disposable {
  let source_observer =
    Observer(notify: fn(n) {
      case n {
        OnNext(x) -> process.send(control, CatchNext(x))
        OnError(e) -> process.send(control, CatchError(e))
        OnCompleted -> process.send(control, CatchCompleted)
      }
    })

  let Observable(subscribe) = source
  subscribe(source_observer)
}

fn catch_loop(
  control: Subject(CatchMsg(a)),
  downstream: fn(types.Notification(a)) -> Nil,
  handler: fn(String) -> Observable(a),
  current_disp: Option(Disposable),
) -> Nil {
  let selector =
    process.new_selector()
    |> process.select(control)

  case process.selector_receive_forever(selector) {
    CatchSetDisposable(disp) -> {
      // Store the disposable from initial subscription
      catch_loop(control, downstream, handler, Some(disp))
    }
    CatchNext(x) -> {
      downstream(OnNext(x))
      catch_loop(control, downstream, handler, current_disp)
    }
    CatchError(e) -> {
      // Dispose source subscription before switching to fallback
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }

      // Switch to fallback observable
      let fallback = handler(e)
      let fallback_observer =
        Observer(notify: fn(n) {
          case n {
            // Fallback emissions use different message types
            OnNext(x) -> process.send(control, FallbackNext(x))
            OnError(err) -> process.send(control, FallbackError(err))
            OnCompleted -> process.send(control, FallbackCompleted)
          }
        })

      let Observable(subscribe_fallback) = fallback
      let fallback_disp = subscribe_fallback(fallback_observer)

      // Continue loop with fallback disposable
      catch_loop(control, downstream, handler, Some(fallback_disp))
    }
    CatchCompleted -> {
      // Dispose current subscription on completion
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }
      downstream(OnCompleted)
      Nil
    }
    CatchDispose -> {
      // Dispose current subscription (source or fallback)
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }
      Nil
    }
    // Fallback messages - forward directly, don't catch errors
    FallbackNext(x) -> {
      downstream(OnNext(x))
      catch_loop(control, downstream, handler, current_disp)
    }
    FallbackError(e) -> {
      // Dispose fallback subscription on error
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }
      // Propagate fallback errors downstream (to be caught by outer catch if any)
      downstream(OnError(e))
      Nil
    }
    FallbackCompleted -> {
      // Dispose fallback subscription on completion
      case current_disp {
        Some(Disposable(dispose)) -> dispose()
        None -> Nil
      }
      downstream(OnCompleted)
      Nil
    }
  }
}
