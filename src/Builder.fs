/// Builder module for Factor
///
/// Provides computation expression builder for composing factors
/// in a monadic style. Each `let!` desugars to flatMap, which
/// in the actor model creates a process boundary.
module Factor.Builder

open Factor.Types

/// Bind a factor to a continuation function (flatMap).
let bind (source: Factor<'a, 'e>) (continuation: 'a -> Factor<'b, 'e>) : Factor<'b, 'e> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let inner = continuation x

                            let innerHandler =
                                { Notify =
                                    fun innerN ->
                                        match innerN with
                                        | OnNext value -> handler.Notify(OnNext value)
                                        | OnError e -> handler.Notify(OnError e)
                                        | OnCompleted -> () }

                            inner.Subscribe innerHandler |> ignore
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Lift a pure value into a factor.
let ret (value: 'a) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnNext value)
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Empty factor - completes immediately with no values.
let zero<'a, 'e> () : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Combine two factors sequentially (concat).
let combine (first: Factor<'a, 'e>) (second: Factor<'a, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let firstHandler =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> second.Subscribe(handler) |> ignore }

            first.Subscribe firstHandler }

/// For each item in a list, apply a function and concat results.
let forEach (items: 'a list) (f: 'a -> Factor<'b, 'e>) : Factor<'b, 'e> =
    match items with
    | [] -> zero ()
    | head :: tail -> List.fold (fun acc item -> combine acc (f item)) (f head) tail

/// Computation expression builder for Factor.
type FactorBuilder() =
    member _.Bind(source, continuation) = bind source continuation
    member _.Return(value) = ret value
    member _.ReturnFrom(source: Factor<'a, 'e>) = source
    member _.Zero() = zero ()
    member _.Combine(first, second) = combine first (second ())
    member _.Delay(f: unit -> Factor<'a, 'e>) = f
    member _.Run(f: unit -> Factor<'a, 'e>) = f ()

    member _.For(items: 'a seq, body: 'a -> Factor<'b, 'e>) : Factor<'b, 'e> =
        let itemList = Seq.toList items

        match itemList with
        | [] -> zero ()
        | head :: tail -> List.fold (fun acc item -> combine acc (body item)) (body head) tail

/// The factor computation expression.
let factor = FactorBuilder()
