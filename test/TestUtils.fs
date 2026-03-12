/// Test utilities for Fable.Actor tests
///
/// Provides assertion helpers and platform-specific sleep.
module Fable.Actor.TestUtils

open Fable.Actor

#if FABLE_COMPILER_BEAM
open Fable.Core

[<Emit("timer:sleep($0)")>]
let sleep_ (ms: int) : unit = nativeOnly

let sleep (ms: int) : ActorOp<unit> =
    actor {
        sleep_ ms
        return ()
    }
#else
#if FABLE_COMPILER

// Python/JS: yield to event loop via Async.Sleep
let sleep (ms: int) : ActorOp<unit> = Async.Sleep ms

#else

// .NET: block the thread, wrapped in Async for uniform signature
let sleep (ms: int) : ActorOp<unit> =
    async {
        System.Threading.Thread.Sleep(ms)
        return ()
    }

#endif
#endif

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
