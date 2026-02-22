/// Error handling operators for Factor
///
/// Every operator spawns a BEAM process. The pipeline IS the supervision tree.
module Factor.Error

open Factor.Types
open Factor.Actor

/// Resubscribes to the source factor when an error occurs,
/// up to the specified number of retries.
let retry (maxRetries: int) (source: Factor<'T>) : Factor<'T> =
    { Spawn =
        fun downstream ->
            Process.spawnOp (fun () ->
                let rec subscribeToSource (retriesLeft: int) =
                    let ref = Process.makeRef ()
                    let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(upstream) |> ignore

                    let rec loop () = actor {
                        let! msg = Process.recvMsg<'T> ref

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

                subscribeToSource maxRetries) }

/// On error, switches to a fallback factor returned by the error handler.
let catch (errorHandler: exn -> Factor<'T>) (source: Factor<'T>) : Factor<'T> =
    { Spawn =
        fun downstream ->
            Process.spawnOp (fun () ->
                let ref = Process.makeRef ()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Spawn(upstream) |> ignore

                let rec loop () = actor {
                    let! msg = Process.recvMsg<'T> ref

                    match msg with
                    | OnNext x ->
                        Process.onNext downstream x
                        return! loop ()
                    | OnError e ->
                        let fallback = errorHandler e
                        let fallbackRef = Process.makeRef ()
                        let fallbackUpstream: Observer<'T> = { Pid = Process.selfPid (); Ref = fallbackRef }
                        fallback.Spawn(fallbackUpstream) |> ignore

                        let rec innerLoop () = actor {
                            let! innerMsg = Process.recvMsg<'T> fallbackRef

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
                loop ()) }
