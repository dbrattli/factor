/// Transform operators for Factor
///
/// These operators transform the elements of an observable sequence.
module Factor.Transform

open Factor.Types

/// Returns an observable whose elements are the result of invoking
/// the mapper function on each element of the source.
let map (mapper: 'a -> 'b) (source: Observable<'a>) : Observable<'b> =
    { Subscribe =
        fun observer ->
            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> observer.Notify(OnNext(mapper x))
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Returns an observable whose elements are the result of invoking
/// the mapper function on each element and its index.
let mapi (mapper: 'a -> int -> 'b) (source: Observable<'a>) : Observable<'b> =
    { Subscribe =
        fun observer ->
            let mutable index = 0

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let i = index
                            index <- index + 1
                            observer.Notify(OnNext(mapper x i))
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Flattens an Observable of Observables by merging inner emissions.
///
/// The maxConcurrency parameter controls how many inner observables
/// can be subscribed to concurrently:
/// - None: Unlimited concurrency
/// - Some(1): Sequential (equivalent to concatInner)
/// - Some(n): At most n inner observables active at once
let mergeInner (maxConcurrency: int option) (source: Observable<Observable<'a>>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable innerCount = 0
            let mutable outerStopped = false
            let mutable disposed = false
            let queue = System.Collections.Generic.Queue<Observable<'a>>()
            let innerDisposables = System.Collections.Generic.Dictionary<int, Disposable>()
            let mutable nextInnerId = 1

            let canSubscribe () =
                match maxConcurrency with
                | None -> true
                | Some max -> innerCount < max

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeToInner: Observable<'a> -> unit = fun _ -> ()

            subscribeToInner <-
                fun (inner: Observable<'a>) ->
                    let innerId = nextInnerId
                    nextInnerId <- nextInnerId + 1
                    innerCount <- innerCount + 1

                    let innerObserver =
                        { Notify =
                            fun n ->
                                if not disposed then
                                    match n with
                                    | OnNext value -> observer.Notify(OnNext value)
                                    | OnError e ->
                                        // Dispose all active inner subscriptions on error
                                        for kv in innerDisposables do
                                            kv.Value.Dispose()

                                        innerDisposables.Clear()
                                        observer.Notify(OnError e)
                                    | OnCompleted ->
                                        innerCount <- innerCount - 1
                                        innerDisposables.Remove(innerId) |> ignore

                                        if queue.Count > 0 then
                                            let next = queue.Dequeue()
                                            subscribeToInner next
                                        elif outerStopped && innerCount <= 0 then
                                            observer.Notify(OnCompleted) }

                    let innerDisp = inner.Subscribe(innerObserver)
                    innerDisposables.[innerId] <- innerDisp

            let outerObserver =
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
                                for kv in innerDisposables do
                                    kv.Value.Dispose()

                                innerDisposables.Clear()
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if innerCount <= 0 && queue.Count = 0 then
                                    observer.Notify(OnCompleted) }

            let outerDisp = source.Subscribe(outerObserver)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerDisp.Dispose()

                    for kv in innerDisposables do
                        kv.Value.Dispose()

                    innerDisposables.Clear() } }

/// Flattens an Observable of Observables by concatenating in order.
let concatInner (source: Observable<Observable<'a>>) : Observable<'a> = mergeInner (Some 1) source

/// Projects each element into an observable and merges the results.
let flatMap (mapper: 'a -> Observable<'b>) (source: Observable<'a>) : Observable<'b> =
    source |> map mapper |> mergeInner None

/// Projects each element into an observable and concatenates in order.
let concatMap (mapper: 'a -> Observable<'b>) (source: Observable<'a>) : Observable<'b> =
    source |> map mapper |> concatInner

/// Projects each element and its index into an observable and merges.
let flatMapi (mapper: 'a -> int -> Observable<'b>) (source: Observable<'a>) : Observable<'b> =
    source |> mapi mapper |> mergeInner None

/// Projects each element and its index into an observable and concatenates.
let concatMapi (mapper: 'a -> int -> Observable<'b>) (source: Observable<'a>) : Observable<'b> =
    source |> mapi mapper |> concatInner

/// Flattens an Observable of Observables by switching to the latest.
let switchInner (source: Observable<Observable<'a>>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable currentId = 0
            let mutable nextId = 1
            let mutable outerStopped = false
            let mutable hasActive = false
            let mutable disposed = false
            let mutable currentDisposable: Disposable option = None

            let outerObserver =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext innerObservable ->
                                // Dispose previous inner
                                match currentDisposable with
                                | Some d -> d.Dispose()
                                | None -> ()

                                let id = nextId
                                nextId <- nextId + 1
                                currentId <- id
                                hasActive <- true

                                let innerObserver =
                                    { Notify =
                                        fun innerN ->
                                            if not disposed then
                                                match innerN with
                                                | OnNext value ->
                                                    if id = currentId then
                                                        observer.Notify(OnNext value)
                                                | OnError e ->
                                                    match currentDisposable with
                                                    | Some d -> d.Dispose()
                                                    | None -> ()

                                                    observer.Notify(OnError e)
                                                | OnCompleted ->
                                                    if id = currentId then
                                                        if outerStopped then
                                                            observer.Notify(OnCompleted)
                                                        else
                                                            hasActive <- false
                                                            currentDisposable <- None }

                                let innerDisp = innerObservable.Subscribe(innerObserver)
                                currentDisposable <- Some innerDisp
                            | OnError e ->
                                match currentDisposable with
                                | Some d -> d.Dispose()
                                | None -> ()

                                observer.Notify(OnError e)
                            | OnCompleted ->
                                outerStopped <- true

                                if not hasActive then
                                    observer.Notify(OnCompleted) }

            let outerDisp = source.Subscribe(outerObserver)

            { Dispose =
                fun () ->
                    disposed <- true
                    outerDisp.Dispose()

                    match currentDisposable with
                    | Some d -> d.Dispose()
                    | None -> () } }

/// Projects each element into an observable and switches to the latest.
let switchMap (mapper: 'a -> Observable<'b>) (source: Observable<'a>) : Observable<'b> =
    source |> map mapper |> switchInner

/// Projects each element and its index into an observable and switches to latest.
let switchMapi (mapper: 'a -> int -> Observable<'b>) (source: Observable<'a>) : Observable<'b> =
    source |> mapi mapper |> switchInner

/// Performs a side effect for each emission without transforming.
let tap (effect: 'a -> unit) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            effect x
                            observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Prepends values before the source emissions.
let startWith (values: 'a list) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            for v in values do
                observer.Notify(OnNext v)

            source.Subscribe(observer) }

/// Emits consecutive pairs of values.
let pairwise (source: Observable<'a>) : Observable<'a * 'a> =
    { Subscribe =
        fun observer ->
            let mutable prev: 'a option = None

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            match prev with
                            | Some p -> observer.Notify(OnNext(p, x))
                            | None -> ()

                            prev <- Some x
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Applies an accumulator function over the source, emitting each intermediate result.
let scan (initial: 'b) (accumulator: 'b -> 'a -> 'b) (source: Observable<'a>) : Observable<'b> =
    { Subscribe =
        fun observer ->
            let mutable acc = initial

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            acc <- accumulator acc x
                            observer.Notify(OnNext acc)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Applies an accumulator function over the source, emitting only the final value.
let reduce (initial: 'b) (accumulator: 'b -> 'a -> 'b) (source: Observable<'a>) : Observable<'b> =
    { Subscribe =
        fun observer ->
            let mutable acc = initial

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> acc <- accumulator acc x
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted ->
                            observer.Notify(OnNext acc)
                            observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Groups elements by key, returning an observable of grouped observables.
let groupBy (keySelector: 'a -> 'k) (source: Observable<'a>) : Observable<'k * Observable<'a>> =
    { Subscribe =
        fun observer ->
            let groups = System.Collections.Generic.Dictionary<'k, Observer<'a> list>()
            let buffers = System.Collections.Generic.Dictionary<'k, 'a list>()
            let emittedKeys = System.Collections.Generic.HashSet<'k>()
            let mutable terminated = false

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            let key = keySelector x
                            let isNew = emittedKeys.Add(key)

                            if isNew then
                                let groupObservable: Observable<'a> =
                                    { Subscribe =
                                        fun groupObserver ->
                                            // Flush buffer
                                            match buffers.TryGetValue(key) with
                                            | true, buf ->
                                                for v in buf do
                                                    groupObserver.Notify(OnNext v)

                                                buffers.Remove(key) |> ignore
                                            | _ -> ()

                                            if terminated then
                                                groupObserver.Notify(OnCompleted)

                                            // Add subscriber
                                            match groups.TryGetValue(key) with
                                            | true, subs -> groups.[key] <- groupObserver :: subs
                                            | _ -> groups.[key] <- [ groupObserver ]

                                            emptyDisposable () }

                                observer.Notify(OnNext(key, groupObservable))

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

                            observer.Notify(OnError e)
                        | OnCompleted ->
                            terminated <- true

                            for kv in groups do
                                for sub in kv.Value do
                                    sub.Notify(OnCompleted)

                            observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }
