/// Platform primitives for BEAM target.
///
/// Replaces the .erl files with F# using [<Emit>] bindings,
/// BeamInterop.Erlang.receive<'T> for selective receive, and
/// emitErlExpr for bound-variable receive patterns.
module Fable.Actor.Platform

#if FABLE_COMPILER_BEAM

open Fable.Core
open Fable.Core.BeamInterop
open Fable.Actor.Types

// ============================================================================
// BEAM BIF bindings
// ============================================================================

[<Emit("erlang:self()")>]
let selfPid () : obj = nativeOnly

[<Emit("erlang:spawn(fun() -> $0(ok) end)")>]
let spawnProcess (f: unit -> unit) : obj = nativeOnly

[<Emit("erlang:spawn_link(fun() -> $0(ok) end)")>]
let spawnLinkedProcess (f: unit -> unit) : obj = nativeOnly

[<Emit("erlang:make_ref()")>]
let makeRef () : obj = nativeOnly

[<Emit("erlang:exit($0, kill)")>]
let killProcess (pid: obj) : unit = nativeOnly

[<Emit("erlang:exit(normal)")>]
let exitNormal () : unit = nativeOnly

[<Emit("erlang:process_flag(trap_exit, true)")>]
let trapExits () : unit = nativeOnly

[<Emit("erlang:monitor(process, $0)")>]
let monitorProcess (pid: obj) : obj = nativeOnly

[<Emit("erlang:demonitor($0, [flush])")>]
let demonitorProcess (ref: obj) : unit = nativeOnly

[<Emit("erlang:list_to_binary(io_lib:format(<<\"~p\">>, [$0]))")>]
let formatReason (reason: obj) : string = nativeOnly

[<Emit("$0 =:= $1")>]
let exactEquals (a: obj) (b: obj) : bool = nativeOnly

// ============================================================================
// Internal message protocol
// ============================================================================

/// DU mapping the tagged-tuple envelope used on the wire.
/// Each case's CompiledName matches the Erlang atom tag.
type InternalMsg =
    | [<CompiledName("fable_actor_msg")>] ActorMsg of payload: obj
    | [<CompiledName("fable_actor_timer")>] ActorTimer of ref: obj * callback: (obj -> unit)
    | [<CompiledName("EXIT")>] Exit of pid: obj * reason: obj

[<Emit("normal")>]
let private atomNormal : obj = nativeOnly

// ============================================================================
// Message passing
// ============================================================================

/// Send a tagged user message: Pid ! {fable_actor_msg, Msg}
[<Emit("$0 ! {fable_actor_msg, $1}, ok")>]
let sendMsg (pid: obj) (msg: obj) : unit = nativeOnly

/// Send a tagged reply: Pid ! {fable_actor_reply, Ref, Value}
[<Emit("$0 ! {fable_actor_reply, $1, $2}, ok")>]
let sendReply (pid: obj) (ref: obj) (value: obj) : unit = nativeOnly

/// CPS receive: blocks until a user message arrives.
/// Dispatches timer callbacks and EXIT signals transparently.
/// Uses emitErlExpr for the blocking receive to avoid overload resolution
/// issues with Erlang.receive<'T>() vs Erlang.receive<'T>(timeout).
let rec receiveMsg (cont: obj -> unit) : unit =
    let msg: InternalMsg =
        emitErlExpr
            ()
            "receive {fable_actor_msg, P__} -> {fable_actor_msg, P__}; {fable_actor_timer, R__, C__} -> {fable_actor_timer, R__, C__}; {'EXIT', P__, R__} -> {'EXIT', P__, R__} end"

    match msg with
    | ActorMsg payload -> cont payload
    | ActorTimer(_, callback) ->
        callback (box ())
        receiveMsg cont
    | Exit(_, reason) when exactEquals reason atomNormal -> receiveMsg cont
    | Exit(pid, reason) -> cont (box ({ Pid = pid; Reason = reason }: ChildExited))

/// Blocking selective receive for a reply matching a specific ref.
/// Uses emitErlExpr to preserve Erlang's bound-variable semantics —
/// only the message with the matching ref is consumed from the mailbox.
let recvReply (ref: obj) : obj =
    emitErlExpr ref "receive {fable_actor_reply, $0, FableReply} -> FableReply end"

/// Selective receive for a reply with timeout.
/// Returns Some(reply) or None on timeout.
let recvReplyWithTimeout (ref: obj) (timeout: int) : obj option =
    emitErlExpr (ref, timeout) "receive {fable_actor_reply, $0, FableReply} -> {some, FableReply} after $1 -> undefined end"

// ============================================================================
// Child exit detection
// ============================================================================

[<Emit("is_map($0) andalso is_map_key(pid, $0) andalso is_map_key(reason, $0)")>]
let isChildExited (msg: obj) : bool = nativeOnly

// ============================================================================
// Timer
// ============================================================================

type private TimerControl =
    | [<CompiledName("cancel")>] Cancel

/// Schedule a callback after ms milliseconds.
/// Returns a handle (pid) for cancellation.
let timerSchedule (ms: int) (callback: unit -> unit) : obj =
    spawnProcess (fun () ->
        match Erlang.receive<TimerControl> ms with
        | Some Cancel -> ()
        | None -> callback ())

/// Cancel a scheduled timer.
[<Emit("$0 ! cancel, ok")>]
let timerCancel (timer: obj) : unit = nativeOnly

#endif
