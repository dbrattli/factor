/// Platform-independent Actor abstraction.
///
/// Provides the CPS computation expression (actor { ... }) and
/// typed actor operations (spawn, start, send, call, receive).
/// All platform-specific work delegates to Platform.platform.
[<AutoOpen>]
module Fable.Actor.Actor

open Fable.Actor.Types
open Fable.Actor.Platform

// --- CPS computation expression ---

/// CPS-based actor computation.
/// On BEAM: blocking selective receive.
/// On Python: async/await continuation.
/// On JS: promise continuation.
type ActorOp<'T> = { Run: ('T -> unit) -> unit }

type ActorBuilder() =
    member _.Bind(op: ActorOp<'T>, f: 'T -> ActorOp<'U>) : ActorOp<'U> = {
        Run = fun cont -> op.Run(fun value -> (f value).Run cont)
    }

    member _.Return(value: 'T) : ActorOp<'T> = { Run = fun cont -> cont value }

    member _.ReturnFrom(op: ActorOp<'T>) : ActorOp<'T> = op

    member _.Zero() : ActorOp<unit> = { Run = fun cont -> cont () }

    member _.Delay(f: unit -> ActorOp<'T>) : ActorOp<'T> = { Run = fun cont -> (f ()).Run cont }

    member _.Combine(first: ActorOp<unit>, second: ActorOp<'T>) : ActorOp<'T> = {
        Run = fun cont -> first.Run(fun () -> second.Run cont)
    }

let actor = ActorBuilder()

// --- Core API ---

/// Receive next message (CPS — suspends until message arrives).
let receive<'Msg> () : ActorOp<'Msg> = {
    Run = fun cont -> platform.receive (fun raw -> cont (unbox<'Msg> raw))
}

/// Spawn an actor running an actor CE body.
let spawn (body: unit -> ActorOp<unit>) : Actor<'Msg> =
    let rawPid = platform.spawn (fun () -> (body ()).Run(fun () -> ()))
    { Pid = rawPid }

/// Spawn a linked child actor (parent gets EXIT signal on crash).
let spawnLinked (body: unit -> ActorOp<unit>) : Actor<'Msg> =
    let rawPid = platform.spawnLinked (fun () -> (body ()).Run(fun () -> ()))
    { Pid = rawPid }

/// Start a stateful actor with a message handler.
/// The handler returns Continue(newState) to keep going or Stop to exit.
let start (initialState: 'State) (handler: 'State -> 'Msg -> Next<'State>) : Actor<'Msg> =
    spawn (fun () ->
        let rec loop state = actor {
            let! msg = receive<'Msg> ()

            match handler state msg with
            | Continue newState -> return! loop newState
            | Stop -> ()
        }

        loop initialState)

/// Send a message (fire and forget).
let send (actor: Actor<'Msg>) (msg: 'Msg) : unit =
    platform.sendMsg (actor.Pid, box msg)

/// Send a message and wait for a reply (blocking).
/// The message type must include a ReplyChannel variant.
let call (actor: Actor<'TargetMsg>) (msgFactory: ReplyChannel<'Reply> -> 'TargetMsg) : 'Reply =
    let ref = platform.makeRef ()
    let callerPid = platform.selfPid ()
    let rc: ReplyChannel<'Reply> = { Reply = fun reply -> platform.sendReply (callerPid, ref, reply) }
    platform.sendMsg (actor.Pid, box (msgFactory rc))
    unbox (platform.recvReply ref)

/// Send a message and await a reply (non-blocking, inside actor { }).
/// The receiving actor must use receiveAndReply — the message type stays clean.
let callAsync<'Reply, 'Msg> (actor: Actor<'Msg>) (msg: 'Msg) : ActorOp<'Reply> = {
    Run = fun cont ->
        let ref = platform.makeRef ()
        let callerPid = platform.selfPid ()
        platform.sendMsg (actor.Pid, box (callerPid, ref, box msg))
        let reply = platform.recvReply ref
        cont (unbox<'Reply> reply)
}

/// Receive a message with a reply channel (inside actor { }).
/// Pairs with callAsync — the only way to obtain a ReplyChannel.
let receiveAndReply<'Msg, 'Reply> () : ActorOp<'Msg * ReplyChannel<'Reply>> = {
    Run = fun cont ->
        platform.receive (fun raw ->
            let (callerPid, ref, msgRaw) = unbox<obj * obj * obj> raw
            let rc: ReplyChannel<'Reply> = {
                Reply = fun reply -> platform.sendReply (callerPid, ref, box reply)
            }
            cont (unbox<'Msg> msgRaw, rc))
}

/// Get own pid.
let self<'Msg> () : Actor<'Msg> = { Pid = platform.selfPid () }

/// Kill an actor.
let kill (actor: Actor<'Msg>) : unit = platform.killProcess actor.Pid

/// Enable supervision — child EXIT signals become messages.
let trapExits () : unit = platform.trapExits ()

/// Format a crash reason as a string.
let formatReason (reason: obj) : string = platform.formatReason reason

/// Schedule a timer callback.
let schedule (ms: int) (callback: unit -> unit) : obj = platform.timerSchedule (ms, callback)

/// Cancel a scheduled timer.
let cancelTimer (timer: obj) : unit = platform.timerCancel timer

/// Compare two platform references for equality.
let refEquals (a: obj) (b: obj) : bool = platform.refEquals (a, b)
