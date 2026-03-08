/// Combining operators for Factor.Reactive
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Reactive.Combine

open System.Collections.Generic
open Factor.Actor.Types
open Factor.Beam
open Factor.Beam.Actor

/// Merges multiple observable sequences into one.
let merge (sources: Observable<'T> list) : Observable<'T> = {
    subscribe =
        fun downstream ->
            match sources with
            | [] ->
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                Operator.spawnOp (fun () ->
                    List.iter
                        (fun (source: Observable<'T>) ->
                            let ref = Process.makeRef ()
                            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            source.Subscribe(self) |> ignore)
                        sources

                    let rec loop remaining =
                        actor {
                            let! (_, rawMsg) = Operator.recvAnyMsg ()

                            match unbox<Msg<'T>> rawMsg with
                            | OnNext x ->
                                Process.onNext downstream x
                                return! loop remaining
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                let r = remaining - 1

                                if r <= 0 then
                                    Process.onCompleted downstream
                                else
                                    return! loop r
                        }

                    loop sources.Length)
}

/// Merge two observables.
let merge2 (source1: Observable<'T>) (source2: Observable<'T>) : Observable<'T> = merge [ source1; source2 ]

/// Combines the latest values from two observables using a combiner function.
let combineLatest (combiner: 'T -> 'U -> 'V) (source1: Observable<'T>) (source2: Observable<'U>) : Observable<'V> =
    Operator.ofMsg2 source1 source2 (None, None, false, false) (fun downstream state choice ->
        let (left, right, leftDone, rightDone) = state

        match choice with
        | Choice1Of2 msg ->
            match msg with
            | OnNext a ->
                match right with
                | Some b -> Process.onNext downstream (combiner a b)
                | None -> ()

                Some(Some a, right, leftDone, rightDone)
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                if rightDone then
                    Process.onCompleted downstream
                    None
                else
                    Some(left, right, true, rightDone)
        | Choice2Of2 msg ->
            match msg with
            | OnNext b ->
                match left with
                | Some a -> Process.onNext downstream (combiner a b)
                | None -> ()

                Some(left, Some b, leftDone, rightDone)
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                if leftDone then
                    Process.onCompleted downstream
                    None
                else
                    Some(left, right, leftDone, true))

/// Combines source with the latest value from another observable.
let withLatestFrom (combiner: 'T -> 'U -> 'V) (sampler: Observable<'U>) (source: Observable<'T>) : Observable<'V> =
    // Subscribe sampler (source1) first so its value is available when source emits
    Operator.ofMsg2 sampler source None (fun downstream latest choice ->
        match choice with
        | Choice1Of2 msg ->
            match msg with
            | OnNext b -> Some(Some b)
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted -> Some latest
        | Choice2Of2 msg ->
            match msg with
            | OnNext a ->
                match latest with
                | Some b -> Process.onNext downstream (combiner a b)
                | None -> ()

                Some latest
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                Process.onCompleted downstream
                None)

/// Pairs elements from two observables by index.
let zip (combiner: 'T -> 'U -> 'V) (source1: Observable<'T>) (source2: Observable<'U>) : Observable<'V> = {
    subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let ref1 = Process.makeRef ()
                let ref2 = Process.makeRef ()
                let self1: Observer<'T> = { Pid = Process.selfPid (); Ref = ref1 }
                source1.Subscribe(self1) |> ignore
                let self2: Observer<'U> = { Pid = Process.selfPid (); Ref = ref2 }
                source2.Subscribe(self2) |> ignore
                let leftQueue = Queue<'T>()
                let rightQueue = Queue<'U>()

                let rec loop state =
                    actor {
                        let (leftDone, rightDone) = state
                        let! (ref, rawMsg) = Operator.recvAnyMsg ()

                        if ref = ref1 then
                            match unbox<Msg<'T>> rawMsg with
                            | OnNext a ->
                                if rightQueue.Count > 0 then
                                    let b = rightQueue.Dequeue()
                                    Process.onNext downstream (combiner a b)

                                    if rightDone && rightQueue.Count = 0 then
                                        Process.onCompleted downstream
                                    else
                                        return! loop state
                                else
                                    leftQueue.Enqueue(a)
                                    return! loop state
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                if leftQueue.Count = 0 then
                                    Process.onCompleted downstream
                                else
                                    return! loop (true, rightDone)
                        else
                            match unbox<Msg<'U>> rawMsg with
                            | OnNext b ->
                                if leftQueue.Count > 0 then
                                    let a = leftQueue.Dequeue()
                                    Process.onNext downstream (combiner a b)

                                    if leftDone && leftQueue.Count = 0 then
                                        Process.onCompleted downstream
                                    else
                                        return! loop state
                                else
                                    rightQueue.Enqueue(b)
                                    return! loop state
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                if rightQueue.Count = 0 then
                                    Process.onCompleted downstream
                                else
                                    return! loop (leftDone, true)
                    }

                loop (false, false))
}

/// Concatenates multiple observables sequentially.
let concat (sources: Observable<'T> list) : Observable<'T> = {
    subscribe =
        fun downstream ->
            match sources with
            | [] ->
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                Operator.spawnOp (fun () ->
                    let rec subscribeNext (remaining: Observable<'T> list) =
                        match remaining with
                        | [] ->
                            Process.onCompleted downstream
                            actor { return () }
                        | current :: rest ->
                            let ref = Process.makeRef ()
                            let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            current.Subscribe(upstream) |> ignore

                            let rec loop () =
                                actor {
                                    let! msg = Operator.recvMsg<'T> ref

                                    match msg with
                                    | OnNext x ->
                                        Process.onNext downstream x
                                        return! loop ()
                                    | OnError e -> Process.onError downstream e
                                    | OnCompleted -> return! subscribeNext rest
                                }

                            loop ()

                    subscribeNext sources)
}

/// Concatenates two observables.
let concat2 (source1: Observable<'T>) (source2: Observable<'T>) : Observable<'T> = concat [ source1; source2 ]

/// Returns the observable that emits first.
let amb (sources: Observable<'T> list) : Observable<'T> = {
    subscribe =
        fun downstream ->
            match sources with
            | [] ->
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                let total = sources.Length

                Operator.spawnOp (fun () ->
                    sources
                    |> List.iter (fun source ->
                        let ref = Process.makeRef ()
                        let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                        source.Subscribe(self) |> ignore)

                    let rec loop winner completedCount =
                        actor {
                            let! (ref, rawMsg) = Operator.recvAnyMsg ()

                            match unbox<Msg<'T>> rawMsg with
                            | OnNext x ->
                                match winner with
                                | None ->
                                    Process.onNext downstream x
                                    return! loop (Some ref) completedCount
                                | Some wRef ->
                                    if Process.refEquals ref wRef then
                                        Process.onNext downstream x

                                    return! loop winner completedCount
                            | OnError e ->
                                match winner with
                                | None -> Process.onError downstream e
                                | Some wRef ->
                                    if Process.refEquals ref wRef then
                                        Process.onError downstream e
                                    else
                                        return! loop winner completedCount
                            | OnCompleted ->
                                match winner with
                                | None ->
                                    let c = completedCount + 1

                                    if c >= total then
                                        Process.onCompleted downstream
                                    else
                                        return! loop winner c
                                | Some wRef ->
                                    if Process.refEquals ref wRef then
                                        Process.onCompleted downstream
                                    else
                                        return! loop winner completedCount
                        }

                    loop None 0)
}

/// Alias for amb.
let race (sources: Observable<'T> list) : Observable<'T> = amb sources

/// Waits for all observables to complete, then emits a list of their last values.
let forkJoin (sources: Observable<'T> list) : Observable<'T list> = {
    subscribe =
        fun downstream ->
            match sources with
            | [] ->
                Process.onNext downstream []
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                let total = sources.Length

                Operator.spawnOp (fun () ->
                    let refs =
                        sources
                        |> List.mapi (fun idx source ->
                            let ref = Process.makeRef ()
                            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            source.Subscribe(self) |> ignore
                            (ref, idx))

                    let values = Dictionary<int, 'T>()

                    let findIdx ref =
                        let rec search lst =
                            match lst with
                            | (r, idx) :: _ when Process.refEquals r ref -> idx
                            | _ :: rest -> search rest
                            | [] -> -1

                        search refs

                    let rec loop completedCount =
                        actor {
                            let! (ref, rawMsg) = Operator.recvAnyMsg ()
                            let idx = findIdx ref

                            match unbox<Msg<'T>> rawMsg with
                            | OnNext x ->
                                values.[idx] <- x
                                return! loop completedCount
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                let c = completedCount + 1

                                if c >= total then
                                    if values.Count = total then
                                        let allValues = [ for i in 0 .. total - 1 -> values.[i] ]
                                        Process.onNext downstream allValues
                                        Process.onCompleted downstream
                                    else
                                        Process.onError
                                            downstream
                                            (ForkJoinException "fork_join: source completed without emitting")
                                else
                                    return! loop c
                        }

                    loop 0)
}
