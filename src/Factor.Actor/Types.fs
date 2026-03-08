/// Core types for Factor — the cross-platform contract.
///
/// Contains both abstract actor types and Rx-specific types.
/// No platform code, no dependencies beyond Fable.Core.
module Factor.Actor.Types

// ============================================================================
// Actor types
// ============================================================================

/// Opaque typed actor wrapping a platform-specific process identifier.
type Actor<'Msg> = { Pid: obj }

/// A reply channel that the receiver calls to send a response back to the caller.
type ReplyChannel<'Reply> = { Reply: 'Reply -> unit }

/// What the actor should do after handling a message.
type Next<'State> =
    | Continue of 'State
    | Stop

// ============================================================================
// Exception types
// ============================================================================

exception FactorException of string
exception TimeoutException of string
exception SequenceEmptyException
exception ProcessExitException of string
exception ForkJoinException of string

// ============================================================================
// Rx types
// ============================================================================

/// Msg represents the three types of events in the Rx grammar:
/// OnNext* (OnError | OnCompleted)?
type Msg<'T> =
    | OnNext of 'T
    | OnError of exn
    | OnCompleted

/// Handle represents a resource that can be cleaned up.
type Handle = { Dispose: unit -> unit }

/// Create an empty handle that does nothing when disposed.
[<Fable.Core.Emit("#{dispose => fun(_) -> ok end}")>]
let emptyHandle () : Handle = { Dispose = ignore }

/// Combine multiple handles into one.
[<Fable.Core.Emit("#{dispose => fun(_) -> lists:foreach(fun(H) -> (maps:get(dispose, H))(ok) end, $0) end}")>]
let compositeHandle (handles: Handle list) : Handle =
    { Dispose =
        fun () ->
            for h in handles do
                h.Dispose() }

/// Observer is a process endpoint — the (Pid, Ref) of a process
/// that receives {factor_child, Ref, Msg} messages.
type Observer<'T> = { Pid: obj; Ref: obj }

/// Channel protocol messages — parameterizes channel actor behavior.
type ChannelMsg<'T> =
    | Notify of Msg<'T>
    | Subscribe of Observer<'T> * ReplyChannel<unit>
    | Unsubscribe of obj

/// Observable is a lazy push-based stream with exception-typed errors.
type Observable<'T> = { Subscribe: Observer<'T> -> Handle }

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
