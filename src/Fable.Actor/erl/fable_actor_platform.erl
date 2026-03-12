%% Platform adapter for BEAM target.
%%
%% Implements IActorPlatform interface.
%% Delegates to fable_actor_core and fable_actor_timer native modules.
-module(fable_actor_platform).

-export([
    spawn/1, spawn_linked/1, self_pid/0, make_ref/0,
    kill_process/1, exit_normal/0, trap_exits/0, format_reason/1,
    send_msg/2, receive_/1,
    send_reply/3, recv_reply/1, ref_equals/2,
    monitor_process/1, demonitor_process/1,
    timer_schedule/2, timer_cancel/1
]).

%% Process lifecycle
spawn(F) -> fable_actor_core:spawn_actor(F).
spawn_linked(F) -> fable_actor_core:spawn_linked(F).
self_pid() -> fable_actor_core:self_pid().
make_ref() -> fable_actor_core:make_ref().
kill_process(Pid) -> fable_actor_core:kill_process(Pid).
exit_normal() -> fable_actor_core:exit_normal().
trap_exits() -> fable_actor_core:trap_exits().
format_reason(Reason) -> fable_actor_core:format_reason(Reason).

%% Message passing — receive handles timer and EXIT messages transparently
send_msg(Pid, Msg) -> fable_actor_core:send_msg(Pid, Msg).
receive_(Cont) -> fable_actor_core:receive_msg(Cont).
send_reply(Pid, Ref, Value) -> fable_actor_core:send_reply(Pid, Ref, Value).
recv_reply(Ref) -> fable_actor_core:recv_reply(Ref).
ref_equals(A, B) -> A =:= B.

%% Process monitoring
monitor_process(Pid) -> fable_actor_core:monitor_process(Pid).
demonitor_process(Ref) -> fable_actor_core:demonitor_process(Ref).

%% Timer scheduling
timer_schedule(Ms, Callback) -> fable_actor_timer:schedule(Ms, Callback).
timer_cancel(Timer) -> fable_actor_timer:cancel(Timer).
