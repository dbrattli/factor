/// Safe Observer - Observer wrapper that enforces Rx grammar
///
/// The safe observer wraps a downstream observer and:
/// 1. Enforces Rx grammar: OnNext* (OnError | OnCompleted)?
/// 2. Disposes resources on terminal events
/// 3. Ignores messages after terminal event
module Factor.SafeObserver

open Factor.Types

/// Wrap an observer with Rx grammar enforcement.
let wrap (observer: Observer<'a>) (disposable: Disposable) : Observer<'a> =
    let mutable stopped = false

    { Notify =
        fun n ->
            if not stopped then
                match n with
                | OnNext x -> observer.Notify(OnNext x)
                | OnError e ->
                    stopped <- true
                    disposable.Dispose()
                    observer.Notify(OnError e)
                | OnCompleted ->
                    stopped <- true
                    disposable.Dispose()
                    observer.Notify(OnCompleted) }

/// Create an observer from a notification handler function,
/// wrapped with Rx grammar enforcement.
let fromNotify (notifyFn: Notification<'a> -> unit) (disposable: Disposable) : Observer<'a> =
    let observer = { Notify = notifyFn }
    wrap observer disposable
