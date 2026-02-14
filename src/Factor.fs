/// Factor - Reactive Extensions for BEAM via Fable
///
/// Main API facade module that re-exports all public types and operators.
module Factor.Rx

open Factor.Types

// ============================================================================
// Re-export types
// ============================================================================

type Observable<'a> = Factor.Types.Observable<'a>
type Observer<'a> = Factor.Types.Observer<'a>
type Disposable = Factor.Types.Disposable
type Notification<'a> = Factor.Types.Notification<'a>

// ============================================================================
// Observer helpers
// ============================================================================

let makeObserver onNext onError onCompleted = Types.makeObserver onNext onError onCompleted
let makeNextObserver onNext = Types.makeNextObserver onNext
let onNext observer value = Types.onNext observer value
let onError observer error = Types.onError observer error
let onCompleted observer = Types.onCompleted observer
let notify observer notification = Types.notify observer notification

// ============================================================================
// Subscribe helper
// ============================================================================

let subscribe (observer: Observer<'a>) (observable: Observable<'a>) : Disposable = observable.Subscribe(observer)

// ============================================================================
// Disposable helpers
// ============================================================================

let emptyDisposable () = Types.emptyDisposable ()
let compositeDisposable disposables = Types.compositeDisposable disposables

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
let mergeInner maxConcurrency source = Transform.mergeInner maxConcurrency source
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
// Subject operators
// ============================================================================

let subject () = Subject.subject ()
let singleSubject () = Subject.singleSubject ()
let publish source = Subject.publish source
let share source = Subject.share source

// ============================================================================
// Error handling operators
// ============================================================================

let retry maxRetries source = Error.retry maxRetries source

let catch handler source = Error.catch handler source

// ============================================================================
// Interop operators
// ============================================================================

let tapSend send source = Interop.tapSend send source
