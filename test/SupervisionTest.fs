/// Tests for supervision policies in mergeInnerSpawned
module Factor.SupervisionTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// Helper: a factor that crashes during subscribe
let crashFactor<'T> : Factor<'T> =
    { Subscribe = fun _ -> failwith "crash!" }

// Helper: a factor that emits a value then crashes
let emitThenCrash (value: 'T) : Factor<'T> =
    { Subscribe =
        fun handler ->
            handler.Notify(OnNext value)
            failwith "crash after emit" }

// ============================================================================
// Terminate policy tests (default behavior)
// ============================================================================

let terminate_produces_error_on_crash_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ crashFactor ]
    |> Transform.mergeInnerSpawned Terminate
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldBeFalse tc.Completed
    shouldBeTrue (tc.Errors.Length > 0)

let terminate_stops_pipeline_on_crash_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ crashFactor; Reactive.single 42 ]
    |> Transform.mergeInnerSpawned Terminate
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    // Pipeline should have errored, not completed
    shouldBeFalse tc.Completed
    shouldBeTrue (tc.Errors.Length > 0)

// ============================================================================
// Skip policy tests
// ============================================================================

let skip_continues_after_crash_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ crashFactor; Reactive.ofList [ 1; 2; 3 ] ]
    |> Transform.mergeInnerSpawned Skip
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let skip_completes_when_all_done_test () =
    let tc = TestCollector<int>()

    // Only inner is a crash — should complete with no results
    Reactive.ofList [ crashFactor ]
    |> Transform.mergeInnerSpawned Skip
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let skip_multiple_crashes_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ crashFactor; crashFactor; Reactive.single 42; crashFactor ]
    |> Transform.mergeInnerSpawned Skip
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let skip_all_crash_completes_empty_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ crashFactor; crashFactor; crashFactor ]
    |> Transform.mergeInnerSpawned Skip
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

// ============================================================================
// Restart policy tests
// ============================================================================

let restart_exhausted_produces_error_test () =
    let tc = TestCollector<int>()

    // Always crashes — Restart(2) means 1 initial + 2 retries = 3 attempts
    Reactive.ofList [ crashFactor ]
    |> Transform.mergeInnerSpawned (Restart 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 200
    shouldBeFalse tc.Completed
    shouldBeTrue (tc.Errors.Length > 0)

let restart_zero_retries_same_as_terminate_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ crashFactor ]
    |> Transform.mergeInnerSpawned (Restart 0)
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldBeFalse tc.Completed
    shouldBeTrue (tc.Errors.Length > 0)

let restart_working_factor_completes_normally_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2; 3 ] ]
    |> Transform.mergeInnerSpawned (Restart 3)
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

// ============================================================================
// Policy with flatMapSpawned (default Terminate)
// ============================================================================

let flatmap_spawned_default_terminate_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Transform.flatMapSpawned (fun x -> Reactive.single (x * 10))
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 100
    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed
