/// Creation operators for Factor
///
/// These functions create new Factor sequences from various sources.
module Factor.Create

open Factor.Types

/// Create a factor from a subscribe function.
let create (subscribe: Handler<'a, 'e> -> Handle) : Factor<'a, 'e> = { Subscribe = subscribe }

/// Returns a factor containing a single element.
let single (value: 'a) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnNext value)
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Returns a factor with no elements that completes immediately.
let empty<'a, 'e> () : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Returns a factor that never emits and never completes.
let never<'a, 'e> () : Factor<'a, 'e> =
    { Subscribe = fun _ -> emptyHandle () }

/// Returns a factor that errors immediately with the given error.
let fail (error: 'e) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnError error)
            emptyHandle () }

/// Returns a factor from a list of values.
let ofList (items: 'a list) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            for x in items do
                handler.Notify(OnNext x)

            handler.Notify(OnCompleted)
            emptyHandle () }

/// Returns a factor that invokes the factory function
/// whenever a new handler subscribes.
let defer (factory: unit -> Factor<'a, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let f = factory ()
            f.Subscribe(handler) }
