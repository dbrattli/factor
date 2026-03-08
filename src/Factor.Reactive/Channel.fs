/// Channel types for Factor.Reactive
///
/// Channels are Observers (push side) and Observables (subscribe side).
/// Each channel is backed by an Agent that defines its behavior.
module Factor.Reactive.Channel

open Factor.Agent.Types
open Factor.Beam

// ============================================================================
// Push helpers (send ChannelMsg to channel agent via Observer)
// ============================================================================

/// Send an OnNext value to a channel observer.
let pushNext (observer: Observer<'T>) (value: 'T) : unit =
    Agent.send { Pid = observer.Pid } (Notify(OnNext value))

/// Send an OnError to a channel observer.
let pushError (observer: Observer<'T>) (error: exn) : unit =
    Agent.send { Pid = observer.Pid } (Notify(OnError error))

/// Send OnCompleted to a channel observer.
let pushCompleted (observer: Observer<'T>) : unit =
    Agent.send { Pid = observer.Pid } (Notify OnCompleted)

// ============================================================================
// Channel constructors
// ============================================================================

/// Wraps an Agent<ChannelMsg<'T>> into an Observer (push side) and Observable (subscribe side).
let channel (agent: Agent<ChannelMsg<'T>>) : Observer<'T> * Observable<'T> =
    let pushObserver: Observer<'T> = { Pid = agent.Pid; Ref = Process.makeRef () }

    let observable = {
        Subscribe =
            fun downstream ->
                Agent.call agent (fun rc -> Subscribe(downstream, rc))
                { Dispose = fun () -> Agent.send agent (Unsubscribe downstream.Ref) }
    }

    (pushObserver, observable)

/// Creates a multicast channel backed by a stateful agent.
///
/// Broadcasts messages to all subscribers. Subscribe is synchronous
/// (via Agent.call) to prevent races between subscribe and first send.
let multicast<'T> () : Observer<'T> * Observable<'T> =
    let agent =
        Agent.start ([] : (obj * Observer<'T>) list) (fun subscribers msg ->
            match msg with
            | Notify notification ->
                for (_, sub) in subscribers do
                    Process.notify sub notification

                match notification with
                | OnCompleted | OnError _ -> Stop
                | _ -> Continue subscribers
            | Subscribe(obs, rc) ->
                rc.Reply()
                Continue((obs.Ref, obs) :: subscribers)
            | Unsubscribe ref ->
                Continue(subscribers |> List.filter (fun (r, _) -> not (Process.refEquals r ref))))

    channel agent

/// Creates a single-subscriber channel with buffering, backed by an agent.
///
/// Messages sent before subscription are buffered and delivered
/// when a subscriber connects.
type private SingleState<'T> =
    | Waiting of Msg<'T> list
    | Active of Observer<'T>

let singleSubscriber<'T> () : Observer<'T> * Observable<'T> =
    let agent =
        Agent.start (Waiting [] : SingleState<'T>) (fun state msg ->
            match state, msg with
            | Waiting pending, Notify n -> Continue(Waiting(n :: pending))
            | Waiting pending, Subscribe(obs, rc) ->
                for n in List.rev pending do
                    Process.notify obs n

                rc.Reply()
                Continue(Active obs)
            | Active obs, Notify n ->
                Process.notify obs n

                match n with
                | OnCompleted | OnError _ -> Stop
                | _ -> Continue(Active obs)
            | Active _, Unsubscribe _ -> Continue(Waiting [])
            | _, _ -> Continue state)

    channel agent

/// Converts a cold observable into a connectable hot observable.
///
/// Returns a tuple of (Observable, connect_fn).
/// The connect function subscribes to the source and multicasts to all subscribers.
let publish (source: Observable<'T>) : Observable<'T> * (unit -> Handle) =
    let mutable subscribers: (int * Observer<'T>) list = []
    let mutable nextId = 0
    let mutable connection: Handle option = None
    let mutable terminal: Msg<'T> option = None

    let observable = {
        Subscribe =
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
                let sourceHandle = source.Subscribe(sourceEndpoint)

                let connHandle = {
                    Dispose =
                        fun () ->
                            Process.unregisterChild ref
                            sourceHandle.Dispose()
                            connection <- None
                }

                connection <- Some connHandle
                connHandle

    (observable, connect)

/// Shares a single subscription to the source among multiple subscribers.
///
/// Automatically connects when the first subscriber subscribes,
/// and disconnects when the last subscriber unsubscribes.
let share (source: Observable<'T>) : Observable<'T> =
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
        let h = source.Subscribe(endpoint)

        if terminal.IsNone then
            sourceHandle <- Some h

    {
        Subscribe =
            fun downstream ->
                let id = nextId
                nextId <- nextId + 1
                subscribers <- (id, downstream) :: subscribers

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
