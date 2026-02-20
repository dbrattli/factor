/// Builder module for Factor
///
/// Provides computation expression builder for composing factors
/// in a monadic style. Each `let!` desugars to flatMap, which
/// spawns a child process creating a process boundary.
module Factor.Builder

open Factor.Types

/// Bind a factor to a continuation function.
/// Each inner runs in a spawned linked child process (supervision boundary).
/// The continuation must NOT reference parent process dictionary state
/// (mutable variables, Dictionary) — only immutable captures and actor-based streams.
let bind (source: Factor<'T>) (continuation: 'T -> Factor<'U>) : Factor<'U> =
    Transform.flatMapSpawned continuation source

/// Lift a pure value into a factor.
let ret (value: 'T) : Factor<'T> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnNext value)
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Empty factor - completes immediately with no values.
let zero<'T> () : Factor<'T> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnCompleted)
            emptyHandle () }

/// Combine two factors sequentially (concat).
let combine (first: Factor<'T>) (second: Factor<'T>) : Factor<'T> =
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
let forEach (items: 'T list) (f: 'T -> Factor<'U>) : Factor<'U> =
    match items with
    | [] -> zero ()
    | head :: tail -> List.fold (fun acc item -> combine acc (f item)) (f head) tail

/// Computation expression builder for Factor.
type FactorBuilder() =
    member _.Bind(source, continuation) = bind source continuation
    member _.Return(value) = ret value
    member _.ReturnFrom(source: Factor<'T>) = source
    member _.Zero() = zero ()
    member _.Combine(first, second) = combine first (second ())
    member _.Delay(f: unit -> Factor<'T>) = f
    member _.Run(f: unit -> Factor<'T>) = f ()

    member _.For(items: 'T seq, body: 'T -> Factor<'U>) : Factor<'U> =
        let itemList = Seq.toList items

        match itemList with
        | [] -> zero ()
        | head :: tail -> List.fold (fun acc item -> combine acc (body item)) (body head) tail

/// The factor computation expression.
let factor = FactorBuilder()
