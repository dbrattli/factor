/// Filter operators for Factor
///
/// These operators filter elements from a Factor sequence.
module Factor.Filter

open System.Collections.Generic
open Factor.Types

/// Filters elements based on a predicate.
let filter (predicate: 'T -> bool) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if predicate x then
                                handler.Notify(n)
                        | _ -> handler.Notify(n) }

            source.Subscribe(upstream) }

/// Applies a function that returns Option. Emits Some values, skips None.
let choose (chooser: 'T -> 'U option) (source: Factor<'T>) : Factor<'U> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            match chooser x with
                            | Some value -> handler.Notify(OnNext value)
                            | None -> ()
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Returns the first N elements from the source.
let take (count: int) (source: Factor<'T>) : Factor<'T> =
    if count <= 0 then
        { Subscribe =
            fun handler ->
                handler.Notify(OnCompleted)
                emptyHandle () }
    else
        { Subscribe =
            fun handler ->
                let mutable remaining = count

                let upstream =
                    { Notify =
                        fun n ->
                            if remaining > 0 then
                                match n with
                                | OnNext x ->
                                    remaining <- remaining - 1
                                    handler.Notify(OnNext x)

                                    if remaining = 0 then
                                        handler.Notify(OnCompleted)
                                | OnError e -> handler.Notify(OnError e)
                                | OnCompleted -> handler.Notify(OnCompleted) }

                source.Subscribe(upstream) }

/// Skips the first N elements from the source.
let skip (count: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable remaining = count

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if remaining > 0 then
                                remaining <- remaining - 1
                            else
                                handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Takes elements while predicate returns true.
let takeWhile (predicate: 'T -> bool) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable taking = true

            let upstream =
                { Notify =
                    fun n ->
                        if taking then
                            match n with
                            | OnNext x ->
                                if predicate x then
                                    handler.Notify(OnNext x)
                                else
                                    taking <- false
                                    handler.Notify(OnCompleted)
                            | OnError e -> handler.Notify(OnError e)
                            | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Skips elements while predicate returns true.
let skipWhile (predicate: 'T -> bool) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable skipping = true

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if skipping then
                                if not (predicate x) then
                                    skipping <- false
                                    handler.Notify(OnNext x)
                            else
                                handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Emits elements that are different from the previous element.
let distinctUntilChanged (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable last: 'T option = None

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            match last with
                            | None ->
                                last <- Some x
                                handler.Notify(OnNext x)
                            | Some prev ->
                                if prev <> x then
                                    last <- Some x
                                    handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Returns elements until the other factor emits.
let takeUntil (other: Factor<'U>) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable stopped = false

            let otherHandler =
                { Notify =
                    fun n ->
                        if not stopped then
                            match n with
                            | OnNext _ ->
                                stopped <- true
                                handler.Notify(OnCompleted)
                            | OnError e ->
                                stopped <- true
                                handler.Notify(OnError e)
                            | OnCompleted -> () }

            let otherHandle = other.Subscribe(otherHandler)

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
                                stopped <- true
                                handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(sourceHandler)

            { Dispose =
                fun () ->
                    sourceHandle.Dispose()
                    otherHandle.Dispose() } }

/// Returns the last N elements from the source.
let takeLast (count: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let buffer = Queue<'T>()

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            buffer.Enqueue(x)

                            if buffer.Count > count then
                                buffer.Dequeue() |> ignore
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted ->
                            for item in buffer do
                                handler.Notify(OnNext item)

                            handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Takes only the first element. Errors if source is empty.
let first (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable gotValue = false

            let upstream =
                { Notify =
                    fun n ->
                        if not gotValue then
                            match n with
                            | OnNext x ->
                                gotValue <- true
                                handler.Notify(OnNext x)
                                handler.Notify(OnCompleted)
                            | OnError e -> handler.Notify(OnError e)
                            | OnCompleted -> handler.Notify(OnError "Sequence contains no elements") }

            source.Subscribe(upstream) }

/// Takes only the last element. Errors if source is empty.
let last (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable latest: 'T option = None

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> latest <- Some x
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted ->
                            match latest with
                            | Some x ->
                                handler.Notify(OnNext x)
                                handler.Notify(OnCompleted)
                            | None -> handler.Notify(OnError "Sequence contains no elements") }

            source.Subscribe(upstream) }

/// Emits a default value if the source completes without emitting.
let defaultIfEmpty (defaultValue: 'T) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable hasValue = false

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            hasValue <- true
                            handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted ->
                            if not hasValue then
                                handler.Notify(OnNext defaultValue)

                            handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }

/// Samples the source when the sampler emits.
let sample (sampler: Factor<'U>) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable latest: 'T option = None
            let mutable sourceDone = false
            let mutable samplerDone = false

            let samplerHandler =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext _ ->
                            match latest with
                            | Some x ->
                                handler.Notify(OnNext x)
                                latest <- None
                            | None -> ()
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted ->
                            samplerDone <- true

                            if sourceDone then
                                handler.Notify(OnCompleted) }

            let samplerHandle = sampler.Subscribe(samplerHandler)

            let sourceHandler =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> latest <- Some x
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted ->
                            sourceDone <- true

                            if samplerDone then
                                handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(sourceHandler)

            { Dispose =
                fun () ->
                    sourceHandle.Dispose()
                    samplerHandle.Dispose() } }

/// Filters out all duplicate values (not just consecutive).
let distinct (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let seen = HashSet<'T>()

            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x ->
                            if seen.Add(x) then
                                handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError e)
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }
