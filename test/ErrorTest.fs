/// Tests for error handling operators (retry, catch)
module Factor.ErrorTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// retry tests
// ============================================================================

let retry_no_error_completes_normally_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.retry 3
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let retry_max_retries_then_error_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Always fails")
    |> Reactive.retry 2
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "Always fails" ] tc.Errors

let retry_zero_retries_propagates_immediately_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Immediate fail")
    |> Reactive.retry 0
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "Immediate fail" ] tc.Errors

let retry_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.retry 3
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let retry_single_value_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.retry 3
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let retry_partial_then_error_respawns_test () =
    let tc = TestCollector<int>()

    // Source that always emits 1, 2 then errors.
    // With retry(2): initial + 2 retries = 3 attempts, each emitting [1, 2].
    // Note: cannot use cross-process mutable to track subscription count
    // because operators run in spawned processes (separate process dictionaries).
    let failingSource =
        Reactive.create (fun observer ->
            Process.onNext observer 1
            Process.onNext observer 2
            Process.onError observer (FactorException "Always fails")
            Reactive.emptyHandle ())

    failingSource
    |> Reactive.retry 2
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    // 1 initial + 2 retries = 3 attempts, each emitting [1, 2]
    shouldEqual [ 1; 2; 1; 2; 1; 2 ] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "Always fails" ] tc.Errors

// ============================================================================
// catch tests
// ============================================================================

let catch_no_error_passes_through_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.catch (fun _ -> Reactive.single 99)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_error_switches_to_fallback_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Oops")
    |> Reactive.catch (fun _ -> Reactive.single 42)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_error_with_fallback_list_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error")
    |> Reactive.catch (fun _ -> Reactive.ofList [ 10; 20; 30 ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_partial_emission_then_error_test () =
    let tc = TestCollector<int>()

    Reactive.create (fun observer ->
        Process.onNext observer 1
        Process.onNext observer 2
        Process.onError observer (FactorException "Midway error")
        Reactive.emptyHandle ())
    |> Reactive.catch (fun _ -> Reactive.ofList [ 100; 200 ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 100; 200 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_handler_receives_error_message_test () =
    let tc = TestCollector<string>()

    Reactive.fail (FactorException "Custom error")
    |> Reactive.catch (fun err -> Reactive.single (sprintf "Caught: %A" err))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ sprintf "Caught: %A" (FactorException "Custom error") ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_fallback_empty_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error")
    |> Reactive.catch (fun _ -> Reactive.empty ())
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_fallback_also_errors_propagates_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error 1")
    |> Reactive.catch (fun _ -> Reactive.fail (FactorException "Error 2"))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "Error 2" ] tc.Errors

let catch_chained_catches_both_errors_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error 1")
    |> Reactive.catch (fun _ -> Reactive.fail (FactorException "Error 2"))
    |> Reactive.catch (fun _ -> Reactive.single 999)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 999 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_chained_first_succeeds_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error 1")
    |> Reactive.catch (fun _ -> Reactive.ofList [ 1; 2; 3 ])
    |> Reactive.catch (fun _ -> Reactive.single 999)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_preserves_notification_sequence_test () =
    let tc = TestCollector<int>()

    Reactive.create (fun observer ->
        Process.onNext observer 1
        Process.onError observer (FactorException "Error")
        Reactive.emptyHandle ())
    |> Reactive.catch (fun _ ->
        Reactive.create (fun observer ->
            Process.onNext observer 2
            Process.onCompleted observer
            Reactive.emptyHandle ()))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ OnNext 1; OnNext 2; OnCompleted ] tc.Msgs

// ============================================================================
// Combined retry + catch tests
// ============================================================================

let retry_then_catch_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error")
    |> Reactive.retry 2
    |> Reactive.catch (fun _ -> Reactive.single 0)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_then_retry_test () =
    let tc = TestCollector<int>()

    Reactive.fail (FactorException "Error")
    |> Reactive.catch (fun _ -> Reactive.single 42)
    |> Reactive.retry 2
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors
