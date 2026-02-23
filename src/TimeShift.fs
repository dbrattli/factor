/// Time-based operators for Factor
///
/// Every operator spawns a BEAM process. Timer callbacks run
/// in the operator's process via factor_timer scheduling.
module Factor.TimeShift

open Factor.Types
open Fable.Core

// Erlang FFI for timer operations using factor_timer module.
[<Emit("factor_timer:schedule($0, $1)")>]
let private timerSchedule (ms: int) (callback: unit -> unit) : obj = nativeOnly

[<Emit("factor_timer:cancel($0)")>]
let private timerCancel (timer: obj) : unit = nativeOnly

/// Creates a factor that emits 0 after the specified delay, then completes.
let timer (delayMs: int) : Factor<int> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let _ =
                        timerSchedule delayMs (fun () ->
                            Process.onNext downstream 0
                            Process.onCompleted downstream
                            Process.exitNormal ())

                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Creates a factor that emits incrementing integers at regular intervals.
let interval (periodMs: int) : Factor<int> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable count = 0

                    // Use mutable function ref to avoid let rec inside closure (Fable.Beam limitation)
                    let mutable tick: unit -> unit = fun () -> ()

                    tick <-
                        fun () ->
                            let _ =
                                timerSchedule periodMs (fun () ->
                                    let c = count
                                    count <- count + 1
                                    Process.onNext downstream c
                                    tick ())

                            ()

                    tick ()
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Delays each emission from the source by the specified time.
let delay (ms: int) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable pending = 0
                    let mutable sourceCompleted = false
                    let ref = Process.makeRef ()

                    Process.registerChild ref (fun msg ->
                        let n = unbox<Msg<'T>> msg

                        match n with
                        | OnNext x ->
                            pending <- pending + 1

                            let _ =
                                timerSchedule ms (fun () ->
                                    Process.onNext downstream x
                                    pending <- pending - 1

                                    if sourceCompleted && pending = 0 then
                                        Process.onCompleted downstream
                                        Process.exitNormal ())

                            ()
                        | OnError e ->
                            Process.onError downstream e
                            Process.exitNormal ()
                        | OnCompleted ->
                            sourceCompleted <- true

                            if pending = 0 then
                                Process.onCompleted downstream
                                Process.exitNormal ())

                    let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(self) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Emits a value only after the specified time has passed without another emission.
let debounce (ms: int) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable latest: 'T option = None
                    let mutable currentTimer: obj option = None
                    let ref = Process.makeRef ()

                    let cancelTimer () =
                        match currentTimer with
                        | Some t ->
                            timerCancel t
                            currentTimer <- None
                        | None -> ()

                    Process.registerChild ref (fun msg ->
                        let n = unbox<Msg<'T>> msg

                        match n with
                        | OnNext x ->
                            cancelTimer ()
                            latest <- Some x

                            let t =
                                timerSchedule ms (fun () ->
                                    match latest with
                                    | Some v ->
                                        Process.onNext downstream v
                                        latest <- None
                                    | None -> ())

                            currentTimer <- Some t
                        | OnError e ->
                            cancelTimer ()
                            Process.onError downstream e
                            Process.exitNormal ()
                        | OnCompleted ->
                            cancelTimer ()

                            match latest with
                            | Some x -> Process.onNext downstream x
                            | None -> ()

                            Process.onCompleted downstream
                            Process.exitNormal ())

                    let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(self) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Rate limits emissions to at most one per specified period.
let throttle (ms: int) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable inWindow = false
                    let mutable latest: 'T option = None
                    let mutable currentTimer: obj option = None
                    let ref = Process.makeRef ()

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
                                timerSchedule ms (fun () ->
                                    match latest with
                                    | Some x ->
                                        Process.onNext downstream x
                                        latest <- None
                                        startWindow ()
                                    | None -> inWindow <- false)

                            currentTimer <- Some t

                    Process.registerChild ref (fun msg ->
                        let n = unbox<Msg<'T>> msg

                        match n with
                        | OnNext x ->
                            if not inWindow then
                                Process.onNext downstream x
                                startWindow ()
                            else
                                latest <- Some x
                        | OnError e ->
                            cancelTimer ()
                            Process.onError downstream e
                            Process.exitNormal ()
                        | OnCompleted ->
                            cancelTimer ()

                            match latest with
                            | Some x -> Process.onNext downstream x
                            | None -> ()

                            Process.onCompleted downstream
                            Process.exitNormal ())

                    let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(self) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Errors if no emission occurs within the specified timeout period.
let timeout (ms: int) (source: Factor<'T>) : Factor<'T> = {
    Spawn =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable currentTimer: obj option = None
                    let ref = Process.makeRef ()

                    let cancelTimer () =
                        match currentTimer with
                        | Some t ->
                            timerCancel t
                            currentTimer <- None
                        | None -> ()

                    let startTimer () =
                        let t =
                            timerSchedule ms (fun () ->
                                Process.onError downstream (TimeoutException(sprintf "Timeout: no emission within %dms" ms))

                                Process.exitNormal ())

                        currentTimer <- Some t

                    startTimer ()

                    Process.registerChild ref (fun msg ->
                        let n = unbox<Msg<'T>> msg

                        match n with
                        | OnNext x ->
                            cancelTimer ()
                            Process.onNext downstream x
                            startTimer ()
                        | OnError e ->
                            cancelTimer ()
                            Process.onError downstream e
                            Process.exitNormal ()
                        | OnCompleted ->
                            cancelTimer ()
                            Process.onCompleted downstream
                            Process.exitNormal ())

                    let self: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                    source.Spawn(self) |> ignore
                    Process.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}
