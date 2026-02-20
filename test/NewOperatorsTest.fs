/// Tests for new operators: switchMap, tap, startWith, first, last, etc.
module Factor.NewOperatorsTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// switchInner / switchMap tests
// ============================================================================

let switch_inner_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2; 3 ]; Reactive.ofList [ 4; 5; 6 ] ]
    |> Reactive.switchInner
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    // With sync sources, each inner completes before next arrives
    shouldBeTrue (tc.Results.Length >= 3)
    shouldBeTrue tc.Completed

let switch_map_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.switchMap (fun x -> Reactive.ofList [ x; x * 10 ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 50

    shouldBeTrue (tc.Results.Length >= 2)
    shouldBeTrue tc.Completed

let switch_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Factor<Factor<int>> = Reactive.empty ()
    emptyOuter |> Reactive.switchInner |> Reactive.subscribe tc.Handler |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let switch_map_async_cancels_previous_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.switchMap (fun x ->
        Reactive.timer (x * 30) |> Reactive.map (fun _ -> x))
    |> Reactive.subscribe tc.Handler
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

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.tap (fun x -> sideEffects <- sideEffects @ [ x ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [ 1; 2; 3 ] sideEffects

let tap_does_not_modify_values_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 10; 20; 30 ]
    |> Reactive.tap (fun _ -> ())
    |> Reactive.map (fun x -> x * 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 20; 40; 60 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// startWith tests
// ============================================================================

let start_with_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 3; 4; 5 ]
    |> Reactive.startWith [ 1; 2 ]
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let start_with_empty_prefix_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.startWith []
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let start_with_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.startWith [ 1; 2 ]
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// pairwise tests
// ============================================================================

let pairwise_basic_test () =
    let tc = TestCollector<int * int>()

    Reactive.ofList [ 1; 2; 3; 4 ]
    |> Reactive.pairwise
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ (1, 2); (2, 3); (3, 4) ] tc.Results
    shouldBeTrue tc.Completed

let pairwise_single_element_test () =
    let tc = TestCollector<int * int>()

    Reactive.single 1
    |> Reactive.pairwise
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let pairwise_empty_test () =
    let tc = TestCollector<int * int>()

    Reactive.empty ()
    |> Reactive.pairwise
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// first tests
// ============================================================================

let first_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.first
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

let first_single_element_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.first
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let first_empty_errors_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.first
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

// ============================================================================
// last tests
// ============================================================================

let last_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.last
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

let last_single_element_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.last
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let last_empty_errors_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.last
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

// ============================================================================
// defaultIfEmpty tests
// ============================================================================

let default_if_empty_with_values_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.defaultIfEmpty 99
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let default_if_empty_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.defaultIfEmpty 42
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// sample tests
// ============================================================================

let sample_basic_test () =
    let tc = TestCollector<int>()

    Reactive.interval 20
    |> Reactive.take 10
    |> Reactive.sample (Reactive.interval 50 |> Reactive.take 3)
    |> Reactive.subscribe tc.Handler
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

    Reactive.concat [ Reactive.ofList [ 1; 2 ]; Reactive.ofList [ 3; 4 ]; Reactive.ofList [ 5; 6 ] ]
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5; 6 ] tc.Results
    shouldBeTrue tc.Completed

let concat2_test () =
    let tc = TestCollector<int>()

    Reactive.concat2 (Reactive.ofList [ 1; 2 ]) (Reactive.ofList [ 3; 4 ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

let concat_empty_list_test () =
    let tc = TestCollector<int>()
    Reactive.concat [] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_with_empty_sources_test () =
    let tc = TestCollector<int>()

    Reactive.concat [ Reactive.empty (); Reactive.ofList [ 1; 2 ]; Reactive.empty (); Reactive.ofList [ 3 ] ]
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let concat_sequential_async_test () =
    let tc = TestCollector<int>()

    Reactive.concat
        [ Reactive.timer 50 |> Reactive.map (fun _ -> 1)
          Reactive.timer 10 |> Reactive.map (fun _ -> 2) ]
    |> Reactive.subscribe tc.Handler
    |> ignore

    sleep 200
    // Despite second being faster, order is preserved
    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed
