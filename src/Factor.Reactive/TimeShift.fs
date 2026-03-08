/// Time-based operators for Factor.Reactive
///
/// Every operator spawns a BEAM process. Timer callbacks run
/// in the operator's process via factor_timer scheduling.
module Factor.Reactive.TimeShift

open Factor.Agent.Types
open Factor.Beam
open Factor.Beam.Agent
open Fable.Core

// Erlang FFI for timer operations using factor_timer module.
[<Emit("factor_timer:schedule($0, $1)")>]
let private timerSchedule (ms: int) (callback: unit -> unit) : obj = nativeOnly

[<Emit("factor_timer:cancel($0)")>]
let private timerCancel (timer: obj) : unit = nativeOnly

/// Creates an observable that emits 0 after the specified delay, then completes.
let timer (delayMs: int) : Observable<int> = {
    Subscribe =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let _ =
                        timerSchedule delayMs (fun () ->
                            Process.onNext downstream 0
                            Process.onCompleted downstream
                            Process.exitNormal ())

                    Operator.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Creates an observable that emits incrementing integers at regular intervals.
let interval (periodMs: int) : Observable<int> = {
    Subscribe =
        fun downstream ->
            let pid =
                Process.spawnLinked (fun () ->
                    let mutable count = 0

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
                    Operator.childLoop ())

            {
                Dispose = fun () -> Process.killProcess pid
            }
}

/// Delays each emission from the source by the specified time.
let delay (ms: int) (source: Observable<'T>) : Observable<'T> = {
    Subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let ref = Process.makeRef ()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe(upstream) |> ignore
                let mutable pending = 0
                let mutable sourceCompleted = false

                let rec loop () =
                    agent {
                        let! msg = Operator.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            pending <- pending + 1

                            let _ =
                                timerSchedule ms (fun () ->
                                    Process.onNext downstream x
                                    pending <- pending - 1

                                    if sourceCompleted && pending = 0 then
                                        Process.onCompleted downstream
                                        Process.exitNormal ())

                            return! loop ()
                        | OnError e -> Process.onError downstream e
                        | OnCompleted ->
                            sourceCompleted <- true

                            if pending = 0 then
                                Process.onCompleted downstream
                            else
                                // Keep looping — timer callbacks fire as side effects
                                // during recvMsg, and call exitNormal when done
                                return! loop ()
                    }

                loop ())
}

/// Emits a value only after the specified time has passed without another emission.
let debounce (ms: int) (source: Observable<'T>) : Observable<'T> = {
    Subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let ref = Process.makeRef ()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe(upstream) |> ignore
                let mutable latest: 'T option = None
                let mutable currentTimer: obj option = None

                let cancelTimer () =
                    match currentTimer with
                    | Some t ->
                        timerCancel t
                        currentTimer <- None
                    | None -> ()

                let rec loop () =
                    agent {
                        let! msg = Operator.recvMsg<'T> ref

                        match msg with
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
                            return! loop ()
                        | OnError e ->
                            cancelTimer ()
                            Process.onError downstream e
                        | OnCompleted ->
                            cancelTimer ()

                            match latest with
                            | Some x -> Process.onNext downstream x
                            | None -> ()

                            Process.onCompleted downstream
                    }

                loop ())
}

/// Rate limits emissions to at most one per specified period.
let throttle (ms: int) (source: Observable<'T>) : Observable<'T> = {
    Subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let ref = Process.makeRef ()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe(upstream) |> ignore
                let mutable inWindow = false
                let mutable latest: 'T option = None
                let mutable currentTimer: obj option = None

                let cancelTimer () =
                    match currentTimer with
                    | Some t ->
                        timerCancel t
                        currentTimer <- None
                    | None -> ()

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

                let rec loop () =
                    agent {
                        let! msg = Operator.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            if not inWindow then
                                Process.onNext downstream x
                                startWindow ()
                            else
                                latest <- Some x

                            return! loop ()
                        | OnError e ->
                            cancelTimer ()
                            Process.onError downstream e
                        | OnCompleted ->
                            cancelTimer ()

                            match latest with
                            | Some x -> Process.onNext downstream x
                            | None -> ()

                            Process.onCompleted downstream
                    }

                loop ())
}

/// Errors if no emission occurs within the specified timeout period.
let timeout (ms: int) (source: Observable<'T>) : Observable<'T> = {
    Subscribe =
        fun downstream ->
            Operator.spawnOp (fun () ->
                let ref = Process.makeRef ()
                let upstream: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
                source.Subscribe(upstream) |> ignore
                let mutable currentTimer: obj option = None

                let cancelTimer () =
                    match currentTimer with
                    | Some t ->
                        timerCancel t
                        currentTimer <- None
                    | None -> ()

                let startTimer () =
                    let t =
                        timerSchedule ms (fun () ->
                            Process.onError
                                downstream
                                (TimeoutException(sprintf "Timeout: no emission within %dms" ms))

                            Process.exitNormal ())

                    currentTimer <- Some t

                startTimer ()

                let rec loop () =
                    agent {
                        let! msg = Operator.recvMsg<'T> ref

                        match msg with
                        | OnNext x ->
                            cancelTimer ()
                            Process.onNext downstream x
                            startTimer ()
                            return! loop ()
                        | OnError e ->
                            cancelTimer ()
                            Process.onError downstream e
                        | OnCompleted ->
                            cancelTimer ()
                            Process.onCompleted downstream
                    }

                loop ())
}
