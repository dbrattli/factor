/// Tests for transform operators (map, flatMap, concatMap, scan, reduce)
module Factor.TransformTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// map tests
// ============================================================================

let map_transforms_values_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let map_chained_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.map (fun x -> x + 1)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 11; 21; 31 ] tc.Results
    shouldBeTrue tc.Completed

let map_identity_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.map id
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results

let map_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let map_single_value_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 420 ] tc.Results
    shouldBeTrue tc.Completed

let map_notifications_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2 ]
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ OnNext 10; OnNext 20; OnCompleted ] tc.Msgs

let map_constant_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.map (fun _ -> 99)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 99; 99; 99 ] tc.Results

// ============================================================================
// flatMap tests
// ============================================================================

let flat_map_flattens_observables_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.flatMap (fun x -> Reactive.single (x * 10))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let flat_map_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.flatMap (fun x -> Reactive.single x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let flat_map_to_empty_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.flatMap (fun _ -> Reactive.empty ())
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let flat_map_expands_to_multiple_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2 ]
    |> Reactive.flatMap (fun x -> Reactive.ofList [ x; x * 10 ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 10; 2; 20 ] tc.Results
    shouldBeTrue tc.Completed

let flat_map_cartesian_product_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2 ]
    |> Reactive.flatMap (fun x ->
        Reactive.ofList [ 10; 20 ]
        |> Reactive.map (fun y -> x + y))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 11; 21; 12; 22 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// flatMap monad laws tests
// ============================================================================

/// Left identity: return x >>= f  ===  f x
let flat_map_monad_law_left_identity_test () =
    let f = fun x -> Reactive.single (x * 10)

    let tc1 = TestCollector<int>()
    Reactive.single 42 |> Reactive.flatMap f |> Reactive.spawn tc1.Observer |> ignore
    sleep 50

    let tc2 = TestCollector<int>()
    f 42 |> Reactive.spawn tc2.Observer |> ignore

    sleep 50

    shouldEqual tc2.Results tc1.Results
    shouldEqual [ 420 ] tc1.Results

/// Right identity: m >>= return  ===  m
let flat_map_monad_law_right_identity_test () =
    let tc1 = TestCollector<int>()
    Reactive.single 42 |> Reactive.spawn tc1.Observer |> ignore

    sleep 50

    let tc2 = TestCollector<int>()
    Reactive.single 42 |> Reactive.flatMap Reactive.single |> Reactive.spawn tc2.Observer |> ignore
    sleep 50

    shouldEqual tc1.Results tc2.Results
    shouldEqual [ 42 ] tc1.Results

/// Associativity: (m >>= f) >>= g  ===  m >>= (\x -> f x >>= g)
let flat_map_monad_law_associativity_test () =
    let m = Reactive.single 42
    let f = fun x -> Reactive.single (x * 1000)
    let g = fun x -> Reactive.single (x * 42)

    let tc1 = TestCollector<int>()
    m |> Reactive.flatMap f |> Reactive.flatMap g |> Reactive.spawn tc1.Observer |> ignore
    sleep 50

    let tc2 = TestCollector<int>()

    Reactive.single 42
    |> Reactive.flatMap (fun x -> f x |> Reactive.flatMap g)
    |> Reactive.spawn tc2.Observer
    |> ignore

    sleep 50

    shouldEqual tc2.Results tc1.Results
    shouldEqual [ 1764000 ] tc1.Results

// ============================================================================
// concatMap tests
// ============================================================================

let concat_map_preserves_order_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.concatMap (fun x -> Reactive.ofList [ x; x * 10 ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc.Results
    shouldBeTrue tc.Completed

let concat_map_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.concatMap (fun x -> Reactive.single x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_map_to_single_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.concatMap (fun x -> Reactive.single (x * 100))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 100; 200; 300 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// scan tests
// ============================================================================

let scan_running_sum_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.scan 0 (fun acc x -> acc + x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 3; 6; 10; 15 ] tc.Results
    shouldBeTrue tc.Completed

let scan_running_product_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4 ]
    |> Reactive.scan 1 (fun acc x -> acc * x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 6; 24 ] tc.Results
    shouldBeTrue tc.Completed

let scan_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.scan 0 (fun acc x -> acc + x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let scan_single_value_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.scan 0 (fun acc x -> acc + x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let scan_collect_to_list_test () =
    let tc = TestCollector<int list>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.scan [] (fun acc x -> acc @ [ x ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ [ 1 ]; [ 1; 2 ]; [ 1; 2; 3 ] ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// reduce tests
// ============================================================================

let reduce_sum_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.reduce 0 (fun acc x -> acc + x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 15 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_product_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4 ]
    |> Reactive.reduce 1 (fun acc x -> acc * x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 24 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.reduce 0 (fun acc x -> acc + x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_single_value_test () =
    let tc = TestCollector<int>()

    Reactive.single 42
    |> Reactive.reduce 0 (fun acc x -> acc + x)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_collect_to_list_test () =
    let tc = TestCollector<int list>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.reduce [] (fun acc x -> acc @ [ x ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ [ 1; 2; 3 ] ] tc.Results
    shouldBeTrue tc.Completed

let reduce_count_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ "a"; "b"; "c"; "d"; "e" ]
    |> Reactive.reduce 0 (fun acc _ -> acc + 1)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 5 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// switchInner / switchMap tests
// ============================================================================

let switch_inner_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ Reactive.ofList [ 1; 2; 3 ]; Reactive.ofList [ 4; 5; 6 ] ]
    |> Reactive.switchInner
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    // With sync sources, each inner completes before next arrives
    shouldBeTrue (tc.Results.Length >= 3)
    shouldBeTrue tc.Completed

let switch_map_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.switchMap (fun x -> Reactive.ofList [ x; x * 10 ])
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldBeTrue (tc.Results.Length >= 2)
    shouldBeTrue tc.Completed

let switch_inner_empty_outer_test () =
    let tc = TestCollector<int>()

    let emptyOuter: Factor<Factor<int>> = Reactive.empty ()
    emptyOuter |> Reactive.switchInner |> Reactive.spawn tc.Observer |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let switch_map_async_cancels_previous_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.switchMap (fun x ->
        Reactive.timer (x * 30) |> Reactive.map (fun _ -> x))
    |> Reactive.spawn tc.Observer
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

    // Note: side-effect tracking via mutable not possible here because
    // tap runs in a spawned operator process (separate process dictionary).
    // Instead, we verify values pass through unchanged.
    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.tap (fun _x -> ())
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let tap_does_not_modify_values_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 10; 20; 30 ]
    |> Reactive.tap (fun _ -> ())
    |> Reactive.map (fun x -> x * 2)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 20; 40; 60 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// startWith tests
// ============================================================================

let start_with_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 3; 4; 5 ]
    |> Reactive.startWith [ 1; 2 ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let start_with_empty_prefix_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.startWith []
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let start_with_empty_source_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.startWith [ 1; 2 ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// pairwise tests
// ============================================================================

let pairwise_basic_test () =
    let tc = TestCollector<int * int>()

    Reactive.ofList [ 1; 2; 3; 4 ]
    |> Reactive.pairwise
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ (1, 2); (2, 3); (3, 4) ] tc.Results
    shouldBeTrue tc.Completed

let pairwise_single_element_test () =
    let tc = TestCollector<int * int>()

    Reactive.single 1
    |> Reactive.pairwise
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let pairwise_empty_test () =
    let tc = TestCollector<int * int>()

    Reactive.empty ()
    |> Reactive.pairwise
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed
