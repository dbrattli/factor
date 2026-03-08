/// SafeObserver — Send-side Rx grammar enforcement
///
/// Wraps user callbacks at the subscribe boundary to enforce:
/// OnNext* (OnError | OnCompleted)?
///
/// Operators enforce Rx grammar via process termination;
/// SafeObserver is only needed at the user-facing subscribe edge.
module Factor.Reactive.SafeObserver

open Factor.Agent.Types

/// A safe observer wrapping user callbacks with Rx grammar enforcement.
type SafeObserver<'T> = {
    OnNext: 'T -> unit
    OnError: exn -> unit
    OnCompleted: unit -> unit
}

/// Create a SafeObserver from user callbacks.
/// Ensures at most one terminal event, ignores events after terminal.
let create
    (onNextFn: 'T -> unit)
    (onErrorFn: exn -> unit)
    (onCompletedFn: unit -> unit)
    (dispose: unit -> unit)
    : SafeObserver<'T>
    =
    let mutable stopped = false

    {
        OnNext =
            fun x ->
                if not stopped then
                    onNextFn x
        OnError =
            fun e ->
                if not stopped then
                    stopped <- true
                    dispose ()
                    onErrorFn e
        OnCompleted =
            fun () ->
                if not stopped then
                    stopped <- true
                    dispose ()
                    onCompletedFn ()
    }
