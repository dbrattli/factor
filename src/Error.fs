/// Error handling operators for Factor
///
/// These operators handle errors in Factor sequences.
module Factor.Error

open Factor.Types

/// Resubscribes to the source factor when an error occurs,
/// up to the specified number of retries.
let retry (maxRetries: int) (source: Factor<'a, 'e>) : Factor<'a, 'e> =
    { Subscribe =
        fun handler ->
            let mutable retryCount = 0
            let mutable currentHandle: Handle option = None
            let mutable disposed = false

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeToSource: unit -> unit = fun () -> ()

            subscribeToSource <-
                fun () ->
                    let sourceHandler =
                        { Notify =
                            fun n ->
                                if not disposed then
                                    match n with
                                    | OnNext x -> handler.Notify(OnNext x)
                                    | OnError e ->
                                        if retryCount < maxRetries then
                                            retryCount <- retryCount + 1

                                            match currentHandle with
                                            | Some h -> h.Dispose()
                                            | None -> ()

                                            subscribeToSource ()
                                        else
                                            match currentHandle with
                                            | Some h -> h.Dispose()
                                            | None -> ()

                                            handler.Notify(OnError e)
                                    | OnCompleted ->
                                        match currentHandle with
                                        | Some h -> h.Dispose()
                                        | None -> ()

                                        handler.Notify(OnCompleted) }

                    let h = source.Subscribe(sourceHandler)
                    currentHandle <- Some h

            subscribeToSource ()

            { Dispose =
                fun () ->
                    disposed <- true

                    match currentHandle with
                    | Some h -> h.Dispose()
                    | None -> () } }

/// On error, switches to a fallback factor returned by the handler.
/// Can change the error type via the fallback.
let catch (errorHandler: 'e1 -> Factor<'a, 'e2>) (source: Factor<'a, 'e1>) : Factor<'a, 'e2> =
    { Subscribe =
        fun handler ->
            let mutable currentHandle: Handle option = None
            let mutable disposed = false

            let sourceHandler =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x -> handler.Notify(OnNext x)
                            | OnError e ->
                                match currentHandle with
                                | Some h -> h.Dispose()
                                | None -> ()

                                let fallback = errorHandler e

                                let fallbackHandler =
                                    { Notify =
                                        fun fn ->
                                            if not disposed then
                                                match fn with
                                                | OnNext x -> handler.Notify(OnNext x)
                                                | OnError fe -> handler.Notify(OnError fe)
                                                | OnCompleted -> handler.Notify(OnCompleted) }

                                let fallbackHandle = fallback.Subscribe(fallbackHandler)
                                currentHandle <- Some fallbackHandle
                            | OnCompleted ->
                                match currentHandle with
                                | Some h -> h.Dispose()
                                | None -> ()

                                handler.Notify(OnCompleted) }

            let h = source.Subscribe(sourceHandler)
            currentHandle <- Some h

            { Dispose =
                fun () ->
                    disposed <- true

                    match currentHandle with
                    | Some h -> h.Dispose()
                    | None -> () } }

/// Transform the error type of a Factor.
let mapError (f: 'e1 -> 'e2) (source: Factor<'a, 'e1>) : Factor<'a, 'e2> =
    { Subscribe =
        fun handler ->
            let upstream =
                { Notify =
                    fun n ->
                        match n with
                        | OnNext x -> handler.Notify(OnNext x)
                        | OnError e -> handler.Notify(OnError(f e))
                        | OnCompleted -> handler.Notify(OnCompleted) }

            source.Subscribe(upstream) }
