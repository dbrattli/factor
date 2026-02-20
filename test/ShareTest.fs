/// Tests for share and publish operators
module Factor.ShareTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// publish tests
// ============================================================================

let publish_no_emissions_before_connect_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let hot, connect = Rx.publish (Rx.ofList [ 1; 2; 3 ])

    hot |> Rx.subscribe tc1.Handler |> ignore
    hot |> Rx.subscribe tc2.Handler |> ignore

    // Nothing received yet - not connected
    sleep 50
    shouldEqual [] tc1.Results
    shouldEqual [] tc2.Results

    // Now connect
    connect () |> ignore

    shouldEqual [ 1; 2; 3 ] tc1.Results
    shouldEqual [ 1; 2; 3 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let publish_multiple_subscribers_share_source_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let hot, connect =
        Rx.publish (Rx.interval 30 |> Rx.take 3)

    hot |> Rx.subscribe tc1.Handler |> ignore
    hot |> Rx.subscribe tc2.Handler |> ignore

    connect () |> ignore

    sleep 200

    shouldEqual [ 0; 1; 2 ] tc1.Results
    shouldEqual [ 0; 1; 2 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let publish_connect_returns_disposable_test () =
    let tc = TestCollector<int>()

    let hot, connect =
        Rx.publish (Rx.interval 30 |> Rx.take 10)

    hot |> Rx.subscribe tc.Handler |> ignore
    let connection = connect ()

    sleep 80
    connection.Dispose()
    sleep 80

    shouldBeTrue (tc.Results.Length < 5)
    shouldBeFalse tc.Completed

let publish_connect_idempotent_test () =
    let tc = TestCollector<int>()

    let hot, connect = Rx.publish (Rx.ofList [ 1; 2; 3 ])

    hot |> Rx.subscribe tc.Handler |> ignore

    let _conn1 = connect ()
    let _conn2 = connect ()

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// share tests
// ============================================================================

let share_connects_on_first_subscriber_test () =
    let tc = TestCollector<int>()

    let shared = Rx.interval 30 |> Rx.take 3 |> Rx.share

    shared |> Rx.subscribe tc.Handler |> ignore

    sleep 200

    shouldEqual [ 0; 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

let share_multiple_subscribers_share_source_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let shared = Rx.interval 30 |> Rx.take 3 |> Rx.share

    shared |> Rx.subscribe tc1.Handler |> ignore
    shared |> Rx.subscribe tc2.Handler |> ignore

    sleep 200

    shouldEqual [ 0; 1; 2 ] tc1.Results
    shouldEqual [ 0; 1; 2 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let share_with_sync_source_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let shared = Rx.ofList [ 1; 2; 3; 4; 5 ] |> Rx.share

    shared |> Rx.subscribe tc1.Handler |> ignore
    shared |> Rx.subscribe tc2.Handler |> ignore

    shouldEqual [ 1; 2; 3; 4; 5 ] tc1.Results
    shouldEqual [ 1; 2; 3; 4; 5 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let share_with_map_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.map (fun x -> x * 10)
    |> Rx.share
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let share_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.share
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let share_error_propagates_test () =
    let tc = TestCollector<int>()

    Rx.fail "Test error"
    |> Rx.share
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "Test error" ] tc.Errors

let share_resubscribe_reconnects_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let shared = Rx.ofList [ 1; 2; 3 ] |> Rx.share

    // First subscription
    let d1 = shared |> Rx.subscribe tc1.Handler
    shouldEqual [ 1; 2; 3 ] tc1.Results
    shouldBeTrue tc1.Completed

    d1.Dispose()

    // Second subscription - should reconnect
    shared |> Rx.subscribe tc2.Handler |> ignore
    shouldEqual [ 1; 2; 3 ] tc2.Results
    shouldBeTrue tc2.Completed
