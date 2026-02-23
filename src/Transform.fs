/// Transform operators for Factor
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Transform

open System.Collections.Generic
open Factor.Types
open Factor.Actor

/// Returns a factor whose elements are the result of invoking
/// the mapper function on each element of the source.
let map (mapper: 'T -> 'U) (source: Factor<'T>) : Factor<'U> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop () =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            Process.onNext downstream (mapper x)
                            return! loop ()
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}

/// Returns a factor whose elements are the result of invoking
/// the mapper function on each element and its index.
let mapi (mapper: 'T -> int -> 'U) (source: Factor<'T>) : Factor<'U> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop index =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            Process.onNext downstream (mapper x index)
                            return! loop (index + 1)
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop 0)
}

/// Flattens a Factor of Factors by merging inner emissions.
/// Each inner runs in its own linked child process (supervision boundary).
///
/// The maxConcurrency parameter controls how many inner factors
/// can be subscribed to concurrently:
/// - None: Unlimited concurrency
/// - Some(1): Sequential (equivalent to concatInner)
/// - Some(n): At most n inner factors active at once
let mergeInner (policy: SupervisionPolicy) (maxConcurrency: int option) (source: Factor<Factor<'T>>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let parentPid = Process.selfPid ()
                    let mutable innerCount = 0
                    let mutable outerStopped = false
                    let mutable disposed = false
                    let queue = Queue<Factor<'T>>()

                    Process.trapExits ()

                    let canSubscribe () =
                        match maxConcurrency with
                        | None -> true
                        | Some max -> innerCount < max

                    let checkComplete () =
                        if outerStopped && innerCount <= 0 && queue.Count = 0 then
                            Process.onCompleted downstream
                            Process.exitNormal ()

                    // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
                    let mutable subscribeToInner: Factor<'T> -> int -> unit = fun _ _ -> ()

                    subscribeToInner <-
                        fun (inner: Factor<'T>) (retriesLeft: int) ->
                            innerCount <- innerCount + 1
                            let childRef = Process.makeRef ()

                            // Register child handler in parent to receive forwarded messages
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

                            // Spawn linked child process for this inner subscription
                            let childPid =
                                Process.spawnLinked (fun () ->
                                    let mutable innerDone = false
                                    let innerRef = Process.makeRef ()

                                    Process.registerChild innerRef (fun msg ->
                                        let n = unbox<Msg<'T>> msg
                                        Process.sendChildMsg parentPid childRef (box n)

                                        match n with
                                        | OnCompleted
                                        | OnError _ ->
                                            innerDone <- true
                                            Process.exitNormal ()
                                        | _ -> ())

                                    let innerSelf: Observer<'T> = {
                                        Pid = Process.selfPid ()
                                        Ref = innerRef
                                    }

                                    inner.Spawn(innerSelf) |> ignore

                                    if not innerDone then
                                        Process.childLoop ())

                            // Register exit handler — behavior depends on supervision policy
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
                        let n = unbox<Msg<Factor<'T>>> msg

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

                    let outerSelf: Observer<Factor<'T>> = {
                        Pid = Process.selfPid ()
                        Ref = outerRef
                    }

                    source.Spawn(outerSelf) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Projects each element into a factor and merges the results.
/// Each inner runs in its own linked child process (supervision boundary).
let flatMap (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> map mapper |> mergeInner Terminate None

/// Flattens a Factor of Factors by concatenating in order.
let concatInner (source: Factor<Factor<'T>>) : Factor<'T> = mergeInner Terminate (Some 1) source

/// Projects each element into a factor and concatenates in order.
let concatMap (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> = source |> map mapper |> concatInner

/// Projects each element and its index into a factor and merges.
let flatMapi (mapper: 'T -> int -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> mapi mapper |> mergeInner Terminate None

/// Projects each element and its index into a factor and concatenates.
let concatMapi (mapper: 'T -> int -> Factor<'U>) (source: Factor<'T>) : Factor<'U> = source |> mapi mapper |> concatInner

/// Flattens a Factor of Factors by switching to the latest.
/// Only messages from the most recent inner factor are forwarded.
let switchInner (source: Factor<Factor<'T>>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable outerStopped = false
                    let mutable disposed = false
                    let mutable currentId = 0
                    let mutable hasInner = false

                    let outerRef = Process.makeRef ()

                    Process.registerChild outerRef (fun msg ->
                        let n = unbox<Msg<Factor<'T>>> msg

                        if not disposed then
                            match n with
                            | OnNext innerFactor ->
                                currentId <- currentId + 1
                                let myId = currentId
                                hasInner <- true

                                let innerRef = Process.makeRef ()

                                Process.registerChild innerRef (fun msg ->
                                    let n = unbox<Msg<'T>> msg

                                    if not disposed && currentId = myId then
                                        match n with
                                        | OnNext value -> Process.onNext downstream value
                                        | OnError e ->
                                            disposed <- true
                                            Process.onError downstream e
                                            Process.exitNormal ()
                                        | OnCompleted ->
                                            hasInner <- false

                                            if outerStopped then
                                                Process.onCompleted downstream
                                                Process.exitNormal ())

                                let innerSelf: Observer<'T> = {
                                    Pid = Process.selfPid ()
                                    Ref = innerRef
                                }

                                innerFactor.Spawn(innerSelf) |> ignore
                            | OnError e ->
                                disposed <- true
                                Process.onError downstream e
                                Process.exitNormal ()
                            | OnCompleted ->
                                outerStopped <- true

                                if not hasInner then
                                    Process.onCompleted downstream
                                    Process.exitNormal ())

                    let outerSelf: Observer<Factor<'T>> = {
                        Pid = Process.selfPid ()
                        Ref = outerRef
                    }

                    source.Spawn(outerSelf) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Projects each element into a factor and switches to the latest.
let switchMap (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> = source |> map mapper |> switchInner

/// Projects each element and its index into a factor and switches to latest.
let switchMapi (mapper: 'T -> int -> Factor<'U>) (source: Factor<'T>) : Factor<'U> = source |> mapi mapper |> switchInner

/// Performs a side effect for each emission without transforming.
let tap (effect: 'T -> unit) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop () =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            effect x
                            Process.onNext downstream x
                            return! loop ()
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}

/// Prepends values before the source emissions.
let startWith (values: 'T list) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            for v in values do
                Process.onNext downstream v

            source.Spawn(downstream)
}

/// Emits consecutive pairs of values.
let pairwise (source: Factor<'T>) : Factor<'T * 'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop prev =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            match prev with
                            | Some p -> Process.onNext downstream (p, x)
                            | None -> ()

                            return! loop (Some x)
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop None)
}

/// Applies an accumulator function over the source, emitting each intermediate result.
let scan (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Factor<'T>) : Factor<'U> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop acc =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            let newAcc = accumulator acc x
                            Process.onNext downstream newAcc
                            return! loop newAcc
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop initial)
}

/// Applies an accumulator function over the source, emitting only the final value.
let reduce (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Factor<'T>) : Factor<'U> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop acc =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x -> return! loop (accumulator acc x)
                        | OnError e -> Process.onError downstream e
                        | OnCompleted ->
                            Process.onNext downstream acc
                            Process.onCompleted downstream
                    }

                loop initial)
}

/// Groups elements by key, returning a factor of grouped factors.
///
/// Each group is backed by a singleChannel actor process, so group
/// sub-factors can be subscribed from any process.
let groupBy (keySelector: 'T -> 'K) (source: Factor<'T>) : Factor<'K * Factor<'T>> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let channels = Dictionary<'K, obj * (Msg<'T> -> unit)>()
                    let ref = Process.makeRef ()

                    Process.registerChild ref (fun msg ->
                        let n = unbox<Msg<'T>> msg

                        match n with
                        | OnNext x ->
                            let key = keySelector x
                            let isNew = not (channels.ContainsKey(key))

                            if isNew then
                                let channelPid = Process.startSingleStream ()

                                let sendToChannel (msg: Msg<'T>) =
                                    match msg with
                                    | OnNext _ -> Process.streamNotify channelPid (box msg)
                                    | OnError _
                                    | OnCompleted -> Process.streamNotifyTerminal channelPid (box msg)

                                channels.[key] <- (channelPid, sendToChannel)

                                let groupFactor: Factor<'T> = {
                                    Spawn =
                                        fun groupObserver ->
                                            let groupRef = Process.makeRef ()

                                            Process.registerChild groupRef (fun msg ->
                                                let gn = unbox<Msg<'T>> msg
                                                Process.notify groupObserver gn)

                                            Process.streamSubscribe channelPid groupRef

                                            {
                                                Dispose =
                                                    fun () ->
                                                        Process.unregisterChild groupRef
                                                        Process.streamUnsubscribe channelPid groupRef
                                            }
                                }

                                Process.onNext downstream (key, groupFactor)

                            let (_pid, sendToChannel) = channels.[key]
                            sendToChannel (OnNext x)
                        | OnError e ->
                            for kv in channels do
                                let (_pid, sendToChannel) = kv.Value
                                sendToChannel (OnError e)

                            Process.onError downstream e
                            Process.exitNormal ()
                        | OnCompleted ->
                            for kv in channels do
                                let (_pid, sendToChannel) = kv.Value
                                sendToChannel OnCompleted

                            Process.onCompleted downstream
                            Process.exitNormal ())

                    let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(self) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}
