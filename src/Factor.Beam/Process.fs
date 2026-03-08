/// BEAM process primitives for Factor.
///
/// Low-level FFI bindings for process management, the observer
/// message protocol, and Erlang reference comparison.
module Factor.Beam.Process

open Fable.Core
open Factor.Actor.Types

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

/// Compare two Erlang references for equality (F# `=` cannot compare refs).
[<Emit("$0 =:= $1")>]
let refEquals (a: obj) (b: obj) : bool = nativeOnly

// ============================================================================
// Observer helpers (the {factor_child, Ref, Msg} protocol)
// ============================================================================

/// Send a notification to an observer process endpoint.
let notify (observer: Observer<'T>) (msg: Msg<'T>) : unit =
    sendChildMsg observer.Pid observer.Ref (box msg)

/// Send an OnNext message to an observer.
let onNext (observer: Observer<'T>) (value: 'T) : unit = notify observer (OnNext value)

/// Send an OnError message to an observer.
let onError (observer: Observer<'T>) (error: exn) : unit = notify observer (OnError error)

/// Send an OnCompleted message to an observer.
let onCompleted (observer: Observer<'T>) : unit = notify observer OnCompleted
