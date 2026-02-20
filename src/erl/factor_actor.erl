-module(factor_actor).
-export([spawn_actor/1, send_msg/2, receive_msg/1, self_pid/0,
         spawn_linked/1, monitor_process/1, demonitor_process/1,
         kill_process/1, trap_exits/0,
         make_ref/0, register_child/2, unregister_child/1,
         register_exit/2, unregister_exit/1,
         exit_normal/0, format_reason/1, child_loop/0]).

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

%% Create a unique reference.
make_ref() ->
    erlang:make_ref().

%% Register a child handler for a specific ref in the process dictionary.
%% Handlers are stored in an Erlang map keyed by ref.
register_child(Ref, Handler) ->
    Map = case get(factor_children) of
        undefined -> #{};
        M -> M
    end,
    put(factor_children, Map#{Ref => Handler}).

%% Unregister a child handler for a specific ref.
unregister_child(Ref) ->
    case get(factor_children) of
        undefined -> ok;
        Map -> put(factor_children, maps:remove(Ref, Map))
    end.

%% Register an exit handler for a specific pid in the process dictionary.
%% Handlers are stored in an Erlang map keyed by pid.
register_exit(Pid, Handler) ->
    Map = case get(factor_exits) of
        undefined -> #{};
        M -> M
    end,
    put(factor_exits, Map#{Pid => Handler}).

%% Unregister an exit handler for a specific pid.
unregister_exit(Pid) ->
    case get(factor_exits) of
        undefined -> ok;
        Map -> put(factor_exits, maps:remove(Pid, Map))
    end.

%% Exit the current process normally.
exit_normal() ->
    exit(normal).

%% Format a crash reason as a string.
format_reason(Reason) ->
    list_to_binary(io_lib:format("~p", [Reason])).

%% Child process message loop.
%% Blocks waiting for timer, child, and EXIT messages. When a terminal
%% event triggers exit_normal(), the process terminates.
child_loop() ->
    receive
        {factor_timer, _Ref, Callback} ->
            Callback(ok),
            child_loop();
        {factor_child, Ref, Notification} ->
            %% Registry-based dispatching: look up handler by ref
            case get(factor_children) of
                undefined -> ok;
                Map ->
                    case Map of
                        #{Ref := Handler} ->
                            Handler(Notification);
                        #{} ->
                            ok
                    end
            end,
            child_loop();
        {'EXIT', Pid, Reason} ->
            case Reason of
                normal -> ok;
                _ ->
                    case get(factor_exits) of
                        undefined -> ok;
                        Map ->
                            case Map of
                                #{Pid := Handler} ->
                                    Handler(Reason);
                                #{} ->
                                    ok
                            end
                    end
            end,
            child_loop()
    end.
