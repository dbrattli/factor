/// Actor interop for Factor.Reactive
///
/// Bridges BEAM processes with reactive streams.
module Factor.Reactive.Interop

open Factor.Agent.Types
open Factor.Beam

/// Sends each emitted value as a side effect while passing through to downstream.
/// This is a general-purpose "tee" for sending values somewhere.
let tapSend (send: 'T -> unit) (source: Observable<'T>) : Observable<'T> =
    Actor.forNext source (fun downstream x ->
        send x
        Process.onNext downstream x)
