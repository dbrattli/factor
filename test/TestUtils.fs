/// Test utilities for Fable.Actor tests
///
/// Provides assertion helpers and sleep FFI.
module Fable.Actor.TestUtils

open Fable.Actor.Types

#if FABLE_COMPILER
open Fable.Core

[<Emit("timer:sleep($0)")>]
let sleep (ms: int) : unit = nativeOnly
#else
let sleep (ms: int) : unit = System.Threading.Thread.Sleep(ms)
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
