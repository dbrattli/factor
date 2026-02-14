/// Time-based operators for Factor
///
/// These operators work with time-based asynchronous streams.
/// On BEAM, timing uses Erlang timer FFI.
module Factor.TimeShift

open Factor.Types
open Fable.Core

// Erlang FFI for timer operations using factor_timer module.
// Callbacks are scheduled as messages to self() and execute in the
// subscriber's process when the timer pump (process_timers) runs.
[<Emit("factor_timer:schedule($0, $1)")>]
let private timerSchedule (ms: int) (callback: unit -> unit) : obj = nativeOnly

[<Emit("factor_timer:cancel($0)")>]
let private timerCancel (timer: obj) : unit = nativeOnly

/// Creates an observable that emits 0 after the specified delay, then completes.
let timer (delayMs: int) : Observable<int> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false

            let _ =
                timerSchedule
                    delayMs
                    (fun () ->
                        if not disposed then
                            observer.Notify(OnNext 0)
                            observer.Notify(OnCompleted))

            { Dispose = fun () -> disposed <- true } }

/// Creates an observable that emits incrementing integers at regular intervals.
let interval (periodMs: int) : Observable<int> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false
            let mutable count = 0

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable tick: unit -> unit = fun () -> ()

            tick <-
                fun () ->
                    let _ =
                        timerSchedule
                            periodMs
                            (fun () ->
                                if not disposed then
                                    let c = count
                                    count <- count + 1
                                    observer.Notify(OnNext c)
                                    tick ())

                    ()

            tick ()
            { Dispose = fun () -> disposed <- true } }

/// Delays each emission from the source by the specified time.
let delay (ms: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false
            let mutable pending = 0
            let mutable sourceCompleted = false

            let upstreamObserver =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x ->
                                pending <- pending + 1

                                let _ =
                                    timerSchedule
                                        ms
                                        (fun () ->
                                            if not disposed then
                                                observer.Notify(OnNext x)
                                                pending <- pending - 1

                                                if sourceCompleted && pending = 0 then
                                                    observer.Notify(OnCompleted))

                                ()
                            | OnError e -> observer.Notify(OnError e)
                            | OnCompleted ->
                                sourceCompleted <- true

                                if pending = 0 then
                                    observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(upstreamObserver)

            { Dispose =
                fun () ->
                    disposed <- true
                    sourceDisp.Dispose() } }

/// Emits a value only after the specified time has passed without another emission.
let debounce (ms: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false
            let mutable latest: 'a option = None
            let mutable currentTimer: obj option = None

            let cancelTimer () =
                match currentTimer with
                | Some t ->
                    timerCancel t
                    currentTimer <- None
                | None -> ()

            let upstreamObserver =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x ->
                                cancelTimer ()
                                latest <- Some x

                                let t =
                                    timerSchedule
                                        ms
                                        (fun () ->
                                            if not disposed then
                                                match latest with
                                                | Some v ->
                                                    observer.Notify(OnNext v)
                                                    latest <- None
                                                | None -> ())

                                currentTimer <- Some t
                            | OnError e ->
                                cancelTimer ()
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                cancelTimer ()

                                match latest with
                                | Some x -> observer.Notify(OnNext x)
                                | None -> ()

                                observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(upstreamObserver)

            { Dispose =
                fun () ->
                    disposed <- true
                    cancelTimer ()
                    sourceDisp.Dispose() } }

/// Rate limits emissions to at most one per specified period.
let throttle (ms: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false
            let mutable inWindow = false
            let mutable latest: 'a option = None
            let mutable currentTimer: obj option = None

            let cancelTimer () =
                match currentTimer with
                | Some t ->
                    timerCancel t
                    currentTimer <- None
                | None -> ()

            // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
            let mutable startWindow: unit -> unit = fun () -> ()

            startWindow <-
                fun () ->
                    inWindow <- true

                    let t =
                        timerSchedule
                            ms
                            (fun () ->
                                if not disposed then
                                    match latest with
                                    | Some x ->
                                        observer.Notify(OnNext x)
                                        latest <- None
                                        startWindow ()
                                    | None -> inWindow <- false)

                    currentTimer <- Some t

            let upstreamObserver =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x ->
                                if not inWindow then
                                    observer.Notify(OnNext x)
                                    startWindow ()
                                else
                                    latest <- Some x
                            | OnError e ->
                                cancelTimer ()
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                cancelTimer ()

                                match latest with
                                | Some x -> observer.Notify(OnNext x)
                                | None -> ()

                                observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(upstreamObserver)

            { Dispose =
                fun () ->
                    disposed <- true
                    cancelTimer ()
                    sourceDisp.Dispose() } }

/// Errors if no emission occurs within the specified timeout period.
let timeout (ms: int) (source: Observable<'a>) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let mutable disposed = false
            let mutable currentTimer: obj option = None

            let cancelTimer () =
                match currentTimer with
                | Some t ->
                    timerCancel t
                    currentTimer <- None
                | None -> ()

            let startTimer () =
                let t =
                    timerSchedule
                        ms
                        (fun () ->
                            if not disposed then
                                observer.Notify(OnError(sprintf "Timeout: no emission within %dms" ms)))

                currentTimer <- Some t

            startTimer ()

            let upstreamObserver =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x ->
                                cancelTimer ()
                                observer.Notify(OnNext x)
                                startTimer ()
                            | OnError e ->
                                cancelTimer ()
                                observer.Notify(OnError e)
                            | OnCompleted ->
                                cancelTimer ()
                                observer.Notify(OnCompleted) }

            let sourceDisp = source.Subscribe(upstreamObserver)

            { Dispose =
                fun () ->
                    disposed <- true
                    cancelTimer ()
                    sourceDisp.Dispose() } }
