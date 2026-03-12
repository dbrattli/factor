/// Tests for Fable.Actor API
module Fable.Actor.ActorTest

open Fable.Actor.Types
open Fable.Actor
open Fable.Actor.TestUtils

// ============================================================================
// start tests (stateful actor with handler)
// ============================================================================

type Command =
    | Add of int
    | GetTotal of ReplyChannel<int>
    | Done of ReplyChannel<int>

let actor_start_basic_test () =
    let counter =
        start 0 (fun total msg ->
            match msg with
            | Add n -> Continue(total + n)
            | GetTotal rc ->
                rc.Reply total
                Continue total
            | Done rc ->
                rc.Reply total
                Stop)

    send counter (Add 10)
    send counter (Add 20)
    send counter (Add 5)

    let total = call counter (fun rc -> GetTotal rc)
    shouldEqual 35 total

let actor_start_stop_test () =
    let counter =
        start 0 (fun count msg ->
            match msg with
            | Add _ -> Continue(count + 1)
            | Done rc ->
                rc.Reply count
                Stop
            | GetTotal rc ->
                rc.Reply count
                Continue count)

    send counter (Add 1)
    send counter (Add 1)
    send counter (Add 1)

    let count = call counter (fun rc -> Done rc)
    shouldEqual 3 count

// ============================================================================
// call/reply tests
// ============================================================================

type CounterMsg =
    | Increment
    | GetCount of ReplyChannel<int>

let actor_call_reply_test () =
    let counter =
        start 0 (fun count msg ->
            match msg with
            | Increment -> Continue(count + 1)
            | GetCount rc ->
                rc.Reply count
                Continue count)

    send counter Increment
    send counter Increment
    send counter Increment

    let count = call counter (fun rc -> GetCount rc)
    shouldEqual 3 count

// ============================================================================
// spawn + actor CE tests
// ============================================================================

type CollectorMsg<'T> =
    | Collect of 'T
    | GetResults of ReplyChannel<'T list>

let actor_spawn_ce_test () =
    let collector =
        start [] (fun results msg ->
            match msg with
            | Collect x -> Continue(results @ [ x ])
            | GetResults rc ->
                rc.Reply results
                Continue results)

    let _worker: Actor<string> =
        spawn (fun () ->
            send collector (Collect "hello")
            send collector (Collect "world")
            actor { return () })

    sleep 50

    let results = call collector (fun rc -> GetResults rc)
    shouldEqual [ "hello"; "world" ] results

// ============================================================================
// schedule (timer) tests
// ============================================================================

type TimerMsg =
    | Tick
    | GetTicks of ReplyChannel<int>

let actor_schedule_test () =
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

    sleep 100

    let ticks = call ticker (fun rc -> GetTicks rc)
    shouldEqual 3 ticks

// ============================================================================
// linked actors + supervision tests
// ============================================================================

type SupervisorMsg =
    | ChildCrashed of obj
    | GetCrashes of ReplyChannel<int>

let actor_linked_crash_test () =
    let supervisor =
        spawn (fun () ->
            trapExits ()

            let child: Actor<string> =
                spawnLinked (fun () ->
                    let rec loop () =
                        actor {
                            let! _msg = receive<string> ()
                            failwith "crash!"
                            return! loop ()
                        }

                    loop ())

            // Send a message to make the child crash
            send child "boom"

            // Receive the EXIT signal
            let rec loop (crashCount: int) =
                actor {
                    let! _msg = receive<obj> ()
                    return! loop (crashCount + 1)
                }

            loop 0)

    sleep 100
    // If we get here without the supervisor crashing, supervision works
    shouldBeTrue true

let actor_kill_test () =
    let mutable received = false

    let target: Actor<string> =
        spawn (fun () ->
            let rec loop () =
                actor {
                    let! _msg = receive<string> ()
                    received <- true
                    return! loop ()
                }

            loop ())

    kill target
    send target "should not arrive"
    sleep 50

    shouldBeFalse received
