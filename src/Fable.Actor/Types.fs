/// Core types for Fable.Actor — cross-platform actor primitives.
///
/// No platform code, no dependencies beyond Fable.Core.
module Fable.Actor.Types

/// A reply channel that the receiver calls to send a response back to the caller.
type ReplyChannel<'Reply> = { Reply: 'Reply -> unit }

/// What the actor should do after handling a message.
type Next<'State> =
    | Continue of 'State
    | Stop

/// Notification when a child actor dies.
type ChildExited = { Pid: obj; Reason: obj }

exception ProcessExitException of string
