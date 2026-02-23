/// Channel types for Factor
///
/// Channels are Senders (push side) and Factors (subscribe side).
/// Each channel is backed by a separate BEAM actor process that manages
/// subscribers via message passing.
module Factor.Channel

open Factor.Types

/// Creates a multicast channel backed by a BEAM actor process.
///
/// Returns a tuple of (Sender, Factor) where:
/// - The Sender side pushes messages to the channel actor
/// - The Factor side registers subscribers with the channel actor
///
/// The channel actor manages a subscriber map and broadcasts
/// messages to all subscribers. Subscribe is synchronous
/// to prevent races between subscribe and first send.
let channel<'T> () : Sender<'T> * Factor<'T> =
    let channelPid = Process.startStream ()

    let sender: Sender<'T> = { ChannelPid = channelPid }

    let factor = {
        Spawn =
            fun downstream ->
                let ref = Process.makeRef ()

                Process.registerChild ref (fun msg ->
                    let n = unbox<Msg<'T>> msg
                    Process.notify downstream n)

                Process.streamSubscribe channelPid ref

                {
                    Dispose =
                        fun () ->
                            Process.unregisterChild ref
                            Process.streamUnsubscribe channelPid ref
                }
    }

    (sender, factor)

/// Creates a single-subscriber channel with buffering, backed by a BEAM actor.
///
/// Messages sent before subscription are buffered and delivered
/// when a subscriber connects.
let singleChannel<'T> () : Sender<'T> * Factor<'T> =
    let channelPid = Process.startSingleStream ()

    let sender: Sender<'T> = { ChannelPid = channelPid }

    let factor = {
        Spawn =
            fun downstream ->
                let ref = Process.makeRef ()

                Process.registerChild ref (fun msg ->
                    let n = unbox<Msg<'T>> msg
                    Process.notify downstream n)

                Process.streamSubscribe channelPid ref

                {
                    Dispose =
                        fun () ->
                            Process.unregisterChild ref
                            Process.streamUnsubscribe channelPid ref
                }
    }

    (sender, factor)

/// Converts a cold factor into a connectable hot factor.
///
/// Returns a tuple of (Factor, connect_fn).
/// The connect function subscribes to the source and multicasts to all subscribers.
let publish (source: Factor<'T>) : Factor<'T> * (unit -> Handle) =
    let mutable subscribers: (int * Observer<'T>) list = []
    let mutable nextId = 0
    let mutable connection: Handle option = None
    let mutable terminal: Msg<'T> option = None

    let factor = {
        Spawn =
            fun downstream ->
                match terminal with
                | Some n ->
                    Process.notify downstream n
                    emptyHandle ()
                | None ->
                    let id = nextId
                    nextId <- nextId + 1
                    subscribers <- (id, downstream) :: subscribers

                    {
                        Dispose =
                            fun () ->
                                subscribers <-
                                    subscribers
                                    |> List.filter (fun (sid, _) -> sid <> id)
                    }
    }

    let connect () =
        match connection with
        | Some existing -> existing
        | None ->
            match terminal with
            | Some _ -> emptyHandle ()
            | None ->
                // Create an endpoint in this process to receive source messages
                let ref = Process.makeRef ()

                Process.registerChild ref (fun msg ->
                    let n = unbox<Msg<'T>> msg

                    for _, sub in subscribers do
                        Process.notify sub n

                    match n with
                    | OnCompleted
                    | OnError _ -> terminal <- Some n
                    | OnNext _ -> ())

                let sourceEndpoint: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                let sourceHandle = source.Spawn(sourceEndpoint)

                let connHandle = {
                    Dispose =
                        fun () ->
                            Process.unregisterChild ref
                            sourceHandle.Dispose()
                            connection <- None
                }

                connection <- Some connHandle
                connHandle

    (factor, connect)

/// Shares a single subscription to the source among multiple subscribers.
///
/// Automatically connects when the first subscriber subscribes,
/// and disconnects when the last subscriber unsubscribes.
let share (source: Factor<'T>) : Factor<'T> =
    let mutable subscribers: (int * Observer<'T>) list = []
    let mutable nextId = 0
    let mutable sourceHandle: Handle option = None
    let mutable sourceRef: obj option = None
    let mutable terminal: Msg<'T> option = None

    let connectToSource () =
        let ref = Process.makeRef ()
        sourceRef <- Some ref

        Process.registerChild ref (fun msg ->
            let n = unbox<Msg<'T>> msg

            for _, sub in subscribers do
                Process.notify sub n

            match n with
            | OnCompleted
            | OnError _ ->
                terminal <- Some n
                sourceHandle <- None
                subscribers <- []
            | OnNext _ -> ())

        let endpoint: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
        let h = source.Spawn(endpoint)

        // Only set if not already terminated (sync sources complete during Spawn)
        if terminal.IsNone then
            sourceHandle <- Some h

    {
        Spawn =
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

                {
                    Dispose =
                        fun () ->
                            subscribers <-
                                subscribers
                                |> List.filter (fun (sid, _) -> sid <> id)

                            if subscribers.IsEmpty then
                                match sourceHandle with
                                | Some h ->
                                    h.Dispose()
                                    sourceHandle <- None

                                    match sourceRef with
                                    | Some r -> Process.unregisterChild r
                                    | None -> ()

                                    sourceRef <- None
                                | None -> ()
                }
    }
