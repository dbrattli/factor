/// Tests for filter operators
module Factor.FilterTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// filter tests
// ============================================================================

let filter_keeps_matching_elements_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6 ]
    |> Reactive.filter (fun x -> x > 3)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let filter_all_pass_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.filter (fun _ -> true)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let filter_none_pass_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.filter (fun _ -> false)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let filter_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.filter (fun x -> x > 0)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let filter_even_numbers_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Reactive.filter (fun x -> x % 2 = 0)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 2; 4; 6; 8; 10 ] tc.Results

let filter_chained_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Reactive.filter (fun x -> x > 3)
    |> Reactive.filter (fun x -> x < 8)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 4; 5; 6; 7 ] tc.Results

// ============================================================================
// take tests
// ============================================================================

let take_first_n_elements_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.take 3
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_zero_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.take 0
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_more_than_available_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.take 10
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_exact_count_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.take 3
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_from_empty_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.take 5
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_one_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.take 1
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// skip tests
// ============================================================================

let skip_first_n_elements_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.skip 2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let skip_zero_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.skip 0
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let skip_more_than_available_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.skip 10
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_exact_count_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.skip 3
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_from_empty_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.skip 5
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_all_but_one_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.skip 4
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 5 ] tc.Results

// ============================================================================
// takeWhile tests
// ============================================================================

let take_while_condition_true_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.takeWhile (fun x -> x < 4)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_while_always_true_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.takeWhile (fun _ -> true)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let take_while_always_false_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.takeWhile (fun _ -> false)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_while_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.takeWhile (fun x -> x > 0)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_while_first_fails_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 5; 4; 3; 2; 1 ]
    |> Reactive.takeWhile (fun x -> x < 5)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// skipWhile tests
// ============================================================================

let skip_while_condition_true_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.skipWhile (fun x -> x < 3)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let skip_while_always_true_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.skipWhile (fun _ -> true)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_while_always_false_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.skipWhile (fun _ -> false)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let skip_while_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.skipWhile (fun x -> x > 0)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let skip_while_first_fails_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 5; 4; 3; 2; 1 ]
    |> Reactive.skipWhile (fun x -> x < 5)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 5; 4; 3; 2; 1 ] tc.Results

// ============================================================================
// distinctUntilChanged tests
// ============================================================================

let distinct_until_changed_removes_consecutive_dupes_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 1; 2; 2; 2; 3; 1; 1 ]
    |> Reactive.distinctUntilChanged
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 1 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_until_changed_all_different_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.distinctUntilChanged
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results

let distinct_until_changed_all_same_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 2; 2; 2; 2; 2 ]
    |> Reactive.distinctUntilChanged
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 2 ] tc.Results

let distinct_until_changed_empty_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.distinctUntilChanged
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let distinct_until_changed_single_value_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.distinctUntilChanged
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results

let distinct_until_changed_alternating_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 1; 2; 1; 2 ]
    |> Reactive.distinctUntilChanged
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 1; 2; 1; 2 ] tc.Results

// ============================================================================
// choose tests
// ============================================================================

let choose_filters_and_maps_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.choose (fun x ->
        if x % 2 = 0 then Some(x * 10) else None)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 20; 40 ] tc.Results
    shouldBeTrue tc.Completed

let choose_all_some_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.choose (fun x -> Some(x * 100))
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 100; 200; 300 ] tc.Results

let choose_all_none_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.choose (fun _ -> None)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let choose_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.choose (fun x -> Some x)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// takeLast tests
// ============================================================================

let take_last_returns_last_n_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.takeLast 2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let take_last_zero_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.takeLast 0
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_last_more_than_available_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.takeLast 10
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_last_exact_count_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.takeLast 3
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let take_last_from_empty_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.takeLast 5
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let take_last_one_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.takeLast 1
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 5 ] tc.Results

// ============================================================================
// Combined operator tests
// ============================================================================

let map_and_filter_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.map (fun x -> x * 2)
    |> Reactive.filter (fun x -> x > 4)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 6; 8; 10 ] tc.Results
    shouldBeTrue tc.Completed

let filter_map_take_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Reactive.filter (fun x -> x % 2 = 0)
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.take 3
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 20; 40; 60 ] tc.Results
    shouldBeTrue tc.Completed

let skip_then_take_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8; 9; 10 ]
    |> Reactive.skip 3
    |> Reactive.take 4
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 4; 5; 6; 7 ] tc.Results

let take_while_then_map_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.takeWhile (fun x -> x < 4)
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results

let distinct_then_take_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 1; 2; 2; 3; 3; 4; 4; 5; 5 ]
    |> Reactive.distinctUntilChanged
    |> Reactive.take 3
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
