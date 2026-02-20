/// Process management for Factor
///
/// Provides FFI bindings for BEAM process operations:
/// spawn_linked, monitor, demonitor, kill, trap_exits.
/// These are the building blocks for boundary operators
/// that create supervision trees via reactive composition.
module Factor.Process

open Fable.Core

// --- Erlang FFI for process management ---

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

/// Send a child notification message to a process.
[<Emit("$0 ! {factor_child, $1, $2}")>]
let sendChildMsg (pid: obj) (id: int) (notification: obj) : unit = nativeOnly

/// Get self pid.
[<Emit("factor_actor:self_pid()")>]
let selfPid () : obj = nativeOnly
