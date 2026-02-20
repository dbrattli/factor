/// Process management for Factor
///
/// Provides FFI bindings for BEAM process operations:
/// spawn_linked, monitor, kill, trap_exits, and registry-based
/// child/exit handler dispatching for boundary operators.
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

/// Child process message loop - processes timer, child, and EXIT messages until process exits.
[<Emit("factor_actor:child_loop()")>]
let childLoop () : unit = nativeOnly

// --- Stream actor FFI ---

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
