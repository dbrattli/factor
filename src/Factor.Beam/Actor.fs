/// Actor — operator process machinery for reactive pipelines.
///
/// Provides the building blocks for operator processes:
/// selective receive, message loops, and composable operator helpers
/// (forNext, forNextStateful, ofMsgStateful, ofMsg2).
module Factor.Beam.Actor

open Fable.Core
open Factor.Agent.Types
open Factor.Beam.Agent

// ============================================================================
// Message loop types and dispatch
// ============================================================================

/// Message types received in operator process message loops.
/// Each case maps to an Erlang tuple tag via CompiledName.
type LoopMsg =
    | [<CompiledName("factor_timer")>] FactorTimer of ref: obj * callback: (unit -> unit)
    | [<CompiledName("factor_child")>] FactorChild of ref: obj * notification: obj
    | [<CompiledName("EXIT")>] Exit of pid: obj * reason: obj

/// Dispatch a child notification using the process dictionary registry.
[<Emit("case erlang:get(factor_children) of undefined -> ok; FcM__ -> case FcM__ of #{$0 := FcH__} -> FcH__($1); #{} -> ok end end")>]
let private dispatchChild (ref: obj) (notification: obj) : unit = nativeOnly

/// Dispatch an exit signal using the process dictionary registry.
[<Emit("case $1 of normal -> ok; _ -> case erlang:get(factor_exits) of undefined -> ok; FeM__ -> case FeM__ of #{$0 := FeH__} -> FeH__($1); #{} -> ok end end end")>]
let private dispatchExit (pid: obj) (reason: obj) : unit = nativeOnly

/// Operator message loop — blocks waiting for timer, child, and EXIT messages.
///
/// Dispatches each message using the process dictionary registries, then loops.
/// When a terminal event triggers Process.exitNormal(), the process terminates.
let rec childLoop () : unit =
    match Erlang.receiveForever<LoopMsg> () with
    | FactorTimer(_, callback) ->
        callback ()
        childLoop ()
    | FactorChild(ref, notification) ->
        dispatchChild ref notification
        childLoop ()
    | Exit(pid, reason) ->
        dispatchExit pid reason
        childLoop ()

/// Timer-aware message pump loop — processes messages until endTime.
let rec private processTimersLoop (endTime: int) : unit =
    let remaining = endTime - Erlang.monotonicTimeMs ()

    if remaining <= 0 then
        ()
    else
        match Erlang.receive<LoopMsg> (min remaining 1) with
        | Some(FactorTimer(_, callback)) ->
            callback ()
            processTimersLoop endTime
        | Some(FactorChild(ref, notification)) ->
            dispatchChild ref notification
            processTimersLoop endTime
        | Some(Exit(pid, reason)) ->
            dispatchExit pid reason
            processTimersLoop endTime
        | None -> processTimersLoop endTime

/// Timer-aware sleep: processes pending timer, child, and EXIT messages
/// for the specified duration. Use this instead of timer:sleep to ensure
/// callbacks execute in the current process.
let processTimers (timeoutMs: int) : unit =
    let endTime = Erlang.monotonicTimeMs () + timeoutMs
    processTimersLoop endTime

// ============================================================================
// Selective receive helpers (for agent CE)
// ============================================================================

/// Selective receive for a specific child ref. Dispatches timers and exits while waiting.
[<Emit("factor_actor:recv_child($0, $1)")>]
let private recvChild (ref: obj) (cont: obj -> unit) : unit = nativeOnly

/// Selective receive for any child ref. Dispatches timers and exits while waiting.
[<Emit("factor_actor:recv_any_child($0)")>]
let private recvAnyChild (cont: obj -> obj -> unit) : unit = nativeOnly

/// Receive next Msg<'T> from a specific source (single-source operators).
let recvMsg<'T> (ref: obj) : AgentOp<Msg<'T>> = {
    Run = fun cont -> recvChild ref (fun msg -> cont (unbox<Msg<'T>> msg))
}

/// Receive next (ref, rawMsg) from any source (multi-source operators).
let recvAnyMsg () : AgentOp<obj * obj> = {
    Run = fun cont -> recvAnyChild (fun ref msg -> cont (ref, msg))
}

/// Spawn linked operator process from agent computation, return dispose handle.
let spawnOp (body: unit -> AgentOp<unit>) : Handle =
    let pid = Process.spawnLinked (fun () -> (body ()).Run(fun () -> ()))
    { Dispose = fun () -> Process.killProcess pid }

// ============================================================================
// Operator helpers (composable lego blocks)
// ============================================================================

/// Stateless single-source: the function IS the agent.
/// Used by: map, filter, tap, choose
let forNext
    (source: Observable<'T>)
    (onNextFn: Observer<'U> -> 'T -> unit)
    : Observable<'U>
    =
    {
        Subscribe =
            fun downstream ->
                let ref = Process.makeRef ()

                spawnOp (fun () ->
                    let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Subscribe(upstream) |> ignore

                    let rec loop () =
                        agent {
                            let! msg = recvMsg<'T> ref

                            match msg with
                            | OnNext x ->
                                onNextFn downstream x
                                return! loop ()
                            | OnError e -> Process.onError downstream e
                            | OnCompleted -> Process.onCompleted downstream
                        }

                    loop ())
    }

/// Stateful single-source: state threads through the agent.
/// Used by: mapi, take, skip, skipWhile, distinctUntilChanged, distinct
let forNextStateful
    (source: Observable<'T>)
    (initialState: 'S)
    (onNextFn: Observer<'U> -> 'S -> 'T -> 'S)
    : Observable<'U>
    =
    {
        Subscribe =
            fun downstream ->
                let ref = Process.makeRef ()

                spawnOp (fun () ->
                    let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Subscribe(upstream) |> ignore

                    let rec loop state =
                        agent {
                            let! msg = recvMsg<'T> ref

                            match msg with
                            | OnNext x ->
                                let newState = onNextFn downstream state x
                                return! loop newState
                            | OnError e -> Process.onError downstream e
                            | OnCompleted -> Process.onCompleted downstream
                        }

                    loop initialState)
    }

/// Full Msg control, stateful. None=stop, Some=continue.
/// Used by: scan, reduce, pairwise, takeLast, first, last, defaultIfEmpty, takeWhile
let ofMsgStateful
    (source: Observable<'T>)
    (initialState: 'S)
    (handler: Observer<'U> -> 'S -> Msg<'T> -> 'S option)
    : Observable<'U>
    =
    {
        Subscribe =
            fun downstream ->
                let ref = Process.makeRef ()

                spawnOp (fun () ->
                    let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Subscribe(upstream) |> ignore

                    let rec loop state =
                        agent {
                            let! msg = recvMsg<'T> ref

                            match handler downstream state msg with
                            | Some newState -> return! loop newState
                            | None -> ()
                        }

                    loop initialState)
    }

/// Dual-source with state. Choice discriminates sources.
/// Used by: combineLatest, withLatestFrom, zip, takeUntil, sample
let ofMsg2
    (source1: Observable<'T1>)
    (source2: Observable<'T2>)
    (initialState: 'S)
    (handler: Observer<'U> -> 'S -> Choice<Msg<'T1>, Msg<'T2>> -> 'S option)
    : Observable<'U>
    =
    {
        Subscribe =
            fun downstream ->
                spawnOp (fun () ->
                    let ref1 = Process.makeRef ()
                    let ref2 = Process.makeRef ()
                    let self1: Observer<'T1> = { Pid = Process.selfPid (); Ref = ref1 }
                    source1.Subscribe(self1) |> ignore
                    let self2: Observer<'T2> = { Pid = Process.selfPid (); Ref = ref2 }
                    source2.Subscribe(self2) |> ignore

                    let rec loop state =
                        agent {
                            let! (ref, rawMsg) = recvAnyMsg ()

                            let choice =
                                if ref = ref1 then
                                    Choice1Of2(unbox<Msg<'T1>> rawMsg)
                                else
                                    Choice2Of2(unbox<Msg<'T2>> rawMsg)

                            match handler downstream state choice with
                            | Some newState -> return! loop newState
                            | None -> ()
                        }

                    loop initialState)
    }
