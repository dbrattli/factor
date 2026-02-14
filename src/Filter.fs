/// Filter operators for Factor
///
/// These operators filter elements from an observable sequence.
module Factor.Filter

open Factor.Types

/// Filters elements based on a predicate.
let filter (predicate: 'a -> bool) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if predicate x then
                                observer.Notify(n)
                        | _ -> observer.Notify(n) }

            source.Subscribe(upstreamObserver) }

/// Applies a function that returns Option. Emits Some values, skips None.
let choose (chooser: 'a -> 'b option) (source: Observable<'a>) : Observable<'b> =
    { Subscribe =
        fun observer ->
            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            match chooser x with
                            | Some value -> observer.Notify(OnNext value)
                            | None -> ()
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Returns the first N elements from the source.
let take (count: int) (source: Observable<'a>) : Observable<'a> =
    if count <= 0 then
        { Subscribe =
            fun observer ->
                observer.Notify(OnCompleted)
                emptyDisposable () }
    else
        { Subscribe =
            fun observer ->
                let mutable remaining = count

                let upstreamObserver =
                    { Notify =
                        fun n ->
                            if remaining > 0 then
                                match n with
                                | OnNext x ->
                                    remaining <- remaining - 1
                                    observer.Notify(OnNext x)

                                    if remaining = 0 then
                                        observer.Notify(OnCompleted)
                                | OnError e -> observer.Notify(OnError e)
                                | OnCompleted -> observer.Notify(OnCompleted) }

                source.Subscribe(upstreamObserver) }

/// Skips the first N elements from the source.
let skip (count: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable remaining = count

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if remaining > 0 then
                                remaining <- remaining - 1
                            else
                                observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Takes elements while predicate returns true.
let takeWhile (predicate: 'a -> bool) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable taking = true

            let upstreamObserver =
                { Notify =
                    fun n ->
                        if taking then
                            match n with
                            | OnNext x ->
                                if predicate x then
                                    observer.Notify(OnNext x)
                                else
                                    taking <- false
                                    observer.Notify(OnCompleted)
                            | OnError e -> observer.Notify(OnError e)
                            | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Skips elements while predicate returns true.
let skipWhile (predicate: 'a -> bool) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable skipping = true

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if skipping then
                                if not (predicate x) then
                                    skipping <- false
                                    observer.Notify(OnNext x)
                            else
                                observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Emits elements that are different from the previous element.
let distinctUntilChanged (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable last: 'a option = None

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            match last with
                            | None ->
                                last <- Some x
                                observer.Notify(OnNext x)
                            | Some prev ->
                                if prev <> x then
                                    last <- Some x
                                    observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Returns elements until the other observable emits.
let takeUntil (other: Observable<'b>) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable stopped = false

            let otherObserver =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext _ ->
                                stopped <- true
                                observer.Notify(OnCompleted)
                            | OnError e ->
                                stopped <- true
                                observer.Notify(OnError e)
                            | OnCompleted -> () }

            let otherDisp = other.Subscribe(otherObserver)

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
                                stopped <- true
                                observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(sourceObserver)

            { Dispose =
                fun () ->
                    sourceDisp.Dispose()
                    otherDisp.Dispose() } }

/// Returns the last N elements from the source.
let takeLast (count: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let buffer = System.Collections.Generic.Queue<'a>()

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            buffer.Enqueue(x)

                            if buffer.Count > count then
                                buffer.Dequeue() |> ignore
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted ->
                            for item in buffer do
                                observer.Notify(OnNext item)

                            observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Takes only the first element. Errors if source is empty.
let first (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable gotValue = false

            let upstreamObserver =
                { Notify =
                    fun n ->
                        if not gotValue then
                            match n with
                            | OnNext x ->
                                gotValue <- true
                                observer.Notify(OnNext x)
                                observer.Notify(OnCompleted)
                            | OnError e -> observer.Notify(OnError e)
                            | OnCompleted -> observer.Notify(OnError "Sequence contains no elements") }

            source.Subscribe(upstreamObserver) }

/// Takes only the last element. Errors if source is empty.
let last (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable latest: 'a option = None

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> latest <- Some x
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted ->
                            match latest with
                            | Some x ->
                                observer.Notify(OnNext x)
                                observer.Notify(OnCompleted)
                            | None -> observer.Notify(OnError "Sequence contains no elements") }

            source.Subscribe(upstreamObserver) }

/// Emits a default value if the source completes without emitting.
let defaultIfEmpty (defaultValue: 'a) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable hasValue = false

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            hasValue <- true
                            observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted ->
                            if not hasValue then
                                observer.Notify(OnNext defaultValue)

                            observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }

/// Samples the source when the sampler emits.
let sample (sampler: Observable<'b>) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable latest: 'a option = None
            let mutable sourceDone = false
            let mutable samplerDone = false

            let samplerObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext _ ->
                            match latest with
                            | Some x ->
                                observer.Notify(OnNext x)
                                latest <- None
                            | None -> ()
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted ->
                            samplerDone <- true

                            if sourceDone then
                                observer.Notify(OnCompleted) }

            let samplerDisp = sampler.Subscribe(samplerObserver)

            let sourceObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> latest <- Some x
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted ->
                            sourceDone <- true

                            if samplerDone then
                                observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(sourceObserver)

            { Dispose =
                fun () ->
                    sourceDisp.Dispose()
                    samplerDisp.Dispose() } }

/// Filters out all duplicate values (not just consecutive).
let distinct (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let seen = System.Collections.Generic.HashSet<'a>()

            let upstreamObserver =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if seen.Add(x) then
                                observer.Notify(OnNext x)
                        | OnError e -> observer.Notify(OnError e)
                        | OnCompleted -> observer.Notify(OnCompleted) }

            source.Subscribe(upstreamObserver) }
