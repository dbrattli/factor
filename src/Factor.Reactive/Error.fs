/// Error handling operators for Factor.Reactive
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Reactive.Error

open Factor.Actor.Types
open Factor.Beam
open Factor.Beam.Actor

/// Resubscribes to the source observable when an error occurs,
/// up to the specified number of retries.
let retry (maxRetries: int) (source: Observable<'T>) : Observable<'T> = {
    subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let rec subscribeToSource (retriesLeft: int) =
                    let ref = Process.makeRef ()
                    let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Subscribe(upstream) |> ignore

                    let rec loop () =
                        actor {
                            let! msg = Operator.recvMsg<'T> ref

                            match msg with
                            | OnNext x ->
                                Process.onNext downstream x
                                return! loop ()
                            | OnError e ->
                                if retriesLeft > 0 then
                                    return! subscribeToSource (retriesLeft - 1)
                                else
                                    Process.onError downstream e
                            | OnCompleted -> Process.onCompleted downstream
                        }

                    loop ()

                subscribeToSource maxRetries)
}

/// On error, switches to a fallback observable returned by the error handler.
let catch (errorHandler: exn -> Observable<'T>) (source: Observable<'T>) : Observable<'T> = {
    subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let ref = Process.makeRef ()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe(upstream) |> ignore

                let rec loop () =
                    actor {
                        let! msg = Operator.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            Process.onNext downstream x
                            return! loop ()
                        | OnError e ->
                            let fallback = errorHandler e
                            let fallbackRef = Process.makeRef ()

                            let fallbackUpstream: Observer<'T> = {
                                Pid = Process.selfPid ()
                                Ref = fallbackRef
                            }

                            fallback.Subscribe(fallbackUpstream) |> ignore

                            let rec innerLoop () =
                                actor {
                                    let! innerMsg = Operator.recvMsg<'T> fallbackRef

                                    match innerMsg with
                                    | OnNext x ->
                                        Process.onNext downstream x
                                        return! innerLoop ()
                                    | OnError fe -> Process.onError downstream fe
                                    | OnCompleted -> Process.onCompleted downstream
                                }

                            return! innerLoop ()
                        | OnCompleted -> Process.onCompleted downstream
                    }

                loop ())
}
