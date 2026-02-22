/// Factor - Composable Actors for BEAM via Fable
///
/// Main API facade module that re-exports all public types and operators.
module Factor.Reactive

open Factor.Types

// ============================================================================
// Observer helpers (message-passing to process endpoints)
// ============================================================================

let onNext observer value = Process.onNext observer value
let onError observer error = Process.onError observer error
let onCompleted observer = Process.onCompleted observer
let notify observer msg = Process.notify observer msg

// ============================================================================
// Sender helpers (push to channel actors)
// ============================================================================

let pushNext sender value = Process.pushNext sender value
let pushError sender error = Process.pushError sender error
let pushCompleted sender = Process.pushCompleted sender

// ============================================================================
// Spawn / Subscribe helpers
// ============================================================================

/// Subscribe an observer endpoint to a factor.
let spawn (observer: Observer<'T>) (factor: Factor<'T>) : Handle = factor.Spawn(observer)

/// Subscribe to a factor with user callbacks.
/// Registers a child handler in the current process for receiving messages.
/// The caller's process must run a message loop (sleep/processTimers).
let subscribe
    (onNextFn: 'T -> unit)
    (onErrorFn: exn -> unit)
    (onCompletedFn: unit -> unit)
    (factor: Factor<'T>)
    : Handle =
    let ref = Process.makeRef ()

    Process.registerChild
        ref
        (fun msg ->
            let n = unbox<Msg<'T>> msg

            match n with
            | OnNext x -> onNextFn x
            | OnError e ->
                Process.unregisterChild ref
                onErrorFn e
            | OnCompleted ->
                Process.unregisterChild ref
                onCompletedFn ())

    let endpoint: Observer<'T> = { Pid = Process.selfPid (); Ref = ref }
    let handle = factor.Spawn(endpoint)

    { Dispose =
        fun () ->
            Process.unregisterChild ref
            handle.Dispose() }

// ============================================================================
// Handle helpers
// ============================================================================

let emptyHandle () = Types.emptyHandle ()
let compositeHandle handles = Types.compositeHandle handles

// ============================================================================
// Creation operators
// ============================================================================

let create subscribe = Create.create subscribe
let single value = Create.single value
let empty () = Create.empty ()
let never () = Create.never ()
let fail error = Create.fail error
let ofList items = Create.ofList items
let defer factory = Create.defer factory

// ============================================================================
// Transform operators
// ============================================================================

let map mapper source = Transform.map mapper source
let mapi mapper source = Transform.mapi mapper source
let flatMap mapper source = Transform.flatMap mapper source
let flatMapi mapper source = Transform.flatMapi mapper source
let concatMap mapper source = Transform.concatMap mapper source
let concatMapi mapper source = Transform.concatMapi mapper source
let mergeInner policy maxConcurrency source = Transform.mergeInner policy maxConcurrency source
let concatInner source = Transform.concatInner source
let switchInner source = Transform.switchInner source
let switchMap mapper source = Transform.switchMap mapper source
let switchMapi mapper source = Transform.switchMapi mapper source
let tap effect source = Transform.tap effect source
let startWith values source = Transform.startWith values source
let pairwise source = Transform.pairwise source
let scan initial accumulator source = Transform.scan initial accumulator source
let reduce initial accumulator source = Transform.reduce initial accumulator source
let groupBy keySelector source = Transform.groupBy keySelector source

// ============================================================================
// Filter operators
// ============================================================================

let filter predicate source = Filter.filter predicate source
let take count source = Filter.take count source
let skip count source = Filter.skip count source
let takeWhile predicate source = Filter.takeWhile predicate source
let skipWhile predicate source = Filter.skipWhile predicate source
let choose chooser source = Filter.choose chooser source
let distinctUntilChanged source = Filter.distinctUntilChanged source
let takeUntil other source = Filter.takeUntil other source
let takeLast count source = Filter.takeLast count source
let first source = Filter.first source
let last source = Filter.last source
let defaultIfEmpty defaultValue source = Filter.defaultIfEmpty defaultValue source
let sample sampler source = Filter.sample sampler source
let distinct source = Filter.distinct source

// ============================================================================
// Combining operators
// ============================================================================

let merge sources = Combine.merge sources
let merge2 source1 source2 = Combine.merge2 source1 source2
let combineLatest combiner source1 source2 = Combine.combineLatest combiner source1 source2
let withLatestFrom combiner sampler source = Combine.withLatestFrom combiner sampler source
let zip combiner source1 source2 = Combine.zip combiner source1 source2
let concat sources = Combine.concat sources
let concat2 source1 source2 = Combine.concat2 source1 source2
let amb sources = Combine.amb sources
let race sources = Combine.race sources
let forkJoin sources = Combine.forkJoin sources

// ============================================================================
// Time-based operators
// ============================================================================

let timer delayMs = TimeShift.timer delayMs
let interval periodMs = TimeShift.interval periodMs
let delay ms source = TimeShift.delay ms source
let debounce ms source = TimeShift.debounce ms source
let throttle ms source = TimeShift.throttle ms source
let timeout ms source = TimeShift.timeout ms source

// ============================================================================
// Channel operators
// ============================================================================

let channel () = Channel.channel ()
let singleChannel () = Channel.singleChannel ()
let publish source = Channel.publish source
let share source = Channel.share source

// ============================================================================
// Error handling operators
// ============================================================================

let retry maxRetries source = Error.retry maxRetries source
let catch handler source = Error.catch handler source

// ============================================================================
// Interop operators
// ============================================================================

let tapSend send source = Interop.tapSend send source

// ============================================================================
// Actor types and operators
// ============================================================================

type Pid<'Msg> = Actor.Pid<'Msg>
type Actor<'Msg, 'T> = Actor.Actor<'Msg, 'T>
type ActorContext<'Msg> = Actor.ActorContext<'Msg>

let actor = Actor.actor
let spawnActor body = Actor.spawn body
let send pid msg = Actor.send pid msg
let self () = Actor.self ()
