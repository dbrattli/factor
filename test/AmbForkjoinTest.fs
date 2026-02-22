/// Tests for amb/race, forkJoin, distinct, and timeout
module Factor.AmbForkjoinTest

open Factor.Types
open Factor.Reactive
open Factor.TestUtils

// ============================================================================
// amb / race tests
// ============================================================================

let amb_first_to_emit_wins_test () =
    let tc = TestCollector<string>()

    Reactive.amb
        [ Reactive.timer 100 |> Reactive.map (fun _ -> "slow")
          Reactive.timer 30 |> Reactive.map (fun _ -> "fast")
          Reactive.timer 200 |> Reactive.map (fun _ -> "slowest") ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 300
    shouldEqual [ "fast" ] tc.Results
    shouldBeTrue tc.Completed

let amb_sync_first_wins_test () =
    let tc = TestCollector<int>()

    Reactive.amb [ Reactive.ofList [ 1; 2; 3 ]; Reactive.ofList [ 4; 5; 6 ] ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let amb_empty_list_test () =
    let tc = TestCollector<int>()
    Reactive.amb [] |> Reactive.spawn tc.Observer |> ignore
    sleep 50
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let amb_single_source_test () =
    let tc = TestCollector<int>()
    Reactive.amb [ Reactive.ofList [ 1; 2; 3 ] ] |> Reactive.spawn tc.Observer |> ignore
    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed

let race_is_alias_for_amb_test () =
    let tc = TestCollector<int>()
    Reactive.race [ Reactive.ofList [ 1; 2 ]; Reactive.ofList [ 3; 4 ] ] |> Reactive.spawn tc.Observer |> ignore
    sleep 50
    shouldEqual [ 1; 2 ] tc.Results
    shouldBeTrue tc.Completed

let amb_error_from_winner_propagates_test () =
    let tc = TestCollector<int>()

    Reactive.amb [ Reactive.fail (FactorException "error"); Reactive.timer 100 |> Reactive.map (fun _ -> 1) ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "error" ] tc.Errors

// ============================================================================
// forkJoin tests
// ============================================================================

let fork_join_basic_test () =
    let tc = TestCollector<int list>()

    Reactive.forkJoin [ Reactive.ofList [ 1; 2; 3 ]; Reactive.ofList [ 4; 5 ]; Reactive.single 6 ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ [ 3; 5; 6 ] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_empty_list_test () =
    let tc = TestCollector<int list>()
    Reactive.forkJoin [] |> Reactive.spawn tc.Observer |> ignore
    sleep 50
    shouldEqual [ [] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_single_source_test () =
    let tc = TestCollector<int list>()
    Reactive.forkJoin [ Reactive.ofList [ 1; 2; 3 ] ] |> Reactive.spawn tc.Observer |> ignore
    sleep 50
    shouldEqual [ [ 3 ] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_async_test () =
    let tc = TestCollector<int list>()

    Reactive.forkJoin
        [ Reactive.timer 50 |> Reactive.map (fun _ -> 1)
          Reactive.timer 30 |> Reactive.map (fun _ -> 2)
          Reactive.timer 70 |> Reactive.map (fun _ -> 3) ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 200
    shouldEqual [ [ 1; 2; 3 ] ] tc.Results
    shouldBeTrue tc.Completed

let fork_join_empty_source_errors_test () =
    let tc = TestCollector<int list>()

    Reactive.forkJoin [ Reactive.ofList [ 1; 2 ]; Reactive.empty (); Reactive.ofList [ 3 ] ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

let fork_join_error_propagates_test () =
    let tc = TestCollector<int list>()

    Reactive.forkJoin [ Reactive.ofList [ 1; 2 ]; Reactive.fail (FactorException "oops"); Reactive.ofList [ 3 ] ]
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual [ FactorException "oops" ] tc.Errors

// ============================================================================
// distinct tests
// ============================================================================

let distinct_basic_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 1; 3; 2; 4; 1 ]
    |> Reactive.distinct
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3; 4 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_all_unique_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3; 4; 5 ]
    |> Reactive.distinct
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3; 4; 5 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_all_same_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 1; 1; 1; 1 ]
    |> Reactive.distinct
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1 ] tc.Results
    shouldBeTrue tc.Completed

let distinct_empty_test () =
    let tc = TestCollector<int>()
    Reactive.empty () |> Reactive.distinct |> Reactive.spawn tc.Observer |> ignore
    sleep 50
    shouldEqual [] tc.Results
    shouldBeTrue tc.Completed

let distinct_vs_distinct_until_changed_test () =
    let tc1 = TestCollector<int>()
    let tc2 = TestCollector<int>()

    let source = Reactive.ofList [ 1; 2; 2; 1; 3; 3 ]

    source |> Reactive.distinct |> Reactive.spawn tc1.Observer |> ignore
    sleep 50
    source |> Reactive.distinctUntilChanged |> Reactive.spawn tc2.Observer |> ignore
    sleep 50

    // distinct: all unique
    shouldEqual [ 1; 2; 3 ] tc1.Results
    // distinctUntilChanged: consecutive only
    shouldEqual [ 1; 2; 1; 3 ] tc2.Results

// ============================================================================
// timeout tests
// ============================================================================

let timeout_no_timeout_test () =
    let tc = TestCollector<int>()

    Reactive.interval 20
    |> Reactive.take 3
    |> Reactive.timeout 100
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 200
    shouldEqual [ 0; 1; 2 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let timeout_triggers_error_test () =
    let tc = TestCollector<int>()

    Reactive.timer 200
    |> Reactive.map (fun _ -> 1)
    |> Reactive.timeout 50
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 150
    shouldEqual [] tc.Results
    shouldBeFalse tc.Completed
    shouldEqual 1 tc.Errors.Length

let timeout_resets_on_emission_test () =
    let tc = TestCollector<int>()

    Reactive.interval 30
    |> Reactive.take 4
    |> Reactive.timeout 50
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 250
    shouldEqual [ 0; 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors

let timeout_sync_source_test () =
    let tc = TestCollector<int>()

    Reactive.ofList [ 1; 2; 3 ]
    |> Reactive.timeout 1000
    |> Reactive.spawn tc.Observer
    |> ignore

    sleep 50
    shouldEqual [ 1; 2; 3 ] tc.Results
    shouldBeTrue tc.Completed
    shouldEqual [] tc.Errors
