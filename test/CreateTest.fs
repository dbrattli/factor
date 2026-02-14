/// Tests for creation operators
module Factor.CreateTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// single tests
// ============================================================================

let single_emits_value_and_completes_test () =
    let tc = TestCollector<int>()
    Rx.single 42 |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let single_notifications_in_order_test () =
    let tc = TestCollector<int>()
    Rx.single 42 |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ OnNext 42; OnCompleted ] tc.Notifications

let single_with_zero_test () =
    let tc = TestCollector<int>()
    Rx.single 0 |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed

let single_with_negative_test () =
    let tc = TestCollector<int>()
    Rx.single -42 |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ -42 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// empty tests
// ============================================================================

let empty_completes_immediately_test () =
    let tc = TestCollector<int>()
    Rx.empty () |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let empty_notifications_test () =
    let tc = TestCollector<int>()
    Rx.empty () |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ OnCompleted ] tc.Notifications

// ============================================================================
// never tests
// ============================================================================

let never_does_not_emit_or_complete_test () =
    let tc = TestCollector<int>()
    Rx.never () |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [] tc.Errors

let never_notifications_test () =
    let tc = TestCollector<int>()
    Rx.never () |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Notifications

// ============================================================================
// fail tests
// ============================================================================

let fail_emits_error_test () =
    let tc = TestCollector<int>()
    Rx.fail "test error" |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "test error" ] tc.Errors

let fail_notifications_test () =
    let tc = TestCollector<int>()
    Rx.fail "error message" |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ OnError "error message" ] tc.Notifications

let fail_with_empty_message_test () =
    let tc = TestCollector<int>()
    Rx.fail "" |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ "" ] tc.Errors

// ============================================================================
// ofList tests
// ============================================================================

let from_list_emits_all_items_test () =
    let tc = TestCollector<int>()
    Rx.ofList [ 1; 2; 3; 4; 5 ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let from_list_empty_completes_test () =
    let tc = TestCollector<int>()
    Rx.ofList [] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let from_list_single_item_test () =
    let tc = TestCollector<int>()
    Rx.ofList [ 42 ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let from_list_notifications_test () =
    let tc = TestCollector<int>()
    Rx.ofList [ 1; 2; 3 ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ OnNext 1; OnNext 2; OnNext 3; OnCompleted ] tc.Notifications

let from_list_preserves_order_test () =
    let tc = TestCollector<int>()
    Rx.ofList [ 5; 4; 3; 2; 1 ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 5; 4; 3; 2; 1 ] tc.Results

// ============================================================================
// defer tests
// ============================================================================

let defer_creates_new_observable_per_subscribe_test () =
    let mutable callCount = 0

    let observable =
        Rx.defer (fun () ->
            callCount <- callCount + 1
            Rx.single callCount)

    let tc1 = TestCollector<int>()
    observable |> Rx.subscribe tc1.Observer |> ignore

    let tc2 = TestCollector<int>()
    observable |> Rx.subscribe tc2.Observer |> ignore

    shouldEqual 2 callCount
    shouldEqual [ 1 ] tc1.Results
    shouldEqual [ 2 ] tc2.Results

let defer_is_lazy_test () =
    let mutable wasCalled = false

    let _observable =
        Rx.defer (fun () ->
            wasCalled <- true
            Rx.single 42)

    shouldBeFalse wasCalled

let defer_with_from_list_test () =
    let tc = TestCollector<int>()

    Rx.defer (fun () -> Rx.ofList [ 10; 20; 30 ])
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed
