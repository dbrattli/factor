/// Tests for new operators: switchMap, tap, startWith, first, last, etc.
module Factor.NewOperatorsTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// switchInner / switchMap tests
// ============================================================================

let switch_inner_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ Rx.ofList [ 1; 2; 3 ]; Rx.ofList [ 4; 5; 6 ] ]
    |> Rx.switchInner
    |> Rx.subscribe tc.Handler
    |> ignore

    // With sync sources, each inner completes before next arrives
    shouldBeTrue (tc.Results.Length >= 3)
    shouldBeTrue tc.Completed

let switch_map_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.switchMap (fun x -> Rx.ofList [ x; x * 10 ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldBeTrue (tc.Results.Length >= 2)
    shouldBeTrue tc.Completed

let switch_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Factor<Factor<int, string>, string> = Rx.empty ()
    emptyOuter |> Rx.switchInner |> Rx.subscribe tc.Handler |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let switch_map_async_cancels_previous_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.switchMap (fun x ->
        Rx.timer (x * 30) |> Rx.map (fun _ -> x))
    |> Rx.subscribe tc.Handler
    |> ignore

    sleep 200
    // Only the last one should complete (others cancelled)
    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// tap tests
// ============================================================================

let tap_basic_test () =
    let tc = TestCollector<int>()
    let mutable sideEffects: int list = []

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.tap (fun x -> sideEffects <- sideEffects @ [ x ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [ 1; 2; 3 ] sideEffects

let tap_does_not_modify_values_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 10; 20; 30 ]
    |> Rx.tap (fun _ -> ())
    |> Rx.map (fun x -> x * 2)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 20; 40; 60 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// startWith tests
// ============================================================================

let start_with_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 3; 4; 5 ]
    |> Rx.startWith [ 1; 2 ]
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let start_with_empty_prefix_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.startWith []
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let start_with_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.startWith [ 1; 2 ]
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// pairwise tests
// ============================================================================

let pairwise_basic_test () =
    let tc = TestCollector<int * int>()

    Rx.ofList [ 1; 2; 3; 4 ]
    |> Rx.pairwise
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ (1, 2); (2, 3); (3, 4) ] tc.Results
    shouldBeTrue tc.Completed

let pairwise_single_element_test () =
    let tc = TestCollector<int * int>()

    Rx.single 1
    |> Rx.pairwise
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let pairwise_empty_test () =
    let tc = TestCollector<int * int>()

    Rx.empty ()
    |> Rx.pairwise
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// first tests
// ============================================================================

let first_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.first
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

let first_single_element_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.first
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let first_empty_errors_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.first
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

// ============================================================================
// last tests
// ============================================================================

let last_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.last
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

let last_single_element_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.last
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let last_empty_errors_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.last
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

// ============================================================================
// defaultIfEmpty tests
// ============================================================================

let default_if_empty_with_values_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.defaultIfEmpty 99
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let default_if_empty_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.defaultIfEmpty 42
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// sample tests
// ============================================================================

let sample_basic_test () =
    let tc = TestCollector<int>()

    Rx.interval 20
    |> Rx.take 10
    |> Rx.sample (Rx.interval 50 |> Rx.take 3)
    |> Rx.subscribe tc.Handler
    |> ignore

    sleep 300
    shouldNotBeEmpty tc.Results
    shouldBeTrue (tc.Results.Length <= 3)
    shouldBeTrue tc.Completed

// ============================================================================
// concat tests
// ============================================================================

let concat_basic_test () =
    let tc = TestCollector<int>()

    Rx.concat [ Rx.ofList [ 1; 2 ]; Rx.ofList [ 3; 4 ]; Rx.ofList [ 5; 6 ] ]
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let concat2_test () =
    let tc = TestCollector<int>()

    Rx.concat2 (Rx.ofList [ 1; 2 ]) (Rx.ofList [ 3; 4 ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

let concat_empty_list_test () =
    let tc = TestCollector<int>()
    Rx.concat [] |> Rx.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_with_empty_sources_test () =
    let tc = TestCollector<int>()

    Rx.concat [ Rx.empty (); Rx.ofList [ 1; 2 ]; Rx.empty (); Rx.ofList [ 3 ] ]
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let concat_sequential_async_test () =
    let tc = TestCollector<int>()

    Rx.concat
        [ Rx.timer 50 |> Rx.map (fun _ -> 1)
          Rx.timer 10 |> Rx.map (fun _ -> 2) ]
    |> Rx.subscribe tc.Handler
    |> ignore

    sleep 200
    // Despite second being faster, order is preserved
    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed
