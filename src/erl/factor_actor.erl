-module(factor_actor).
-export([spawn_actor/1, send_msg/2, receive_msg/1, self_pid/0,
         spawn_linked/1, monitor_process/1, demonitor_process/1,
         kill_process/1, trap_exits/0]).

%% Spawn a new process that runs Fun(ok).
spawn_actor(Fun) ->
    erlang:spawn(fun() -> Fun(ok) end).

%% Send a tagged message to a process.
send_msg(Pid, Msg) ->
    Pid ! {factor_msg, Msg},
    ok.

%% Block until a factor_msg arrives, then call Cont with the message.
receive_msg(Cont) ->
    receive
        {factor_msg, Msg} -> Cont(Msg)
    end.

%% Return self().
self_pid() ->
    self().

%% Spawn a linked process. If the child dies, the parent gets an EXIT signal.
%% If the parent dies, the child dies too.
spawn_linked(Fun) ->
    spawn_link(fun() -> Fun(ok) end).

%% Monitor a process. Returns a monitor reference.
monitor_process(Pid) ->
    erlang:monitor(process, Pid).

%% Demonitor a process, flushing any pending DOWN message.
demonitor_process(Ref) ->
    erlang:demonitor(Ref, [flush]).

%% Kill a process immediately.
kill_process(Pid) ->
    exit(Pid, kill).

%% Enable trap_exit so EXIT signals become messages instead of killing us.
trap_exits() ->
    process_flag(trap_exit, true).
