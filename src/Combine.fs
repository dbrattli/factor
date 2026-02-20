/// Combining operators for Factor
///
/// These operators combine multiple Factor sequences.
module Factor.Combine

open Factor.Types

/// Merges multiple factor sequences into one.
let merge (sources: Factor<'a, 'e> list) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            match sources with
            | [] ->
                handler.Notify(OnCompleted)
                emptyHandle ()
            | _ ->
                let mutable remaining = sources.Length
                let mutable stopped = false

                let handles =
                    sources
                    |> List.map (fun source ->
                        let sourceHandler =
                            { Notify =
                                fun n ->
                                    if not stopped then
                                        match n with
                                        | OnNext x -> handler.Notify(OnNext x)
                                        | OnError e ->
                                            stopped <- true
                                            handler.Notify(OnError e)
                                        | OnCompleted ->
                                            remaining <- remaining - 1

                                            if remaining <= 0 then
                                                handler.Notify(OnCompleted) }

                        source.Subscribe(sourceHandler))

                { Dispose =
                    fun () ->
                        stopped <- true

                        for h in handles do
                            h.Dispose() } }

/// Merge two factors.
let merge2 (source1: Factor<'a, 'e>) (source2: Factor<'a, 'e>) : Factor<'a, 'e> = merge [ source1; source2 ]

/// Combines the latest values from two factors using a combiner function.
let combineLatest (combiner: 'a -> 'b -> 'c) (source1: Factor<'a, 'e>) (source2: Factor<'b, 'e>) : Factor<'c, 'e> =
    { Subscribe =
        fun handler ->
            let mutable left: 'a option = None
            let mutable right: 'b option = None
            let mutable leftDone = false
            let mutable rightDone = false
            let mutable stopped = false

            let h1 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext a ->
                                left <- Some a

                                match right with
                                | Some b -> handler.Notify(OnNext(combiner a b))
                                | None -> ()
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                leftDone <- true

                                if rightDone then
                                    handler.Notify(OnCompleted) }

            let h2 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext b ->
                                right <- Some b

                                match left with
                                | Some a -> handler.Notify(OnNext(combiner a b))
                                | None -> ()
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                rightDone <- true

                                if leftDone then
                                    handler.Notify(OnCompleted) }

            let handle1 = source1.Subscribe(h1)
            let handle2 = source2.Subscribe(h2)

            { Dispose =
                fun () ->
                    stopped <- true
                    handle1.Dispose()
                    handle2.Dispose() } }

/// Combines source with the latest value from another factor.
let withLatestFrom
    (combiner: 'a -> 'b -> 'c)
    (sampler: Factor<'b, 'e>)
    (source: Factor<'a, 'e>)
    : Factor<'c, 'e> =
    { Subscribe =
        fun handler ->
            let mutable latest: 'b option = None
            let mutable stopped = false

            let samplerH =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext b -> latest <- Some b
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted -> () }

            let samplerHandle = sampler.Subscribe(samplerH)

            let sourceH =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext a ->
                                match latest with
                                | Some b -> handler.Notify(OnNext(combiner a b))
                                | None -> ()
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted -> handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(sourceH)

            { Dispose =
                fun () ->
                    stopped <- true
                    sourceHandle.Dispose()
                    samplerHandle.Dispose() } }

/// Pairs elements from two factors by index.
let zip (combiner: 'a -> 'b -> 'c) (source1: Factor<'a, 'e>) (source2: Factor<'b, 'e>) : Factor<'c, 'e> =
    { Subscribe =
        fun handler ->
            let leftQueue = System.Collections.Generic.Queue<'a>()
            let rightQueue = System.Collections.Generic.Queue<'b>()
            let mutable leftDone = false
            let mutable rightDone = false
            let mutable stopped = false

            let h1 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext a ->
                                if rightQueue.Count > 0 then
                                    let b = rightQueue.Dequeue()
                                    handler.Notify(OnNext(combiner a b))

                                    if rightDone && rightQueue.Count = 0 then
                                        stopped <- true
                                        handler.Notify(OnCompleted)
                                else
                                    leftQueue.Enqueue(a)
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                leftDone <- true

                                if leftQueue.Count = 0 then
                                    stopped <- true
                                    handler.Notify(OnCompleted) }

            let h2 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext b ->
                                if leftQueue.Count > 0 then
                                    let a = leftQueue.Dequeue()
                                    handler.Notify(OnNext(combiner a b))

                                    if leftDone && leftQueue.Count = 0 then
                                        stopped <- true
                                        handler.Notify(OnCompleted)
                                else
                                    rightQueue.Enqueue(b)
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                rightDone <- true

                                if rightQueue.Count = 0 then
                                    stopped <- true
                                    handler.Notify(OnCompleted) }

            let handle1 = source1.Subscribe(h1)
            let handle2 = source2.Subscribe(h2)

            { Dispose =
                fun () ->
                    stopped <- true
                    handle1.Dispose()
                    handle2.Dispose() } }

/// Concatenates multiple factors sequentially.
let concat (sources: Factor<'a, 'e> list) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let mutable disposed = false
            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeTo: Factor<'a, 'e> list -> unit = fun _ -> ()

            subscribeTo <-
                fun (remaining: Factor<'a, 'e> list) ->
                    match remaining with
                    | [] -> handler.Notify(OnCompleted)
                    | current :: rest ->
                        let sourceHandler =
                            { Notify =
                                fun n ->
                                    if not disposed then
                                        match n with
                                        | OnNext x -> handler.Notify(OnNext x)
                                        | OnError e -> handler.Notify(OnError e)
                                        | OnCompleted -> subscribeTo rest }

                        current.Subscribe(sourceHandler) |> ignore

            subscribeTo sources

            { Dispose = fun () -> disposed <- true } }

/// Concatenates two factors.
let concat2 (source1: Factor<'a, 'e>) (source2: Factor<'a, 'e>) : Factor<'a, 'e> = concat [ source1; source2 ]

/// Returns the factor that emits first.
let amb (sources: Factor<'a, 'e> list) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            match sources with
            | [] ->
                handler.Notify(OnCompleted)
                emptyHandle ()
            | _ ->
                let mutable winner: int option = None
                let mutable completedCount = 0
                let total = sources.Length

                let handles =
                    sources
                    |> List.mapi (fun idx source ->
                        let sourceHandler =
                            { Notify =
                                fun n ->
                                    match n with
                                    | OnNext x ->
                                        match winner with
                                        | None ->
                                            winner <- Some idx
                                            handler.Notify(OnNext x)
                                        | Some w ->
                                            if idx = w then
                                                handler.Notify(OnNext x)
                                    | OnError e ->
                                        match winner with
                                        | None -> handler.Notify(OnError e)
                                        | Some w ->
                                            if idx = w then
                                                handler.Notify(OnError e)
                                    | OnCompleted ->
                                        match winner with
                                        | None ->
                                            completedCount <- completedCount + 1

                                            if completedCount >= total then
                                                handler.Notify(OnCompleted)
                                        | Some w ->
                                            if idx = w then
                                                handler.Notify(OnCompleted) }

                        source.Subscribe(sourceHandler))

                { Dispose =
                    fun () ->
                        for h in handles do
                            h.Dispose() } }

/// Alias for amb.
let race (sources: Factor<'a, 'e> list) : Factor<'a, 'e> = amb sources

/// Waits for all factors to complete, then emits a list of their last values.
let forkJoin (sources: Factor<'a, string> list) : Factor<'a list, string> =
    { Subscribe =
        fun handler ->
            match sources with
            | [] ->
                handler.Notify(OnNext [])
                handler.Notify(OnCompleted)
                emptyHandle ()
            | _ ->
                let total = sources.Length
                let values = System.Collections.Generic.Dictionary<int, 'a>()
                let mutable completedCount = 0
                let mutable stopped = false

                let handles =
                    sources
                    |> List.mapi (fun idx source ->
                        let sourceHandler =
                            { Notify =
                                fun n ->
                                    if not stopped then
                                        match n with
                                        | OnNext x -> values.[idx] <- x
                                        | OnError e ->
                                            stopped <- true
                                            handler.Notify(OnError e)
                                        | OnCompleted ->
                                            completedCount <- completedCount + 1

                                            if completedCount >= total then
                                                if values.Count = total then
                                                    let allValues =
                                                        [ for i in 0 .. total - 1 -> values.[i] ]

                                                    handler.Notify(OnNext allValues)
                                                    handler.Notify(OnCompleted)
                                                else
                                                    handler.Notify(
                                                        OnError "fork_join: source completed without emitting"
                                                    ) }

                        source.Subscribe(sourceHandler))

                { Dispose =
                    fun () ->
                        stopped <- true

                        for h in handles do
                            h.Dispose() } }
