/// Core types for Factor - Composable Actors for BEAM
///
/// This module defines the fundamental types:
/// - Notification: The atoms of the Rx grammar (OnNext, OnError, OnCompleted)
/// - Handle: Resource cleanup handle
/// - Handler: Receives notifications from a Factor
/// - Factor: Lazy push-based stream with exception-typed errors
module Factor.Types

// ============================================================================
// Exception types
// ============================================================================

exception FactorException of string
exception TimeoutException of string
exception SequenceEmptyException
exception ProcessExitException of string
exception ForkJoinException of string

/// Notification represents the three types of events in the Rx grammar:
/// OnNext* (OnError | OnCompleted)?
type Notification<'T> =
    | OnNext of 'T
    | OnError of exn
    | OnCompleted

/// Handle represents a resource that can be cleaned up.
type Handle = { Dispose: unit -> unit }

/// Create an empty handle that does nothing when disposed.
let emptyHandle () : Handle = { Dispose = fun () -> () }

/// Combine multiple handles into one.
let compositeHandle (handles: Handle list) : Handle =
    { Dispose =
        fun () ->
            for h in handles do
                h.Dispose() }

/// Handler receives notifications from a Factor.
type Handler<'T> = { Notify: Notification<'T> -> unit }

/// Create a handler from three callback functions.
let makeHandler (onNext: 'T -> unit) (onError: exn -> unit) (onCompleted: unit -> unit) : Handler<'T> =
    { Notify =
        fun n ->
            match n with
            | OnNext x -> onNext x
            | OnError e -> onError e
            | OnCompleted -> onCompleted () }

/// Create a handler that only handles OnNext events.
let makeNextHandler (onNext: 'T -> unit) : Handler<'T> =
    { Notify =
        fun n ->
            match n with
            | OnNext x -> onNext x
            | _ -> () }

/// Send an OnNext notification to a handler.
let onNext (handler: Handler<'T>) (value: 'T) : unit = handler.Notify(OnNext value)

/// Send an OnError notification to a handler.
let onError (handler: Handler<'T>) (error: exn) : unit = handler.Notify(OnError error)

/// Send an OnCompleted notification to a handler.
let onCompleted (handler: Handler<'T>) : unit = handler.Notify(OnCompleted)

/// Forward a notification to a handler.
let notify (handler: Handler<'T>) (notification: Notification<'T>) : unit = handler.Notify(notification)

/// Factor is a lazy push-based stream with exception-typed errors.
type Factor<'T> = { Subscribe: Handler<'T> -> Handle }

/// Supervision policy for spawned child processes.
///
/// Controls what happens when a child process crashes:
/// - Terminate: propagate as OnError, kill entire pipeline (default)
/// - Skip: ignore the crash, continue with other inners
/// - Restart: resubscribe the failed inner, up to N times
type SupervisionPolicy =
    | Terminate
    | Skip
    | Restart of int
