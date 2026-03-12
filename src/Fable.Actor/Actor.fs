/// Platform-independent Actor abstraction.
///
/// BEAM: actor { } is a CPS computation (no-op wrapper, BEAM processes block natively).
/// Non-BEAM: actor { } delegates to async { }, Actor wraps MailboxProcessor.
///
/// Actor<'Msg> provides MailboxProcessor-compatible API:
///   inbox.Receive() — get next message (inside body)
///   actor.Post(msg)  — send a message (from outside)
[<AutoOpen>]
module Fable.Actor.Actor

open Fable.Actor.Types

// ============================================================================
// Actor type + Computation expression
// ============================================================================

#if FABLE_COMPILER_BEAM

open Fable.Actor.Platform

// === BEAM: CPS-based, native processes ===

type ActorOp<'T> = { Run: ('T -> unit) -> unit }

type Actor<'Msg> = {
    Pid: obj
} with

    member _.Receive() : ActorOp<'Msg> = {
        Run = fun cont -> platform.receive (fun raw -> cont (unbox<'Msg> raw))
    }

    member this.Post(msg: 'Msg) = platform.sendMsg (this.Pid, box msg)

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

    member _.TryWith(body: ActorOp<'T>, handler: exn -> ActorOp<'T>) : ActorOp<'T> = {
        Run = fun cont ->
            try
                body.Run cont
            with ex ->
                (handler ex).Run cont
    }

#else

// === Non-BEAM: MailboxProcessor-based ===

type ActorOp<'T> = Async<'T>

type Actor<'Msg> = {
    Mb: MailboxProcessor<'Msg>
    Cts: System.Threading.CancellationTokenSource
} with

    member this.Receive() : Async<'Msg> = this.Mb.Receive()

    member this.Post(msg: 'Msg) =
        if not this.Cts.IsCancellationRequested then
            this.Mb.Post(msg)

type ActorBuilder() =
    member _.Bind(op: Async<'T>, f: 'T -> Async<'U>) : Async<'U> = async.Bind(op, f)
    member _.Return(value: 'T) : Async<'T> = async.Return(value)
    member _.ReturnFrom(op: Async<'T>) : Async<'T> = async.ReturnFrom(op)
    member _.Zero() : Async<unit> = async.Zero()
    member _.Delay(f: unit -> Async<'T>) : Async<'T> = async.Delay(f)

    member _.Combine(first: Async<unit>, second: Async<'T>) : Async<'T> =
        async.Combine(first, async.Delay(fun () -> second))

    member _.TryWith(body: Async<'T>, handler: exn -> Async<'T>) : Async<'T> =
        async.TryWith(body, handler)

#endif

let actor = ActorBuilder()

// ============================================================================
// Core API
// ============================================================================

#if FABLE_COMPILER_BEAM

/// Spawn an actor. Body receives inbox (self-reference) for Receive/Post.
let spawn (body: Actor<'Msg> -> ActorOp<unit>) : Actor<'Msg> =
    let rawPid =
        platform.spawn (fun () ->
            let me: Actor<'Msg> = { Pid = platform.selfPid () }
            (body me).Run(fun () -> ()))

    { Pid = rawPid }

/// Spawn a linked child actor (parent gets EXIT signal on crash).
let spawnLinked (_parent: Actor<'ParentMsg>) (body: Actor<'Msg> -> ActorOp<unit>) : Actor<'Msg> =
    let rawPid =
        platform.spawnLinked (fun () ->
            let me: Actor<'Msg> = { Pid = platform.selfPid () }
            (body me).Run(fun () -> ()))

    { Pid = rawPid }

/// Get own pid (only valid inside actor body).
let self<'Msg> () : Actor<'Msg> = { Pid = platform.selfPid () }

/// Kill an actor and its linked children.
let kill (actor: Actor<'Msg>) : unit = platform.killProcess actor.Pid

/// Enable supervision — child EXIT signals become messages.
let trapExits () : unit = platform.trapExits ()

/// Format a crash reason as a string.
let formatReason (reason: obj) : string = platform.formatReason reason

/// Send a message and await a reply (inside actor { }).
let call (actor: Actor<'TargetMsg>) (msgFactory: ReplyChannel<'Reply> -> 'TargetMsg) : ActorOp<'Reply> = {
    Run = fun cont ->
        let ref = platform.makeRef ()
        let callerPid = platform.selfPid ()
        let rc: ReplyChannel<'Reply> = { Reply = fun reply -> platform.sendReply (callerPid, ref, reply) }
        platform.sendMsg (actor.Pid, box (msgFactory rc))
        cont (unbox (platform.recvReply ref))
}

/// Send a message and await a reply (non-blocking, inside actor { }).
let callAsync<'Reply, 'Msg> (actor: Actor<'Msg>) (msg: 'Msg) : ActorOp<'Reply> = {
    Run = fun cont ->
        let ref = platform.makeRef ()
        let callerPid = platform.selfPid ()
        platform.sendMsg (actor.Pid, box (callerPid, ref, box msg))
        let reply = platform.recvReply ref
        cont (unbox<'Reply> reply)
}

/// Receive a message with a reply channel (inside actor { }).
let receiveAndReply<'Msg, 'Reply> () : ActorOp<'Msg * ReplyChannel<'Reply>> = {
    Run = fun cont ->
        platform.receive (fun raw ->
            let (callerPid, ref, msgRaw) = unbox<obj * obj * obj> raw
            let rc: ReplyChannel<'Reply> = {
                Reply = fun reply -> platform.sendReply (callerPid, ref, box reply)
            }
            cont (unbox<'Msg> msgRaw, rc))
}

/// Receive next message (free function, uses platform receive).
let receive<'Msg> () : ActorOp<'Msg> = {
    Run = fun cont -> platform.receive (fun raw -> cont (unbox<'Msg> raw))
}

#else

/// Spawn an actor. Body receives inbox (self-reference) for Receive/Post.
let spawn (body: Actor<'Msg> -> Async<unit>) : Actor<'Msg> =
    let cts = new System.Threading.CancellationTokenSource()
    let mutable inbox: Actor<'Msg> option = None

    let mb =
        MailboxProcessor.Start(
            (fun mb ->
                let actor = { Mb = mb; Cts = cts }
                inbox <- Some actor
                body actor),
            cts.Token
        )

    match inbox with
    | Some a -> a
    | None -> { Mb = mb; Cts = cts }

/// Spawn a linked child actor. On crash, delivers ChildExited to parent.
let spawnLinked (parent: Actor<'ParentMsg>) (body: Actor<'Msg> -> Async<unit>) : Actor<'Msg> =
    let cts = new System.Threading.CancellationTokenSource()
    let mutable inbox: Actor<'Msg> option = None

    let mb =
        MailboxProcessor.Start(
            (fun mb ->
                let actor = { Mb = mb; Cts = cts }
                inbox <- Some actor

                async {
                    try
                        do! body actor
                    with ex ->
                        parent.Post(
                            unbox {
                                Pid = box mb
                                Reason = box ex
                            }
                        )
                }),
            cts.Token
        )

    match inbox with
    | Some a -> a
    | None -> { Mb = mb; Cts = cts }

/// Kill an actor — cancels its token, stopping message delivery.
let kill (actor: Actor<'Msg>) : unit = actor.Cts.Cancel()

/// Enable supervision (stub on non-BEAM).
let trapExits () : unit = ()

/// Send a message and await a reply (inside actor { }).
let call (target: Actor<'TargetMsg>) (msgFactory: ReplyChannel<'Reply> -> 'TargetMsg) : ActorOp<'Reply> =
    actor {
        let! reply =
            target.Mb.PostAndAsyncReply(fun rc ->
                msgFactory { Reply = fun r -> rc.Reply(r) })

        return reply
    }

/// Receive next message (free function, for backwards compatibility).
let receive<'Msg> (inbox: Actor<'Msg>) : Async<'Msg> = inbox.Receive()

/// Send a message and await a reply (non-blocking, inside actor { }).
let callAsync<'Reply, 'Msg> (target: Actor<'Msg>) (msg: 'Msg) : Async<'Reply> =
    target.Mb.PostAndAsyncReply(fun rc ->
        unbox (box (rc, box 0, box msg)))

/// Receive a message with a reply channel (inside actor { }).
let receiveAndReply<'Msg, 'Reply> (inbox: Actor<obj>) : Async<'Msg * ReplyChannel<'Reply>> =
    async {
        let! raw = inbox.Receive()
        let (rc, _ref, msgRaw) = unbox<obj * obj * obj> raw
        let replyChannel: ReplyChannel<'Reply> = {
            Reply = fun reply -> (unbox<Control.AsyncReplyChannel<'Reply>> rc).Reply(reply)
        }
        return (unbox<'Msg> msgRaw, replyChannel)
    }

#endif

// ============================================================================
// Supervision
// ============================================================================

/// A supervised child — wraps an actor ref with restart capability.
type SupervisedChild<'ParentMsg, 'Msg> = {
    mutable Actor: Actor<'Msg>
    Body: Actor<'Msg> -> ActorOp<unit>
    Strategy: Strategy
}

#if FABLE_COMPILER_BEAM

/// Check if a message is a ChildExited notification.
let tryAsChildExited (msg: obj) : ChildExited option =
    try
        Some(unbox<ChildExited> msg)
    with _ ->
        None

/// Spawn a supervised child actor. Retains the body for restart.
let spawnSupervised
    (parent: Actor<'ParentMsg>)
    (strategy: Strategy)
    (body: Actor<'Msg> -> ActorOp<unit>)
    : SupervisedChild<'ParentMsg, 'Msg> =
    let child = spawnLinked parent body
    { Actor = child; Body = body; Strategy = strategy }

/// Handle a ChildExited event for a supervised child.
/// Returns true if the child was restarted, false if stopped/not matching.
/// Raises ProcessExitException if Escalate.
let handleChildExit
    (parent: Actor<'ParentMsg>)
    (supervised: SupervisedChild<'ParentMsg, 'Msg>)
    (exited: ChildExited)
    : bool =
    let (OneForOne decider) = supervised.Strategy

    let ex =
        match exited.Reason with
        | :? exn as e -> e
        | r -> ProcessExitException(sprintf "%A" r)

    match decider ex with
    | Directive.Stop -> false
    | Directive.Escalate -> raise ex
    | Directive.Restart ->
        let newChild = spawnLinked parent supervised.Body
        supervised.Actor <- newChild
        true

#else

/// Check if a message is a ChildExited notification.
let tryAsChildExited (msg: obj) : ChildExited option =
    match msg with
    | :? ChildExited as ce -> Some ce
    | _ -> None

/// Spawn a supervised child actor. Retains the body for restart.
let spawnSupervised
    (parent: Actor<'ParentMsg>)
    (strategy: Strategy)
    (body: Actor<'Msg> -> Async<unit>)
    : SupervisedChild<'ParentMsg, 'Msg> =
    let child = spawnLinked parent body
    { Actor = child; Body = body; Strategy = strategy }

/// Handle a ChildExited event for a supervised child.
/// Returns true if the child was restarted, false if stopped/not matching.
/// Raises ProcessExitException if Escalate.
let handleChildExit
    (parent: Actor<'ParentMsg>)
    (supervised: SupervisedChild<'ParentMsg, 'Msg>)
    (exited: ChildExited)
    : bool =
    let (OneForOne decider) = supervised.Strategy

    let ex =
        match exited.Reason with
        | :? exn as e -> e
        | r -> ProcessExitException(sprintf "%A" r)

    match decider ex with
    | Directive.Stop -> false
    | Directive.Escalate -> raise ex
    | Directive.Restart ->
        let newChild = spawnLinked parent supervised.Body
        supervised.Actor <- newChild
        true

#endif

// === Common API (both platforms) ===

/// Send a message (fire and forget).
let send (actor: Actor<'Msg>) (msg: 'Msg) : unit = actor.Post(msg)

/// Start a stateful actor with a message handler.
let start (initialState: 'State) (handler: 'State -> 'Msg -> Next<'State>) : Actor<'Msg> =
    let body (inbox: Actor<'Msg>) =
        let rec loop state =
            actor {
                let! msg = inbox.Receive()

                match handler state msg with
                | Continue newState -> return! loop newState
                | Stop -> ()
            }

        loop initialState

#if FABLE_COMPILER_BEAM
    let rawPid =
        platform.spawn (fun () ->
            let me: Actor<'Msg> = { Pid = platform.selfPid () }
            (body me).Run(fun () -> ()))

    { Pid = rawPid }
#else
    spawn body
#endif

#if FABLE_COMPILER_BEAM

/// Schedule a timer callback. Returns an opaque handle for cancellation.
let schedule (ms: int) (callback: unit -> unit) : obj = platform.timerSchedule (ms, callback)

/// Cancel a scheduled timer.
let cancelTimer (timer: obj) : unit = platform.timerCancel timer

#else

/// Schedule a timer callback. Returns a cancellation handle.
let schedule (ms: int) (callback: unit -> unit) : obj =
    let cts = new System.Threading.CancellationTokenSource()

    Async.StartImmediate(
        async {
            do! Async.Sleep ms
            callback ()
        },
        cts.Token
    )

    box cts

/// Cancel a scheduled timer.
let cancelTimer (timer: obj) : unit =
    (unbox<System.Threading.CancellationTokenSource> timer).Cancel()

#endif
