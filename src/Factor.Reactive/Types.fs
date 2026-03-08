/// Re-exports core types from Factor.Agent.Types for convenience.
///
/// Users of Factor.Reactive can open this module to get all types.
module Factor.Reactive.Types

open Factor.Agent.Types

// Re-export all types so existing `open Factor.Reactive.Types` works
type Msg<'T> = Factor.Agent.Types.Msg<'T>
type Handle = Factor.Agent.Types.Handle
type Observer<'T> = Factor.Agent.Types.Observer<'T>
type ChannelMsg<'T> = Factor.Agent.Types.ChannelMsg<'T>
type Observable<'T> = Factor.Agent.Types.Observable<'T>
type SupervisionPolicy = Factor.Agent.Types.SupervisionPolicy

let emptyHandle () = Factor.Agent.Types.emptyHandle ()
let compositeHandle handles = Factor.Agent.Types.compositeHandle handles
