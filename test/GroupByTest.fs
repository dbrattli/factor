/// Tests for groupBy operator
module Factor.GroupByTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// groupBy tests
// ============================================================================

let group_by_basic_test () =
    let tc = TestCollector<int * int list>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6 ]
    |> Reactive.groupBy (fun x -> x % 2)
    |> Reactive.flatMap (fun (key, values) ->
        values
        |> Reactive.reduce [] (fun acc v -> acc @ [ v ])
        |> Reactive.map (fun collected -> (key, collected)))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    let sorted = tc.Results |> List.sortBy fst
    shouldEqual [ (0, [ 2; 4; 6 ]); (1, [ 1; 3; 5 ]) ] sorted
    shouldBeTrue tc.Completed

let group_by_single_group_test () =
    let tc = TestCollector<string * int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.groupBy (fun _ -> "all")
    |> Reactive.flatMap (fun (key, values) ->
        values |> Reactive.map (fun v -> (key, v)))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ ("all", 1); ("all", 2); ("all", 3) ] tc.Results
    shouldBeTrue tc.Completed

let group_by_each_unique_key_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.groupBy (fun x -> x)
    |> Reactive.flatMap (fun (_, values) -> values)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldHaveLength 3 tc.Results
    shouldBeTrue tc.Completed

let group_by_empty_source_test () =
    let mutable groupCount = 0

    Reactive.empty ()
    |> Reactive.groupBy (fun (x: int) -> x % 2)
    |> Reactive.subscribe
        (fun _ -> groupCount <- groupCount + 1)
        (fun _ -> ())
        (fun () -> ())
    |> ignore

    shouldEqual 0 groupCount

let group_by_count_groups_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5; 6; 7; 8 ]
    |> Reactive.groupBy (fun x -> x % 3)
    |> Reactive.reduce 0 (fun count _ -> count + 1)
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

let group_by_preserves_order_within_group_test () =
    let tc = TestCollector<int list>()

    Reactive.ofList [ 2; 4; 6; 8; 10 ]
    |> Reactive.groupBy (fun _ -> "evens")
    |> Reactive.flatMap (fun (_, values) ->
        values |> Reactive.reduce [] (fun acc v -> acc @ [ v ]))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    shouldEqual [ [ 2; 4; 6; 8; 10 ] ] tc.Results
    shouldBeTrue tc.Completed

let group_by_with_strings_test () =
    let tc = TestCollector<string * string list>()

    Reactive.ofList [ "apple"; "banana"; "avocado"; "blueberry"; "cherry" ]
    |> Reactive.groupBy (fun s ->
        if s.StartsWith("a") then "a"
        elif s.StartsWith("b") then "b"
        elif s.StartsWith("c") then "c"
        else "other")
    |> Reactive.flatMap (fun (key, values) ->
        values
        |> Reactive.reduce [] (fun acc v -> acc @ [ v ])
        |> Reactive.map (fun collected -> (key, collected)))
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50

    let groups = tc.Results |> Map.ofList
    shouldEqual (Some [ "apple"; "avocado" ]) (Map.tryFind "a" groups)
    shouldEqual (Some [ "banana"; "blueberry" ]) (Map.tryFind "b" groups)
    shouldEqual (Some [ "cherry" ]) (Map.tryFind "c" groups)
    shouldBeTrue tc.Completed
