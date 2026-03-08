/// BEAM implementation of the Actor abstraction.
///
/// Provides spawn, start, send, call operations for BEAM processes,
/// plus the internal CPS computation expression used by operators.
module Factor.Beam.Actor

open Fable.Core
open Factor.Actor.Types

// --- Internal CPS type (used by operators) ---

/// CPS-based actor computation (used internally by operators).
type ActorOp<'T> = { Run: ('T -> unit) -> unit }

// --- Erlang FFI ---

[<Erase>]
type private IFactorActor =
    abstract spawnActor: f: (unit -> unit) -> obj
    abstract sendMsg: pid: obj * msg: obj -> unit
    abstract receiveMsgBlocking: unit -> obj
    abstract selfPid: unit -> obj
    abstract makeRef: unit -> obj
    abstract sendReply: pid: obj * ref: obj * value: obj -> unit
    abstract recvReply: ref: obj -> obj

[<ImportAll("factor_actor")>]
let private factorActor: IFactorActor = nativeOnly

// --- CE Builder (internal, used by operators) ---

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

// --- Public API ---

/// Spawn a raw actor process. The body runs in a new BEAM process.
let spawn (body: unit -> unit) : Actor<'Msg> =
    let rawPid = factorActor.spawnActor body
    { Pid = rawPid }

/// Start a stateful actor with a message handler (gen_server style).
/// The actor loops, receiving messages and calling the handler with current state.
/// The handler returns Continue(newState) to keep going or Stop to exit.
let start (initialState: 'State) (handler: 'State -> 'Msg -> Next<'State>) : Actor<'Msg> =
    let rawPid =
        factorActor.spawnActor (fun () ->
            let rec loop state =
                let msg: 'Msg = unbox (factorActor.receiveMsgBlocking ())

                match handler state msg with
                | Continue newState -> loop newState
                | Stop -> ()

            loop initialState)

    { Pid = rawPid }

/// Send a message (fire and forget)
let send (actor: Actor<'Msg>) (msg: 'Msg) : unit = factorActor.sendMsg (actor.Pid, msg)

/// Get own pid
let self<'Msg> () : Actor<'Msg> = { Pid = factorActor.selfPid () }

/// Send a message to an actor and wait for a reply (blocking).
/// The msgFactory receives a ReplyChannel that the target actor calls to respond.
let call (actor: Actor<'TargetMsg>) (msgFactory: ReplyChannel<'Reply> -> 'TargetMsg) : 'Reply =
    let ref = factorActor.makeRef ()
    let callerPid = factorActor.selfPid ()

    let rc: ReplyChannel<'Reply> = {
        Reply = fun reply -> factorActor.sendReply (callerPid, ref, reply)
    }

    factorActor.sendMsg (actor.Pid, msgFactory rc)
    unbox (factorActor.recvReply ref)

/// Register a child handler for a specific ref in the process dictionary.
[<Emit("factor_actor:register_child($0, $1)")>]
let private registerChild (ref: obj) (handler: obj -> unit) : unit = nativeOnly

/// Unregister a child handler for a specific ref.
[<Emit("factor_actor:unregister_child($0)")>]
let private unregisterChild (ref: obj) : unit = nativeOnly

/// Create an Observer in the current process from an OnNext handler.
/// Registers a child handler that dispatches OnNext values to the handler.
let asObserver (onNext: 'T -> unit) : Observer<'T> =
    let ref = factorActor.makeRef ()

    registerChild ref (fun msg ->
        let n = unbox<Msg<'T>> msg

        match n with
        | OnNext x -> onNext x
        | OnError _ -> unregisterChild ref
        | OnCompleted -> unregisterChild ref)

    { Pid = factorActor.selfPid (); Ref = ref }
