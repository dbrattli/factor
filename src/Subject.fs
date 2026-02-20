/// Subject types for Factor
///
/// Subjects are both Handlers and Factors - they can receive
/// notifications and forward them to subscribers.
module Factor.Subject

open Factor.Types

/// Creates a multicast subject that allows multiple subscribers.
///
/// Returns a tuple of (Handler, Factor) where:
/// - The Handler side is used to push notifications
/// - The Factor side can be subscribed to by multiple handlers
let subject<'a, 'e> () : Handler<'a, 'e> * Factor<'a, 'e> =
    let mutable subscribers: (int * Handler<'a, 'e>) list = []
    let mutable nextId = 0

    let handler =
        { Notify =
            fun n ->
                for _, sub in subscribers do
                    sub.Notify(n)

                match n with
                | OnCompleted
                | OnError _ -> subscribers <- []
                | OnNext _ -> () }

    let factor =
        { Subscribe =
            fun downstream ->
                let id = nextId
                nextId <- nextId + 1
                subscribers <- (id, downstream) :: subscribers

                { Dispose = fun () -> subscribers <- subscribers |> List.filter (fun (sid, _) -> sid <> id) } }

    (handler, factor)

/// Creates a single-subscriber subject with buffering.
///
/// Notifications sent before subscription are buffered and delivered
/// when a subscriber connects.
let singleSubject<'a, 'e> () : Handler<'a, 'e> * Factor<'a, 'e> =
    let mutable subscriber: Handler<'a, 'e> option = None
    let pending = System.Collections.Generic.Queue<Notification<'a, 'e>>()

    let handler =
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

    let factor =
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

    (handler, factor)

/// Converts a cold factor into a connectable hot factor.
///
/// Returns a tuple of (Factor, connect_fn).
let publish (source: Factor<'a, 'e>) : Factor<'a, 'e> * (unit -> Handle) =
    let mutable subscribers: (int * Handler<'a, 'e>) list = []
    let mutable nextId = 0
    let mutable connection: Handle option = None
    let mutable terminal: Notification<'a, 'e> option = None

    let factor =
        { Subscribe =
            fun downstream ->
                match terminal with
                | Some n ->
                    downstream.Notify(n)
                    emptyHandle ()
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
            | Some _ -> emptyHandle ()
            | None ->
                let sourceHandler =
                    { Notify =
                        fun n ->
                            for _, sub in subscribers do
                                sub.Notify(n)

                            match n with
                            | OnCompleted
                            | OnError _ -> terminal <- Some n
                            | OnNext _ -> () }

                let sourceHandle = source.Subscribe(sourceHandler)

                let connHandle =
                    { Dispose =
                        fun () ->
                            sourceHandle.Dispose()
                            connection <- None }

                connection <- Some connHandle
                connHandle

    (factor, connect)

/// Shares a single subscription to the source among multiple subscribers.
///
/// Automatically connects when the first subscriber subscribes,
/// and disconnects when the last subscriber unsubscribes.
let share (source: Factor<'a, 'e>) : Factor<'a, 'e> =
    let mutable subscribers: (int * Handler<'a, 'e>) list = []
    let mutable nextId = 0
    let mutable sourceHandle: Handle option = None
    let mutable terminal: Notification<'a, 'e> option = None

    let connectToSource () =
        let sourceHandler =
            { Notify =
                fun n ->
                    for _, sub in subscribers do
                        sub.Notify(n)

                    match n with
                    | OnCompleted
                    | OnError _ ->
                        terminal <- Some n
                        sourceHandle <- None
                        subscribers <- []
                    | OnNext _ -> () }

        let h = source.Subscribe(sourceHandler)

        // Only set if not already terminated (sync sources complete during Subscribe)
        if terminal.IsNone then
            sourceHandle <- Some h

    { Subscribe =
        fun downstream ->
            // Add subscriber FIRST so sync sources deliver to it
            let id = nextId
            nextId <- nextId + 1
            subscribers <- (id, downstream) :: subscribers

            // Then connect if needed
            match terminal with
            | Some _ when sourceHandle.IsNone ->
                terminal <- None
                connectToSource ()
            | _ ->
                if sourceHandle.IsNone then
                    connectToSource ()

            { Dispose =
                fun () ->
                    subscribers <- subscribers |> List.filter (fun (sid, _) -> sid <> id)

                    if subscribers.IsEmpty then
                        match sourceHandle with
                        | Some h ->
                            h.Dispose()
                            sourceHandle <- None
                        | None -> () } }
