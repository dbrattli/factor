/// Creation operators for Factor
///
/// These functions create new Factor sequences from various sources.
module Factor.Create

open Factor.Types

/// Create a factor from a subscribe function.
let create (subscribe: Handler<'T> -> Handle) : Factor<'T> = { Subscribe = subscribe }

/// Returns a factor containing a single element.
let single (value: 'T) : Factor<'T> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnNext value)
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Returns a factor with no elements that completes immediately.
let empty<'T> () : Factor<'T> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Returns a factor that never emits and never completes.
let never<'T> () : Factor<'T> =
    { Subscribe = fun _ -> emptyHandle () }

/// Returns a factor that errors immediately with the given error.
let fail (error: exn) : Factor<'T> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnError error)
            emptyHandle () }

/// Returns a factor from a list of values.
let ofList (items: 'T list) : Factor<'T> =
    { Subscribe =
        fun handler ->
            for x in items do
                handler.Notify(OnNext x)

            handler.Notify(OnCompleted)
            emptyHandle () }

/// Returns a factor that invokes the factory function
/// whenever a new handler subscribes.
let defer (factory: unit -> Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let f = factory ()
            f.Subscribe(handler) }
