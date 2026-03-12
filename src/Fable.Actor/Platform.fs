/// Platform abstraction for Fable.Actor.
///
/// On Fable targets (BEAM, Python, JS): uses [<ImportAll("factor_platform")>]
/// so each platform provides a native module with matching function names.
/// On .NET: wraps MailboxProcessor for native async actor support.
module Fable.Actor.Platform

#if FABLE_COMPILER
open Fable.Core

[<Erase>]
type IActorPlatform =
    // Process lifecycle
    abstract spawn: f: (unit -> unit) -> obj
    abstract spawnLinked: f: (unit -> unit) -> obj
    abstract selfPid: unit -> obj
    abstract killProcess: pid: obj -> unit
    abstract exitNormal: unit -> unit
    abstract trapExits: unit -> unit
    abstract formatReason: reason: obj -> string

    // Message passing
    abstract sendMsg: pid: obj * msg: obj -> unit
    abstract receive: cont: (obj -> unit) -> unit
    abstract makeRef: unit -> obj
    abstract sendReply: pid: obj * ref: obj * value: obj -> unit
    abstract recvReply: ref: obj -> obj
    abstract refEquals: a: obj * b: obj -> bool

    // Monitoring
    abstract monitorProcess: pid: obj -> obj
    abstract demonitorProcess: ref: obj -> unit

    // Timers
    abstract timerSchedule: ms: int * callback: (unit -> unit) -> obj
    abstract timerCancel: timer: obj -> unit

[<ImportAll("factor_platform")>]
let platform: IActorPlatform = nativeOnly

#else

open System
open System.Threading
open System.Collections.Concurrent

/// .NET actor backed by a MailboxProcessor.
/// The Pid is the DotNetActor instance itself.
type DotNetActor(body: unit -> unit, parent: DotNetActor option, cts: CancellationTokenSource) =
    let mailbox = new BlockingCollection<obj>()
    let mutable trapExit = false
    let children = ConcurrentBag<DotNetActor>()
    let thread = Thread(fun () ->
        try
            body ()
        with ex ->
            match parent with
            | Some p when not (cts.IsCancellationRequested) ->
                if p.TrapExit then
                    p.Post(box {| Pid = box (obj()); Reason = box ex |})
                else
                    p.Cancel()
            | _ -> ())

    do thread.IsBackground <- true
    do thread.Start()

    member _.Post(msg: obj) = mailbox.Add(msg)
    member _.Receive(cont: obj -> unit) =
        let msg = mailbox.Take(cts.Token)
        cont msg
    member _.TrapExit with get() = trapExit and set(v) = trapExit <- v
    member _.Cancel() = cts.Cancel()
    member _.Token = cts.Token
    member _.AddChild(child: DotNetActor) = children.Add(child)
    member _.Kill() =
        cts.Cancel()
        for child in children do
            child.Kill()

/// Thread-local storage for the current actor context.
type ActorContext() =
    [<ThreadStatic; DefaultValue>]
    static val mutable private current: DotNetActor option

    static member Current
        with get() = ActorContext.current
        and set(v) = ActorContext.current <- v

/// .NET platform implementation wrapping MailboxProcessor/threads.
type IActorPlatform =
    // Process lifecycle
    abstract spawn: f: (unit -> unit) -> obj
    abstract spawnLinked: f: (unit -> unit) -> obj
    abstract selfPid: unit -> obj
    abstract killProcess: pid: obj -> unit
    abstract exitNormal: unit -> unit
    abstract trapExits: unit -> unit
    abstract formatReason: reason: obj -> string

    // Message passing
    abstract sendMsg: pid: obj * msg: obj -> unit
    abstract receive: cont: (obj -> unit) -> unit
    abstract makeRef: unit -> obj
    abstract sendReply: pid: obj * ref: obj * value: obj -> unit
    abstract recvReply: ref: obj -> obj
    abstract refEquals: a: obj * b: obj -> bool

    // Monitoring
    abstract monitorProcess: pid: obj -> obj
    abstract demonitorProcess: ref: obj -> unit

    // Timers
    abstract timerSchedule: ms: int * callback: (unit -> unit) -> obj
    abstract timerCancel: timer: obj -> unit

let private replyBoxes = ConcurrentDictionary<obj, BlockingCollection<obj>>()

let platform: IActorPlatform =
    { new IActorPlatform with
        member _.spawn(f) =
            let cts = new CancellationTokenSource()
            let mutable actor = Unchecked.defaultof<DotNetActor>
            actor <- DotNetActor((fun () ->
                ActorContext.Current <- Some actor
                f ()), None, cts)
            box actor

        member _.spawnLinked(f) =
            let parent = ActorContext.Current
            let cts = new CancellationTokenSource()
            let mutable actor = Unchecked.defaultof<DotNetActor>
            actor <- DotNetActor((fun () ->
                ActorContext.Current <- Some actor
                f ()), parent, cts)
            match parent with
            | Some p -> p.AddChild(actor)
            | None -> ()
            box actor

        member _.selfPid() =
            match ActorContext.Current with
            | Some actor -> box actor
            | None -> box 0 // Outside actor context (e.g. call from main thread)

        member _.killProcess(pid) =
            let actor = unbox<DotNetActor> pid
            actor.Kill()

        member _.exitNormal() =
            match ActorContext.Current with
            | Some actor -> actor.Cancel()
            | None -> ()

        member _.trapExits() =
            match ActorContext.Current with
            | Some actor -> actor.TrapExit <- true
            | None -> ()

        member _.formatReason(reason) =
            sprintf "%A" reason

        member _.sendMsg(pid, msg) =
            let actor = unbox<DotNetActor> pid
            actor.Post(msg)

        member _.receive(cont) =
            match ActorContext.Current with
            | Some actor -> actor.Receive(cont)
            | None -> failwith "receive called outside actor context"

        member _.makeRef() =
            box (Guid.NewGuid())

        member _.sendReply(pid, ref, value) =
            let box' = replyBoxes.GetOrAdd(ref, fun _ -> new BlockingCollection<obj>())
            box'.Add(value)

        member _.recvReply(ref) =
            let box' = replyBoxes.GetOrAdd(ref, fun _ -> new BlockingCollection<obj>())
            let result = box'.Take()
            replyBoxes.TryRemove(ref) |> ignore
            result

        member _.refEquals(a, b) =
            obj.Equals(a, b)

        member _.monitorProcess(_pid) =
            box (Guid.NewGuid()) // stub

        member _.demonitorProcess(_ref) =
            () // stub

        member _.timerSchedule(ms, callback) =
            let cts = new CancellationTokenSource()
            let timer = async {
                do! Async.Sleep ms
                if not cts.IsCancellationRequested then
                    callback ()
            }
            Async.Start(timer, cts.Token)
            box cts

        member _.timerCancel(timer) =
            let cts = unbox<CancellationTokenSource> timer
            cts.Cancel()
    }

#endif
