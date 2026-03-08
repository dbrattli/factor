namespace Factor.Reactive

open Factor.Actor.Types
open Factor.Beam

[<AutoOpen>]
module ObservableExtensions =

    type Observable<'T> with

        /// Subscribe an observer endpoint to this observable.
        member this.Subscribe(observer: Observer<'T>) : Handle = this.subscribe observer

        /// Subscribe to an observable with user callbacks.
        /// Registers a child handler in the current process for receiving messages.
        /// The caller's process must run a message loop (sleep/processTimers).
        member this.Subscribe(onNextFn: 'T -> unit, onErrorFn: exn -> unit, onCompletedFn: unit -> unit) : Handle =
            let ref = Process.makeRef ()

            Process.registerChild ref (fun msg ->
                let n = unbox<Msg<'T>> msg

                match n with
                | OnNext x -> onNextFn x
                | OnError e ->
                    Process.unregisterChild ref
                    onErrorFn e
                | OnCompleted ->
                    Process.unregisterChild ref
                    onCompletedFn ())

            let endpoint: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
            let handle = this.subscribe endpoint

            {
                Dispose =
                    fun () ->
                        Process.unregisterChild ref
                        handle.Dispose()
            }
