/// Builder module for Factor
///
/// Provides computation expression builder for composing observables
/// in a monadic style.
module Factor.Builder

open Factor.Types

/// Bind an observable to a continuation function (flatMap).
let bind (source: Observable<'a>) (continuation: 'a -> Observable<'b>) : Observable<'b> =
    { Subscribe =
        fun observer ->
            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let inner = continuation x

                            let innerObserver =
                                { Notify =
                                    fun innerN ->
                                        match innerN with
                                        | OnNext value -> observer.Notify(OnNext value)
                                        | OnError e -> observer.Notify(OnError e)
                                        | OnCompleted -> () }

                            inner.Subscribe(innerObserver) |> ignore
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Lift a pure value into an observable.
let ret (value: 'a) : Observable<'a> =
    { Subscribe =
        fun observer ->
            observer.Notify(OnNext value)
            observer.Notify(OnCompleted)
            emptyDisposable () }

/// Empty observable - completes immediately with no values.
let zero<'a> () : Observable<'a> =
    { Subscribe =
        fun observer ->
            observer.Notify(OnCompleted)
            emptyDisposable () }

/// Combine two observables sequentially (concat).
let combine (first: Observable<'a>) (second: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let firstObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> second.Subscribe(observer) |> ignore }

            first.Subscribe(firstObserver) }

/// For each item in a list, apply a function and concat results.
let forEach (items: 'a list) (f: 'a -> Observable<'b>) : Observable<'b> =
    match items with
    | [] -> zero ()
    | head :: tail -> List.fold (fun acc item -> combine acc (f item)) (f head) tail

/// Computation expression builder for Observable.
type ObservableBuilder() =
    member _.Bind(source, continuation) = bind source continuation
    member _.Return(value) = ret value
    member _.ReturnFrom(source: Observable<'a>) = source
    member _.Zero() = zero ()
    member _.Combine(first, second) = combine first (second ())
    member _.Delay(f: unit -> Observable<'a>) = f
    member _.Run(f: unit -> Observable<'a>) = f ()

    member _.For(items: 'a seq, body: 'a -> Observable<'b>) : Observable<'b> =
        let itemList = Seq.toList items

        match itemList with
        | [] -> zero ()
        | head :: tail -> List.fold (fun acc item -> combine acc (body item)) (body head) tail

/// The observable computation expression.
let rx = ObservableBuilder()
