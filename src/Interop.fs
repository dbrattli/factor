/// Actor interop for Factor
///
/// Bridges BEAM processes with reactive streams.
module Factor.Interop

open Factor.Types

/// Sends each emitted value as a side effect while passing through to downstream.
/// This is a general-purpose "tee" for sending values somewhere.
let tapSend (send: 'a -> unit) (source: Factor<'a, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext value ->
                            send value
                            handler.Notify(OnNext value)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }
