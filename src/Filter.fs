/// Filter operators for Factor
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Filter

open System.Collections.Generic
open Factor.Types
open Factor.Actor

/// Filters elements based on a predicate.
let filter (predicate: 'T -> bool) (source: Factor<'T>) : Factor<'T> = {
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
                            if predicate x then
                                Process.onNext downstream x

                            return! loop ()
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}

/// Applies a function that returns Option. Emits Some values, skips None.
let choose (chooser: 'T -> 'U option) (source: Factor<'T>) : Factor<'U> = {
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
                            match chooser x with
                            | Some value -> Process.onNext downstream value
                            | None -> ()

                            return! loop ()
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}

/// Returns the first N elements from the source.
let take (count: int) (source: Factor<'T>) : Factor<'T> =
    if count <= 0 then
        {
            Spawn =
                fun downstream ->
                    Process.onCompleted downstream
                    emptyHandle ()
        }
    else
        {
            Spawn =
                fun downstream ->
                    let ref = Process.makeRef ()

                    Process.spawnOp (fun () ->
                        let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                        source.Spawn(upstream) |> ignore

                        let rec loop remaining =
                            actor {
                                let! msg = Process.recvMsg<'T> ref

                                match msg with
                                | OnNext x ->
                                    let r = remaining - 1
                                    Process.onNext downstream x

                                    if r = 0 then
                                        Process.onCompleted downstream
                                    else
                                        return! loop r
                                | OnError e -> Process.onError downstream e
                                | OnCompleted -> Process.onCompleted downstream
                            }

                        loop count)
        }

/// Skips the first N elements from the source.
let skip (count: int) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop remaining =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            if remaining > 0 then
                                return! loop (remaining - 1)
                            else
                                Process.onNext downstream x
                                return! loop 0
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop count)
}

/// Takes elements while predicate returns true.
let takeWhile (predicate: 'T -> bool) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn upstream |> ignore

                let rec loop () =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            if predicate x then
                                Process.onNext downstream x
                                return! loop ()
                            else
                                Process.onCompleted downstream
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}

/// Skips elements while predicate returns true.
let skipWhile (predicate: 'T -> bool) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop skipping =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            if skipping then
                                if not (predicate x) then
                                    Process.onNext downstream x
                                    return! loop false
                                else
                                    return! loop true
                            else
                                Process.onNext downstream x
                                return! loop false
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop true)
}

/// Emits elements that are different from the previous element.
let distinctUntilChanged (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop last =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            match last with
                            | None ->
                                Process.onNext downstream x
                                return! loop (Some x)
                            | Some prev ->
                                if prev <> x then
                                    Process.onNext downstream x
                                    return! loop (Some x)
                                else
                                    return! loop last
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop None)
}

/// Returns elements until the other factor emits.
let takeUntil (other: Factor<'U>) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            Process.spawnOp (fun () ->
                let otherRef = Process.makeRef ()
                let sourceRef = Process.makeRef ()

                let otherSelf: Observer<'U> = {
                    Pid = Process.selfPid ()
                    Ref = otherRef
                }

                other.Spawn(otherSelf) |> ignore

                let sourceSelf: Observer<'T> = {
                    Pid = Process.selfPid ()
                    Ref = sourceRef
                }

                source.Spawn(sourceSelf) |> ignore

                let rec loop () =
                    actor {
                        let! (ref, rawMsg) = Process.recvAnyMsg ()

                        if ref = otherRef then
                            match unbox<Msg<'U>> rawMsg with
                            | OnNext _ -> Process.onCompleted downstream
                            | OnError e -> Process.onError downstream e
                            | OnCompleted -> return! loop ()
                        else
                            match unbox<Msg<'T>> rawMsg with
                            | OnNext x ->
                                Process.onNext downstream x
                                return! loop ()
                            | OnError e -> Process.onError downstream e
                            | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}

/// Returns the last N elements from the source.
let takeLast (count: int) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop (buffer: 'T list) =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            let newBuffer = buffer @ [ x ]

                            let trimmed =
                                if List.length newBuffer > count then
                                    List.tail newBuffer
                                else
                                    newBuffer

                            return! loop trimmed
                        | OnError e -> Process.onError downstream e
                        | OnCompleted ->
                            List.iter (fun item -> Process.onNext downstream item) buffer
                            Process.onCompleted downstream
                    }

                loop [])
}

/// Takes only the first element. Errors if source is empty.
let first (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn upstream |> ignore

                actor {
                    let! msg = Process.recvMsg<'T> ref

                    match msg with
                    | OnNext x ->
                        Process.onNext downstream x
                        Process.onCompleted downstream
                    | OnError e -> Process.onError downstream e
                    | OnCompleted -> Process.onError downstream SequenceEmptyException
                })
}

/// Takes only the last element. Errors if source is empty.
let last (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop latest =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x -> return! loop (Some x)
                        | OnError e -> Process.onError downstream e
                        | OnCompleted ->
                            match latest with
                            | Some x ->
                                Process.onNext downstream x
                                Process.onCompleted downstream
                            | None -> Process.onError downstream SequenceEmptyException
                    }

                loop None)
}

/// Emits a default value if the source completes without emitting.
let defaultIfEmpty (defaultValue: 'T) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn upstream |> ignore

                let rec loop hasValue =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            Process.onNext downstream x
                            return! loop true
                        | OnError e -> Process.onError downstream e
                        | OnCompleted ->
                            if not hasValue then
                                Process.onNext downstream defaultValue

                            Process.onCompleted downstream
                    }

                loop false)
}

/// Samples the source when the sampler emits.
let sample (sampler: Factor<'U>) (source: Factor<'T>) : Factor<'T> = {
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

                source.Spawn sourceSelf |> ignore

                let rec loop state =
                    actor {
                        let latest, sourceDone, samplerDone = state
                        let! ref, rawMsg = Process.recvAnyMsg ()

                        if ref = samplerRef then
                            match unbox<Msg<'U>> rawMsg with
                            | OnNext _ ->
                                match latest with
                                | Some x ->
                                    Process.onNext downstream x
                                    return! loop (None, sourceDone, samplerDone)
                                | None -> return! loop (latest, sourceDone, samplerDone)
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                if sourceDone then
                                    Process.onCompleted downstream
                                else
                                    return! loop (latest, sourceDone, true)
                        else
                            match unbox<Msg<'T>> rawMsg with
                            | OnNext x -> return! loop (Some x, sourceDone, samplerDone)
                            | OnError e -> Process.onError downstream e
                            | OnCompleted ->
                                if samplerDone then
                                    Process.onCompleted downstream
                                else
                                    return! loop (latest, true, samplerDone)
                    }

                loop (None, false, false))
}

/// Filters out all duplicate values (not just consecutive).
let distinct (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let ref = Process.makeRef ()

            Process.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn upstream |> ignore
                let seen = HashSet<'T>()

                let rec loop () =
                    actor {
                        let! msg = Process.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            if seen.Add(x) then
                                Process.onNext downstream x

                            return! loop ()
                        | OnError e -> Process.onError downstream e
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}
