/// Tests for combining operators (merge, combineLatest, withLatestFrom, zip)
module Factor.CombineTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// merge tests
// ============================================================================

let merge_empty_list_test () =
    let tc = TestCollector<int>()
    Reactive.merge [] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let merge_single_source_test () =
    let tc = TestCollector<int>()
    Reactive.merge [ Reactive.ofList [ 1; 2; 3 ] ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let merge_two_sources_test () =
    let tc = TestCollector<int>()
    let obs1 = Reactive.ofList [ 1; 2 ]
    let obs2 = Reactive.ofList [ 10; 20 ]
    Reactive.merge [ obs1; obs2 ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 1; 2; 10; 20 ] tc.Results
    shouldBeTrue tc.Completed

let merge2_test () =
    let tc = TestCollector<int>()
    let obs1 = Reactive.ofList [ 1; 2 ]
    let obs2 = Reactive.ofList [ 10; 20 ]
    Reactive.merge2 obs1 obs2 |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 1; 2; 10; 20 ] tc.Results
    shouldBeTrue tc.Completed

let merge_with_empty_test () =
    let tc = TestCollector<int>()
    let obs1 = Reactive.ofList [ 1; 2 ]
    let obs2 = Reactive.empty ()
    Reactive.merge [ obs1; obs2 ] |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// combineLatest tests
// ============================================================================

let combine_latest_basic_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.ofList [ 1; 2 ]
    let obs2 = Reactive.ofList [ "a"; "b" ]

    Reactive.combineLatest (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    // Sync: obs1 completes with latest=2, then obs2 emits "a" -> (2,"a"), "b" -> (2,"b")
    shouldEqual [ (2, "a"); (2, "b") ] tc.Results
    shouldBeTrue tc.Completed

let combine_latest_with_singles_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.single 42
    let obs2 = Reactive.single "hello"

    Reactive.combineLatest (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ (42, "hello") ] tc.Results
    shouldBeTrue tc.Completed

let combine_latest_one_empty_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.ofList [ 1; 2 ]
    let obs2: Factor<string> = Reactive.empty ()

    Reactive.combineLatest (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// withLatestFrom tests
// ============================================================================

let with_latest_from_basic_test () =
    let tc = TestCollector<int * string>()
    let source = Reactive.ofList [ 1; 2; 3 ]
    let sampler = Reactive.single "x"

    source
    |> Reactive.withLatestFrom (fun a b -> (a, b)) sampler
    |> Reactive.subscribe tc.Handler
    |> ignore

    // withLatestFrom subscribes to sampler first, then source.
    // Sampler (sync) emits "x" immediately, so all source values combine with it.
    shouldEqual [ (1, "x"); (2, "x"); (3, "x") ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// zip tests
// ============================================================================

let zip_basic_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.ofList [ 1; 2; 3 ]
    let obs2 = Reactive.ofList [ "a"; "b"; "c" ]

    Reactive.zip (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ (1, "a"); (2, "b"); (3, "c") ] tc.Results
    shouldBeTrue tc.Completed

let zip_different_lengths_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.ofList [ 1; 2; 3; 4; 5 ]
    let obs2 = Reactive.ofList [ "a"; "b" ]

    Reactive.zip (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ (1, "a"); (2, "b") ] tc.Results
    shouldBeTrue tc.Completed

let zip_one_empty_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.ofList [ 1; 2; 3 ]
    let obs2: Factor<string> = Reactive.empty ()

    Reactive.zip (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let zip_singles_test () =
    let tc = TestCollector<int * string>()
    let obs1 = Reactive.single 42
    let obs2 = Reactive.single "hello"

    Reactive.zip (fun a b -> (a, b)) obs1 obs2
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ (42, "hello") ] tc.Results
    shouldBeTrue tc.Completed
