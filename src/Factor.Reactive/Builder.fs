/// Builder module for Factor.Reactive
///
/// Provides computation expression builder for composing observables
/// in a monadic style. Each `let!` desugars to flatMap, which
/// spawns child processes creating supervision boundaries.
module Factor.Reactive.Builder

open Factor.Actor.Types

/// Bind an observable to a continuation function.
/// Uses flatMap — each inner runs in a spawned linked child process.
let bind (source: Observable<'T>) (continuation: 'T -> Observable<'U>) : Observable<'U> = Transform.flatMap continuation source

/// Lift a pure value into an observable.
let ret (value: 'T) : Observable<'T> = Create.single value

/// Empty observable - completes immediately with no values.
let zero<'T> () : Observable<'T> = Create.empty ()

/// Combine two observables sequentially (concat).
let combine (first: Observable<'T>) (second: Observable<'T>) : Observable<'T> = Combine.concat2 first second

/// For each item in a list, apply a function and concat results.
let forEach (items: 'T list) (f: 'T -> Observable<'U>) : Observable<'U> =
    match items with
    | [] -> zero ()
    | head :: tail -> List.fold (fun acc item -> combine acc (f item)) (f head) tail

/// Computation expression builder for Observable.
type ObservableBuilder() =
    member _.Bind(source, continuation) = bind source continuation
    member _.Return(value) = ret value
    member _.ReturnFrom(source: Observable<'T>) = source
    member _.Zero() = zero ()
    member _.Combine(first, second) = combine first (second ())
    member _.Delay(f: unit -> Observable<'T>) = f
    member _.Run(f: unit -> Observable<'T>) = f ()

    member _.For(items: 'T seq, body: 'T -> Observable<'U>) : Observable<'U> =
        let itemList = Seq.toList items

        match itemList with
        | [] -> zero ()
        | head :: tail -> List.fold (fun acc item -> combine acc (body item)) (body head) tail

/// The observable computation expression.
let observable = ObservableBuilder()
