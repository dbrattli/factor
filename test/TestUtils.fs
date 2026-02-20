/// Test utilities for Factor tests
///
/// Provides test collectors, assertion helpers, and sleep FFI.
module Factor.TestUtils

open Factor.Types
open Fable.Core

/// Timer-aware sleep: processes pending factor_timer callbacks
/// while waiting, ensuring timer events execute in the current process.
[<Emit("factor_timer:process_timers($0)")>]
let sleep (ms: int) : unit = failwith "native"

/// Simple test result collector using mutable state.
/// Collects OnNext values, completion status, and errors.
type TestCollector<'a>() =
    let mutable results: 'a list = []
    let mutable completed = false
    let mutable errors: string list = []
    let mutable notifications: Notification<'a, string> list = []

    member _.Results = List.rev results
    member _.Completed = completed
    member _.Errors = List.rev errors
    member _.Notifications = List.rev notifications

    member _.Handler: Handler<'a, string> =
        { Notify =
            fun n ->
                notifications <- n :: notifications

                match n with
                | OnNext x -> results <- x :: results
                | OnError e -> errors <- e :: errors
                | OnCompleted -> completed <- true }

/// Assertion: check equality
let shouldEqual (expected: 'a) (actual: 'a) =
    if expected <> actual then
        failwithf "Expected %A but got %A" expected actual

/// Assertion: check true
let shouldBeTrue (value: bool) =
    if not value then
        failwith "Expected true but got false"

/// Assertion: check false
let shouldBeFalse (value: bool) =
    if value then
        failwith "Expected false but got true"

/// Assertion: check list length
let shouldHaveLength (expected: int) (lst: 'a list) =
    if lst.Length <> expected then
        failwithf "Expected length %d but got %d" expected lst.Length

/// Assertion: check that a list is not empty
let shouldNotBeEmpty (lst: 'a list) =
    if lst.IsEmpty then
        failwith "Expected non-empty list but got empty"
