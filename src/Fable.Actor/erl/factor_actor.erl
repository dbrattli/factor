-module(factor_actor).
-export([spawn_actor/1, spawn_linked/1, self_pid/0, make_ref/0,
         send_msg/2, receive_msg/1,
         kill_process/1, exit_normal/0, trap_exits/0, format_reason/1,
         monitor_process/1, demonitor_process/1,
         send_reply/3, recv_reply/1]).

%% Spawn a new process that runs Fun(ok).
spawn_actor(Fun) ->
    erlang:spawn(fun() -> Fun(ok) end).

%% Spawn a linked process. If the child dies, the parent gets an EXIT signal.
spawn_linked(Fun) ->
    spawn_link(fun() -> Fun(ok) end).

%% Return self().
self_pid() ->
    self().

%% Create a unique reference.
make_ref() ->
    erlang:make_ref().

%% Send a tagged message to a process.
send_msg(Pid, Msg) ->
    Pid ! {factor_msg, Msg},
    ok.

%% Block until a factor_msg arrives, then call Cont with the message (CPS).
%% Transparently handles timer callbacks and EXIT signals while waiting.
receive_msg(Cont) ->
    receive
        {factor_msg, Msg} -> Cont(Msg);
        {factor_timer, _Ref, Callback} ->
            Callback(ok),
            receive_msg(Cont);
        {'EXIT', _Pid, normal} ->
            receive_msg(Cont);
        {'EXIT', Pid, Reason} ->
            %% Deliver EXIT as a message so actors can handle supervision
            Cont({factor_exit, Pid, Reason})
    end.

%% Kill a process immediately.
kill_process(Pid) ->
    exit(Pid, kill).

%% Exit the current process normally.
exit_normal() ->
    exit(normal).

%% Enable trap_exit so EXIT signals become messages instead of killing us.
trap_exits() ->
    process_flag(trap_exit, true).

%% Format a crash reason as a string.
format_reason(Reason) ->
    list_to_binary(io_lib:format("~p", [Reason])).

%% Monitor a process. Returns a monitor reference.
monitor_process(Pid) ->
    erlang:monitor(process, Pid).

%% Demonitor a process, flushing any pending DOWN message.
demonitor_process(Ref) ->
    erlang:demonitor(Ref, [flush]).

%% Send a reply tagged with Ref back to the caller Pid.
send_reply(Pid, Ref, Value) ->
    Pid ! {factor_reply, Ref, Value},
    ok.

%% Blocking selective receive for a reply matching Ref.
%% Dispatches timer callbacks while waiting.
recv_reply(Ref) ->
    receive
        {factor_reply, Ref, Reply} -> Reply;
        {factor_timer, _TRef, Callback} ->
            Callback(ok),
            recv_reply(Ref)
    end.
