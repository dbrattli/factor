/// Stream types for Factor
///
/// Streams are both Handlers and Factors - they can receive
/// notifications and forward them to subscribers. Each stream
/// is backed by a separate BEAM actor process that manages
/// subscribers via message passing, solving cross-process
/// mutable state issues.
module Factor.Stream

open Factor.Types

/// Creates a multicast stream backed by a BEAM actor process.
///
/// Returns a tuple of (Handler, Factor) where:
/// - The Handler side sends notifications to the stream actor
/// - The Factor side registers subscribers with the stream actor
///
/// The stream actor manages a subscriber map and broadcasts
/// notifications to all subscribers. Subscribe is synchronous
/// to prevent races between subscribe and first notify.
let stream<'T> () : Handler<'T> * Factor<'T> =
    let streamPid = Process.startStream ()

    let handler =
        { Notify =
            fun n ->
                match n with
                | OnNext _ -> Process.streamNotify streamPid (box n)
                | OnError _
                | OnCompleted -> Process.streamNotifyTerminal streamPid (box n) }

    let factor =
        { Subscribe =
            fun downstream ->
                let ref = Process.makeRef ()

                // Register local child handler to receive notifications from stream actor
                Process.registerChild
                    ref
                    (fun notification ->
                        let n = unbox<Notification<'T>> notification
                        downstream.Notify(n))

                // Synchronously subscribe to stream actor
                Process.streamSubscribe streamPid ref

                { Dispose =
                    fun () ->
                        Process.unregisterChild ref
                        Process.streamUnsubscribe streamPid ref } }

    (handler, factor)

/// Creates a single-subscriber stream with buffering, backed by a BEAM actor.
///
/// Notifications sent before subscription are buffered and delivered
/// when a subscriber connects.
let singleStream<'T> () : Handler<'T> * Factor<'T> =
    let streamPid = Process.startSingleStream ()

    let handler =
        { Notify =
            fun n ->
                match n with
                | OnNext _ -> Process.streamNotify streamPid (box n)
                | OnError _
                | OnCompleted -> Process.streamNotifyTerminal streamPid (box n) }

    let factor =
        { Subscribe =
            fun downstream ->
                let ref = Process.makeRef ()

                // Register local child handler to receive notifications from stream actor
                Process.registerChild
                    ref
                    (fun notification ->
                        let n = unbox<Notification<'T>> notification
                        downstream.Notify(n))

                // Synchronously subscribe to stream actor
                Process.streamSubscribe streamPid ref

                { Dispose =
                    fun () ->
                        Process.unregisterChild ref
                        Process.streamUnsubscribe streamPid ref } }

    (handler, factor)

/// Converts a cold factor into a connectable hot factor.
///
/// Returns a tuple of (Factor, connect_fn).
/// Uses inline mutable state for synchronous multicast.
let publish (source: Factor<'T>) : Factor<'T> * (unit -> Handle) =
    let mutable subscribers: (int * Handler<'T>) list = []
    let mutable nextId = 0
    let mutable connection: Handle option = None
    let mutable terminal: Notification<'T> option = None

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
/// Note: connection management is per-process (uses mutable state).
let share (source: Factor<'T>) : Factor<'T> =
    let mutable subscribers: (int * Handler<'T>) list = []
    let mutable nextId = 0
    let mutable sourceHandle: Handle option = None
    let mutable terminal: Notification<'T> option = None

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
