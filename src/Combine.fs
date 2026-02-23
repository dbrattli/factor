/// Combining operators for Factor
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Combine

open System.Collections.Generic
open Factor.Types
open Factor.Actor

/// Merges multiple factor sequences into one.
let merge (sources: Factor<'T> list) : Factor<'T> = {
    Spawn =
        fun downstream ->
            match sources with
            | [] ->
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                Process.spawnOp (fun () ->
                    List.iter
                        (fun source ->
                            let ref = Process.makeRef ()
                            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            source.Spawn(self) |> ignore)
                        sources

                    let rec loop remaining =
                        actor {
                            let! (_, rawMsg) = Process.recvAnyMsg ()

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

/// Merge two factors.
let merge2 (source1: Factor<'T>) (source2: Factor<'T>) : Factor<'T> = merge [ source1; source2 ]

/// Combines the latest values from two factors using a combiner function.
let combineLatest (combiner: 'T -> 'U -> 'V) (source1: Factor<'T>) (source2: Factor<'U>) : Factor<'V> = {
    Spawn =
        fun downstream ->
            Process.spawnOp (fun () ->
                let ref1 = Process.makeRef ()
                let ref2 = Process.makeRef ()
                let self1: Observer<'T> = { Pid = Process.selfPid (); Ref = ref1 }
                source1.Spawn(self1) |> ignore
                let self2: Observer<'U> = { Pid = Process.selfPid (); Ref = ref2 }
                source2.Spawn(self2) |> ignore

                let rec loop state =
                    actor {
                        let (left, right, leftDone, rightDone) = state
                        let! (ref, rawMsg) = Process.recvAnyMsg ()

                        if ref = ref1 then
                            match unbox<Msg<'T>> rawMsg with
                            | OnNext a ->
                                match right with
                                | Some b -> Process.onNext downstream (combiner a b)
                                | None -> ()

                                return! loop (Some a, right, leftDone, rightDone)
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                if rightDone then
                                    Process.onCompleted downstream
                                else
                                    return! loop (left, right, true, rightDone)
                        else
                            match unbox<Msg<'U>> rawMsg with
                            | OnNext b ->
                                match left with
                                | Some a -> Process.onNext downstream (combiner a b)
                                | None -> ()

                                return! loop (left, Some b, leftDone, rightDone)
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                if leftDone then
                                    Process.onCompleted downstream
                                else
                                    return! loop (left, right, leftDone, true)
                    }

                loop (None, None, false, false))
}

/// Combines source with the latest value from another factor.
let withLatestFrom (combiner: 'T -> 'U -> 'V) (sampler: Factor<'U>) (source: Factor<'T>) : Factor<'V> = {
    Spawn =
        fun downstream ->
            Process.spawnOp (fun () ->
                let samplerRef = Process.makeRef ()
                let sourceRef = Process.makeRef ()

                let samplerSelf: Observer<'U> = {
                    Pid = Process.selfPid ()
                    Ref = samplerRef
                }

                sampler.Spawn(samplerSelf) |> ignore

                let sourceSelf: Observer<'T> = {
                    Pid = Process.selfPid ()
                    Ref = sourceRef
                }

                source.Spawn(sourceSelf) |> ignore

                let rec loop latest =
                    actor {
                        let! (ref, rawMsg) = Process.recvAnyMsg ()

                        if ref = samplerRef then
                            match unbox<Msg<'U>> rawMsg with
                            | OnNext b -> return! loop (Some b)
                            | OnError e -> Process.onError downstream e
                            | OnCompleted -> return! loop latest
                        else
                            match unbox<Msg<'T>> rawMsg with
                            | OnNext a ->
                                match latest with
                                | Some b -> Process.onNext downstream (combiner a b)
                                | None -> ()

                                return! loop latest
                            | OnError e -> Process.onError downstream e
                            | OnCompleted -> Process.onCompleted downstream
                    }

                loop None)
}

/// Pairs elements from two factors by index.
let zip (combiner: 'T -> 'U -> 'V) (source1: Factor<'T>) (source2: Factor<'U>) : Factor<'V> = {
    Spawn =
        fun downstream ->
            Process.spawnOp (fun () ->
                let ref1 = Process.makeRef ()
                let ref2 = Process.makeRef ()
                let self1: Observer<'T> = { Pid = Process.selfPid (); Ref = ref1 }
                source1.Spawn(self1) |> ignore
                let self2: Observer<'U> = { Pid = Process.selfPid (); Ref = ref2 }
                source2.Spawn(self2) |> ignore
                let leftQueue = Queue<'T>()
                let rightQueue = Queue<'U>()

                let rec loop state =
                    actor {
                        let (leftDone, rightDone) = state
                        let! (ref, rawMsg) = Process.recvAnyMsg ()

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

/// Concatenates multiple factors sequentially.
let concat (sources: Factor<'T> list) : Factor<'T> = {
    Spawn =
        fun downstream ->
            match sources with
            | [] ->
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                Process.spawnOp (fun () ->
                    let rec subscribeNext (remaining: Factor<'T> list) =
                        match remaining with
                        | [] ->
                            Process.onCompleted downstream
                            actor { return () }
                        | current :: rest ->
                            let ref = Process.makeRef ()
                            let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            current.Spawn(upstream) |> ignore

                            let rec loop () =
                                actor {
                                    let! msg = Process.recvMsg<'T> ref

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

/// Concatenates two factors.
let concat2 (source1: Factor<'T>) (source2: Factor<'T>) : Factor<'T> = concat [ source1; source2 ]

/// Returns the factor that emits first.
let amb (sources: Factor<'T> list) : Factor<'T> = {
    Spawn =
        fun downstream ->
            match sources with
            | [] ->
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                let pid =
                    Process.spawnLinked (fun () ->
                        let mutable winner: int option = None
                        let mutable completedCount = 0
                        let total = sources.Length

                        sources
                        |> List.iteri (fun idx source ->
                            let ref = Process.makeRef ()

                            Process.registerChild ref (fun msg ->
                                let n = unbox<Msg<'T>> msg

                                match n with
                                | OnNext x ->
                                    match winner with
                                    | None ->
                                        winner <- Some idx
                                        Process.onNext downstream x
                                    | Some w ->
                                        if idx = w then
                                            Process.onNext downstream x
                                | OnError e ->
                                    match winner with
                                    | None ->
                                        Process.onError downstream e
                                        Process.exitNormal ()
                                    | Some w ->
                                        if idx = w then
                                            Process.onError downstream e
                                            Process.exitNormal ()
                                | OnCompleted ->
                                    match winner with
                                    | None ->
                                        completedCount <- completedCount + 1

                                        if completedCount >= total then
                                            Process.onCompleted downstream
                                            Process.exitNormal ()
                                    | Some w ->
                                        if idx = w then
                                            Process.onCompleted downstream
                                            Process.exitNormal ())

                            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            source.Spawn(self) |> ignore)

                        Process.childLoop ())

                {
                    Dispose = fun () -> Process.killProcess pid
                }
}

/// Alias for amb.
let race (sources: Factor<'T> list) : Factor<'T> = amb sources

/// Waits for all factors to complete, then emits a list of their last values.
let forkJoin (sources: Factor<'T> list) : Factor<'T list> = {
    Spawn =
        fun downstream ->
            match sources with
            | [] ->
                Process.onNext downstream []
                Process.onCompleted downstream
                emptyHandle ()
            | _ ->
                let pid =
                    Process.spawnLinked (fun () ->
                        let total = sources.Length
                        let values = Dictionary<int, 'T>()
                        let mutable completedCount = 0
                        let mutable stopped = false

                        sources
                        |> List.iteri (fun idx source ->
                            let ref = Process.makeRef ()

                            Process.registerChild ref (fun msg ->
                                if not stopped then
                                    let n = unbox<Msg<'T>> msg

                                    match n with
                                    | OnNext x -> values.[idx] <- x
                                    | OnError e ->
                                        stopped <- true
                                        Process.onError downstream e
                                        Process.exitNormal ()
                                    | OnCompleted ->
                                        completedCount <- completedCount + 1

                                        if completedCount >= total then
                                            if values.Count = total then
                                                let allValues = [ for i in 0 .. total - 1 -> values.[i] ]

                                                Process.onNext downstream allValues
                                                Process.onCompleted downstream
                                            else
                                                Process.onError
                                                    downstream
                                                    (ForkJoinException "fork_join: source completed without emitting")

                                            Process.exitNormal ())

                            let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                            source.Spawn(self) |> ignore)

                        Process.childLoop ())

                {
                    Dispose = fun () -> Process.killProcess pid
                }
}
