-module(factor_timer).
-export([schedule/2, cancel/1]).

%% Schedule a callback to fire in the caller's process after Ms milliseconds.
%%
%% Uses erlang:send_after to post a {factor_timer, Ref, Callback} message
%% to self(). The callback executes when receive_msg handles it transparently.
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
