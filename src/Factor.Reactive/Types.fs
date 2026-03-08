/// Re-exports core types from Factor.Actor.Types for convenience.
///
/// Users of Factor.Reactive can open this module to get all types.
module Factor.Reactive.Types

open Factor.Actor.Types

// Re-export all types so existing `open Factor.Reactive.Types` works
type Msg<'T> = Factor.Actor.Types.Msg<'T>
type Handle = Factor.Actor.Types.Handle
type Observer<'T> = Factor.Actor.Types.Observer<'T>
type ChannelMsg<'T> = Factor.Actor.Types.ChannelMsg<'T>
type Observable<'T> = Factor.Actor.Types.Observable<'T>
type SupervisionPolicy = Factor.Actor.Types.SupervisionPolicy

let emptyHandle () = Factor.Actor.Types.emptyHandle ()
let compositeHandle handles = Factor.Actor.Types.compositeHandle handles
