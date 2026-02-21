/// Tests for mergeInner, concatInner, and related operators
module Factor.MergeInnerTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// mergeInner tests
// ============================================================================

let merge_inner_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2 ]; Reactive.ofList [ 3; 4 ]; Reactive.ofList [ 5; 6 ] ]
    |> Reactive.mergeInner None
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldHaveLength 6 tc.Results
    shouldEqual [ 1; 2; 3; 4; 5; 6 ] (List.sort tc.Results)
    shouldBeTrue tc.Completed

let merge_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Factor<Factor<int>> = Reactive.empty ()
    emptyOuter |> Reactive.mergeInner None |> Reactive.subscribe tc.Handler |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let merge_inner_empty_inners_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.empty (); Reactive.empty (); Reactive.empty () ]
    |> Reactive.mergeInner None
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let merge_inner_single_inner_test () =
    let tc = TestCollector<int>()

    Reactive.single (Reactive.ofList [ 1; 2; 3 ])
    |> Reactive.mergeInner None
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let merge_inner_error_propagates_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2 ]; Reactive.fail (FactorException "inner error"); Reactive.ofList [ 3; 4 ] ]
    |> Reactive.mergeInner None
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

// ============================================================================
// concatInner tests
// ============================================================================

let concat_inner_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2 ]; Reactive.ofList [ 3; 4 ]; Reactive.ofList [ 5; 6 ] ]
    |> Reactive.concatInner
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Factor<Factor<int>> = Reactive.empty ()
    emptyOuter |> Reactive.concatInner |> Reactive.subscribe tc.Handler |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_empty_inners_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.empty (); Reactive.empty (); Reactive.empty () ]
    |> Reactive.concatInner
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_preserves_order_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1 ]; Reactive.ofList [ 2; 3; 4 ]; Reactive.ofList [ 5; 6 ] ]
    |> Reactive.concatInner
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let concat_inner_error_stops_processing_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2 ]; Reactive.fail (FactorException "inner error"); Reactive.ofList [ 3; 4 ] ]
    |> Reactive.concatInner
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [ 1; 2 ] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "inner error" ] tc.Errors

// ============================================================================
// flatMap composition verification
// ============================================================================

let flat_map_is_map_plus_merge_inner_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Reactive.ofList [ 1; 2; 3 ]
    let mapper = fun x -> Reactive.ofList [ x; x * 10 ]

    source |> Reactive.flatMap mapper |> Reactive.subscribe tc1.Handler |> ignore
    sleep 50

    source
    |> Reactive.map mapper
    |> Reactive.mergeInner None
    |> Reactive.subscribe tc2.Handler
    |> ignore

    sleep 50

    shouldEqual (List.sort tc1.Results) (List.sort tc2.Results)
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let concat_map_is_map_plus_concat_inner_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Reactive.ofList [ 1; 2; 3 ]
    let mapper = fun x -> Reactive.ofList [ x; x * 10 ]

    source |> Reactive.concatMap mapper |> Reactive.subscribe tc1.Handler |> ignore
    sleep 50

    source
    |> Reactive.map mapper
    |> Reactive.concatInner
    |> Reactive.subscribe tc2.Handler
    |> ignore

    sleep 50

    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc1.Results
    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let concat_map_vs_flat_map_order_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Reactive.ofList [ 1; 2; 3 ]
    let mapper = fun x -> Reactive.ofList [ x; x * 10 ]

    source |> Reactive.flatMap mapper |> Reactive.subscribe tc1.Handler |> ignore
    sleep 50
    source |> Reactive.concatMap mapper |> Reactive.subscribe tc2.Handler |> ignore
    sleep 50

    // concatMap always has strict order
    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc2.Results
    // flatMap has same elements but order may differ
    shouldHaveLength 6 tc1.Results

// ============================================================================
// mapi tests
// ============================================================================

let mapi_basic_test () =
    let tc = TestCollector<int * string>()

    Reactive.ofList [ "a"; "b"; "c" ]
    |> Reactive.mapi (fun x i -> (i, x))
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ (0, "a"); (1, "b"); (2, "c") ] tc.Results
    shouldBeTrue tc.Completed

let mapi_empty_test () =
    let tc = TestCollector<int * int>()

    Reactive.empty ()
    |> Reactive.mapi (fun (x: int) i -> (i, x))
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let mapi_index_starts_at_zero_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 100; 200; 300 ]
    |> Reactive.mapi (fun _ i -> i)
    |> Reactive.subscribe tc.Handler
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
        Reactive.ofList [ Reactive.ofList [ 1; 2 ]; Reactive.ofList [ 3; 4 ]; Reactive.ofList [ 5; 6 ] ]

    source |> Reactive.mergeInner (Some 1) |> Reactive.subscribe tc1.Handler |> ignore
    sleep 50
    source |> Reactive.concatInner |> Reactive.subscribe tc2.Handler |> ignore
    sleep 50

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc1.Results
    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc2.Results
    shouldBeTrue tc1.Completed
    shouldBeTrue tc2.Completed

let merge_inner_max_concurrency_two_test () =
    let tc = TestCollector<int>()

    Reactive.ofList
        [ Reactive.ofList [ 1; 2 ]
          Reactive.ofList [ 3; 4 ]
          Reactive.ofList [ 5; 6 ]
          Reactive.ofList [ 7; 8 ] ]
    |> Reactive.mergeInner (Some 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldHaveLength 8 tc.Results
    shouldEqual [ 1; 2; 3; 4; 5; 6; 7; 8 ] (List.sort tc.Results)
    shouldBeTrue tc.Completed

let merge_inner_max_concurrency_empty_inners_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.empty (); Reactive.empty (); Reactive.empty () ]
    |> Reactive.mergeInner (Some 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
