/// Safe Observer - Observer wrapper that enforces Rx grammar
///
/// The safe observer wraps a downstream observer and:
/// 1. Enforces Rx grammar: OnNext* (OnError | OnCompleted)?
/// 2. Disposes resources on terminal events
/// 3. Ignores messages after terminal event
module Factor.SafeObserver

open Factor.Types

/// Wrap an observer with Rx grammar enforcement.
let wrap (observer: Observer<'T>) (handle: Handle) : Observer<'T> =
    let mutable stopped = false

    {
        Send =
            fun n ->
                if not stopped then
                    match n with
                    | OnNext x -> observer.Send(OnNext x)
                    | OnError e ->
                        stopped <- true
                        handle.Dispose()
                        observer.Send(OnError e)
                    | OnCompleted ->
                        stopped <- true
                        handle.Dispose()
                        observer.Send(OnCompleted)
    }

/// Create an observer from a message handler function,
/// wrapped with Rx grammar enforcement.
let fromSend (notifyFn: Msg<'T> -> unit) (handle: Handle) : Observer<'T> =
    let observer = { Send = notifyFn }
    wrap observer handle
