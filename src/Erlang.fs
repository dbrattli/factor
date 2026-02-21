/// Erlang interop for Factor
///
/// Provides typed wrappers for BEAM-specific operations that require
/// compiler support (selective receive) or direct Erlang FFI.
///
/// The receive/receiveForever functions use marker Emit strings that
/// Fable.Beam intercepts and expands into proper Erlang receive expressions.
/// Each DU case maps to a receive clause with the case's CompiledName
/// (or snake_case name) as the Erlang atom tag.
module Factor.Erlang

open Fable.Core

// ============================================================================
// Selective receive
// ============================================================================

/// Selective receive with timeout (milliseconds).
/// Returns Some(msg) if a matching message arrives, None on timeout.
///
/// The type parameter must be a DU where each case maps to an Erlang
/// message pattern. Use [<CompiledName("atom")>] to control the tag atom.
///
/// Example:
///   type Msg =
///       | [<CompiledName("factor_timer")>] Timer of ref: obj * callback: (obj -> unit)
///       | [<CompiledName("EXIT")>] Exit of pid: obj * reason: obj
///
///   match Erlang.receive<Msg>(1000) with
///   | Some(Timer(ref, cb)) -> cb(obj())
///   | Some(Exit(pid, reason)) -> handleExit pid reason
///   | None -> () // timeout
[<Emit("__fable_beam_receive__($0)")>]
let receive<'T> (timeoutMs: int) : 'T option = nativeOnly

/// Blocking selective receive (no timeout).
/// Blocks the current process until a matching message arrives.
///
/// Same DU mapping as receive, but never times out.
[<Emit("__fable_beam_receive_forever__")>]
let receiveForever<'T> () : 'T = nativeOnly

// ============================================================================
// Erlang BIFs (Built-in Functions)
// ============================================================================

/// Get the current process's pid.
[<Emit("erlang:self()")>]
let self () : obj = nativeOnly

/// Create a unique reference.
[<Emit("erlang:make_ref()")>]
let makeRef () : obj = nativeOnly

/// Spawn a linked process.
[<Emit("erlang:spawn_link(fun() -> $0(ok) end)")>]
let spawnLink (f: unit -> unit) : obj = nativeOnly

/// Send a message to a process (Pid ! Msg).
[<Emit("$0 ! $1")>]
let send (pid: obj) (msg: obj) : unit = nativeOnly

/// Enable trap_exit so EXIT signals become messages.
[<Emit("erlang:process_flag(trap_exit, true)")>]
let trapExit () : unit = nativeOnly

/// Exit the current process with a reason.
[<Emit("erlang:exit($0)")>]
let exit (reason: obj) : unit = nativeOnly

/// Exit the current process normally.
[<Emit("erlang:exit(normal)")>]
let exitNormal () : unit = nativeOnly

/// Kill a process immediately.
[<Emit("erlang:exit($0, kill)")>]
let killProcess (pid: obj) : unit = nativeOnly

/// Monitor a process.
[<Emit("erlang:monitor(process, $0)")>]
let monitorProcess (pid: obj) : obj = nativeOnly

/// Demonitor a process with flush.
[<Emit("erlang:demonitor($0, [flush])")>]
let demonitorProcess (ref: obj) : unit = nativeOnly

/// Schedule a message to be sent after Ms milliseconds.
[<Emit("erlang:send_after($0, erlang:self(), $1)")>]
let sendAfter (ms: int) (msg: obj) : obj = nativeOnly

/// Cancel a timer.
[<Emit("erlang:cancel_timer($0)")>]
let cancelTimer (timerRef: obj) : unit = nativeOnly

/// Get the current monotonic time in milliseconds.
[<Emit("erlang:monotonic_time(millisecond)")>]
let monotonicTimeMs () : int = nativeOnly

/// Get a value from the process dictionary.
[<Emit("erlang:get($0)")>]
let processGet (key: obj) : obj = nativeOnly

/// Put a value in the process dictionary.
[<Emit("erlang:put($0, $1)")>]
let processPut (key: obj) (value: obj) : unit = nativeOnly

/// Format a term as a binary string.
[<Emit("erlang:list_to_binary(io_lib:format(<<\"~p\">>, [$0]))")>]
let formatTerm (term: obj) : string = nativeOnly
