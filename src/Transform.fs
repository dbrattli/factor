/// Transform operators for Factor
///
/// These operators transform the elements of a Factor sequence.
module Factor.Transform

open System.Collections.Generic
open Factor.Types

/// Returns a factor whose elements are the result of invoking
/// the mapper function on each element of the source.
let map (mapper: 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> handler.Notify(OnNext(mapper x))
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Returns a factor whose elements are the result of invoking
/// the mapper function on each element and its index.
let mapi (mapper: 'T -> int -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Subscribe =
        fun handler ->
            let mutable index = 0

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let i = index
                            index <- index + 1
                            handler.Notify(OnNext(mapper x i))
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Flattens a Factor of Factors by merging inner emissions.
///
/// The maxConcurrency parameter controls how many inner factors
/// can be subscribed to concurrently:
/// - None: Unlimited concurrency
/// - Some(1): Sequential (equivalent to concatInner)
/// - Some(n): At most n inner factors active at once
let mergeInner (maxConcurrency: int option) (source: Factor<Factor<'T>>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable innerCount = 0
            let mutable outerStopped = false
            let mutable disposed = false
            let queue = Queue<Factor<'T>>()

            let canSubscribe () =
                match maxConcurrency with
                | None -> true
                | Some max -> innerCount < max

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeToInner: Factor<'T> -> unit = fun _ -> ()

            subscribeToInner <-
                fun (inner: Factor<'T>) ->
                    innerCount <- innerCount + 1

                    let innerHandler =
                        { Notify =
                            fun n ->
                                if not disposed then
                                    match n with
                                    | OnNext value -> handler.Notify(OnNext value)
                                    | OnError e ->
                                        disposed <- true
                                        handler.Notify(OnError e)
                                    | OnCompleted ->
                                        innerCount <- innerCount - 1

                                        if queue.Count > 0 then
                                            let next = queue.Dequeue()
                                            subscribeToInner next
                                        elif outerStopped && innerCount <= 0 then
                                            handler.Notify(OnCompleted) }

                    inner.Subscribe(innerHandler) |> ignore

            let outerHandler =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext inner ->
                                if canSubscribe () then
                                    subscribeToInner inner
                                else
                                    queue.Enqueue(inner)
                            | OnError e ->
                                disposed <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if innerCount <= 0 && queue.Count = 0 then
                                    handler.Notify(OnCompleted) }

            let outerHandle = source.Subscribe(outerHandler)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerHandle.Dispose() } }

/// Flattens a Factor of Factors by merging inner emissions,
/// spawning a linked child process for each inner subscription.
///
/// This creates a process boundary: the inner factor's Subscribe runs
/// in a child process, so it must NOT reference parent process dictionary
/// state (mutable variables, Dictionary, etc). Use this for the CE builder
/// where inner factors are actor-based streams or pure computations.
///
/// Child processes forward notifications to the parent via sendChildMsg.
/// The supervision policy controls crash handling:
/// - Terminate: crash → OnError, pipeline dies (default)
/// - Skip: crash → ignore, continue with other inners
/// - Restart(n): crash → resubscribe inner, up to n times
let mergeInnerSpawned (policy: SupervisionPolicy) (source: Factor<Factor<'T>>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let parentPid = Process.selfPid ()
            let mutable innerCount = 0
            let mutable outerStopped = false
            let mutable disposed = false

            Process.trapExits ()

            let checkComplete () =
                if outerStopped && innerCount <= 0 then
                    handler.Notify(OnCompleted)

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeToInner: Factor<'T> -> int -> unit = fun _ _ -> ()

            subscribeToInner <-
                fun (inner: Factor<'T>) (retriesLeft: int) ->
                    innerCount <- innerCount + 1
                    let childRef = Process.makeRef ()

                    // Register child handler in parent to receive forwarded notifications
                    Process.registerChild
                        childRef
                        (fun notification ->
                            let n = unbox<Notification<'T>> notification

                            if not disposed then
                                match n with
                                | OnNext value -> handler.Notify(OnNext value)
                                | OnError e ->
                                    disposed <- true
                                    handler.Notify(OnError e)
                                | OnCompleted ->
                                    innerCount <- innerCount - 1
                                    Process.unregisterChild childRef
                                    checkComplete ())

                    // Spawn linked child process for this inner subscription
                    let childPid =
                        Process.spawnLinked (fun () ->
                            let mutable innerDone = false

                            let innerHandler =
                                { Notify =
                                    fun n ->
                                        Process.sendChildMsg parentPid childRef (box n)

                                        match n with
                                        | OnCompleted
                                        | OnError _ ->
                                            innerDone <- true
                                            Process.exitNormal ()
                                        | _ -> () }

                            inner.Subscribe(innerHandler) |> ignore

                            if not innerDone then
                                Process.childLoop ())

                    // Register exit handler — behavior depends on supervision policy
                    Process.registerExit
                        childPid
                        (fun reason ->
                            if not disposed then
                                Process.unregisterChild childRef
                                Process.unregisterExit childPid
                                innerCount <- innerCount - 1

                                match policy with
                                | Terminate ->
                                    disposed <- true
                                    handler.Notify(OnError(ProcessExitException(Process.formatReason reason)))
                                | Skip ->
                                    checkComplete ()
                                | Restart _ ->
                                    if retriesLeft > 0 then
                                        subscribeToInner inner (retriesLeft - 1)
                                    else
                                        disposed <- true
                                        handler.Notify(OnError(ProcessExitException(Process.formatReason reason))))

            let outerHandler =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext inner ->
                                let maxRetries =
                                    match policy with
                                    | Restart n -> n
                                    | _ -> 0

                                subscribeToInner inner maxRetries
                            | OnError e ->
                                disposed <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if innerCount <= 0 then
                                    handler.Notify(OnCompleted) }

            let outerHandle = source.Subscribe(outerHandler)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerHandle.Dispose() } }

/// Projects each element into a factor and merges with process spawning.
/// Each inner runs in its own linked child process (supervision boundary).
/// Uses Terminate policy by default — child crashes become OnError.
let flatMapSpawned (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> map mapper |> mergeInnerSpawned Terminate

/// Flattens a Factor of Factors by concatenating in order.
let concatInner (source: Factor<Factor<'T>>) : Factor<'T> = mergeInner (Some 1) source

/// Projects each element into a factor and merges the results.
let flatMap (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> map mapper |> mergeInner None

/// Projects each element into a factor and concatenates in order.
let concatMap (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> map mapper |> concatInner

/// Projects each element and its index into a factor and merges.
let flatMapi (mapper: 'T -> int -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> mapi mapper |> mergeInner None

/// Projects each element and its index into a factor and concatenates.
let concatMapi (mapper: 'T -> int -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> mapi mapper |> concatInner

/// Flattens a Factor of Factors by switching to the latest.
/// Only notifications from the most recent inner factor are forwarded.
let switchInner (source: Factor<Factor<'T>>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable outerStopped = false
            let mutable disposed = false
            let mutable currentId = 0
            let mutable hasInner = false

            let outerHandler =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext innerFactor ->
                                currentId <- currentId + 1
                                let myId = currentId
                                hasInner <- true

                                let innerHandler =
                                    { Notify =
                                        fun n ->
                                            if not disposed && currentId = myId then
                                                match n with
                                                | OnNext value -> handler.Notify(OnNext value)
                                                | OnError e ->
                                                    disposed <- true
                                                    handler.Notify(OnError e)
                                                | OnCompleted ->
                                                    hasInner <- false

                                                    if outerStopped then
                                                        handler.Notify(OnCompleted) }

                                innerFactor.Subscribe(innerHandler) |> ignore
                            | OnError e ->
                                disposed <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if not hasInner then
                                    handler.Notify(OnCompleted) }

            let outerHandle = source.Subscribe(outerHandler)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerHandle.Dispose() } }

/// Projects each element into a factor and switches to the latest.
let switchMap (mapper: 'T -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> map mapper |> switchInner

/// Projects each element and its index into a factor and switches to latest.
let switchMapi (mapper: 'T -> int -> Factor<'U>) (source: Factor<'T>) : Factor<'U> =
    source |> mapi mapper |> switchInner

/// Performs a side effect for each emission without transforming.
let tap (effect: 'T -> unit) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            effect x
                            handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Prepends values before the source emissions.
let startWith (values: 'T list) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            for v in values do
                handler.Notify(OnNext v)

            source.Subscribe(handler) }

/// Emits consecutive pairs of values.
let pairwise (source: Factor<'T>) : Factor<'T * 'T> =
    { Subscribe =
        fun handler ->
            let mutable prev: 'T option = None

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            match prev with
                            | Some p -> handler.Notify(OnNext(p, x))
                            | None -> ()

                            prev <- Some x
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Applies an accumulator function over the source, emitting each intermediate result.
let scan (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Subscribe =
        fun handler ->
            let mutable acc = initial

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            acc <- accumulator acc x
                            handler.Notify(OnNext acc)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Applies an accumulator function over the source, emitting only the final value.
let reduce (initial: 'U) (accumulator: 'U -> 'T -> 'U) (source: Factor<'T>) : Factor<'U> =
    { Subscribe =
        fun handler ->
            let mutable acc = initial

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> acc <- accumulator acc x
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted ->
                            handler.Notify(OnNext acc)
                            handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Groups elements by key, returning a factor of grouped factors.
///
/// Each group is backed by a singleStream actor process, so group
/// sub-factors can be subscribed from any process (including CE children).
/// The groupBy operator maintains a key→streamPid map; all buffering
/// and subscriber management is handled by the stream actors.
let groupBy (keySelector: 'T -> 'K) (source: Factor<'T>) : Factor<'K * Factor<'T>> =
    { Subscribe =
        fun handler ->
            // Map from key to (streamPid, groupHandler) — streamPid for routing,
            // groupHandler for sending notifications to the stream actor
            let streams = Dictionary<'K, obj * (Notification<'T> -> unit)>()

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let key = keySelector x
                            let isNew = not (streams.ContainsKey(key))

                            if isNew then
                                // Spawn a singleStream actor for this group
                                let streamPid = Process.startSingleStream ()

                                let notify (notification: Notification<'T>) =
                                    match notification with
                                    | OnNext _ -> Process.streamNotify streamPid (box notification)
                                    | OnError _
                                    | OnCompleted -> Process.streamNotifyTerminal streamPid (box notification)

                                streams.[key] <- (streamPid, notify)

                                // Build the group factor backed by the stream actor
                                let groupFactor: Factor<'T> =
                                    { Subscribe =
                                        fun groupHandler ->
                                            let ref = Process.makeRef ()

                                            Process.registerChild
                                                ref
                                                (fun notification ->
                                                    let gn = unbox<Notification<'T>> notification
                                                    groupHandler.Notify(gn))

                                            Process.streamSubscribe streamPid ref

                                            { Dispose =
                                                fun () ->
                                                    Process.unregisterChild ref
                                                    Process.streamUnsubscribe streamPid ref } }

                                handler.Notify(OnNext(key, groupFactor))

                            // Route value to the group's stream actor
                            let (_pid, notify) = streams.[key]
                            notify (OnNext x)
                        | OnError e ->
                            for kv in streams do
                                let (_pid, notify) = kv.Value
                                notify (OnError e)

                            handler.Notify(OnError e)
                        | OnCompleted ->
                            for kv in streams do
                                let (_pid, notify) = kv.Value
                                notify OnCompleted

                            handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }
