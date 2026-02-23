/// Core types for Factor - Composable Actors for BEAM
///
/// This module defines the fundamental types:
/// - Msg: The atoms of the Rx grammar (OnNext, OnError, OnCompleted)
/// - Handle: Resource cleanup handle
/// - Observer: Process endpoint that receives messages
/// - Sender: Push-side handle for channel actors
/// - Factor: Lazy push-based actor with exception-typed errors
module Factor.Types

// ============================================================================
// Exception types
// ============================================================================

exception FactorException of string
exception TimeoutException of string
exception SequenceEmptyException
exception ProcessExitException of string
exception ForkJoinException of string

/// Msg represents the three types of events in the Rx grammar:
/// OnNext* (OnError | OnCompleted)?
type Msg<'T> =
    | OnNext of 'T
    | OnError of exn
    | OnCompleted

/// Handle represents a resource that can be cleaned up.
type Handle = { Dispose: unit -> unit }

/// Create an empty handle that does nothing when disposed.
let emptyHandle () : Handle = { Dispose = fun () -> () }

/// Combine multiple handles into one.
let compositeHandle (handles: Handle list) : Handle = {
    Dispose =
        fun () ->
            for h in handles do
                h.Dispose()
}

/// Observer is a process endpoint — the (Pid, Ref) of a process
/// that receives {factor_child, Ref, Msg} messages.
type Observer<'T> = { Pid: obj; Ref: obj }

/// Push-side handle for channel actors.
type Sender<'T> = { ChannelPid: obj }

/// Factor is a lazy push-based actor with exception-typed errors.
type Factor<'T> = { Spawn: Observer<'T> -> Handle }

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
