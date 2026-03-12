/// Platform abstraction for Fable.Actor.
///
/// BEAM: CPS-based interface using native processes and mailboxes.
/// Non-BEAM: No platform needed — uses MailboxProcessor directly.
module Fable.Actor.Platform

#if FABLE_COMPILER_BEAM

// === BEAM: CPS-based, native processes ===

open Fable.Core

[<Erase>]
type IActorPlatform =
    // Process lifecycle
    abstract spawn: f: (unit -> unit) -> obj
    abstract spawnLinked: f: (unit -> unit) -> obj
    abstract selfPid: unit -> obj
    abstract killProcess: pid: obj -> unit
    abstract exitNormal: unit -> unit
    abstract trapExits: unit -> unit
    abstract formatReason: reason: obj -> string

    // Message passing
    abstract sendMsg: pid: obj * msg: obj -> unit
    abstract receive: cont: (obj -> unit) -> unit
    abstract makeRef: unit -> obj
    abstract sendReply: pid: obj * ref: obj * value: obj -> unit
    abstract recvReply: ref: obj -> obj
    abstract refEquals: a: obj * b: obj -> bool

    // Monitoring
    abstract monitorProcess: pid: obj -> obj
    abstract demonitorProcess: ref: obj -> unit

    // Timers
    abstract timerSchedule: ms: int * callback: (unit -> unit) -> obj
    abstract timerCancel: timer: obj -> unit

[<ImportAll("factor_platform")>]
let platform: IActorPlatform = nativeOnly

#endif
