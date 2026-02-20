/// Tests for subject module
module Factor.SubjectTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// singleSubject tests
// ============================================================================

let single_subject_forwards_values_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.singleSubject ()
    output |> Rx.subscribe tc.Handler |> ignore

    Rx.onNext input 1
    Rx.onNext input 2
    Rx.onNext input 3
    Rx.onCompleted input

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let single_subject_buffers_before_subscribe_test () =
    let input, output = Rx.singleSubject ()

    // Send values BEFORE subscribing
    Rx.onNext input 10
    Rx.onNext input 20

    sleep 10

    // Now subscribe
    let tc = TestCollector<int>()
    output |> Rx.subscribe tc.Handler |> ignore

    // Send more after subscribe
    Rx.onNext input 30
    Rx.onCompleted input

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let single_subject_forwards_errors_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.singleSubject ()
    output |> Rx.subscribe tc.Handler |> ignore

    Rx.onNext input 1
    Rx.onError input "test error"

    shouldEqual [ 1 ] tc.Results
    shouldEqual [ "test error" ] tc.Errors

let single_subject_dispose_stops_forwarding_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.singleSubject ()
    let disp = output |> Rx.subscribe tc.Handler

    Rx.onNext input 1
    sleep 10
    disp.Dispose()
    sleep 10

    // These should not be received
    Rx.onNext input 2
    Rx.onNext input 3

    shouldEqual [ 1 ] tc.Results

let single_subject_with_facade_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.singleSubject ()
    output |> Rx.subscribe tc.Handler |> ignore

    Rx.onNext input 42
    Rx.onCompleted input

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let single_subject_works_with_map_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.singleSubject ()

    output
    |> Rx.map (fun x -> x * 2)
    |> Rx.subscribe tc.Handler
    |> ignore

    Rx.onNext input 1
    Rx.onNext input 2
    Rx.onNext input 3
    Rx.onCompleted input

    shouldEqual [ 2; 4; 6 ] tc.Results
    shouldBeTrue tc.Completed

let single_subject_works_with_filter_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.singleSubject ()

    output
    |> Rx.filter (fun x -> x > 2)
    |> Rx.subscribe tc.Handler
    |> ignore

    Rx.onNext input 1
    Rx.onNext input 2
    Rx.onNext input 3
    Rx.onNext input 4
    Rx.onCompleted input

    shouldEqual [ 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// multicast subject tests
// ============================================================================

let subject_forwards_values_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.subject ()
    output |> Rx.subscribe tc.Handler |> ignore

    Rx.onNext input 1
    Rx.onNext input 2
    Rx.onNext input 3
    Rx.onCompleted input

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let subject_allows_multiple_subscribers_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()
    let input, output = Rx.subject ()

    output |> Rx.subscribe tc1.Handler |> ignore
    output |> Rx.subscribe tc2.Handler |> ignore

    Rx.onNext input 42
    Rx.onCompleted input

    shouldEqual [ 42 ] tc1.Results
    shouldEqual [ 42 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let subject_does_not_buffer_test () =
    let input, output = Rx.subject ()

    // Send values BEFORE subscribing
    Rx.onNext input 10
    Rx.onNext input 20

    sleep 10

    // Now subscribe
    let tc = TestCollector<int>()
    output |> Rx.subscribe tc.Handler |> ignore

    // Send more after subscribe
    Rx.onNext input 30
    Rx.onCompleted input

    // Should only receive values after subscription
    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let subject_dispose_stops_receiving_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.subject ()
    let disp = output |> Rx.subscribe tc.Handler

    Rx.onNext input 1
    sleep 10
    disp.Dispose()
    sleep 10

    // These should not be received
    Rx.onNext input 2
    Rx.onNext input 3

    shouldEqual [ 1 ] tc.Results

let subject_with_facade_test () =
    let tc = TestCollector<int>()
    let input, output = Rx.subject ()
    output |> Rx.subscribe tc.Handler |> ignore

    Rx.onNext input 42
    Rx.onCompleted input

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
