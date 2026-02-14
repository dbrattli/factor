/// Tests for amb/race, forkJoin, distinct, and timeout
module Factor.AmbForkjoinTest

open Factor.Types
open Factor.Rx
open Factor.TestUtils

// ============================================================================
// amb / race tests
// ============================================================================

let amb_first_to_emit_wins_test () =
    let tc = TestCollector<string>()

    Rx.amb
        [ Rx.timer 100 |> Rx.map (fun _ -> "slow")
          Rx.timer 30 |> Rx.map (fun _ -> "fast")
          Rx.timer 200 |> Rx.map (fun _ -> "slowest") ]
    |> Rx.subscribe tc.Observer
    |> ignore

    sleep 300
    shouldEqual [ "fast" ] tc.Results
    shouldBeTrue tc.Completed

let amb_sync_first_wins_test () =
    let tc = TestCollector<int>()

    Rx.amb [ Rx.ofList [ 1; 2; 3 ]; Rx.ofList [ 4; 5; 6 ] ]
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let amb_empty_list_test () =
    let tc = TestCollector<int>()
    Rx.amb [] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let amb_single_source_test () =
    let tc = TestCollector<int>()
    Rx.amb [ Rx.ofList [ 1; 2; 3 ] ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let race_is_alias_for_amb_test () =
    let tc = TestCollector<int>()
    Rx.race [ Rx.ofList [ 1; 2 ]; Rx.ofList [ 3; 4 ] ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

let amb_error_from_winner_propagates_test () =
    let tc = TestCollector<int>()

    Rx.amb [ Rx.fail "error"; Rx.timer 100 |> Rx.map (fun _ -> 1) ]
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "error" ] tc.Errors

// ============================================================================
// forkJoin tests
// ============================================================================

let fork_join_basic_test () =
    let tc = TestCollector<int list>()

    Rx.forkJoin [ Rx.ofList [ 1; 2; 3 ]; Rx.ofList [ 4; 5 ]; Rx.single 6 ]
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ [ 3; 5; 6 ] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_empty_list_test () =
    let tc = TestCollector<int list>()
    Rx.forkJoin [] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ [] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_single_source_test () =
    let tc = TestCollector<int list>()
    Rx.forkJoin [ Rx.ofList [ 1; 2; 3 ] ] |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [ [ 3 ] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_async_test () =
    let tc = TestCollector<int list>()

    Rx.forkJoin
        [ Rx.timer 50 |> Rx.map (fun _ -> 1)
          Rx.timer 30 |> Rx.map (fun _ -> 2)
          Rx.timer 70 |> Rx.map (fun _ -> 3) ]
    |> Rx.subscribe tc.Observer
    |> ignore

    sleep 200
    shouldEqual [ [ 1; 2; 3 ] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_empty_source_errors_test () =
    let tc = TestCollector<int list>()

    Rx.forkJoin [ Rx.ofList [ 1; 2 ]; Rx.empty (); Rx.ofList [ 3 ] ]
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

let fork_join_error_propagates_test () =
    let tc = TestCollector<int list>()

    Rx.forkJoin [ Rx.ofList [ 1; 2 ]; Rx.fail "oops"; Rx.ofList [ 3 ] ]
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ "oops" ] tc.Errors

// ============================================================================
// distinct tests
// ============================================================================

let distinct_basic_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 1; 3; 2; 4; 1 ]
    |> Rx.distinct
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_all_unique_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3; 4; 5 ]
    |> Rx.distinct
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_all_same_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 1; 1; 1; 1 ]
    |> Rx.distinct
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_empty_test () =
    let tc = TestCollector<int>()
    Rx.empty () |> Rx.distinct |> Rx.subscribe tc.Observer |> ignore
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let distinct_vs_distinct_until_changed_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Rx.ofList [ 1; 2; 2; 1; 3; 3 ]

    source |> Rx.distinct |> Rx.subscribe tc1.Observer |> ignore
    source |> Rx.distinctUntilChanged |> Rx.subscribe tc2.Observer |> ignore

    // distinct: all unique
    shouldEqual [ 1; 2; 3 ] tc1.Results
    // distinctUntilChanged: consecutive only
    shouldEqual [ 1; 2; 1; 3 ] tc2.Results

// ============================================================================
// timeout tests
// ============================================================================

let timeout_no_timeout_test () =
    let tc = TestCollector<int>()

    Rx.interval 20
    |> Rx.take 3
    |> Rx.timeout 100
    |> Rx.subscribe tc.Observer
    |> ignore

    sleep 200
    shouldEqual [ 0; 1; 2 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let timeout_triggers_error_test () =
    let tc = TestCollector<int>()

    Rx.timer 200
    |> Rx.map (fun _ -> 1)
    |> Rx.timeout 50
    |> Rx.subscribe tc.Observer
    |> ignore

    sleep 150
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

let timeout_resets_on_emission_test () =
    let tc = TestCollector<int>()

    Rx.interval 30
    |> Rx.take 4
    |> Rx.timeout 50
    |> Rx.subscribe tc.Observer
    |> ignore

    sleep 250
    shouldEqual [ 0; 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let timeout_sync_source_test () =
    let tc = TestCollector<int>()

    Rx.ofList [ 1; 2; 3 ]
    |> Rx.timeout 1000
    |> Rx.subscribe tc.Observer
    |> ignore

    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors
