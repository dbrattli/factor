/// Filter operators for Factor.Reactive
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Reactive.Filter

open System.Collections.Generic
open Factor.Actor.Types
open Factor.Beam
open Factor.Beam.Actor

/// Filters elements based on a predicate.
let filter (predicate: 'T -> bool) (source: Observable<'T>) : Observable<'T> =
    Operator.forNext source (fun downstream x ->
        if predicate x then
            Process.onNext downstream x)

/// Applies a function that returns Option. Emits Some values, skips None.
let choose (chooser: 'T -> 'U option) (source: Observable<'T>) : Observable<'U> =
    Operator.forNext source (fun downstream x ->
        match chooser x with
        | Some value -> Process.onNext downstream value
        | None -> ())

/// Returns the first N elements from the source.
let take (count: int) (source: Observable<'T>) : Observable<'T> =
    if count <= 0 then
        {
            Subscribe =
                fun downstream ->
                    Process.onCompleted downstream
                    emptyHandle ()
        }
    else
        Operator.ofMsgStateful source count (fun downstream remaining msg ->
            match msg with
            | OnNext x ->
                let r = remaining - 1
                Process.onNext downstream x

                if r = 0 then
                    Process.onCompleted downstream
                    None
                else
                    Some r
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                Process.onCompleted downstream
                None)

/// Skips the first N elements from the source.
let skip (count: int) (source: Observable<'T>) : Observable<'T> =
    Operator.forNextStateful source count (fun downstream remaining x ->
        if remaining > 0 then
            remaining - 1
        else
            Process.onNext downstream x
            0)

/// Takes elements while predicate returns true.
let takeWhile (predicate: 'T -> bool) (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsgStateful source () (fun downstream () msg ->
        match msg with
        | OnNext x ->
            if predicate x then
                Process.onNext downstream x
                Some()
            else
                Process.onCompleted downstream
                None
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            Process.onCompleted downstream
            None)

/// Skips elements while predicate returns true.
let skipWhile (predicate: 'T -> bool) (source: Observable<'T>) : Observable<'T> =
    Operator.forNextStateful source true (fun downstream skipping x ->
        if skipping then
            if not (predicate x) then
                Process.onNext downstream x
                false
            else
                true
        else
            Process.onNext downstream x
            false)

/// Emits elements that are different from the previous element.
let distinctUntilChanged (source: Observable<'T>) : Observable<'T> =
    Operator.forNextStateful source None (fun downstream last x ->
        match last with
        | None ->
            Process.onNext downstream x
            Some x
        | Some prev ->
            if prev <> x then
                Process.onNext downstream x
                Some x
            else
                last)

/// Returns elements until the other observable emits.
let takeUntil (other: Observable<'U>) (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsg2 source other () (fun downstream () choice ->
        match choice with
        | Choice1Of2 msg ->
            match msg with
            | OnNext x ->
                Process.onNext downstream x
                Some()
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                Process.onCompleted downstream
                None
        | Choice2Of2 msg ->
            match msg with
            | OnNext _ ->
                Process.onCompleted downstream
                None
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted -> Some())

/// Returns the last N elements from the source.
let takeLast (count: int) (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsgStateful source ([]: 'T list) (fun downstream buffer msg ->
        match msg with
        | OnNext x ->
            let newBuffer = buffer @ [ x ]

            let trimmed =
                if List.length newBuffer > count then
                    List.tail newBuffer
                else
                    newBuffer

            Some trimmed
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            List.iter (fun item -> Process.onNext downstream item) buffer
            Process.onCompleted downstream
            None)

/// Takes only the first element. Errors if source is empty.
let first (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsgStateful source () (fun downstream () msg ->
        match msg with
        | OnNext x ->
            Process.onNext downstream x
            Process.onCompleted downstream
            None
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            Process.onError downstream SequenceEmptyException
            None)

/// Takes only the last element. Errors if source is empty.
let last (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsgStateful source None (fun downstream latest msg ->
        match msg with
        | OnNext x -> Some(Some x)
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            match latest with
            | Some x ->
                Process.onNext downstream x
                Process.onCompleted downstream
            | None -> Process.onError downstream SequenceEmptyException

            None)

/// Emits a default value if the source completes without emitting.
let defaultIfEmpty (defaultValue: 'T) (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsgStateful source false (fun downstream hasValue msg ->
        match msg with
        | OnNext x ->
            Process.onNext downstream x
            Some true
        | OnError e ->
            Process.onError downstream e
            None
        | OnCompleted ->
            if not hasValue then
                Process.onNext downstream defaultValue

            Process.onCompleted downstream
            None)

/// Samples the source when the sampler emits.
let sample (sampler: Observable<'U>) (source: Observable<'T>) : Observable<'T> =
    Operator.ofMsg2 source sampler (None, false, false) (fun downstream state choice ->
        let (latest, sourceDone, samplerDone) = state

        match choice with
        | Choice1Of2 msg ->
            match msg with
            | OnNext x -> Some(Some x, sourceDone, samplerDone)
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                if samplerDone then
                    Process.onCompleted downstream
                    None
                else
                    Some(latest, true, samplerDone)
        | Choice2Of2 msg ->
            match msg with
            | OnNext _ ->
                match latest with
                | Some x ->
                    Process.onNext downstream x
                    Some(None, sourceDone, samplerDone)
                | None -> Some(latest, sourceDone, samplerDone)
            | OnError e ->
                Process.onError downstream e
                None
            | OnCompleted ->
                if sourceDone then
                    Process.onCompleted downstream
                    None
                else
                    Some(latest, sourceDone, true))

/// Filters out all duplicate values (not just consecutive).
let distinct (source: Observable<'T>) : Observable<'T> = {
    Subscribe =
        fun downstream ->
            let ref = Process.makeRef ()

            Operator.spawnOp (fun () ->
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe upstream |> ignore
                let seen = HashSet<'T>()

                let rec loop () =
                    actor {
                        let! msg = Operator.recvMsg<'T> ref

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
