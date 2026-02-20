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

/// Creates a factor that emits 0 after the specified delay, then completes.
let timer (delayMs: int) : Factor<int> =
    { Subscribe =
        fun handler ->
            let mutable disposed = false

            let _ =
                timerSchedule
                    delayMs
                    (fun () ->
                        if not disposed then
                            handler.Notify(OnNext 0)
                            handler.Notify(OnCompleted))

            { Dispose = fun () -> disposed <- true } }

/// Creates a factor that emits incrementing integers at regular intervals.
let interval (periodMs: int) : Factor<int> =
    { Subscribe =
        fun handler ->
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
                                    handler.Notify(OnNext c)
                                    tick ())

                    ()

            tick ()
            { Dispose = fun () -> disposed <- true } }

/// Delays each emission from the source by the specified time.
let delay (ms: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable disposed = false
            let mutable pending = 0
            let mutable sourceCompleted = false

            let upstream =
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
                                                handler.Notify(OnNext x)
                                                pending <- pending - 1

                                                if sourceCompleted && pending = 0 then
                                                    handler.Notify(OnCompleted))

                                ()
                            | OnError e -> handler.Notify(OnError e)
                            | OnCompleted ->
                                sourceCompleted <- true

                                if pending = 0 then
                                    handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(upstream)

            { Dispose =
                fun () ->
                    disposed <- true
                    sourceHandle.Dispose() } }

/// Emits a value only after the specified time has passed without another emission.
let debounce (ms: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable disposed = false
            let mutable latest: 'T option = None
            let mutable currentTimer: obj option = None

            let cancelTimer () =
                match currentTimer with
                | Some t ->
                    timerCancel t
                    currentTimer <- None
                | None -> ()

            let upstream =
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
                                                    handler.Notify(OnNext v)
                                                    latest <- None
                                                | None -> ())

                                currentTimer <- Some t
                            | OnError e ->
                                cancelTimer ()
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                cancelTimer ()

                                match latest with
                                | Some x -> handler.Notify(OnNext x)
                                | None -> ()

                                handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(upstream)

            { Dispose =
                fun () ->
                    disposed <- true
                    cancelTimer ()
                    sourceHandle.Dispose() } }

/// Rate limits emissions to at most one per specified period.
let throttle (ms: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
            let mutable disposed = false
            let mutable inWindow = false
            let mutable latest: 'T option = None
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
                                        handler.Notify(OnNext x)
                                        latest <- None
                                        startWindow ()
                                    | None -> inWindow <- false)

                    currentTimer <- Some t

            let upstream =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x ->
                                if not inWindow then
                                    handler.Notify(OnNext x)
                                    startWindow ()
                                else
                                    latest <- Some x
                            | OnError e ->
                                cancelTimer ()
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                cancelTimer ()

                                match latest with
                                | Some x -> handler.Notify(OnNext x)
                                | None -> ()

                                handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(upstream)

            { Dispose =
                fun () ->
                    disposed <- true
                    cancelTimer ()
                    sourceHandle.Dispose() } }

/// Errors if no emission occurs within the specified timeout period.
let timeout (ms: int) (source: Factor<'T>) : Factor<'T> =
    { Subscribe =
        fun handler ->
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
                                handler.Notify(OnError(sprintf "Timeout: no emission within %dms" ms)))

                currentTimer <- Some t

            startTimer ()

            let upstream =
                { Notify =
                    fun n ->
                        if not disposed then
                            match n with
                            | OnNext x ->
                                cancelTimer ()
                                handler.Notify(OnNext x)
                                startTimer ()
                            | OnError e ->
                                cancelTimer ()
                                handler.Notify(OnError e)
                            | OnCompleted ->
                                cancelTimer ()
                                handler.Notify(OnCompleted) }

            let sourceHandle = source.Subscribe(upstream)

            { Dispose =
                fun () ->
                    disposed <- true
                    cancelTimer ()
                    sourceHandle.Dispose() } }
