-module(factor_timer).
-export([schedule/2, cancel/1, process_timers/1]).

%% Schedule a callback to run in the caller's process after Ms milliseconds.
%%
%% Uses erlang:send_after to post a message to self(). The callback
%% will execute when the process calls process_timers/1 (or any
%% receive loop that handles {factor_timer, Ref, Callback} messages).
%%
%% Returns a timer reference for cancellation.
schedule(Ms, Callback) ->
    Ref = make_ref(),
    TimerRef = erlang:send_after(Ms, self(), {factor_timer, Ref, Callback}),
    {Ref, TimerRef}.

%% Cancel a scheduled timer.
cancel({Ref, TimerRef}) ->
    erlang:cancel_timer(TimerRef),
    %% Flush any already-delivered message
    receive
        {factor_timer, Ref, _} -> ok
    after 0 ->
        ok
    end.

%% Unified message pump: processes timer, child, and EXIT messages.
%%
%% Handles:
%% - {factor_timer, Ref, Callback} - timer callbacks
%% - {factor_child, Id, Notification} - child process messages
%% - {'EXIT', Pid, Reason} - linked process exit signals (requires trap_exit)
%%
%% ChildHandler and ExitHandler are called when child/exit messages arrive.
%% Pass 'undefined' for handlers you don't need.
%%
%% Use this as a timer-aware replacement for timer:sleep/1.
process_timers(TimeoutMs) ->
    EndTime = erlang:monotonic_time(millisecond) + TimeoutMs,
    process_timers_loop(EndTime).

process_timers_loop(EndTime) ->
    Remaining = EndTime - erlang:monotonic_time(millisecond),
    case Remaining =< 0 of
        true -> ok;
        false ->
            receive
                {factor_timer, _Ref, Callback} ->
                    Callback(ok),
                    process_timers_loop(EndTime);
                {factor_child, Id, Notification} ->
                    %% Forward child messages to the process dictionary handler
                    case get(factor_child_handler) of
                        undefined -> ok;
                        Handler -> Handler({Id, Notification})
                    end,
                    process_timers_loop(EndTime);
                {'EXIT', Pid, Reason} ->
                    %% Forward EXIT signals to the process dictionary handler
                    case get(factor_exit_handler) of
                        undefined -> ok;
                        Handler -> Handler({Pid, Reason})
                    end,
                    process_timers_loop(EndTime)
            after min(Remaining, 1) ->
                process_timers_loop(EndTime)
            end
    end.
