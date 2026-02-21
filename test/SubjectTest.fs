/// Tests for subject module
module Factor.SubjectTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// singleSubject tests
// ============================================================================

let single_subject_forwards_values_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleSubject ()
    output |> Reactive.subscribe tc.Handler |> ignore

    Reactive.onNext input 1
    Reactive.onNext input 2
    Reactive.onNext input 3
    Reactive.onCompleted input

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let single_subject_buffers_before_subscribe_test () =
    let input, output = Reactive.singleSubject ()

    // Send values BEFORE subscribing
    Reactive.onNext input 10
    Reactive.onNext input 20

    sleep 10

    // Now subscribe
    let tc = TestCollector<int>()
    output |> Reactive.subscribe tc.Handler |> ignore

    // Send more after subscribe
    Reactive.onNext input 30
    Reactive.onCompleted input

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let single_subject_forwards_errors_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleSubject ()
    output |> Reactive.subscribe tc.Handler |> ignore

    Reactive.onNext input 1
    Reactive.onError input (FactorException "test error")

    shouldEqual [ 1 ] tc.Results
    shouldEqual [ FactorException "test error" ] tc.Errors

let single_subject_dispose_stops_forwarding_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleSubject ()
    let disp = output |> Reactive.subscribe tc.Handler

    Reactive.onNext input 1
    sleep 10
    disp.Dispose()
    sleep 10

    // These should not be received
    Reactive.onNext input 2
    Reactive.onNext input 3

    shouldEqual [ 1 ] tc.Results

let single_subject_with_facade_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleSubject ()
    output |> Reactive.subscribe tc.Handler |> ignore

    Reactive.onNext input 42
    Reactive.onCompleted input

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let single_subject_works_with_map_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleSubject ()

    output
    |> Reactive.map (fun x -> x * 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    Reactive.onNext input 1
    Reactive.onNext input 2
    Reactive.onNext input 3
    Reactive.onCompleted input

    shouldEqual [ 2; 4; 6 ] tc.Results
    shouldBeTrue tc.Completed

let single_subject_works_with_filter_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.singleSubject ()

    output
    |> Reactive.filter (fun x -> x > 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    Reactive.onNext input 1
    Reactive.onNext input 2
    Reactive.onNext input 3
    Reactive.onNext input 4
    Reactive.onCompleted input

    shouldEqual [ 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// multicast subject tests
// ============================================================================

let subject_forwards_values_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.subject ()
    output |> Reactive.subscribe tc.Handler |> ignore

    Reactive.onNext input 1
    Reactive.onNext input 2
    Reactive.onNext input 3
    Reactive.onCompleted input

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let subject_allows_multiple_subscribers_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()
    let input, output = Reactive.subject ()

    output |> Reactive.subscribe tc1.Handler |> ignore
    output |> Reactive.subscribe tc2.Handler |> ignore

    Reactive.onNext input 42
    Reactive.onCompleted input

    shouldEqual [ 42 ] tc1.Results
    shouldEqual [ 42 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let subject_does_not_buffer_test () =
    let input, output = Reactive.subject ()

    // Send values BEFORE subscribing
    Reactive.onNext input 10
    Reactive.onNext input 20

    sleep 10

    // Now subscribe
    let tc = TestCollector<int>()
    output |> Reactive.subscribe tc.Handler |> ignore

    // Send more after subscribe
    Reactive.onNext input 30
    Reactive.onCompleted input

    // Should only receive values after subscription
    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let subject_dispose_stops_receiving_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.subject ()
    let disp = output |> Reactive.subscribe tc.Handler

    Reactive.onNext input 1
    sleep 10
    disp.Dispose()
    sleep 10

    // These should not be received
    Reactive.onNext input 2
    Reactive.onNext input 3

    shouldEqual [ 1 ] tc.Results

let subject_with_facade_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.subject ()
    output |> Reactive.subscribe tc.Handler |> ignore

    Reactive.onNext input 42
    Reactive.onCompleted input

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
