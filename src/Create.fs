/// Creation operators for Factor
///
/// These functions create new Observable sequences from various sources.
module Factor.Create

open Factor.Types

/// Create an observable from a subscribe function.
let create (subscribe: Observer<'a> -> Disposable) : Observable<'a> = { Subscribe = subscribe }

/// Returns an observable sequence containing a single element.
let single (value: 'a) : Observable<'a> =
    { Subscribe =
        fun observer ->
            observer.Notify(OnNext value)
            observer.Notify(OnCompleted)
            emptyDisposable () }

/// Returns an observable sequence with no elements that completes immediately.
let empty<'a> () : Observable<'a> =
    { Subscribe =
        fun observer ->
            observer.Notify(OnCompleted)
            emptyDisposable () }

/// Returns an observable sequence that never emits and never completes.
let never<'a> () : Observable<'a> =
    { Subscribe = fun _ -> emptyDisposable () }

/// Returns an observable sequence that errors immediately.
let fail (error: string) : Observable<'a> =
    { Subscribe =
        fun observer ->
            observer.Notify(OnError error)
            emptyDisposable () }

/// Returns an observable sequence from a list of values.
let ofList (items: 'a list) : Observable<'a> =
    { Subscribe =
        fun observer ->
            for x in items do
                observer.Notify(OnNext x)

            observer.Notify(OnCompleted)
            emptyDisposable () }

/// Returns an observable that invokes the factory function
/// whenever a new observer subscribes.
let defer (factory: unit -> Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let obs = factory ()
            obs.Subscribe(observer) }
