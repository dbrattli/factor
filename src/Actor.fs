module Factor.Actor

open Fable.Core

// --- Types ---

/// Opaque typed PID wrapping an Erlang pid()
type Pid<'Msg> = { Pid: obj }

/// CPS-based actor computation. Run takes a continuation.
type Actor<'Msg, 'T> = { Run: ('T -> unit) -> unit }

/// Context provided to actor body with Recv capability
type ActorContext<'Msg> = { Recv: unit -> Actor<'Msg, 'Msg> }

// --- Erlang FFI ---

[<Emit("factor_actor:spawn_actor($0)")>]
let private spawnActor (f: unit -> unit) : obj = nativeOnly

[<Emit("factor_actor:send_msg($0, $1)")>]
let private sendMsg (pid: obj) (msg: obj) : unit = nativeOnly

[<Emit("factor_actor:receive_msg($0)")>]
let private receiveMsg (cont: obj -> unit) : unit = nativeOnly

[<Emit("factor_actor:self_pid()")>]
let private selfPid () : obj = nativeOnly

// --- CE Builder ---

type ActorBuilder() =
    member _.Bind(actor: Actor<'Msg, 'T>, f: 'T -> Actor<'Msg, 'U>) : Actor<'Msg, 'U> = {
        Run = fun cont -> actor.Run(fun value -> (f value).Run cont)
    }

    member _.Return(value: 'T) : Actor<'Msg, 'T> = { Run = fun cont -> cont value }

    member _.ReturnFrom(actor: Actor<'Msg, 'T>) : Actor<'Msg, 'T> = actor

    member _.Zero() : Actor<'Msg, unit> = { Run = fun cont -> cont () }

    member _.Delay(f: unit -> Actor<'Msg, 'T>) : Actor<'Msg, 'T> = { Run = fun cont -> (f ()).Run cont }

    member _.Combine(first: Actor<'Msg, unit>, second: Actor<'Msg, 'T>) : Actor<'Msg, 'T> = {
        Run = fun cont -> first.Run(fun () -> second.Run cont)
    }

let actor = ActorBuilder()

// --- Public API ---

let spawn (body: ActorContext<'Msg> -> Actor<'Msg, unit>) : Pid<'Msg> =
    let rawPid =
        spawnActor (fun () ->
            let ctx: ActorContext<'Msg> = {
                Recv =
                    fun () -> {
                        Run = fun cont -> receiveMsg (fun msg -> cont (unbox msg))
                    }
            }

            let computation = body ctx
            computation.Run(fun () -> ()))

    { Pid = rawPid }

let send (pid: Pid<'Msg>) (msg: 'Msg) : unit = sendMsg pid.Pid msg

let self<'Msg> () : Pid<'Msg> = { Pid = selfPid () }
