/// Test utilities for Factor tests
///
/// Provides test collectors, assertion helpers, and sleep FFI.
module Factor.TestUtils

open Factor.Types

/// Timer-aware sleep: processes pending factor_timer, factor_child,
/// and EXIT messages for the specified duration.
let sleep (ms: int) : unit = Process.processTimers ms

/// Simple test result collector using mutable state.
/// Collects OnNext values, completion status, and errors.
/// Registers a child handler in the current process and creates
/// an observer endpoint (Pid + Ref) for the test process.
type TestCollector<'T>() =
    let mutable results: 'T list = []
    let mutable completed = false
    let mutable errors: exn list = []
    let mutable msgs: Msg<'T> list = []

    let ref = Process.makeRef ()

    do
        Process.registerChild
            ref
            (fun msg ->
                let n = unbox<Msg<'T>> msg

                msgs <- n :: msgs

                match n with
                | OnNext x -> results <- x :: results
                | OnError e -> errors <- e :: errors
                | OnCompleted -> completed <- true)

    member _.Results = List.rev results
    member _.Completed = completed
    member _.Errors = List.rev errors
    member _.Msgs = List.rev msgs

    member _.Observer: Observer<'T> =
        { Pid = Process.selfPid (); Ref = ref }

/// Assertion: check equality
let shouldEqual (expected: 'T) (actual: 'T) =
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
let shouldHaveLength (expected: int) (lst: 'T list) =
    if lst.Length <> expected then
        failwithf "Expected length %d but got %d" expected lst.Length

/// Assertion: check that a list is not empty
let shouldNotBeEmpty (lst: 'T list) =
    if lst.IsEmpty then
        failwith "Expected non-empty list but got empty"
