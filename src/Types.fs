/// Core types for Factor - Reactive Extensions for BEAM
///
/// This module defines the fundamental types for reactive programming:
/// - Notification: The atoms of the Rx grammar (OnNext, OnError, OnCompleted)
/// - Disposable: Resource cleanup handle
/// - Observer: Receives notifications from an observable
/// - Observable: Source of asynchronous events
module Factor.Types

/// Notification represents the three types of events in the Rx grammar:
/// OnNext* (OnError | OnCompleted)?
type Notification<'a> =
    | OnNext of 'a
    | OnError of string
    | OnCompleted

/// Disposable represents a resource that can be cleaned up.
type Disposable = { Dispose: unit -> unit }

/// Create an empty disposable that does nothing when disposed.
let emptyDisposable () : Disposable = { Dispose = fun () -> () }

/// Combine multiple disposables into one.
let compositeDisposable (disposables: Disposable list) : Disposable =
    { Dispose =
        fun () ->
            for d in disposables do
                d.Dispose() }

/// Observer receives notifications from an Observable.
type Observer<'a> = { Notify: Notification<'a> -> unit }

/// Create an observer from three callback functions.
let makeObserver (onNext: 'a -> unit) (onError: string -> unit) (onCompleted: unit -> unit) : Observer<'a> =
    { Notify =
        fun n ->
            match n with
            | OnNext x -> onNext x
            | OnError e -> onError e
            | OnCompleted -> onCompleted () }

/// Create an observer that only handles OnNext events.
let makeNextObserver (onNext: 'a -> unit) : Observer<'a> =
    { Notify =
        fun n ->
            match n with
            | OnNext x -> onNext x
            | _ -> () }

/// Send an OnNext notification to an observer.
let onNext (observer: Observer<'a>) (value: 'a) : unit = observer.Notify(OnNext value)

/// Send an OnError notification to an observer.
let onError (observer: Observer<'a>) (error: string) : unit = observer.Notify(OnError error)

/// Send an OnCompleted notification to an observer.
let onCompleted (observer: Observer<'a>) : unit = observer.Notify(OnCompleted)

/// Forward a notification to an observer.
let notify (observer: Observer<'a>) (notification: Notification<'a>) : unit = observer.Notify(notification)

/// Observable is a source of asynchronous events.
type Observable<'a> = { Subscribe: Observer<'a> -> Disposable }
