/// Tests for channel module
module Factor.ChannelTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// singleChannel tests
// ============================================================================

let single_channel_forwards_values_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    Reactive.pushNext input 1
    Reactive.pushNext input 2
    Reactive.pushNext input 3
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let single_channel_buffers_before_subscribe_test () =
    let input, output = Reactive.singleChannel ()

    // Send values BEFORE subscribing
    Reactive.pushNext input 10
    Reactive.pushNext input 20

    sleep 10

    // Now subscribe
    let tc = TestCollector<int>()
    output |> Reactive.spawn tc.Observer |> ignore

    // Send more after subscribe
    Reactive.pushNext input 30
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let single_channel_forwards_errors_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    Reactive.pushNext input 1
    Reactive.pushError input (FactorException "test error")

    sleep 50
    shouldEqual [ 1 ] tc.Results
    shouldEqual [ FactorException "test error" ] tc.Errors

let single_channel_dispose_stops_forwarding_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleChannel ()
    let disp = output |> Reactive.spawn tc.Observer

    Reactive.pushNext input 1
    sleep 50
    disp.Dispose()
    sleep 10

    // These should not be received
    Reactive.pushNext input 2
    Reactive.pushNext input 3

    sleep 50
    shouldEqual [ 1 ] tc.Results

let single_channel_with_facade_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleChannel ()
    output |> Reactive.spawn tc.Observer |> ignore

    Reactive.pushNext input 42
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let single_channel_works_with_map_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleChannel ()

    output
    |> Reactive.map (fun x -> x * 2)
    |> Reactive.spawn tc.Observer
    |> ignore

    Reactive.pushNext input 1
    Reactive.pushNext input 2
    Reactive.pushNext input 3
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 2; 4; 6 ] tc.Results
    shouldBeTrue tc.Completed

let single_channel_works_with_filter_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleChannel ()

    output
    |> Reactive.filter (fun x -> x > 2)
    |> Reactive.spawn tc.Observer
    |> ignore

    Reactive.pushNext input 1
    Reactive.pushNext input 2
    Reactive.pushNext input 3
    Reactive.pushNext input 4
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// multicast channel tests
// ============================================================================

let channel_forwards_values_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.channel ()
    output |> Reactive.spawn tc.Observer |> ignore

    Reactive.pushNext input 1
    Reactive.pushNext input 2
    Reactive.pushNext input 3
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let channel_allows_multiple_subscribers_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()
    let input, output = Reactive.channel ()

    output |> Reactive.spawn tc1.Observer |> ignore
    output |> Reactive.spawn tc2.Observer |> ignore

    Reactive.pushNext input 42
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 42 ] tc1.Results
    shouldEqual [ 42 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let channel_does_not_buffer_test () =
    let input, output = Reactive.channel ()

    // Send values BEFORE subscribing
    Reactive.pushNext input 10
    Reactive.pushNext input 20

    sleep 10

    // Now subscribe
    let tc = TestCollector<int>()
    output |> Reactive.spawn tc.Observer |> ignore

    // Send more after subscribe
    Reactive.pushNext input 30
    Reactive.pushCompleted input

    sleep 50
    // Should only receive values after subscription
    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let channel_dispose_stops_receiving_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.channel ()
    let disp = output |> Reactive.spawn tc.Observer

    Reactive.pushNext input 1
    sleep 50
    disp.Dispose()
    sleep 10

    // These should not be received
    Reactive.pushNext input 2
    Reactive.pushNext input 3

    sleep 50
    shouldEqual [ 1 ] tc.Results

let channel_with_facade_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.channel ()
    output |> Reactive.spawn tc.Observer |> ignore

    Reactive.pushNext input 42
    Reactive.pushCompleted input

    sleep 50
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
