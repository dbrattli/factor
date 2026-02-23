/// Creation operators for Factor
///
/// These functions create new Factor sequences from various sources.
/// Creation operators do NOT spawn processes — they send messages
/// directly to the downstream observer's mailbox via message passing.
/// BEAM mailbox buffering handles the sync-to-async transition.
module Factor.Create

open Factor.Types

/// Create a factor from a subscribe function.
let create (subscribe: Observer<'T> -> Handle) : Factor<'T> = { Spawn = subscribe }

/// Returns a factor containing a single element.
let single (value: 'T) : Factor<'T> = {
    Spawn =
        fun observer ->
            Process.onNext observer value
            Process.onCompleted observer
            emptyHandle ()
}

/// Returns a factor with no elements that completes immediately.
let empty<'T> () : Factor<'T> = {
    Spawn =
        fun observer ->
            Process.onCompleted observer
            emptyHandle ()
}

/// Returns a factor that never emits and never completes.
let never<'T> () : Factor<'T> = { Spawn = fun _ -> emptyHandle () }

/// Returns a factor that errors immediately with the given error.
let fail (error: exn) : Factor<'T> = {
    Spawn =
        fun observer ->
            Process.onError observer error
            emptyHandle ()
}

/// Returns a factor from a list of values.
let ofList (items: 'T list) : Factor<'T> = {
    Spawn =
        fun observer ->
            for x in items do
                Process.onNext observer x

            Process.onCompleted observer
            emptyHandle ()
}

/// Returns a factor that invokes the factory function
/// whenever a new observer subscribes.
let defer (factory: unit -> Factor<'T>) : Factor<'T> = {
    Spawn =
        fun observer ->
            let f = factory ()
            f.Spawn(observer)
}
