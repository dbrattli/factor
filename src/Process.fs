/// Process management for Factor
///
/// Provides FFI bindings for BEAM process operations and
/// receive-based message loops for child/timer dispatching.
module Factor.Process

open Fable.Core

// ============================================================================
// Erlang FFI for process management
// ============================================================================

/// Spawn a linked process. If the child dies, the parent gets an EXIT signal.
[<Emit("factor_actor:spawn_linked($0)")>]
let spawnLinked (f: unit -> unit) : obj = nativeOnly

/// Monitor a process. Returns a monitor reference.
[<Emit("factor_actor:monitor_process($0)")>]
let monitorProcess (pid: obj) : obj = nativeOnly

/// Demonitor a process, flushing any pending DOWN message.
[<Emit("factor_actor:demonitor_process($0)")>]
let demonitorProcess (ref: obj) : unit = nativeOnly

/// Kill a process immediately.
[<Emit("factor_actor:kill_process($0)")>]
let killProcess (pid: obj) : unit = nativeOnly

/// Enable trap_exit so EXIT signals become messages.
[<Emit("factor_actor:trap_exits()")>]
let trapExits () : unit = nativeOnly

/// Get self pid.
[<Emit("factor_actor:self_pid()")>]
let selfPid () : obj = nativeOnly

/// Create a unique Erlang reference.
[<Emit("factor_actor:make_ref()")>]
let makeRef () : obj = nativeOnly

/// Register a child handler for a specific ref in the process dictionary.
[<Emit("factor_actor:register_child($0, $1)")>]
let registerChild (ref: obj) (handler: obj -> unit) : unit = nativeOnly

/// Unregister a child handler for a specific ref.
[<Emit("factor_actor:unregister_child($0)")>]
let unregisterChild (ref: obj) : unit = nativeOnly

/// Register an exit handler for a specific pid in the process dictionary.
[<Emit("factor_actor:register_exit($0, $1)")>]
let registerExit (pid: obj) (handler: obj -> unit) : unit = nativeOnly

/// Unregister an exit handler for a specific pid.
[<Emit("factor_actor:unregister_exit($0)")>]
let unregisterExit (pid: obj) : unit = nativeOnly

/// Send a child notification message to a process.
[<Emit("$0 ! {factor_child, $1, $2}")>]
let sendChildMsg (pid: obj) (ref: obj) (notification: obj) : unit = nativeOnly

/// Exit the current process normally.
[<Emit("factor_actor:exit_normal()")>]
let exitNormal () : unit = nativeOnly

/// Format a crash reason as a string.
[<Emit("factor_actor:format_reason($0)")>]
let formatReason (reason: obj) : string = nativeOnly

// ============================================================================
// Receive-based message loops
// ============================================================================

/// Message types received in process message loops.
/// Each case maps to an Erlang tuple tag via CompiledName.
type LoopMsg =
    | [<CompiledName("factor_timer")>] FactorTimer of ref: obj * callback: (unit -> unit)
    | [<CompiledName("factor_child")>] FactorChild of ref: obj * notification: obj
    | [<CompiledName("EXIT")>] Exit of pid: obj * reason: obj

/// Dispatch a child notification using the process dictionary registry.
/// Looks up the handler by ref in the factor_children map.
[<Emit("case erlang:get(factor_children) of undefined -> ok; FcM__ -> case FcM__ of #{$0 := FcH__} -> FcH__($1); #{} -> ok end end")>]
let private dispatchChild (ref: obj) (notification: obj) : unit = nativeOnly

/// Dispatch an exit signal using the process dictionary registry.
/// Normal exits are filtered; abnormal exits look up handler by pid.
[<Emit("case $1 of normal -> ok; _ -> case erlang:get(factor_exits) of undefined -> ok; FeM__ -> case FeM__ of #{$0 := FeH__} -> FeH__($1); #{} -> ok end end end")>]
let private dispatchExit (pid: obj) (reason: obj) : unit = nativeOnly

/// Child process message loop — F# implementation using Erlang.receive.
///
/// Blocks waiting for timer, child, and EXIT messages. Dispatches each
/// message using the process dictionary registries, then loops.
/// When a terminal event triggers exitNormal(), the process terminates.
let rec childLoop () : unit =
    match Erlang.receiveForever<LoopMsg> () with
    | FactorTimer (_, callback) ->
        callback ()
        childLoop ()
    | FactorChild (ref, notification) ->
        dispatchChild ref notification
        childLoop ()
    | Exit (pid, reason) ->
        dispatchExit pid reason
        childLoop ()

/// Timer-aware message pump loop — processes messages until endTime.
let rec private processTimersLoop (endTime: int) : unit =
    let remaining = endTime - Erlang.monotonicTimeMs ()

    if remaining <= 0 then
        ()
    else
        match Erlang.receive<LoopMsg> (min remaining 1) with
        | Some (FactorTimer (_, callback)) ->
            callback ()
            processTimersLoop endTime
        | Some (FactorChild (ref, notification)) ->
            dispatchChild ref notification
            processTimersLoop endTime
        | Some (Exit (pid, reason)) ->
            dispatchExit pid reason
            processTimersLoop endTime
        | None -> processTimersLoop endTime

/// Timer-aware sleep: processes pending timer, child, and EXIT messages
/// for the specified duration. Use this instead of timer:sleep to ensure
/// callbacks execute in the current process.
let processTimers (timeoutMs: int) : unit =
    let endTime = Erlang.monotonicTimeMs () + timeoutMs
    processTimersLoop endTime

// ============================================================================
// Stream actor FFI
// ============================================================================

/// Start a multicast stream actor process. Returns the pid.
[<Emit("factor_stream:start_stream()")>]
let startStream () : obj = nativeOnly

/// Start a single-subscriber stream actor process. Returns the pid.
[<Emit("factor_stream:start_single_stream()")>]
let startSingleStream () : obj = nativeOnly

/// Send a non-terminal notification to a stream actor.
[<Emit("$0 ! {stream_notify, $1}")>]
let streamNotify (pid: obj) (notification: obj) : unit = nativeOnly

/// Send a terminal notification to a stream actor (causes it to broadcast and exit).
[<Emit("$0 ! {stream_notify_terminal, $1}")>]
let streamNotifyTerminal (pid: obj) (notification: obj) : unit = nativeOnly

/// Synchronously subscribe to a stream actor. Blocks until ack received.
[<Emit("factor_stream:subscribe($0, $1)")>]
let streamSubscribe (pid: obj) (ref: obj) : unit = nativeOnly

/// Unsubscribe from a stream actor.
[<Emit("$0 ! {stream_unsubscribe, $1}")>]
let streamUnsubscribe (pid: obj) (ref: obj) : unit = nativeOnly
