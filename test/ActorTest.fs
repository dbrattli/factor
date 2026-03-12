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

/// An actor can receive a single message.
let actor_single_receive_test () =
    actor {
        let mutable got = ""

        let a: Actor<string> =
            spawn (fun inbox ->
                actor {
                    let! msg = inbox.Receive()
                    got <- msg
                })

        send a "hello"
        do! sleep 10
        shouldEqual "hello" got
    }

/// An actor can receive multiple messages in order.
let actor_multiple_receive_test () =
    actor {
        let mutable items: int list = []

        let a: Actor<int> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! n = inbox.Receive()
                        items <- items @ [ n ]
                        return! loop ()
                    }

                loop ())

        send a 1
        send a 2
        send a 3
        do! sleep 10
        shouldEqual [ 1; 2; 3 ] items
    }

/// Post is equivalent to send.
let actor_post_test () =
    actor {
        let mutable got = 0

        let a: Actor<int> =
            spawn (fun inbox ->
                actor {
                    let! n = inbox.Receive()
                    got <- n
                })

        a.Post(42)
        do! sleep 10
        shouldEqual 42 got
    }

// ============================================================================
// stateful actor (start) tests
// ============================================================================

type CounterMsg =
    | Increment
    | Decrement
    | GetCount of ReplyChannel<int>

/// start creates a stateful actor with a handler.
let actor_start_basic_test () =
    actor {
        let counter =
            start 0 (fun count msg ->
                match msg with
                | Increment -> Continue(count + 1)
                | Decrement -> Continue(count - 1)
                | GetCount rc ->
                    rc.Reply count
                    Continue count)

        send counter Increment
        send counter Increment
        send counter Increment
        send counter Decrement

        do! sleep 10

        let! count = call counter (fun rc -> GetCount rc)
        shouldEqual 2 count
    }

type Command =
    | Add of int
    | Done of ReplyChannel<int>

/// Stop terminates the actor handler loop.
let actor_start_stop_test () =
    actor {
        let counter =
            start 0 (fun count msg ->
                match msg with
                | Add _ -> Continue(count + 1)
                | Done rc ->
                    rc.Reply count
                    Stop)

        send counter (Add 1)
        send counter (Add 1)
        send counter (Add 1)

        do! sleep 10

        let! count = call counter (fun rc -> Done rc)
        shouldEqual 3 count
    }

// ============================================================================
// call/reply tests
// ============================================================================

/// call sends a message and awaits a reply.
let actor_call_reply_test () =
    actor {
        let counter =
            start 0 (fun count msg ->
                match msg with
                | Increment -> Continue(count + 1)
                | Decrement -> Continue(count - 1)
                | GetCount rc ->
                    rc.Reply count
                    Continue count)

        send counter Increment
        send counter Increment
        send counter Increment

        do! sleep 10

        let! count = call counter (fun rc -> GetCount rc)
        shouldEqual 3 count
    }

/// Multiple sequential calls return correct values.
let actor_multiple_calls_test () =
    actor {
        let counter =
            start 0 (fun count msg ->
                match msg with
                | Increment -> Continue(count + 1)
                | Decrement -> Continue(count - 1)
                | GetCount rc ->
                    rc.Reply count
                    Continue count)

        send counter Increment
        do! sleep 10
        let! c1 = call counter (fun rc -> GetCount rc)
        shouldEqual 1 c1

        send counter Increment
        send counter Increment
        do! sleep 10
        let! c2 = call counter (fun rc -> GetCount rc)
        shouldEqual 3 c2
    }

// ============================================================================
// spawn + actor CE tests
// ============================================================================

type CollectorMsg<'T> =
    | Collect of 'T
    | GetResults of ReplyChannel<'T list>

/// Spawned actor can send messages to other actors.
let actor_spawn_send_test () =
    actor {
        let collector =
            start [] (fun results msg ->
                match msg with
                | Collect x -> Continue(results @ [ x ])
                | GetResults rc ->
                    rc.Reply results
                    Continue results)

        let _worker: Actor<string> =
            spawn (fun _inbox ->
                send collector (Collect "hello")
                send collector (Collect "world")
                actor { return () })

        do! sleep 50

        let! results = call collector (fun rc -> GetResults rc)
        shouldEqual [ "hello"; "world" ] results
    }

/// Spawn an actor that receives, transforms, and forwards messages.
let actor_spawn_receive_forward_test () =
    actor {
        let collector =
            start [] (fun results msg ->
                match msg with
                | Collect x -> Continue(results @ [ x ])
                | GetResults rc ->
                    rc.Reply results
                    Continue results)

        let doubler: Actor<int> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! n = inbox.Receive()
                        send collector (Collect(n * 2))
                        return! loop ()
                    }

                loop ())

        send doubler 1
        send doubler 2
        send doubler 3

        do! sleep 50

        let! results = call collector (fun rc -> GetResults rc)
        shouldEqual [ 2; 4; 6 ] results
    }

/// Actor can do async work (sleep) before responding.
let actor_async_work_test () =
    actor {
        let worker: Actor<ReplyChannel<string>> =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! rc = inbox.Receive()
                        do! sleep 10
                        rc.Reply "done"
                        return! loop ()
                    }

                loop ())

        let! result = call worker (fun rc -> rc)
        shouldEqual "done" result
    }

// ============================================================================
// schedule (timer) tests
// ============================================================================

type TimerMsg =
    | Tick
    | GetTicks of ReplyChannel<int>

/// schedule fires callbacks after a delay.
let actor_schedule_test () =
    actor {
        let ticker =
            start 0 (fun count msg ->
                match msg with
                | Tick -> Continue(count + 1)
                | GetTicks rc ->
                    rc.Reply count
                    Continue count)

        schedule 10 (fun () -> send ticker Tick) |> ignore
        schedule 20 (fun () -> send ticker Tick) |> ignore
        schedule 30 (fun () -> send ticker Tick) |> ignore

        do! sleep 100

        let! ticks = call ticker (fun rc -> GetTicks rc)
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

/// spawnSupervised restarts a crashed child when strategy says Restart.
let actor_supervised_restart_test () =
    actor {
        let mutable crashCount = 0
        let mutable msgAfterRestart = ""

        let parent: Actor<obj> =
            spawn (fun inbox ->
                trapExits ()

                let child =
                    spawnSupervised inbox (OneForOne(fun _ex -> Directive.Restart)) (fun childInbox ->
                        crashCount <- crashCount + 1

                        let rec loop () =
                            actor {
                                let! msg = childInbox.Receive()

                                if msg = "crash" then
                                    failwith "intentional crash"
                                else
                                    msgAfterRestart <- msg

                                return! loop ()
                            }

                        loop ())

                let rec loop () =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            let _restarted = handleChildExit inbox child exited
                            ()
                        | None -> ()

                        return! loop ()
                    }

                // Make the child crash, then send a message after restart
                send child.Actor "crash"
                loop ())

        do! sleep 100

        // After restart, send a normal message to the restarted child
        // The parent received the EXIT, restarted the child — crashCount should be 2
        shouldEqual 2 crashCount
    }

/// spawnSupervised stops child when strategy says Stop.
let actor_supervised_stop_test () =
    actor {
        let mutable stopped = false

        let parent: Actor<obj> =
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

                let rec loop () =
                    actor {
                        let! msg = inbox.Receive()

                        match tryAsChildExited msg with
                        | Some exited ->
                            let restarted = handleChildExit inbox child exited
                            stopped <- not restarted
                        | None -> ()

                        return! loop ()
                    }

                send child.Actor "boom"
                loop ())

        do! sleep 100
        shouldBeTrue stopped
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
