/// Tests for transform operators (map, flatMap, concatMap, scan, reduce)
module Factor.TransformTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// map tests
// ============================================================================

let map_transforms_values_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.map (fun x -> x * 10)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let map_chained_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.map (fun x -> x * 10)
    |> Rx.map (fun x -> x + 1)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 11; 21; 31 ] tc.Results
    shouldBeTrue tc.Completed

let map_identity_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.map id
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let map_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.map (fun x -> x * 10)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let map_single_value_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.map (fun x -> x * 10)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 420 ] tc.Results
    shouldBeTrue tc.Completed

let map_notifications_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2 ]
    |> Rx.map (fun x -> x * 10)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ OnNext 10; OnNext 20; OnCompleted ] tc.Notifications

let map_constant_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.map (fun _ -> 99)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 99; 99; 99 ] tc.Results

// ============================================================================
// flatMap tests
// ============================================================================

let flat_map_flattens_observables_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.flatMap (fun x -> Rx.single (x * 10))
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let flat_map_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.flatMap (fun x -> Rx.single x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let flat_map_to_empty_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.flatMap (fun _ -> Rx.empty ())
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let flat_map_expands_to_multiple_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2 ]
    |> Rx.flatMap (fun x -> Rx.ofList [ x; x * 10 ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 10; 2; 20 ] tc.Results
    shouldBeTrue tc.Completed

let flat_map_cartesian_product_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2 ]
    |> Rx.flatMap (fun x ->
        Rx.ofList [ 10; 20 ]
        |> Rx.map (fun y -> x + y))
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 11; 21; 12; 22 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// flatMap monad laws tests
// ============================================================================

/// Left identity: return x >>= f  ===  f x
let flat_map_monad_law_left_identity_test () =
    let f = fun x -> Rx.single (x * 10)

    let tc1 = TestCollector<int>()
    Rx.single 42 |> Rx.flatMap f |> Rx.subscribe tc1.Handler |> ignore

    let tc2 = TestCollector<int>()
    f 42 |> Rx.subscribe tc2.Handler |> ignore

    shouldEqual tc2.Results tc1.Results
    shouldEqual [ 420 ] tc1.Results

/// Right identity: m >>= return  ===  m
let flat_map_monad_law_right_identity_test () =
    let tc1 = TestCollector<int>()
    Rx.single 42 |> Rx.subscribe tc1.Handler |> ignore

    let tc2 = TestCollector<int>()
    Rx.single 42 |> Rx.flatMap Rx.single |> Rx.subscribe tc2.Handler |> ignore

    shouldEqual tc1.Results tc2.Results
    shouldEqual [ 42 ] tc1.Results

/// Associativity: (m >>= f) >>= g  ===  m >>= (\x -> f x >>= g)
let flat_map_monad_law_associativity_test () =
    let m = Rx.single 42
    let f = fun x -> Rx.single (x * 1000)
    let g = fun x -> Rx.single (x * 42)

    let tc1 = TestCollector<int>()
    m |> Rx.flatMap f |> Rx.flatMap g |> Rx.subscribe tc1.Handler |> ignore

    let tc2 = TestCollector<int>()

    Rx.single 42
    |> Rx.flatMap (fun x -> f x |> Rx.flatMap g)
    |> Rx.subscribe tc2.Handler
    |> ignore

    shouldEqual tc2.Results tc1.Results
    shouldEqual [ 1764000 ] tc1.Results

// ============================================================================
// concatMap tests
// ============================================================================

let concat_map_preserves_order_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.concatMap (fun x -> Rx.ofList [ x; x * 10 ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 10; 2; 20; 3; 30 ] tc.Results
    shouldBeTrue tc.Completed

let concat_map_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.concatMap (fun x -> Rx.single x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let concat_map_to_single_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.concatMap (fun x -> Rx.single (x * 100))
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 100; 200; 300 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// scan tests
// ============================================================================

let scan_running_sum_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.scan 0 (fun acc x -> acc + x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 3; 6; 10; 15 ] tc.Results
    shouldBeTrue tc.Completed

let scan_running_product_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4 ]
    |> Rx.scan 1 (fun acc x -> acc * x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 6; 24 ] tc.Results
    shouldBeTrue tc.Completed

let scan_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.scan 0 (fun acc x -> acc + x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let scan_single_value_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.scan 0 (fun acc x -> acc + x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let scan_collect_to_list_test () =
    let tc = TestCollector<int list>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.scan [] (fun acc x -> acc @ [ x ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ [ 1 ]; [ 1; 2 ]; [ 1; 2; 3 ] ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// reduce tests
// ============================================================================

let reduce_sum_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.reduce 0 (fun acc x -> acc + x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 15 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_product_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4 ]
    |> Rx.reduce 1 (fun acc x -> acc * x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 24 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_empty_source_test () =
    let tc = TestCollector<int>()

    Rx.empty ()
    |> Rx.reduce 0 (fun acc x -> acc + x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 0 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_single_value_test () =
    let tc = TestCollector<int>()

    Rx.single 42
    |> Rx.reduce 0 (fun acc x -> acc + x)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let reduce_collect_to_list_test () =
    let tc = TestCollector<int list>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.reduce [] (fun acc x -> acc @ [ x ])
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ [ 1; 2; 3 ] ] tc.Results
    shouldBeTrue tc.Completed

let reduce_count_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ "a"; "b"; "c"; "d"; "e" ]
    |> Rx.reduce 0 (fun acc _ -> acc + 1)
    |> Rx.subscribe tc.Handler
    |> ignore

    shouldEqual [ 5 ] tc.Results
    shouldBeTrue tc.Completed
