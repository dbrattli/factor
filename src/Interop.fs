/// Actor interop for Factor
///
/// Bridges BEAM processes with reactive streams.
module Factor.Interop

open Factor.Types

/// Sends each emitted value as a side effect while passing through to downstream.
/// This is a general-purpose "tee" for sending values somewhere.
let tapSend (send: 'T -> unit) (source: Factor<'T>) : Factor<'T> =
    { Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let ref = Process.makeRef ()

                    Process.registerChild
                        ref
                        (fun msg ->
                            let n = unbox<Msg<'T>> msg

                            match n with
                            | OnNext value ->
                                send value
                                Process.onNext downstream value
                            | OnError e ->
                                Process.onError downstream e
                                Process.exitNormal ()
                            | OnCompleted ->
                                Process.onCompleted downstream
                                Process.exitNormal ())

                    let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(self) |> ignore
                    Process.childLoop ())

            { Dispose = fun () -> Process.killProcess pid } }
