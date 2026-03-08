/// Tests for actor API
module Factor.ActorTest

open Factor.Actor.Types
open Factor.Reactive
open Factor.Beam
open Factor.TestUtils

// ============================================================================
// spawn tests (raw BEAM process)
// ============================================================================

let actor_spawn_raw_test () =
    let tc = TestCollector<string>()
    let (input, output) = Reactive.singleSubscriber ()
    output |> Reactive.spawn tc.Observer |> ignore

    // Raw spawn — the body is just a function, no message handling
    Actor.spawn (fun () ->
        Reactive.pushNext input "spawned"
        Reactive.pushCompleted input)
    |> ignore

    sleep 50

    shouldEqual [ "spawned" ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// start tests (gen_server style with handler)
// ============================================================================

type Command =
    | Add of int
    | GetTotal
    | Done

let actor_start_basic_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleSubscriber ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.start 0 (fun total msg ->
            match msg with
            | Add n -> Continue(total + n)
            | GetTotal ->
                Reactive.pushNext input total
                Continue total
            | Done ->
                Reactive.pushNext input total
                Reactive.pushCompleted input
                Stop)

    Actor.send pid (Add 10)
    Actor.send pid (Add 20)
    Actor.send pid (Add 5)
    Actor.send pid GetTotal
    sleep 50

    shouldEqual [ 35 ] tc.Results

let actor_start_stop_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleSubscriber ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.start 0 (fun count msg ->
            match msg with
            | Add _ ->
                let newCount = count + 1
                Continue newCount
            | Done ->
                Reactive.pushNext input count
                Reactive.pushCompleted input
                Stop
            | GetTotal -> Continue count)

    Actor.send pid (Add 1)
    Actor.send pid (Add 1)
    Actor.send pid (Add 1)
    Actor.send pid Done
    sleep 50

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// call/reply tests
// ============================================================================

type CounterMsg =
    | Increment
    | GetCount of ReplyChannel<int>

let actor_call_reply_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleSubscriber ()
    output |> Reactive.spawn tc.Observer |> ignore

    // Spawn a counter actor using start
    let counter =
        Actor.start 0 (fun count msg ->
            match msg with
            | Increment -> Continue(count + 1)
            | GetCount rc ->
                rc.Reply count
                Continue count)

    // Spawn a client actor that increments then queries via call
    let _client =
        Actor.spawn (fun () ->
            Actor.send counter Increment
            Actor.send counter Increment
            Actor.send counter Increment
            let count = Actor.call counter (fun rc -> GetCount rc)
            Reactive.pushNext input count
            Reactive.pushCompleted input)

    sleep 50

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed
