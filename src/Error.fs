/// Error handling operators for Factor
///
/// These operators handle errors in observable sequences.
module Factor.Error

open Factor.Types

/// Resubscribes to the source observable when an error occurs,
/// up to the specified number of retries.
let retry (maxRetries: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable retryCount = 0
            let mutable currentDisp: Disposable option = None
            let mutable disposed = false

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable subscribeToSource: unit -> unit = fun () -> ()

            subscribeToSource <-
                fun () ->
                    let sourceObserver =
                        { Notify =
                            fun n ->
                                if not disposed then
                                    match n with
                                    | OnNext x -> observer.Notify(OnNext x)
                                    | OnError e ->
                                        if retryCount < maxRetries then
                                            retryCount <- retryCount + 1

                                            match currentDisp with
                                            | Some d -> d.Dispose()
                                            | None -> ()

                                            subscribeToSource ()
                                        else
                                            match currentDisp with
                                            | Some d -> d.Dispose()
                                            | None -> ()

                                            observer.Notify(OnError e)
                                    | OnCompleted ->
                                        match currentDisp with
                                        | Some d -> d.Dispose()
                                        | None -> ()

                                        observer.Notify(OnCompleted) }

                    let disp = source.Subscribe(sourceObserver)
                    currentDisp <- Some disp

            subscribeToSource ()

            { Dispose =
                fun () ->
                    disposed <- true

                    match currentDisp with
                    | Some d -> d.Dispose()
                    | None -> () } }

/// On error, switches to a fallback observable returned by the handler.
let catch (handler: string -> Observable<'a>) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable currentDisp: Disposable option = None
            let mutable disposed = false

            let sourceObserver =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x -> observer.Notify(OnNext x)
                            | OnError e ->
                                match currentDisp with
                                | Some d -> d.Dispose()
                                | None -> ()

                                let fallback = handler e

                                let fallbackObserver =
                                    { Notify =
                                        fun fn ->
                                            if not disposed then
                                                match fn with
                                                | OnNext x -> observer.Notify(OnNext x)
                                                | OnError fe -> observer.Notify(OnError fe)
                                                | OnCompleted -> observer.Notify(OnCompleted) }

                                let fallbackDisp = fallback.Subscribe(fallbackObserver)
                                currentDisp <- Some fallbackDisp
                            | OnCompleted ->
                                match currentDisp with
                                | Some d -> d.Dispose()
                                | None -> ()

                                observer.Notify(OnCompleted) }

            let disp = source.Subscribe(sourceObserver)
            currentDisp <- Some disp

            { Dispose =
                fun () ->
                    disposed <- true

                    match currentDisp with
                    | Some d -> d.Dispose()
                    | None -> () } }
