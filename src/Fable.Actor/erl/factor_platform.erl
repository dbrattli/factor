%% Platform adapter for BEAM target.
%%
%% Implements IActorPlatform interface.
%% Delegates to factor_actor and factor_timer native modules.
-module(factor_platform).

-export([
    spawn/1, spawn_linked/1, self_pid/0, make_ref/0,
    kill_process/1, exit_normal/0, trap_exits/0, format_reason/1,
    send_msg/2, receive_/1,
    send_reply/3, recv_reply/1, ref_equals/2,
    monitor_process/1, demonitor_process/1,
    timer_schedule/2, timer_cancel/1
]).

%% Process lifecycle
spawn(F) -> factor_actor:spawn_actor(F).
spawn_linked(F) -> factor_actor:spawn_linked(F).
self_pid() -> factor_actor:self_pid().
make_ref() -> factor_actor:make_ref().
kill_process(Pid) -> factor_actor:kill_process(Pid).
exit_normal() -> factor_actor:exit_normal().
trap_exits() -> factor_actor:trap_exits().
format_reason(Reason) -> factor_actor:format_reason(Reason).

%% Message passing — receive handles timer and EXIT messages transparently
send_msg(Pid, Msg) -> factor_actor:send_msg(Pid, Msg).
receive_(Cont) -> factor_actor:receive_msg(Cont).
send_reply(Pid, Ref, Value) -> factor_actor:send_reply(Pid, Ref, Value).
recv_reply(Ref) -> factor_actor:recv_reply(Ref).
ref_equals(A, B) -> A =:= B.

%% Process monitoring
monitor_process(Pid) -> factor_actor:monitor_process(Pid).
demonitor_process(Ref) -> factor_actor:demonitor_process(Ref).

%% Timer scheduling
timer_schedule(Ms, Callback) -> factor_timer:schedule(Ms, Callback).
timer_cancel(Timer) -> factor_timer:cancel(Timer).
