/// Tests for timeshift operators
///
/// These tests use sleep + mutable collectors for async operators.
/// On BEAM, timer callbacks run in spawned processes, so these tests
/// rely on the Fable.Beam mutable state mechanism working across processes.
module Factor.TimeshiftTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// Timer tests
// ============================================================================

let timer_emits_zero_after_delay_test () =
    let tc = TestCollector<int>()
    let _disp = Reactive.timer 50 |> Reactive.spawn tc.Observer
    sleep 200
    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let timer_completes_after_emission_test () =
    let tc = TestCollector<int>()
    let _disp = Reactive.timer 30 |> Reactive.spawn tc.Observer
    sleep 150
    shouldBeTrue tc.Completed

let timer_disposal_prevents_emission_test () =
    let tc = TestCollector<int>()
    let disp = Reactive.timer 100 |> Reactive.spawn tc.Observer
    sleep 20
    disp.Dispose()
    sleep 200
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed

// ============================================================================
// Interval tests
// ============================================================================

let interval_emits_incrementing_values_test () =
    let tc = TestCollector<int>()
    let disp = Reactive.interval 30 |> Reactive.spawn tc.Observer
    sleep 130
    disp.Dispose()
    sleep 50
    shouldBeTrue (tc.Results.Length >= 3)

    match tc.Results with
    | first :: second :: third :: _ ->
        shouldEqual 0 first
        shouldEqual 1 second
        shouldEqual 2 third
    | _ -> failwith "Expected at least 3 values"

let interval_disposal_stops_emissions_test () =
    let tc = TestCollector<int>()
    let disp = Reactive.interval 30 |> Reactive.spawn tc.Observer
    sleep 80
    disp.Dispose()
    let countAtDisposal = tc.Results.Length
    sleep 100
    // Should not have received more values after disposal
    shouldEqual countAtDisposal tc.Results.Length
    shouldBeTrue (countAtDisposal >= 2)

// ============================================================================
// Delay tests
// ============================================================================

let delay_shifts_emissions_in_time_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.delay 50
    |> Reactive.spawn tc.Observer
    |> ignore

    // Immediately after subscribe, should have no values
    shouldEqual [] tc.Results
    sleep 150
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let delay_preserves_order_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 5; 4; 3; 2; 1 ]
    |> Reactive.delay 30
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150
    shouldEqual [ 5; 4; 3; 2; 1 ] tc.Results

let delay_completes_after_all_emitted_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2 ]
    |> Reactive.delay 50
    |> Reactive.spawn tc.Observer
    |> ignore

    // Immediately after, should not be complete
    shouldBeFalse tc.Completed
    sleep 150
    shouldBeTrue tc.Completed

// ============================================================================
// Debounce tests
// ============================================================================

let debounce_waits_for_silence_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1 ]
    |> Reactive.debounce 50
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150
    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

let debounce_emits_latest_value_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.debounce 50
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150
    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// Throttle tests
// ============================================================================

let throttle_emits_first_immediately_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.throttle 100
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    match tc.Results with
    | first :: _ -> shouldEqual 1 first
    | _ -> failwith "Expected at least one value"

let throttle_completes_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.throttle 30
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150
    shouldBeTrue tc.Completed

// ============================================================================
// Integration with Rx facade
// ============================================================================

let timer_via_facade_test () =
    let tc = TestCollector<int>()
    Reactive.timer 50 |> Reactive.spawn tc.Observer |> ignore
    sleep 150
    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed

let debounce_via_facade_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.debounce 50
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150
    shouldEqual [ 3 ] tc.Results

let throttle_via_facade_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.throttle 50
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150

    match tc.Results with
    | first :: _ -> shouldEqual 1 first
    | _ -> failwith "Expected at least one value"

// ============================================================================
// Timer cancellation on dispose tests
// ============================================================================

let debounce_dispose_cancels_timer_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.channel ()

    let disp =
        output
        |> Reactive.debounce 100
        |> Reactive.spawn tc.Observer

    Reactive.pushNext input 42
    sleep 30
    disp.Dispose()
    sleep 150
    shouldEqual [] tc.Results

let throttle_dispose_cancels_timer_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.channel ()

    let disp =
        output
        |> Reactive.throttle 100
        |> Reactive.spawn tc.Observer

    // Allow throttle process to subscribe to channel
    sleep 50

    // First value emitted immediately, starts window
    Reactive.pushNext input 1
    sleep 50
    shouldEqual [ 1 ] tc.Results

    // Send second value during window
    Reactive.pushNext input 2
    sleep 50
    disp.Dispose()
    sleep 150
    // Should not have received the "latest" value from window end
    shouldEqual [ 1 ] tc.Results

let delay_dispose_cancels_pending_timers_test () =
    let tc = TestCollector<int>()
    let input, output = Reactive.channel ()

    let disp =
        output
        |> Reactive.delay 100
        |> Reactive.spawn tc.Observer

    Reactive.pushNext input 1
    Reactive.pushNext input 2
    Reactive.pushNext input 3
    sleep 30
    disp.Dispose()
    sleep 150
    shouldEqual [] tc.Results
