module Fable.Actor.TestRunner

open Fable.Actor.ActorTest

let runTest name f =
    try
        f ()
        printfn "  PASS: %s" name
        true
    with ex ->
        printfn "  FAIL: %s - %s" name (ex.Message)
        false

[<EntryPoint>]
let main _argv =
    printfn "Fable.Actor Tests"
    printfn "================="

    let results =
        [ "actor_start_basic", actor_start_basic_test
          "actor_start_stop", actor_start_stop_test
          "actor_call_reply", actor_call_reply_test
          "actor_spawn_ce", actor_spawn_ce_test
          "actor_schedule", actor_schedule_test
          "actor_linked_crash", actor_linked_crash_test
          "actor_kill", actor_kill_test
          "actor_callAsync", actor_callAsync_test ]
        |> List.map (fun (name, f) -> runTest name f)

    let passed = results |> List.filter id |> List.length
    let total = results.Length
    printfn ""
    printfn "%d/%d tests passed" passed total

    if passed = total then 0 else 1
