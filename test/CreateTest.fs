/// Tests for creation operators
module Factor.CreateTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// single tests
// ============================================================================

let single_emits_value_and_completes_test () =
    let tc = TestCollector<int>()
    Reactive.single 42 |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let single_notifications_in_order_test () =
    let tc = TestCollector<int>()
    Reactive.single 42 |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ OnNext 42; OnCompleted ] tc.Notifications

let single_with_zero_test () =
    let tc = TestCollector<int>()
    Reactive.single 0 |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed

let single_with_negative_test () =
    let tc = TestCollector<int>()
    Reactive.single -42 |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ -42 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// empty tests
// ============================================================================

let empty_completes_immediately_test () =
    let tc = TestCollector<int>()
    Reactive.empty () |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let empty_notifications_test () =
    let tc = TestCollector<int>()
    Reactive.empty () |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ OnCompleted ] tc.Notifications

// ============================================================================
// never tests
// ============================================================================

let never_does_not_emit_or_complete_test () =
    let tc = TestCollector<int>()
    Reactive.never () |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [] tc.Errors

let never_notifications_test () =
    let tc = TestCollector<int>()
    Reactive.never () |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Notifications

// ============================================================================
// fail tests
// ============================================================================

let fail_emits_error_test () =
    let tc = TestCollector<int>()
    Reactive.fail (FactorException "test error") |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "test error" ] tc.Errors

let fail_notifications_test () =
    let tc = TestCollector<int>()
    Reactive.fail (FactorException "error message") |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ OnError(FactorException "error message") ] tc.Notifications

let fail_with_empty_message_test () =
    let tc = TestCollector<int>()
    Reactive.fail (FactorException "") |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ FactorException "" ] tc.Errors

// ============================================================================
// ofList tests
// ============================================================================

let from_list_emits_all_items_test () =
    let tc = TestCollector<int>()
    Reactive.ofList [ 1; 2; 3; 4; 5 ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let from_list_empty_completes_test () =
    let tc = TestCollector<int>()
    Reactive.ofList [] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let from_list_single_item_test () =
    let tc = TestCollector<int>()
    Reactive.ofList [ 42 ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let from_list_notifications_test () =
    let tc = TestCollector<int>()
    Reactive.ofList [ 1; 2; 3 ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ OnNext 1; OnNext 2; OnNext 3; OnCompleted ] tc.Notifications

let from_list_preserves_order_test () =
    let tc = TestCollector<int>()
    Reactive.ofList [ 5; 4; 3; 2; 1 ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 5; 4; 3; 2; 1 ] tc.Results

// ============================================================================
// defer tests
// ============================================================================

let defer_creates_new_observable_per_subscribe_test () =
    let mutable callCount = 0

    let observable =
        Reactive.defer (fun () ->
            callCount <- callCount + 1
            Reactive.single callCount)

    let tc1 = TestCollector<int>()
    observable |> Reactive.subscribe tc1.Handler |> ignore

    let tc2 = TestCollector<int>()
    observable |> Reactive.subscribe tc2.Handler |> ignore

    shouldEqual 2 callCount
    shouldEqual [ 1 ] tc1.Results
    shouldEqual [ 2 ] tc2.Results

let defer_is_lazy_test () =
    let mutable wasCalled = false

    let _observable =
        Reactive.defer (fun () ->
            wasCalled <- true
            Reactive.single 42)

    shouldBeFalse wasCalled

let defer_with_from_list_test () =
    let tc = TestCollector<int>()

    Reactive.defer (fun () -> Reactive.ofList [ 10; 20; 30 ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed
