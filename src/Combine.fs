/// Combining operators for Factor
///
/// These operators combine multiple observable sequences.
module Factor.Combine

open Factor.Types

/// Merges multiple observable sequences into one.
let merge (sources: Observable<'a> list) : Observable<'a> =
    { Subscribe =
        fun observer ->
            match sources with
            | [] ->
                observer.Notify(OnCompleted)
                emptyDisposable ()
            | _ ->
                let mutable remaining = sources.Length
                let mutable stopped = false

                let disposables =
                    sources
                    |> List.map (fun source ->
                        let sourceObserver =
                            { Notify =
                                fun n ->
                                    if not stopped then
                                        match n with
                                        | OnNext x -> observer.Notify(OnNext x)
                                        | OnError e ->
                                            stopped <- true
                                            observer.Notify(OnError e)
                                        | OnCompleted ->
                                            remaining <- remaining - 1

                                            if remaining <= 0 then
                                                observer.Notify(OnCompleted) }

                        source.Subscribe(sourceObserver))

                { Dispose =
                    fun () ->
                        stopped <- true

                        for d in disposables do
                            d.Dispose() } }

/// Merge two observables.
let merge2 (source1: Observable<'a>) (source2: Observable<'a>) : Observable<'a> = merge [ source1; source2 ]

/// Combines the latest values from two observables using a combiner function.
let combineLatest (combiner: 'a -> 'b -> 'c) (source1: Observable<'a>) (source2: Observable<'b>) : Observable<'c> =
    { Subscribe =
        fun observer ->
            let mutable left: 'a option = None
            let mutable right: 'b option = None
            let mutable leftDone = false
            let mutable rightDone = false
            let mutable stopped = false

            let obs1 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext a ->
                                left <- Some a

                                match right with
                                | Some b -> observer.Notify(OnNext(combiner a b))
                                | None -> ()
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                leftDone <- true

                                if rightDone then
                                    observer.Notify(OnCompleted) }

            let obs2 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext b ->
                                right <- Some b

                                match left with
                                | Some a -> observer.Notify(OnNext(combiner a b))
                                | None -> ()
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                rightDone <- true

                                if leftDone then
                                    observer.Notify(OnCompleted) }

            let disp1 = source1.Subscribe(obs1)
            let disp2 = source2.Subscribe(obs2)

            { Dispose =
                fun () ->
                    stopped <- true
                    disp1.Dispose()
                    disp2.Dispose() } }

/// Combines source with the latest value from another observable.
let withLatestFrom
    (combiner: 'a -> 'b -> 'c)
    (sampler: Observable<'b>)
    (source: Observable<'a>)
    : Observable<'c> =
    { Subscribe =
        fun observer ->
            let mutable latest: 'b option = None
            let mutable stopped = false

            let samplerObs =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext b -> latest <- Some b
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted -> () }

            let samplerDisp = sampler.Subscribe(samplerObs)

            let sourceObs =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext a ->
                                match latest with
                                | Some b -> observer.Notify(OnNext(combiner a b))
                                | None -> ()
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted -> observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(sourceObs)

            { Dispose =
                fun () ->
                    stopped <- true
                    sourceDisp.Dispose()
                    samplerDisp.Dispose() } }

/// Pairs elements from two observables by index.
let zip (combiner: 'a -> 'b -> 'c) (source1: Observable<'a>) (source2: Observable<'b>) : Observable<'c> =
    { Subscribe =
        fun observer ->
            let leftQueue = System.Collections.Generic.Queue<'a>()
            let rightQueue = System.Collections.Generic.Queue<'b>()
            let mutable leftDone = false
            let mutable rightDone = false
            let mutable stopped = false

            let obs1 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext a ->
                                if rightQueue.Count > 0 then
                                    let b = rightQueue.Dequeue()
                                    observer.Notify(OnNext(combiner a b))

                                    if rightDone && rightQueue.Count = 0 then
                                        stopped <- true
                                        observer.Notify(OnCompleted)
                                else
                                    leftQueue.Enqueue(a)
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                leftDone <- true

                                if leftQueue.Count = 0 then
                                    stopped <- true
                                    observer.Notify(OnCompleted) }

            let obs2 =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext b ->
                                if leftQueue.Count > 0 then
                                    let a = leftQueue.Dequeue()
                                    observer.Notify(OnNext(combiner a b))

                                    if leftDone && leftQueue.Count = 0 then
                                        stopped <- true
                                        observer.Notify(OnCompleted)
                                else
                                    rightQueue.Enqueue(b)
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                rightDone <- true

                                if rightQueue.Count = 0 then
                                    stopped <- true
                                    observer.Notify(OnCompleted) }

            let disp1 = source1.Subscribe(obs1)
            let disp2 = source2.Subscribe(obs2)

            { Dispose =
                fun () ->
                    stopped <- true
                    disp1.Dispose()
                    disp2.Dispose() } }

/// Concatenates multiple observables sequentially.
let concat (sources: Observable<'a> list) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false
            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeTo: Observable<'a> list -> unit = fun _ -> ()

            subscribeTo <-
                fun (remaining: Observable<'a> list) ->
                    match remaining with
                    | [] -> observer.Notify(OnCompleted)
                    | current :: rest ->
                        let sourceObserver =
                            { Notify =
                                fun n ->
                                    if not disposed then
                                        match n with
                                        | OnNext x -> observer.Notify(OnNext x)
                                        | OnError e -> observer.Notify(OnError e)
                                        | OnCompleted -> subscribeTo rest }

                        current.Subscribe(sourceObserver) |> ignore

            subscribeTo sources

            { Dispose = fun () -> disposed <- true } }

/// Concatenates two observables.
let concat2 (source1: Observable<'a>) (source2: Observable<'a>) : Observable<'a> = concat [ source1; source2 ]

/// Returns the observable that emits first.
let amb (sources: Observable<'a> list) : Observable<'a> =
    { Subscribe =
        fun observer ->
            match sources with
            | [] ->
                observer.Notify(OnCompleted)
                emptyDisposable ()
            | _ ->
                let mutable winner: int option = None
                let mutable completedCount = 0
                let total = sources.Length

                let disposables =
                    sources
                    |> List.mapi (fun idx source ->
                        let sourceObserver =
                            { Notify =
                                fun n ->
                                    match n with
                                    | OnNext x ->
                                        match winner with
                                        | None ->
                                            winner <- Some idx
                                            observer.Notify(OnNext x)
                                        | Some w ->
                                            if idx = w then
                                                observer.Notify(OnNext x)
                                    | OnError e ->
                                        match winner with
                                        | None -> observer.Notify(OnError e)
                                        | Some w ->
                                            if idx = w then
                                                observer.Notify(OnError e)
                                    | OnCompleted ->
                                        match winner with
                                        | None ->
                                            completedCount <- completedCount + 1

                                            if completedCount >= total then
                                                observer.Notify(OnCompleted)
                                        | Some w ->
                                            if idx = w then
                                                observer.Notify(OnCompleted) }

                        source.Subscribe(sourceObserver))

                { Dispose =
                    fun () ->
                        for d in disposables do
                            d.Dispose() } }

/// Alias for amb.
let race (sources: Observable<'a> list) : Observable<'a> = amb sources

/// Waits for all observables to complete, then emits a list of their last values.
let forkJoin (sources: Observable<'a> list) : Observable<'a list> =
    { Subscribe =
        fun observer ->
            match sources with
            | [] ->
                observer.Notify(OnNext [])
                observer.Notify(OnCompleted)
                emptyDisposable ()
            | _ ->
                let total = sources.Length
                let values = System.Collections.Generic.Dictionary<int, 'a>()
                let mutable completedCount = 0
                let mutable stopped = false

                let disposables =
                    sources
                    |> List.mapi (fun idx source ->
                        let sourceObserver =
                            { Notify =
                                fun n ->
                                    if not stopped then
                                        match n with
                                        | OnNext x -> values.[idx] <- x
                                        | OnError e ->
                                            stopped <- true
                                            observer.Notify(OnError e)
                                        | OnCompleted ->
                                            completedCount <- completedCount + 1

                                            if completedCount >= total then
                                                if values.Count = total then
                                                    let allValues =
                                                        [ for i in 0 .. total - 1 -> values.[i] ]

                                                    observer.Notify(OnNext allValues)
                                                    observer.Notify(OnCompleted)
                                                else
                                                    observer.Notify(
                                                        OnError "fork_join: source completed without emitting"
                                                    ) }

                        source.Subscribe(sourceObserver))

                { Dispose =
                    fun () ->
                        stopped <- true

                        for d in disposables do
                            d.Dispose() } }
