/// Tests for builder module - computation expression support
module Factor.BuilderTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// bind tests (using Builder module directly)
// ============================================================================

let bind_simple_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.single 42) (fun x -> Factor.Builder.ret (x * 2))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 84 ] tc.Results
    shouldBeTrue tc.Completed

let bind_chained_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.single 10) (fun x ->
            Factor.Builder.bind (Reactive.single 20) (fun y -> Factor.Builder.ret (x + y)))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let bind_three_values_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.single 1) (fun x ->
            Factor.Builder.bind (Reactive.single 2) (fun y ->
                Factor.Builder.bind (Reactive.single 3) (fun z -> Factor.Builder.ret (x + y + z))))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 6 ] tc.Results
    shouldBeTrue tc.Completed

let bind_flatmap_behavior_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.ofList [ 1; 2; 3 ]) (fun x -> Factor.Builder.ret (x + 10))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 11; 12; 13 ] tc.Results
    shouldBeTrue tc.Completed

let bind_nested_flatmap_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.ofList [ 1; 2 ]) (fun x ->
            Factor.Builder.bind (Reactive.ofList [ 10; 20 ]) (fun y -> Factor.Builder.ret (x + y)))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 11; 21; 12; 22 ] tc.Results
    shouldBeTrue tc.Completed

let bind_empty_source_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.empty ()) (fun x -> Factor.Builder.ret (x * 10))

    observable |> Reactive.subscribe tc.Handler |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let bind_with_empty_inner_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.ofList [ 1; 2; 3 ]) (fun _ -> Factor.Builder.zero ())

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// rx computation expression tests
// ============================================================================

let rx_return_test () =
    let tc = TestCollector<int>()

    let observable = Factor.Builder.factor { return 42 }

    observable |> Reactive.subscribe tc.Handler |> ignore

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let rx_bind_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.factor {
            let! x = Reactive.single 10
            let! y = Reactive.single 20
            return x + y
        }

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let rx_return_from_test () =
    let tc = TestCollector<int>()

    let observable = Factor.Builder.factor { return! Reactive.ofList [ 1; 2; 3 ] }

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results

// ============================================================================
// return/pure tests
// ============================================================================

let return_single_value_test () =
    let tc = TestCollector<int>()
    Factor.Builder.ret 42 |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// map_over tests (using Reactive.map)
// ============================================================================

let map_over_transforms_values_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.map (fun x -> x * 100)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 100; 200; 300 ] tc.Results
    shouldBeTrue tc.Completed

let map_over_single_value_test () =
    let tc = TestCollector<int>()

    Reactive.single 5
    |> Reactive.map (fun x -> x * x)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 25 ] tc.Results

let map_over_empty_test () =
    let tc = TestCollector<int>()

    Reactive.empty ()
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// filter_with tests (using Reactive.filter)
// ============================================================================

let filter_with_keeps_matching_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.filter (fun x -> x > 2)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let filter_with_all_pass_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.filter (fun _ -> true)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let filter_with_none_pass_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.filter (fun _ -> false)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let filter_with_even_numbers_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6 ]
    |> Reactive.filter (fun x -> x % 2 = 0)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 2; 4; 6 ] tc.Results

// ============================================================================
// forEach tests
// ============================================================================

let for_each_iterates_list_test () =
    let tc = TestCollector<int>()

    Factor.Builder.forEach [ 1; 2; 3 ] (fun x -> Reactive.single (x * 10))
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let for_each_empty_list_test () =
    let tc = TestCollector<int>()

    Factor.Builder.forEach [] (fun x -> Reactive.single (x * 10))
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let for_each_single_item_test () =
    let tc = TestCollector<int>()

    Factor.Builder.forEach [ 42 ] (fun x -> Reactive.single (x * 2))
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 84 ] tc.Results

let for_each_multiple_emissions_test () =
    let tc = TestCollector<int>()

    Factor.Builder.forEach [ 1; 2 ] (fun x -> Reactive.ofList [ x; x * 10 ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 10; 2; 20 ] tc.Results

// ============================================================================
// combine tests
// ============================================================================

let combine_concatenates_test () =
    let tc = TestCollector<int>()

    Factor.Builder.combine (Reactive.ofList [ 1; 2 ]) (Reactive.ofList [ 3; 4 ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

let combine_first_empty_test () =
    let tc = TestCollector<int>()

    Factor.Builder.combine (Reactive.empty ()) (Reactive.ofList [ 1; 2; 3 ])
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let combine_second_empty_test () =
    let tc = TestCollector<int>()

    Factor.Builder.combine (Reactive.ofList [ 1; 2; 3 ]) (Reactive.empty ())
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results

let combine_both_empty_test () =
    let tc = TestCollector<int>()

    Factor.Builder.combine (Reactive.empty ()) (Reactive.empty ())
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// empty/zero tests
// ============================================================================

let builder_empty_test () =
    let tc = TestCollector<int>()
    Factor.Builder.zero () |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// Complex composition tests
// ============================================================================

let complex_composition_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind (Reactive.ofList [ 1; 2; 3; 4; 5 ]) (fun x ->
            Factor.Builder.bind (Reactive.single 10) (fun y ->
                if x % 2 = 0 then
                    Factor.Builder.ret (x * y)
                else
                    Factor.Builder.zero ()))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 20; 40 ] tc.Results
    shouldBeTrue tc.Completed

let nested_for_each_test () =
    let tc = TestCollector<int>()

    let observable =
        Factor.Builder.bind
            (Factor.Builder.forEach [ 1; 2 ] (fun x -> Reactive.single x))
            (fun x ->
                Factor.Builder.bind
                    (Factor.Builder.forEach [ 10; 20 ] (fun y -> Reactive.single y))
                    (fun y -> Factor.Builder.ret (x + y)))

    observable |> Reactive.subscribe tc.Handler |> ignore
    sleep 50

    shouldEqual [ 11; 21; 12; 22 ] tc.Results

let map_over_then_filter_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.map (fun x -> x * 10)
    |> Reactive.filter (fun x -> x > 20)
    |> Reactive.subscribe tc.Handler
    |> ignore

    shouldEqual [ 30; 40; 50 ] tc.Results

let yield_from_identity_test () =
    let tc = TestCollector<int>()
    let source = Reactive.ofList [ 1; 2; 3 ]
    // yield_from is just identity
    source |> Reactive.subscribe tc.Handler |> ignore
    shouldEqual [ 1; 2; 3 ] tc.Results
