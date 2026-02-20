/// Transform operators for Factor
///
/// These operators transform the elements of a Factor sequence.
module Factor.Transform

open Factor.Types

/// Returns a factor whose elements are the result of invoking
/// the mapper function on each element of the source.
let map (mapper: 'a -> 'b) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
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
let mapi (mapper: 'a -> int -> 'b) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
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
let mergeInner (maxConcurrency: int option) (source: Factor<Factor<'a, 'e>, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let mutable innerCount = 0
            let mutable outerStopped = false
            let mutable disposed = false
            let queue = System.Collections.Generic.Queue<Factor<'a, 'e>>()
            let innerHandles = System.Collections.Generic.Dictionary<int, Handle>()
            let mutable nextInnerId = 1

            let canSubscribe () =
                match maxConcurrency with
                | None -> true
                | Some max -> innerCount < max

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeToInner: Factor<'a, 'e> -> unit = fun _ -> ()

            subscribeToInner <-
                fun (inner: Factor<'a, 'e>) ->
                    let innerId = nextInnerId
                    nextInnerId <- nextInnerId + 1
                    innerCount <- innerCount + 1

                    let innerHandler =
                        { Notify =
                            fun n ->
                                if not disposed then
                                    match n with
                                    | OnNext value -> handler.Notify(OnNext value)
                                    | OnError e ->
                                        // Dispose all active inner subscriptions on error
                                        for kv in innerHandles do
                                            kv.Value.Dispose()

                                        innerHandles.Clear()
                                        handler.Notify(OnError e)
                                    | OnCompleted ->
                                        innerCount <- innerCount - 1
                                        innerHandles.Remove(innerId) |> ignore

                                        if queue.Count > 0 then
                                            let next = queue.Dequeue()
                                            subscribeToInner next
                                        elif outerStopped && innerCount <= 0 then
                                            handler.Notify(OnCompleted) }

                    let innerHandle = inner.Subscribe(innerHandler)
                    innerHandles.[innerId] <- innerHandle

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
                                for kv in innerHandles do
                                    kv.Value.Dispose()

                                innerHandles.Clear()
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if innerCount <= 0 && queue.Count = 0 then
                                    handler.Notify(OnCompleted) }

            let outerHandle = source.Subscribe(outerHandler)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerHandle.Dispose()

                    for kv in innerHandles do
                        kv.Value.Dispose()

                    innerHandles.Clear() } }

/// Flattens a Factor of Factors by concatenating in order.
let concatInner (source: Factor<Factor<'a, 'e>, 'e>) : Factor<'a, 'e> = mergeInner (Some 1) source

/// Projects each element into a factor and merges the results.
let flatMap (mapper: 'a -> Factor<'b, 'e>) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
    source |> map mapper |> mergeInner None

/// Projects each element into a factor and concatenates in order.
let concatMap (mapper: 'a -> Factor<'b, 'e>) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
    source |> map mapper |> concatInner

/// Projects each element and its index into a factor and merges.
let flatMapi (mapper: 'a -> int -> Factor<'b, 'e>) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
    source |> mapi mapper |> mergeInner None

/// Projects each element and its index into a factor and concatenates.
let concatMapi (mapper: 'a -> int -> Factor<'b, 'e>) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
    source |> mapi mapper |> concatInner

/// Flattens a Factor of Factors by switching to the latest.
let switchInner (source: Factor<Factor<'a, 'e>, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let mutable currentId = 0
            let mutable nextId = 1
            let mutable outerStopped = false
            let mutable hasActive = false
            let mutable disposed = false
            let mutable currentHandle: Handle option = None

            let outerHandler =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext innerFactor ->
                                // Dispose previous inner
                                match currentHandle with
                                | Some h -> h.Dispose()
                                | None -> ()

                                let id = nextId
                                nextId <- nextId + 1
                                currentId <- id
                                hasActive <- true

                                let innerHandler =
                                    { Notify =
                                        fun innerN ->
                                            if not disposed then
                                                match innerN with
                                                | OnNext value ->
                                                    if id = currentId then
                                                        handler.Notify(OnNext value)
                                                | OnError e ->
                                                    match currentHandle with
                                                    | Some h -> h.Dispose()
                                                    | None -> ()

                                                    handler.Notify(OnError e)
                                                | OnCompleted ->
                                                    if id = currentId then
                                                        if outerStopped then
                                                            handler.Notify(OnCompleted)
                                                        else
                                                            hasActive <- false
                                                            currentHandle <- None }

                                let innerH = innerFactor.Subscribe(innerHandler)
                                currentHandle <- Some innerH
                            | OnError e ->
                                match currentHandle with
                                | Some h -> h.Dispose()
                                | None -> ()

                                handler.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if not hasActive then
                                    handler.Notify(OnCompleted) }

            let outerHandle = source.Subscribe(outerHandler)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerHandle.Dispose()

                    match currentHandle with
                    | Some h -> h.Dispose()
                    | None -> () } }

/// Projects each element into a factor and switches to the latest.
let switchMap (mapper: 'a -> Factor<'b, 'e>) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
    source |> map mapper |> switchInner

/// Projects each element and its index into a factor and switches to latest.
let switchMapi (mapper: 'a -> int -> Factor<'b, 'e>) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
    source |> mapi mapper |> switchInner

/// Performs a side effect for each emission without transforming.
let tap (effect: 'a -> unit) (source: Factor<'a, 'e>) : Factor<'a, 'e> =
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
let startWith (values: 'a list) (source: Factor<'a, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            for v in values do
                handler.Notify(OnNext v)

            source.Subscribe(handler) }

/// Emits consecutive pairs of values.
let pairwise (source: Factor<'a, 'e>) : Factor<'a * 'a, 'e> =
    { Subscribe =
        fun handler ->
            let mutable prev: 'a option = None

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
let scan (initial: 'b) (accumulator: 'b -> 'a -> 'b) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
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
let reduce (initial: 'b) (accumulator: 'b -> 'a -> 'b) (source: Factor<'a, 'e>) : Factor<'b, 'e> =
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
let groupBy (keySelector: 'a -> 'k) (source: Factor<'a, 'e>) : Factor<'k * Factor<'a, 'e>, 'e> =
    { Subscribe =
        fun handler ->
            let groups = System.Collections.Generic.Dictionary<'k, Handler<'a, 'e> list>()
            let buffers = System.Collections.Generic.Dictionary<'k, 'a list>()
            let emittedKeys = System.Collections.Generic.HashSet<'k>()
            let mutable terminated = false

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let key = keySelector x
                            let isNew = emittedKeys.Add(key)

                            if isNew then
                                let groupFactor: Factor<'a, 'e> =
                                    { Subscribe =
                                        fun groupHandler ->
                                            // Flush buffer
                                            match buffers.TryGetValue(key) with
                                            | true, buf ->
                                                for v in buf do
                                                    groupHandler.Notify(OnNext v)

                                                buffers.Remove(key) |> ignore
                                            | _ -> ()

                                            if terminated then
                                                groupHandler.Notify(OnCompleted)

                                            // Add subscriber
                                            match groups.TryGetValue(key) with
                                            | true, subs -> groups.[key] <- groupHandler :: subs
                                            | _ -> groups.[key] <- [ groupHandler ]

                                            emptyHandle () }

                                handler.Notify(OnNext(key, groupFactor))

                            // Forward to group subscribers or buffer
                            match groups.TryGetValue(key) with
                            | true, subs when subs.Length > 0 ->
                                for sub in subs do
                                    sub.Notify(OnNext x)
                            | _ ->
                                match buffers.TryGetValue(key) with
                                | true, buf -> buffers.[key] <- buf @ [ x ]
                                | _ -> buffers.[key] <- [ x ]
                        | OnError e ->
                            for kv in groups do
                                for sub in kv.Value do
                                    sub.Notify(OnError e)

                            handler.Notify(OnError e)
                        | OnCompleted ->
                            terminated <- true

                            for kv in groups do
                                for sub in kv.Value do
                                    sub.Notify(OnCompleted)

                            handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }
