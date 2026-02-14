/// Tests for filter operators
module Factor.FilterTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// filter tests
// ============================================================================

let filter_keeps_matching_elements_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6 ]
    |> Rx.filter (fun x -> x > 3)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let filter_all_pass_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.filter (fun _ -> true)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let filter_none_pass_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.filter (fun _ -> false)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let filter_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.filter (fun x -> x > 0)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let filter_even_numbers_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Rx.filter (fun x -> x % 2 = 0)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 2; 4; 6; 8; 10 ] tc.Results

let filter_chained_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Rx.filter (fun x -> x > 3)
    |> Rx.filter (fun x -> x < 8)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 4; 5; 6; 7 ] tc.Results

// ============================================================================
// take tests
// ============================================================================

let take_first_n_elements_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.take 3
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_zero_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.take 0
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_more_than_available_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.take 10
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_exact_count_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.take 3
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_from_empty_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.take 5
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_one_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.take 1
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// skip tests
// ============================================================================

let skip_first_n_elements_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.skip 2
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let skip_zero_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.skip 0
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let skip_more_than_available_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.skip 10
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_exact_count_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.skip 3
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_from_empty_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.skip 5
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_all_but_one_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.skip 4
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 5 ] tc.Results

// ============================================================================
// takeWhile tests
// ============================================================================

let take_while_condition_true_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.takeWhile (fun x -> x < 4)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_while_always_true_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.takeWhile (fun _ -> true)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let take_while_always_false_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.takeWhile (fun _ -> false)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_while_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.takeWhile (fun x -> x > 0)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_while_first_fails_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 5; 4; 3; 2; 1 ]
    |> Rx.takeWhile (fun x -> x < 5)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// skipWhile tests
// ============================================================================

let skip_while_condition_true_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.skipWhile (fun x -> x < 3)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let skip_while_always_true_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.skipWhile (fun _ -> true)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_while_always_false_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.skipWhile (fun _ -> false)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let skip_while_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.skipWhile (fun x -> x > 0)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_while_first_fails_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 5; 4; 3; 2; 1 ]
    |> Rx.skipWhile (fun x -> x < 5)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 5; 4; 3; 2; 1 ] tc.Results

// ============================================================================
// distinctUntilChanged tests
// ============================================================================

let distinct_until_changed_removes_consecutive_dupes_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 1; 2; 2; 2; 3; 1; 1 ]
    |> Rx.distinctUntilChanged
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3; 1 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_until_changed_all_different_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.distinctUntilChanged
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results

let distinct_until_changed_all_same_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 2; 2; 2; 2; 2 ]
    |> Rx.distinctUntilChanged
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 2 ] tc.Results

let distinct_until_changed_empty_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.distinctUntilChanged
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let distinct_until_changed_single_value_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.distinctUntilChanged
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 42 ] tc.Results

let distinct_until_changed_alternating_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 1; 2; 1; 2 ]
    |> Rx.distinctUntilChanged
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 1; 2; 1; 2 ] tc.Results

// ============================================================================
// choose tests
// ============================================================================

let choose_filters_and_maps_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.choose (fun x ->
        if x % 2 = 0 then Some(x * 10) else None)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 20; 40 ] tc.Results
    shouldBeTrue tc.Completed

let choose_all_some_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.choose (fun x -> Some(x * 100))
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 100; 200; 300 ] tc.Results

let choose_all_none_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.choose (fun _ -> None)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let choose_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.choose (fun x -> Some x)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// takeLast tests
// ============================================================================

let take_last_returns_last_n_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.takeLast 2
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let take_last_zero_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.takeLast 0
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_last_more_than_available_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.takeLast 10
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_last_exact_count_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.takeLast 3
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_last_from_empty_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.takeLast 5
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_last_one_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.takeLast 1
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 5 ] tc.Results

// ============================================================================
// Combined operator tests
// ============================================================================

let map_and_filter_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.map (fun x -> x * 2)
    |> Rx.filter (fun x -> x > 4)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 6; 8; 10 ] tc.Results
    shouldBeTrue tc.Completed

let filter_map_take_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Rx.filter (fun x -> x % 2 = 0)
    |> Rx.map (fun x -> x * 10)
    |> Rx.take 3
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 20; 40; 60 ] tc.Results
    shouldBeTrue tc.Completed

let skip_then_take_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Rx.skip 3
    |> Rx.take 4
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 4; 5; 6; 7 ] tc.Results

let take_while_then_map_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.takeWhile (fun x -> x < 4)
    |> Rx.map (fun x -> x * 10)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results

let distinct_then_take_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 1; 2; 2; 3; 3; 4; 4; 5; 5 ]
    |> Rx.distinctUntilChanged
    |> Rx.take 3
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
