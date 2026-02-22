/// Tests for actor computation expression
module Factor.ActorTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// spawn and send tests
// ============================================================================

let actor_spawn_and_receive_test () =
    let tc = TestCollector<string>()
    let (input, output) = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.spawn (fun ctx ->
            actor {
                let! msg = ctx.Recv()
                Reactive.pushNext input msg
                Reactive.pushCompleted input
            })

    Actor.send pid "hello"
    sleep 50

    shouldEqual [ "hello" ] tc.Results
    shouldBeTrue tc.Completed

let actor_multiple_messages_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.spawn (fun ctx ->
            actor {
                let! x = ctx.Recv()
                Reactive.pushNext input x
                let! y = ctx.Recv()
                Reactive.pushNext input y
                let! z = ctx.Recv()
                Reactive.pushNext input z
                Reactive.pushCompleted input
            })

    Actor.send pid 10
    Actor.send pid 20
    Actor.send pid 30
    sleep 50

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let actor_return_value_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.spawn (fun ctx ->
            actor {
                let! x = ctx.Recv()
                let doubled = x * 2
                Reactive.pushNext input doubled
                Reactive.pushCompleted input
                return ()
            })

    Actor.send pid 21
    sleep 50

    shouldEqual [ 42 ] tc.Results

// ============================================================================
// recursive actor loop tests
// ============================================================================

let actor_rec_accumulate_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.spawn (fun ctx ->
            let rec loop acc =
                actor {
                    let! msg = ctx.Recv()

                    match msg with
                    | 0 ->
                        Reactive.pushNext input acc
                        Reactive.pushCompleted input
                        return ()
                    | n -> return! loop (acc + n)
                }
            loop 0)

    Actor.send pid 1
    Actor.send pid 2
    Actor.send pid 3
    Actor.send pid 0 // sentinel to stop
    sleep 50

    shouldEqual [ 6 ] tc.Results
    shouldBeTrue tc.Completed

let actor_rec_count_messages_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.spawn (fun ctx ->
            let rec loop n =
                actor {
                    let! msg = ctx.Recv()

                    match msg with
                    | "stop" ->
                        Reactive.pushNext input n
                        Reactive.pushCompleted input
                        return ()
                    | _ -> return! loop (n + 1)
                }
            loop 0)

    Actor.send pid "a"
    Actor.send pid "b"
    Actor.send pid "c"
    Actor.send pid "stop"
    sleep 50

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// typed message tests
// ============================================================================

type Command =
    | Add of int
    | Get

let actor_typed_messages_test () =
    let tc = TestCollector<int>()
    let (input, output) = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    let pid =
        Actor.spawn (fun ctx ->
            let rec loop total =
                actor {
                    let! msg = ctx.Recv()

                    match msg with
                    | Add n -> return! loop (total + n)
                    | Get ->
                        Reactive.pushNext input total
                        return! loop total
                }
            loop 0)

    Actor.send pid (Add 10)
    Actor.send pid (Add 20)
    Actor.send pid (Add 5)
    Actor.send pid Get
    sleep 50

    shouldEqual [ 35 ] tc.Results
