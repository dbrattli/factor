/// Tests for mergeInner, concatInner, and related operators
module Factor.MergeInnerTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// mergeInner tests
// ============================================================================

let merge_inner_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.ofList [ 1; 2 ]; Rx.ofList [ 3; 4 ]; Rx.ofList [ 5; 6 ] ]
    |> Rx.mergeInner None
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldHaveLength 6 tc.Results
    shouldEqual [ 1; 2; 3; 4; 5; 6 ] (List.sort tc.Results)
    shouldBeTrue tc.Completed

let merge_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Observable<Observable<int>> = Rx.empty ()
    emptyOuter |> Rx.mergeInner None |> Rx.subscribe tc.Observer |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let merge_inner_empty_inners_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.empty (); Rx.empty (); Rx.empty () ]
    |> Rx.mergeInner None
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let merge_inner_single_inner_test () =
    let tc = TestCollector<int>()

    Rx.single (Rx.ofList [ 1; 2; 3 ])
    |> Rx.mergeInner None
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let merge_inner_error_propagates_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.ofList [ 1; 2 ]; Rx.fail "inner error"; Rx.ofList [ 3; 4 ] ]
    |> Rx.mergeInner None
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

// ============================================================================
// concatInner tests
// ============================================================================

let concat_inner_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.ofList [ 1; 2 ]; Rx.ofList [ 3; 4 ]; Rx.ofList [ 5; 6 ] ]
    |> Rx.concatInner
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Observable<Observable<int>> = Rx.empty ()
    emptyOuter |> Rx.concatInner |> Rx.subscribe tc.Observer |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_empty_inners_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.empty (); Rx.empty (); Rx.empty () ]
    |> Rx.concatInner
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_preserves_order_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.ofList [ 1 ]; Rx.ofList [ 2; 3; 4 ]; Rx.ofList [ 5; 6 ] ]
    |> Rx.concatInner
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_error_stops_processing_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.ofList [ 1; 2 ]; Rx.fail "inner error"; Rx.ofList [ 3; 4 ] ]
    |> Rx.concatInner
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2 ] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "inner error" ] tc.Errors

// ============================================================================
// flatMap composition verification
// ============================================================================

let flat_map_is_map_plus_merge_inner_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Rx.ofList [ 1; 2; 3 ]
    let mapper = fun x -> Rx.ofList [ x; x * 10 ]

    source |> Rx.flatMap mapper |> Rx.subscribe tc1.Observer |> ignore

    source
    |> Rx.map mapper
    |> Rx.mergeInner None
    |> Rx.subscribe tc2.Observer
    |> ignore

    shouldEqual (List.sort tc1.Results) (List.sort tc2.Results)
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let concat_map_is_map_plus_concat_inner_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Rx.ofList [ 1; 2; 3 ]
    let mapper = fun x -> Rx.ofList [ x; x * 10 ]

    source |> Rx.concatMap mapper |> Rx.subscribe tc1.Observer |> ignore

    source
    |> Rx.map mapper
    |> Rx.concatInner
    |> Rx.subscribe tc2.Observer
    |> ignore

    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc1.Results
    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let concat_map_vs_flat_map_order_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Rx.ofList [ 1; 2; 3 ]
    let mapper = fun x -> Rx.ofList [ x; x * 10 ]

    source |> Rx.flatMap mapper |> Rx.subscribe tc1.Observer |> ignore
    source |> Rx.concatMap mapper |> Rx.subscribe tc2.Observer |> ignore

    // concatMap always has strict order
    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc2.Results
    // flatMap has same elements but order may differ
    shouldHaveLength 6 tc1.Results

// ============================================================================
// mapi tests
// ============================================================================

let mapi_basic_test () =
    let tc = TestCollector<int * string>()

    Rx.ofList [ "a"; "b"; "c" ]
    |> Rx.mapi (fun x i -> (i, x))
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ (0, "a"); (1, "b"); (2, "c") ] tc.Results
    shouldBeTrue tc.Completed

let mapi_empty_test () =
    let tc = TestCollector<int * int>()

    Rx.empty ()
    |> Rx.mapi (fun (x: int) i -> (i, x))
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let mapi_index_starts_at_zero_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 100; 200; 300 ]
    |> Rx.mapi (fun _ i -> i)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 0; 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// max concurrency tests
// ============================================================================

let merge_inner_max_concurrency_one_equals_concat_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source =
        Rx.ofList [ Rx.ofList [ 1; 2 ]; Rx.ofList [ 3; 4 ]; Rx.ofList [ 5; 6 ] ]

    source |> Rx.mergeInner (Some 1) |> Rx.subscribe tc1.Observer |> ignore
    source |> Rx.concatInner |> Rx.subscribe tc2.Observer |> ignore

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc1.Results
    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let merge_inner_max_concurrency_two_test () =
    let tc = TestCollector<int>()

    Rx.ofList
        [ Rx.ofList [ 1; 2 ]
          Rx.ofList [ 3; 4 ]
          Rx.ofList [ 5; 6 ]
          Rx.ofList [ 7; 8 ] ]
    |> Rx.mergeInner (Some 2)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldHaveLength 8 tc.Results
    shouldEqual [ 1; 2; 3; 4; 5; 6; 7; 8 ] (List.sort tc.Results)
    shouldBeTrue tc.Completed

let merge_inner_max_concurrency_empty_inners_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.empty (); Rx.empty (); Rx.empty () ]
    |> Rx.mergeInner (Some 2)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
