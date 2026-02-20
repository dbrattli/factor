/// Tests for error handling operators (retry, catch)
module Factor.ErrorTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// retry tests
// ============================================================================

let retry_no_error_completes_normally_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.retry 3
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let retry_max_retries_then_error_test () =
    let tc = TestCollector<int>()

    Rx.fail "Always fails"
    |> Rx.retry 2
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "Always fails" ] tc.Errors

let retry_zero_retries_propagates_immediately_test () =
    let tc = TestCollector<int>()

    Rx.fail "Immediate fail"
    |> Rx.retry 0
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "Immediate fail" ] tc.Errors

let retry_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.retry 3
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let retry_single_value_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.retry 3
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let retry_partial_then_error_resubscribes_test () =
    let tc = TestCollector<int>()
    let mutable subscriptionCount = 0

    let observable =
        Rx.defer (fun () ->
            subscriptionCount <- subscriptionCount + 1
            let count = subscriptionCount

            Rx.create (fun observer ->
                Rx.onNext observer 1
                Rx.onNext observer 2

                if count = 1 then
                    Rx.onError observer "First try fails"
                else
                    Rx.onCompleted observer

                Rx.emptyHandle ()))
        |> Rx.retry 2

    observable |> Rx.subscribe tc.Handler |> ignore

    // First attempt: 1, 2, error -> retry
    // Second attempt: 1, 2, complete
    shouldEqual [ 1; 2; 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// catch tests
// ============================================================================

let catch_no_error_passes_through_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.catch (fun _ -> Rx.single 99)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_error_switches_to_fallback_test () =
    let tc = TestCollector<int>()

    Rx.fail "Oops"
    |> Rx.catch (fun _ -> Rx.single 42)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_error_with_fallback_list_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error"
    |> Rx.catch (fun _ -> Rx.ofList [ 10; 20; 30 ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_partial_emission_then_error_test () =
    let tc = TestCollector<int>()

    Rx.create (fun observer ->
        Rx.onNext observer 1
        Rx.onNext observer 2
        Rx.onError observer "Midway error"
        Rx.emptyHandle ())
    |> Rx.catch (fun _ -> Rx.ofList [ 100; 200 ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 100; 200 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_handler_receives_error_message_test () =
    let tc = TestCollector<string>()

    Rx.fail "Custom error"
    |> Rx.catch (fun err -> Rx.single ("Caught: " + err))
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ "Caught: Custom error" ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_fallback_empty_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error"
    |> Rx.catch (fun _ -> Rx.empty ())
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_fallback_also_errors_propagates_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error 1"
    |> Rx.catch (fun _ -> Rx.fail "Error 2")
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "Error 2" ] tc.Errors

let catch_chained_catches_both_errors_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error 1"
    |> Rx.catch (fun _ -> Rx.fail "Error 2")
    |> Rx.catch (fun _ -> Rx.single 999)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 999 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_chained_first_succeeds_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error 1"
    |> Rx.catch (fun _ -> Rx.ofList [ 1; 2; 3 ])
    |> Rx.catch (fun _ -> Rx.single 999)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_preserves_notification_sequence_test () =
    let tc = TestCollector<int>()

    Rx.create (fun observer ->
        Rx.onNext observer 1
        Rx.onError observer "Error"
        Rx.emptyHandle ())
    |> Rx.catch (fun _ ->
        Rx.create (fun observer ->
            Rx.onNext observer 2
            Rx.onCompleted observer
            Rx.emptyHandle ()))
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ OnNext 1; OnNext 2; OnCompleted ] tc.Notifications

// ============================================================================
// Combined retry + catch tests
// ============================================================================

let retry_then_catch_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error"
    |> Rx.retry 2
    |> Rx.catch (fun _ -> Rx.single 0)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let catch_then_retry_test () =
    let tc = TestCollector<int>()

    Rx.fail "Error"
    |> Rx.catch (fun _ -> Rx.single 42)
    |> Rx.retry 2
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors
