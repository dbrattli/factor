/// Creation operators for Factor.Reactive
///
/// These functions create new Observable sequences from various sources.
/// Creation operators do NOT spawn processes — they send messages
/// directly to the downstream observer's mailbox via message passing.
/// BEAM mailbox buffering handles the sync-to-async transition.
module Factor.Reactive.Create

open Factor.Actor.Types
open Factor.Beam

/// Create an observable from a subscribe function.
let create (subscribe: Observer<'T> -> Handle) : Observable<'T> = { subscribe = subscribe }

/// Returns an observable containing a single element.
let single (value: 'T) : Observable<'T> = {
    subscribe =
        fun observer ->
            Process.onNext observer value
            Process.onCompleted observer
            emptyHandle ()
}

/// Returns an observable with no elements that completes immediately.
let empty<'T> () : Observable<'T> = {
    subscribe =
        fun observer ->
            Process.onCompleted observer
            emptyHandle ()
}

/// Returns an observable that never emits and never completes.
let never<'T> () : Observable<'T> = { subscribe = fun _ -> emptyHandle () }

/// Returns an observable that errors immediately with the given error.
let fail (error: exn) : Observable<'T> = {
    subscribe =
        fun observer ->
            Process.onError observer error
            emptyHandle ()
}

/// Returns an observable from a list of values.
let ofList (items: 'T list) : Observable<'T> = {
    subscribe =
        fun observer ->
            for x in items do
                Process.onNext observer x

            Process.onCompleted observer
            emptyHandle ()
}

/// Returns an observable that invokes the factory function
/// whenever a new observer subscribes.
let defer (factory: unit -> Observable<'T>) : Observable<'T> = {
    subscribe =
        fun observer ->
            let f = factory ()
            f.Subscribe(observer)
}
