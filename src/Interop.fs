/// Actor interop for Factor
///
/// Bridges BEAM processes with reactive streams.
module Factor.Interop

open Factor.Types

/// Sends each emitted value as a side effect while passing through to downstream.
/// This is a general-purpose "tee" for sending values somewhere.
let tapSend (send: 'a -> unit) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext value ->
                            send value
                            observer.Notify(OnNext value)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }
