-module(factor_timer).
-export([schedule/2, cancel/1]).

%% Schedule a callback to fire after Ms milliseconds.
%%
%% Spawns a process that sleeps then runs the callback. This avoids
%% coupling to the caller's receive loop.
%%
%% Returns a timer reference for cancellation.
schedule(Ms, Callback) ->
    Pid = erlang:spawn(fun() ->
        receive
            cancel -> ok
        after Ms ->
            Callback(ok)
        end
    end),
    Pid.

%% Cancel a scheduled timer.
cancel(Pid) ->
    Pid ! cancel,
    ok.
