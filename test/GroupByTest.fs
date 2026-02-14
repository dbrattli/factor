/// Tests for groupBy operator
module Factor.GroupByTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// groupBy tests
// ============================================================================

let group_by_basic_test () =
    let tc = TestCollector<int * int list>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6 ]
    |> Rx.groupBy (fun x -> x % 2)
    |> Rx.flatMap (fun (key, values) ->
        values
        |> Rx.reduce [] (fun acc v -> acc @ [ v ])
        |> Rx.map (fun collected -> (key, collected)))
    |> Rx.subscribe tc.Observer
    |> ignore

    let sorted = tc.Results |> List.sortBy fst
    shouldEqual [ (0, [ 2; 4; 6 ]); (1, [ 1; 3; 5 ]) ] sorted
    shouldBeTrue tc.Completed

let group_by_single_group_test () =
    let tc = TestCollector<string * int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.groupBy (fun _ -> "all")
    |> Rx.flatMap (fun (key, values) ->
        values |> Rx.map (fun v -> (key, v)))
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ ("all", 1); ("all", 2); ("all", 3) ] tc.Results
    shouldBeTrue tc.Completed

let group_by_each_unique_key_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.groupBy (fun x -> x)
    |> Rx.flatMap (fun (_, values) -> values)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldHaveLength 3 tc.Results
    shouldBeTrue tc.Completed

let group_by_empty_source_test () =
    let mutable groupCount = 0

    Rx.empty ()
    |> Rx.groupBy (fun (x: int) -> x % 2)
    |> Rx.subscribe
        { Notify =
            fun n ->
                match n with
                | OnNext _ -> groupCount <- groupCount + 1
                | _ -> () }
    |> ignore

    shouldEqual 0 groupCount

let group_by_count_groups_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5; 6; 7; 8 ]
    |> Rx.groupBy (fun x -> x % 3)
    |> Rx.reduce 0 (fun count _ -> count + 1)
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 3 ] tc.Results
    shouldBeTrue tc.Completed

let group_by_preserves_order_within_group_test () =
    let tc = TestCollector<int list>()

    Rx.ofList [ 2; 4; 6; 8; 10 ]
    |> Rx.groupBy (fun _ -> "evens")
    |> Rx.flatMap (fun (_, values) ->
        values |> Rx.reduce [] (fun acc v -> acc @ [ v ]))
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ [ 2; 4; 6; 8; 10 ] ] tc.Results
    shouldBeTrue tc.Completed

let group_by_with_strings_test () =
    let tc = TestCollector<string * string list>()

    Rx.ofList [ "apple"; "banana"; "avocado"; "blueberry"; "cherry" ]
    |> Rx.groupBy (fun s ->
        if s.StartsWith("a") then "a"
        elif s.StartsWith("b") then "b"
        elif s.StartsWith("c") then "c"
        else "other")
    |> Rx.flatMap (fun (key, values) ->
        values
        |> Rx.reduce [] (fun acc v -> acc @ [ v ])
        |> Rx.map (fun collected -> (key, collected)))
    |> Rx.subscribe tc.Observer
    |> ignore

    let groups = tc.Results |> Map.ofList
    shouldEqual (Some [ "apple"; "avocado" ]) (Map.tryFind "a" groups)
    shouldEqual (Some [ "banana"; "blueberry" ]) (Map.tryFind "b" groups)
    shouldEqual (Some [ "cherry" ]) (Map.tryFind "c" groups)
    shouldBeTrue tc.Completed
