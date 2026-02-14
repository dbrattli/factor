/// Subject types for Factor
///
/// Subjects are both Observers and Observables - they can receive
/// notifications and forward them to subscribers.
module Factor.Subject

open Factor.Types

/// Creates a multicast subject that allows multiple subscribers.
///
/// Returns a tuple of (Observer, Observable) where:
/// - The Observer side is used to push notifications
/// - The Observable side can be subscribed to by multiple observers
let subject<'a> () : Observer<'a> * Observable<'a> =
    let mutable subscribers: (int * Observer<'a>) list = []
    let mutable nextId = 0

    let observer =
        { Notify =
            fun n ->
                for _, sub in subscribers do
                    sub.Notify(n)

                match n with
                | OnCompleted
                | OnError _ -> subscribers <- []
                | OnNext _ -> () }

    let observable =
        { Subscribe =
            fun downstream ->
                let id = nextId
                nextId <- nextId + 1
                subscribers <- (id, downstream) :: subscribers

                { Dispose = fun () -> subscribers <- subscribers |> List.filter (fun (sid, _) -> sid <> id) } }

    (observer, observable)

/// Creates a single-subscriber subject with buffering.
///
/// Notifications sent before subscription are buffered and delivered
/// when a subscriber connects.
let singleSubject<'a> () : Observer<'a> * Observable<'a> =
    let mutable subscriber: Observer<'a> option = None
    let pending = System.Collections.Generic.Queue<Notification<'a>>()

    let observer =
        { Notify =
            fun n ->
                match subscriber with
                | Some obs ->
                    obs.Notify(n)

                    match n with
                    | OnCompleted
                    | OnError _ -> subscriber <- None
                    | OnNext _ -> ()
                | None -> pending.Enqueue(n) }

    let observable =
        { Subscribe =
            fun downstream ->
                match subscriber with
                | Some _ -> failwith "single_subject: Already subscribed"
                | None ->
                    // Flush pending
                    while pending.Count > 0 do
                        downstream.Notify(pending.Dequeue())

                    subscriber <- Some downstream

                    { Dispose = fun () -> subscriber <- None } }

    (observer, observable)

/// Converts a cold observable into a connectable hot observable.
///
/// Returns a tuple of (Observable, connect_fn).
let publish (source: Observable<'a>) : Observable<'a> * (unit -> Disposable) =
    let mutable subscribers: (int * Observer<'a>) list = []
    let mutable nextId = 0
    let mutable connection: Disposable option = None
    let mutable terminal: Notification<'a> option = None

    let observable =
        { Subscribe =
            fun downstream ->
                match terminal with
                | Some n ->
                    downstream.Notify(n)
                    emptyDisposable ()
                | None ->
                    let id = nextId
                    nextId <- nextId + 1
                    subscribers <- (id, downstream) :: subscribers

                    { Dispose =
                        fun () -> subscribers <- subscribers |> List.filter (fun (sid, _) -> sid <> id) } }

    let connect () =
        match connection with
        | Some existing -> existing
        | None ->
            match terminal with
            | Some _ -> emptyDisposable ()
            | None ->
                let sourceObserver =
                    { Notify =
                        fun n ->
                            for _, sub in subscribers do
                                sub.Notify(n)

                            match n with
                            | OnCompleted
                            | OnError _ -> terminal <- Some n
                            | OnNext _ -> () }

                let sourceDisp = source.Subscribe(sourceObserver)

                let connDisp =
                    { Dispose =
                        fun () ->
                            sourceDisp.Dispose()
                            connection <- None }

                connection <- Some connDisp
                connDisp

    (observable, connect)

/// Shares a single subscription to the source among multiple subscribers.
///
/// Automatically connects when the first subscriber subscribes,
/// and disconnects when the last subscriber unsubscribes.
let share (source: Observable<'a>) : Observable<'a> =
    let mutable subscribers: (int * Observer<'a>) list = []
    let mutable nextId = 0
    let mutable sourceDisposable: Disposable option = None
    let mutable terminal: Notification<'a> option = None

    let connectToSource () =
        let sourceObserver =
            { Notify =
                fun n ->
                    for _, sub in subscribers do
                        sub.Notify(n)

                    match n with
                    | OnCompleted
                    | OnError _ ->
                        terminal <- Some n
                        sourceDisposable <- None
                        subscribers <- []
                    | OnNext _ -> () }

        let disp = source.Subscribe(sourceObserver)

        // Only set if not already terminated (sync sources complete during Subscribe)
        if terminal.IsNone then
            sourceDisposable <- Some disp

    { Subscribe =
        fun downstream ->
            // Add subscriber FIRST so sync sources deliver to it
            let id = nextId
            nextId <- nextId + 1
            subscribers <- (id, downstream) :: subscribers

            // Then connect if needed
            match terminal with
            | Some _ when sourceDisposable.IsNone ->
                terminal <- None
                connectToSource ()
            | _ ->
                if sourceDisposable.IsNone then
                    connectToSource ()

            { Dispose =
                fun () ->
                    subscribers <- subscribers |> List.filter (fun (sid, _) -> sid <> id)

                    if subscribers.IsEmpty then
                        match sourceDisposable with
                        | Some d ->
                            d.Dispose()
                            sourceDisposable <- None
                        | None -> () } }
