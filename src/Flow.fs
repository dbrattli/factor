/// Flow module for Factor
///
/// Provides computation expression builder for composing factors
/// in a monadic style. Each `let!` desugars to flatMap, which
/// spawns child processes creating supervision boundaries.
module Factor.Flow

open Factor.Types

/// Bind a factor to a continuation function.
/// Uses flatMap — each inner runs in a spawned linked child process.
let bind (source: Factor<'T>) (continuation: 'T -> Factor<'U>) : Factor<'U> = Transform.flatMap continuation source

/// Lift a pure value into a factor.
let ret (value: 'T) : Factor<'T> = Create.single value

/// Empty factor - completes immediately with no values.
let zero<'T> () : Factor<'T> = Create.empty ()

/// Combine two factors sequentially (concat).
let combine (first: Factor<'T>) (second: Factor<'T>) : Factor<'T> = Combine.concat2 first second

/// For each item in a list, apply a function and concat results.
let forEach (items: 'T list) (f: 'T -> Factor<'U>) : Factor<'U> =
    match items with
    | [] -> zero ()
    | head :: tail -> List.fold (fun acc item -> combine acc (f item)) (f head) tail

/// Computation expression builder for Factor.
type FlowBuilder() =
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

/// The flow computation expression.
let flow = FlowBuilder()
