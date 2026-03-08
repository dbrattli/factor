/// Tests for builder module - computation expression support
module Factor.BuilderTest

open Factor.Reactive
open Factor.Reactive.Builder
open Factor.TestUtils

// ============================================================================
// bind tests (using Builder module directly)
// ============================================================================

let bind_simple_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.single 42) (fun x -> Factor.Reactive.Builder.ret (x * 2))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 84 ] tc.Results
    shouldBeTrue tc.Completed

let bind_chained_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.single 10) (fun x ->
            Factor.Reactive.Builder.bind (Reactive.single 20) (fun y -> Factor.Reactive.Builder.ret (x + y)))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let bind_three_values_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.single 1) (fun x ->
            Factor.Reactive.Builder.bind (Reactive.single 2) (fun y ->
                Factor.Reactive.Builder.bind (Reactive.single 3) (fun z -> Factor.Reactive.Builder.ret (x + y + z))))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 6 ] tc.Results
    shouldBeTrue tc.Completed

let bind_flatmap_behavior_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.ofList [ 1; 2; 3 ]) (fun x -> Factor.Reactive.Builder.ret (x + 10))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 11; 12; 13 ] tc.Results
    shouldBeTrue tc.Completed

let bind_nested_flatmap_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.ofList [ 1; 2 ]) (fun x ->
            Factor.Reactive.Builder.bind (Reactive.ofList [ 10; 20 ]) (fun y -> Factor.Reactive.Builder.ret (x + y)))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 11; 21; 12; 22 ] tc.Results
    shouldBeTrue tc.Completed

let bind_empty_source_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.empty ()) (fun x -> Factor.Reactive.Builder.ret (x * 10))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let bind_with_empty_inner_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.ofList [ 1; 2; 3 ]) (fun _ -> Factor.Reactive.Builder.zero ())

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// observable { } computation expression tests
// ============================================================================

let flow_return_test () =
    let tc = TestCollector<int>()

    let obs = observable { return 42 }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 42 ] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_single_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.single 10
            return x * 2
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 20 ] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_two_values_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.single 10
            let! y = Reactive.single 20
            return x + y
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 30 ] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_three_values_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.single 1
            let! y = Reactive.single 2
            let! z = Reactive.single 3
            return x + y + z
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 6 ] tc.Results
    shouldBeTrue tc.Completed

let flow_return_from_test () =
    let tc = TestCollector<int>()

    let obs = observable { return! Reactive.ofList [ 1; 2; 3 ] }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_with_list_source_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.ofList [ 1; 2; 3 ]
            return x * 10
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_empty_source_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.empty ()
            return x * 10
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_cartesian_product_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.ofList [ 1; 2 ]
            let! y = Reactive.ofList [ 10; 20 ]
            return x + y
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 11; 21; 12; 22 ] tc.Results
    shouldBeTrue tc.Completed

let flow_for_loop_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            for x in [ 1; 2; 3 ] do
                return x * 10
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let flow_for_loop_empty_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            for _x in ([] : int list) do
                return 42
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let flow_bind_then_return_from_test () =
    let tc = TestCollector<int>()

    let obs =
        observable {
            let! x = Reactive.single 10
            return! Reactive.ofList [ x; x + 1; x + 2 ]
        }

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 10; 11; 12 ] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// forEach tests
// ============================================================================

let for_each_iterates_list_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.forEach [ 1; 2; 3 ] (fun x -> Reactive.single (x * 10))
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [ 10; 20; 30 ] tc.Results
    shouldBeTrue tc.Completed

let for_each_empty_list_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.forEach [] (fun x -> Reactive.single (x * 10))
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let for_each_single_item_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.forEach [ 42 ] (fun x -> Reactive.single (x * 2))
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [ 84 ] tc.Results

let for_each_multiple_emissions_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.forEach [ 1; 2 ] (fun x -> Reactive.ofList [ x; x * 10 ])
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [ 1; 10; 2; 20 ] tc.Results

// ============================================================================
// combine tests
// ============================================================================

let combine_concatenates_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.combine (Reactive.ofList [ 1; 2 ]) (Reactive.ofList [ 3; 4 ])
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [ 1; 2; 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

let combine_first_empty_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.combine (Reactive.empty ()) (Reactive.ofList [ 1; 2; 3 ])
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results

let combine_second_empty_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.combine (Reactive.ofList [ 1; 2; 3 ]) (Reactive.empty ())
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [ 1; 2; 3 ] tc.Results

let combine_both_empty_test () =
    let tc = TestCollector<int>()

    Factor.Reactive.Builder.combine (Reactive.empty ()) (Reactive.empty ())
    |> _.Subscribe(tc.Observer)
    |> ignore
    sleep 50

    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// empty/zero tests
// ============================================================================

let builder_empty_test () =
    let tc = TestCollector<int>()
    Factor.Reactive.Builder.zero () |> _.Subscribe(tc.Observer) |> ignore
    sleep 50
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

// ============================================================================
// Complex composition tests
// ============================================================================

let complex_composition_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind (Reactive.ofList [ 1; 2; 3; 4; 5 ]) (fun x ->
            Factor.Reactive.Builder.bind (Reactive.single 10) (fun y ->
                if x % 2 = 0 then
                    Factor.Reactive.Builder.ret (x * y)
                else
                    Factor.Reactive.Builder.zero ()))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 20; 40 ] (List.sort tc.Results)
    shouldBeTrue tc.Completed

let nested_for_each_test () =
    let tc = TestCollector<int>()

    let obs =
        Factor.Reactive.Builder.bind
            (Factor.Reactive.Builder.forEach [ 1; 2 ] (fun x -> Reactive.single x))
            (fun x ->
                Factor.Reactive.Builder.bind
                    (Factor.Reactive.Builder.forEach [ 10; 20 ] (fun y -> Reactive.single y))
                    (fun y -> Factor.Reactive.Builder.ret (x + y)))

    obs |> _.Subscribe(tc.Observer) |> ignore
    sleep 50

    shouldEqual [ 11; 21; 12; 22 ] tc.Results
