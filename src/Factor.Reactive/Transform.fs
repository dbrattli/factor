/// Transform operators for Factor.Reactive
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Reactive.Transform

open System.Collections.Generic
open Factor.Actor.Types
open Factor.Beam
open Factor.Beam.Actor

/// Returns an observable whose elements are the result of invoking
/// the mapper function on each element of the source.
let map (mapper: 'T -> 'U) (source: Observable<'T>) : Observable<'U> =
    Operator.forNext source (fun downstream x ->
        Process.onNext downstream (mapper x))

/// Returns an observable whose elements are the result of invoking
/// the mapper function on each element and its index.
let mapi (mapper: 'T -> int -> 'U) (source: Observable<'T>) : Observable<'U> =
    Operator.forNextStateful source 0 (fun downstream index x ->
        Process.onNext downstream (mapper x index)
        index + 1)

/// Flattens an Observable of Observables by merging inner emissions.
/// Each inner runs in its own linked child process (supervision boundary).
///
/// The maxConcurrency parameter controls how many inner observables
/// can be subscribed to concurrently:
/// - None: Unlimited concurrency
/// - Some(1): Sequential (equivalent to concatInner)
/// - Some(n): At most n inner observables active at once
let mergeInner (policy: SupervisionPolicy) (maxConcurrency: int option) (source: Observable<Observable<'T>>) : Observable<'T> = {
    subscribe =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let parentPid = Process.selfPid ()
                    let mutable innerCount = 0
                    let mutable outerStopped = false
                    let mutable disposed = false
                    let queue = Queue<Observable<'T>>()

                    Process.trapExits ()

                    let canSubscribe () =
                        match maxConcurrency with
                        | None -> true
                        | Some max -> innerCount < max

                    let checkComplete () =
                        if outerStopped && innerCount <= 0 && queue.Count = 0 then
                            Process.onCompleted downstream
                            Process.exitNormal ()

                    let mutable subscribeToInner: Observable<'T> -> int -> unit = fun _ _ -> ()

                    subscribeToInner <-
                        fun (inner: Observable<'T>) (retriesLeft: int) ->
                            innerCount <- innerCount + 1
                            let childRef = Process.makeRef ()

                            Process.registerChild childRef (fun msg ->
                                let n = unbox<Msg<'T>> msg

                                if not disposed then
                                    match n with
                                    | OnNext value -> Process.onNext downstream value
                                    | OnError e ->
                                        disposed <- true
                                        Process.onError downstream e
                                        Process.exitNormal ()
                                    | OnCompleted ->
                                        innerCount <- innerCount - 1
                                        Process.unregisterChild childRef

                                        if queue.Count > 0 then
                                            let next = queue.Dequeue()

                                            let maxRetries =
                                                match policy with
                                                | Restart n -> n
                                                | _ -> 0

                                            subscribeToInner next maxRetries
                                        else
                                            checkComplete ())

                            let childPid =
                                Process.spawnLinked (fun () ->
                                    let innerRef = Process.makeRef ()

                                    let innerSelf: Observer<'T> = {
                                        Pid = Process.selfPid ()
                                        Ref = innerRef
                                    }

                                    inner.Subscribe(innerSelf) |> ignore

                                    let rec loop () =
                                        (Operator.recvMsg<'T> innerRef).Run(fun msg ->
                                            Process.sendChildMsg parentPid childRef (box msg)

                                            match msg with
                                            | OnCompleted
                                            | OnError _ -> ()
                                            | _ -> loop ())

                                    loop ())

                            Process.registerExit childPid (fun reason ->
                                if not disposed then
                                    Process.unregisterChild childRef
                                    Process.unregisterExit childPid
                                    innerCount <- innerCount - 1

                                    match policy with
                                    | Terminate ->
                                        disposed <- true
                                        Process.onError downstream (ProcessExitException(Process.formatReason reason))
                                        Process.exitNormal ()
                                    | Skip ->
                                        if queue.Count > 0 then
                                            let next = queue.Dequeue()

                                            let maxRetries =
                                                match policy with
                                                | Restart n -> n
                                                | _ -> 0

                                            subscribeToInner next maxRetries
                                        else
                                            checkComplete ()
                                    | Restart _ ->
                                        if retriesLeft > 0 then
                                            subscribeToInner inner (retriesLeft - 1)
                                        else
                                            disposed <- true

                                            Process.onError downstream (ProcessExitException(Process.formatReason reason))

                                            Process.exitNormal ())

                    let outerRef = Process.makeRef ()

                    Process.registerChild outerRef (fun msg ->
                        let n = unbox<Msg<Observable<'T>>> msg

                        if not disposed then
                            match n with
                            | OnNext inner ->
                                if canSubscribe () then
                                    let maxRetries =
                                        match policy with
                                        | Restart n -> n
                                        | _ -> 0

                                    subscribeToInner inner maxRetries
                                else
                                    queue.Enqueue(inner)
                            | OnError e ->
                                disposed <- true
                                Process.onError downstream e
                                Process.exitNormal ()
                            | OnCompleted ->
                                outerStopped <- true
                                checkComplete ())

                    let outerSelf: Observer<Observable<'T>> = {
                        Pid = Process.selfPid ()
                        Ref = outerRef
                    }

                    source.Subscribe(outerSelf) |> ignore
                    Operator.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Projects each element into an observable and merges the results.
/// Each inner runs in its own linked child process (supervision boundary).
let flatMap (mapper: 'T -> Observable<'U>) (source: Observable<'T>) : Observable<'U> =
    source |> map mapper |> mergeInner Terminate None

/// Flattens an Observable of Observables by concatenating in order.
let concatInner (source: Observable<Observable<'T>>) : Observable<'T> = mergeInner Terminate (Some 1) source

/// Projects each element into an observable and concatenates in order.
let concatMap (mapper: 'T -> Observable<'U>) (source: Observable<'T>) : Observable<'U> = source |> map mapper |> concatInner

/// Projects each element and its index into an observable and merges.
let flatMapi (mapper: 'T -> int -> Observable<'U>) (source: Observable<'T>) : Observable<'U> =
    source |> mapi mapper |> mergeInner Terminate None

/// Projects each element and its index into an observable and concatenates.
let concatMapi (mapper: 'T -> int -> Observable<'U>) (source: Observable<'T>) : Observable<'U> = source |> mapi mapper |> concatInner

/// Flattens an Observable of Observables by switching to the latest.
/// Only messages from the most recent inner observable are forwarded.
let switchInner (source: Observable<Observable<'T>>) : Observable<'T> = {
    subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let outerRef = Process.makeRef ()

                let outerSelf: Observer<Observable<'T>> = {
                    Pid = Process.selfPid ()
                    Ref = outerRef
                }

                source.Subscribe(outerSelf) |> ignore

                let rec loop outerDone currentInnerRef =
                    actor {
                        let! (ref, rawMsg) = Operator.recvAnyMsg ()

                        if Process.refEquals ref outerRef then
                            match unbox<Msg<Observable<'T>>> rawMsg with
                            | OnNext innerObs ->
                                let innerRef = Process.makeRef ()

                                let innerSelf: Observer<'T> = {
                                    Pid = Process.selfPid ()
                                    Ref = innerRef
                                }

                                innerObs.Subscribe(innerSelf) |> ignore
                                return! loop outerDone (Some innerRef)
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                match currentInnerRef with
                                | None -> Process.onCompleted downstream
                                | Some _ -> return! loop true currentInnerRef
                        else
                            match currentInnerRef with
                            | Some activeRef when Process.refEquals ref activeRef ->
                                match unbox<Msg<'T>> rawMsg with
                                | OnNext x ->
                                    Process.onNext downstream x
                                    return! loop outerDone currentInnerRef
                                | OnError e -> Process.onError downstream e
                                | OnCompleted ->
                                    if outerDone then
                                        Process.onCompleted downstream
                                    else
                                        return! loop outerDone None
                            | _ ->
                                // Message from old/cancelled inner, ignore
                                return! loop outerDone currentInnerRef
                    }

                loop false None)
}

/// Projects each element into an observable and switches to the latest.
let switchMap (mapper: 'T -> Observable<'U>) (source: Observable<'T>) : Observable<'U> = source |> map mapper |> switchInner

/// Projects each element and its index into an observable and switches to latest.
let switchMapi (mapper: 'T -> int -> Observable<'U>) (source: Observable<'T>) : Observable<'U> = source |> mapi mapper |> switchInner

/// Performs a side effect for each emission without transforming.
let tap (effect: 'T -> unit) (source: Observable<'T>) : Observable<'T> =
    Operator.forNext source (fun downstream x ->
        effect x
        Process.onNext downstream x)

/// Prepends values before the source emissions.
let startWith (values: 'T list) (source: Observable<'T>) : Observable<'T> = {
    subscribe =
        fun downstream ->
            for v in values do
                Process.onNext downstream v

            source.Subscribe(downstream)
}

/// Emits consecutive pairs of values.
let pairwise (source: Observable<'T>) : Observable<'T * 'T> =
    Operator.ofMsgStateful source None (fun downstream prev msg ->
        match msg with
        | OnNext x ->
            match prev with
            | Some p -> Process.onNext downstream (p, x)
            | None -> ()
            Some(Some x)
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            Process.onCompleted downstream
            None)

/// Applies an accumulator function over the source, emitting each intermediate result.
let scan (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Observable<'T>) : Observable<'U> =
    Operator.ofMsgStateful source initial (fun downstream acc msg ->
        match msg with
        | OnNext x ->
            let newAcc = accumulator acc x
            Process.onNext downstream newAcc
            Some newAcc
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            Process.onCompleted downstream
            None)

/// Applies an accumulator function over the source, emitting only the final value.
let reduce (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Observable<'T>) : Observable<'U> =
    Operator.ofMsgStateful source initial (fun downstream acc msg ->
        match msg with
        | OnNext x -> Some(accumulator acc x)
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            Process.onNext downstream acc
            Process.onCompleted downstream
            None)

/// Groups elements by key, returning an observable of grouped observables.
///
/// Each group is backed by a singleSubscriber channel agent, so group
/// sub-observables can be subscribed from any process.
let groupBy (keySelector: 'T -> 'K) (source: Observable<'T>) : Observable<'K * Observable<'T>> = {
    subscribe =
        fun downstream ->
            let ref = Process.makeRef ()

            Operator.spawnOp (fun () ->
                let channels = Dictionary<'K, Observer<'T>>()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe(upstream) |> ignore

                let rec loop () =
                    actor {
                        let! msg = Operator.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            let key = keySelector x

                            if not (channels.ContainsKey(key)) then
                                let (groupPush, groupObservable) = Channel.singleSubscriber<'T> ()
                                channels.[key] <- groupPush
                                Process.onNext downstream (key, groupObservable)

                            Channel.pushNext channels.[key] x
                            return! loop ()
                        | OnError e ->
                            channels |> Seq.iter (fun kv -> Channel.pushError kv.Value e)
                            Process.onError downstream e
                        | OnCompleted ->
                            channels |> Seq.iter (fun kv -> Channel.pushCompleted kv.Value)
                            Process.onCompleted downstream
                    }

                loop ())
}
