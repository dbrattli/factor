/// Safe Handler - Handler wrapper that enforces Rx grammar
///
/// The safe handler wraps a downstream handler and:
/// 1. Enforces Rx grammar: OnNext* (OnError | OnCompleted)?
/// 2. Disposes resources on terminal events
/// 3. Ignores messages after terminal event
module Factor.SafeHandler

open Factor.Types

/// Wrap a handler with Rx grammar enforcement.
let wrap (handler: Handler<'T, 'E>) (handle: Handle) : Handler<'T, 'E> =
    let mutable stopped = false

    { Notify =
        fun n ->
            if not stopped then
                match n with
                | OnNext x -> handler.Notify(OnNext x)
                | OnError e ->
                    stopped <- true
                    handle.Dispose()
                    handler.Notify(OnError e)
                | OnCompleted ->
                    stopped <- true
                    handle.Dispose()
                    handler.Notify(OnCompleted) }

/// Create a handler from a notification handler function,
/// wrapped with Rx grammar enforcement.
let fromNotify (notifyFn: Notification<'T, 'E> -> unit) (handle: Handle) : Handler<'T, 'E> =
    let handler = { Notify = notifyFn }
    wrap handler handle
