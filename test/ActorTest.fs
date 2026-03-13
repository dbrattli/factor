/// Tests for Fable.Actor API
module Fable.Actor.ActorTest

open Fable.Actor.Types
open Fable.Actor
open Fable.Actor.TestUtils

// ============================================================================
// basic spawn tests
// ============================================================================

/// An actor that does nothing should not crash.
let actor_empty_test () =
    actor {
        let _a: Actor<string> =
            spawn (fun _inbox -> actor { return () })

        do! sleep 10
        shouldBeTrue true
    }

type SingleMsg =
    | SetValue of string
    | GetValue

/// An actor can receive a single message.
let actor_single_receive_test () =
    actor {
        let a =
            start "" (fun state (msg, rc) ->
                match msg with
                | SetValue s -> Continue s
                | GetValue ->
                    rc.Reply state
                    Continue state)

        cast a (SetValue "hello")
        do! sleep 10
        let! got = call a GetValue
        shouldEqual "hello" got
    }

type CollectIntMsg =
    | AddItem of int
    | GetItems

/// An actor can receive multiple messages in order.
let actor_multiple_receive_test () =
    actor {
        let a =
            start [] (fun items (msg, rc) ->
                match msg with
                | AddItem n -> Continue(items @ [ n ])
                | GetItems ->
                    rc.Reply items
                    Continue items)

        cast a (AddItem 1)
        cast a (AddItem 2)
        cast a (AddItem 3)
        do! sleep 10
        let! items = call a GetItems
        shouldEqual [ 1; 2; 3 ] items
    }

type PostMsg =
    | SetInt of int
    | GetInt

/// Post is equivalent to send.
let actor_post_test () =
    actor {
        let a =
            start 0 (fun state (msg, rc) ->
                match msg with
                | SetInt n -> Continue n
                | GetInt ->
                    rc.Reply state
                    Continue state)

        a.Post((SetInt 42, { Reply = fun _ -> () }))
        do! sleep 10
        let! got = call a GetInt
        shouldEqual 42 got
    }

// ============================================================================
// stateful actor (start) tests
// ============================================================================

type CounterMsg =
    | Increment
    | Decrement
    | GetCount

/// start creates a stateful actor with a handler.
let actor_start_basic_test () =
    actor {
        let counter =
            start 0 (fun count (msg, rc) ->
                match msg with
                | Increment -> Continue(count + 1)
                | Decrement -> Continue(count - 1)
                | GetCount ->
                    rc.Reply count
                    Continue count)

        cast counter Increment
        cast counter Increment
        cast counter Increment
        cast counter Decrement

        do! sleep 10

        let! count = call counter GetCount
        shouldEqual 2 count
    }

type Command =
    | Add of int
    | Done

/// Stop terminates the actor handler loop.
let actor_start_stop_test () =
    actor {
        let counter =
            start 0 (fun count (msg, rc) ->
                match msg with
                | Add _ -> Continue(count + 1)
                | Done ->
                    rc.Reply count
                    Stop)

        cast counter (Add 1)
        cast counter (Add 1)
        cast counter (Add 1)

        do! sleep 10

        let! count = call counter Done
        shouldEqual 3 count
    }

// ============================================================================
// call/reply tests
// ============================================================================

/// call sends a message and awaits a reply.
let actor_call_reply_test () =
    actor {
        let counter =
            start 0 (fun count (msg, rc) ->
                match msg with
                | Increment -> Continue(count + 1)
                | Decrement -> Continue(count - 1)
                | GetCount ->
                    rc.Reply count
                    Continue count)

        cast counter Increment
        cast counter Increment
        cast counter Increment

        do! sleep 10

        let! count = call counter GetCount
        shouldEqual 3 count
    }

/// Multiple sequential calls return correct values.
let actor_multiple_calls_test () =
    actor {
        let counter =
            start 0 (fun count (msg, rc) ->
                match msg with
                | Increment -> Continue(count + 1)
                | Decrement -> Continue(count - 1)
                | GetCount ->
                    rc.Reply count
                    Continue count)

        cast counter Increment
        do! sleep 10
        let! c1 = call counter GetCount
        shouldEqual 1 c1

        cast counter Increment
        cast counter Increment
        do! sleep 10
        let! c2 = call counter GetCount
        shouldEqual 3 c2
    }

// ============================================================================
// spawn + actor CE tests
// ============================================================================

type CollectorMsg<'T> =
    | Collect of 'T
    | GetResults

/// Spawned actor can send messages to other actors.
let actor_spawn_send_test () =
    actor {
        let collector =
            start [] (fun results (msg, rc) ->
                match msg with
                | Collect x -> Continue(results @ [ x ])
                | GetResults ->
                    rc.Reply results
                    Continue results)

        let _worker: Actor<string> =
            spawn (fun _inbox ->
                cast collector (Collect "hello")
                cast collector (Collect "world")
                actor { return () })

        do! sleep 50

        let! results = call collector GetResults
        shouldEqual [ "hello"; "world" ] results
    }

/// Spawn an actor that receives, transforms, and forwards messages.
let actor_spawn_receive_forward_test () =
    actor {
        let collector =
            start [] (fun results (msg, rc) ->
                match msg with
                | Collect x -> Continue(results @ [ x ])
                | GetResults ->
                    rc.Reply results
                    Continue results)

        let doubler: Actor<int> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! n = inbox.Receive()
                        cast collector (Collect(n * 2))
                        return! loop ()
                    }

                loop ())

        send doubler 1
        send doubler 2
        send doubler 3

        do! sleep 50

        let! results = call collector GetResults
        shouldEqual [ 2; 4; 6 ] results
    }

/// Actor can do async work (sleep) before responding.
let actor_async_work_test () =
    actor {
        let worker: Actor<unit * ReplyChannel<string>> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! (), rc = inbox.Receive()
                        do! sleep 10
                        rc.Reply "done"
                        return! loop ()
                    }

                loop ())

        let! result = call worker ()
        shouldEqual "done" result
    }

// ============================================================================
// schedule (timer) tests
// ============================================================================

type TimerMsg =
    | Tick
    | GetTicks

/// schedule fires callbacks after a delay.
let actor_schedule_test () =
    actor {
        let ticker =
            start 0 (fun count (msg, rc) ->
                match msg with
                | Tick -> Continue(count + 1)
                | GetTicks ->
                    rc.Reply count
                    Continue count)

        schedule 10 (fun () -> cast ticker Tick) |> ignore
        schedule 20 (fun () -> cast ticker Tick) |> ignore
        schedule 30 (fun () -> cast ticker Tick) |> ignore

        do! sleep 100

        let! ticks = call ticker GetTicks
        shouldEqual 3 ticks
    }

// ============================================================================
// linked actors + supervision tests
// ============================================================================

/// spawnLinked child crash does not crash the parent (with trapExits).
let actor_linked_crash_test () =
    actor {
        let supervisor =
            spawn (fun inbox ->
                trapExits ()

                let child: Actor<string> =
                    spawnLinked inbox (fun childInbox ->
                        let rec loop () =
                            actor {
                                let! _msg = childInbox.Receive()
                                failwith "crash!"
                                return! loop ()
                            }

                        loop ())

                // Send a message to make the child crash
                send child "boom"

                // Receive the EXIT signal
                let rec loop (crashCount: int) =
                    actor {
                        let! _msg = inbox.Receive()
                        return! loop (crashCount + 1)
                    }

                loop 0)

        do! sleep 100
        // If we get here without the supervisor crashing, supervision works
        shouldBeTrue true
    }

// ============================================================================
// supervised actor tests
// ============================================================================

/// A reporter actor for cross-process state sharing on BEAM.
/// Set via cast (Some value), query via call None.
let reporter initial =
    start initial (fun state (msg, rc) ->
        match msg with
        | Some v -> Continue v
        | None -> rc.Reply state; Continue state)

/// spawnSupervised restarts a crashed child when strategy says Restart.
let actor_supervised_restart_test () =
    actor {
        let restarts = reporter 0

        let _parent: Actor<obj> =
            spawn (fun inbox ->
                trapExits ()

                let child =
                    spawnSupervised inbox (OneForOne(fun _ex -> Directive.Restart)) (fun childInbox ->
                        let rec loop () =
                            actor {
                                let! msg = childInbox.Receive()

                                if msg = "crash" then
                                    failwith "intentional crash"

                                return! loop ()
                            }

                        loop ())

                send child.Actor "crash"

                let rec loop restartCount =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            let restarted = handleChildExit inbox child exited
                            let newCount = if restarted then restartCount + 1 else restartCount
                            cast restarts (Some newCount)
                            return! loop newCount
                        | None -> return! loop restartCount
                    }

                loop 0)

        do! sleep 200
        let! count = call restarts None
        shouldBeTrue (count >= 1)
    }

/// spawnSupervised stops child when strategy says Stop.
let actor_supervised_stop_test () =
    actor {
        let flag = reporter false

        let _parent: Actor<obj> =
            spawn (fun inbox ->
                trapExits ()

                let child =
                    spawnSupervised inbox (OneForOne(fun _ex -> Directive.Stop)) (fun childInbox ->
                        let rec loop () =
                            actor {
                                let! _msg = childInbox.Receive()
                                failwith "crash!"
                                return! loop ()
                            }

                        loop ())

                send child.Actor "boom"

                let rec loop () =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            let restarted = handleChildExit inbox child exited
                            if not restarted then cast flag (Some true)
                        | None -> ()

                        return! loop ()
                    }

                loop ())

        do! sleep 200
        let! stopped = call flag None
        shouldBeTrue stopped
    }

// ============================================================================
// StopAbnormal tests
// ============================================================================

/// StopAbnormal triggers ChildExited on the parent via spawnLinked.
let actor_stop_abnormal_test () =
    actor {
        let flag = reporter false

        let _parent: Actor<obj> =
            spawn (fun inbox ->
                trapExits ()

                let stoppingChild =
                    spawnSupervised inbox (OneForOne(fun _ex -> Directive.Stop)) (fun childInbox ->
                        let rec loop () =
                            actor {
                                let! msg = childInbox.Receive()

                                if msg = "stop-abnormal" then
                                    raise (ProcessExitException "intentional abnormal stop")

                                return! loop ()
                            }

                        loop ())

                send stoppingChild.Actor "stop-abnormal"

                let rec loop () =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            handleChildExit inbox stoppingChild exited |> ignore
                            cast flag (Some true)
                        | None -> ()

                        return! loop ()
                    }

                loop ())

        do! sleep 200
        let! gotExit = call flag None
        shouldBeTrue gotExit
    }

/// StopAbnormal in start handler propagates as ChildExited to parent.
let actor_start_stop_abnormal_test () =
    actor {
        let flag = reporter false

        let _parent: Actor<obj> =
            spawn (fun inbox ->
                trapExits ()

                let child =
                    spawnSupervised inbox (OneForOne(fun _ex -> Directive.Stop)) (fun childInbox ->
                        let rec loop (state: int) =
                            actor {
                                let! msg = childInbox.Receive()

                                match msg with
                                | "fail" -> raise (ProcessExitException "bad state")
                                | _ -> return! loop (state + 1)
                            }

                        loop 0)

                send child.Actor "fail"

                let rec loop () =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            handleChildExit inbox child exited |> ignore
                            cast flag (Some true)
                        | None -> ()

                        return! loop ()
                    }

                loop ())

        do! sleep 200
        let! gotExit = call flag None
        shouldBeTrue gotExit
    }

/// start handler returning StopAbnormal raises and triggers supervision.
let actor_start_handler_stop_abnormal_test () =
    actor {
        let flag = reporter false

        let _parent: Actor<obj> =
            spawn (fun inbox ->
                trapExits ()

                let child =
                    spawnSupervised inbox (OneForOne(fun _ex -> Directive.Restart)) (fun childInbox ->
                        let handler state msg =
                            match msg with
                            | "crash" -> StopAbnormal(ProcessExitException "handler decided to crash")
                            | _ -> Continue(state + 1)

                        let rec loop state =
                            actor {
                                let! msg = childInbox.Receive()

                                match handler state msg with
                                | Continue newState -> return! loop newState
                                | Stop -> ()
                                | StopAbnormal ex -> raise ex
                            }

                        loop 0)

                send child.Actor "crash"

                let rec loop () =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            handleChildExit inbox child exited |> ignore
                            cast flag (Some true)
                        | None -> ()

                        return! loop ()
                    }

                loop ())

        do! sleep 200
        let! gotExit = call flag None
        shouldBeTrue gotExit
    }

// ============================================================================
// callWithTimeout tests
// ============================================================================

type TimeoutMsg =
    | Slow
    | Fast

/// callWithTimeout succeeds when reply arrives within timeout.
let actor_call_with_timeout_success_test () =
    actor {
        let worker =
            start () (fun _state (msg, rc) ->
                match msg with
                | Fast ->
                    rc.Reply "fast"
                    Continue()
                | Slow ->
                    rc.Reply "slow"
                    Continue())

        let! result = callWithTimeout 1000 worker Fast
        shouldEqual "fast" result
    }

/// callWithTimeout raises TimeoutException when reply takes too long.
let actor_call_with_timeout_expires_test () =
    actor {
        let worker: Actor<unit * ReplyChannel<string>> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! (), _rc = inbox.Receive()
                        // Never reply — let it time out
                        do! sleep 5000
                        return! loop ()
                    }

                loop ())

        let mutable timedOut = false

        try
            let! _result = callWithTimeout 50 worker ()
            ()
        with :? System.TimeoutException ->
            timedOut <- true

        shouldBeTrue timedOut
    }

// ============================================================================
// kill tests
// ============================================================================

/// kill prevents an actor from receiving further messages.
let actor_kill_test () =
    actor {
        let mutable received = false

        let target: Actor<string> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! _msg = inbox.Receive()
                        received <- true
                        return! loop ()
                    }

                loop ())

        kill target
        send target "should not arrive"
        do! sleep 50

        shouldBeFalse received
    }
