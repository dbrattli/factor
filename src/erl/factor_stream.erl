-module(factor_stream).
-export([start_stream/0, start_single_stream/0, subscribe/2]).

%% --- Multicast Stream Actor ---
%%
%% Each stream is its own BEAM process holding a subscriber map #{Ref => Pid}.
%% Subscribe is synchronous (send + wait for ack) to prevent races.
%% spawn_link ties stream lifetime to creator.

start_stream() ->
    spawn_link(fun() -> stream_loop(#{}) end).

stream_loop(Subscribers) ->
    receive
        {stream_subscribe, Ref, Pid, AckRef} ->
            Pid ! {stream_subscribed, AckRef},
            stream_loop(Subscribers#{Ref => Pid});
        {stream_unsubscribe, Ref} ->
            stream_loop(maps:remove(Ref, Subscribers));
        {stream_notify, Notification} ->
            maps:foreach(
                fun(Ref, Pid) ->
                    Pid ! {factor_child, Ref, Notification}
                end,
                Subscribers
            ),
            stream_loop(Subscribers);
        {stream_notify_terminal, Notification} ->
            maps:foreach(
                fun(Ref, Pid) ->
                    Pid ! {factor_child, Ref, Notification}
                end,
                Subscribers
            ),
            %% Terminal event: clear subscribers and exit normally
            exit(normal)
    end.

%% --- Single-subscriber Stream Actor ---
%%
%% Buffers notifications until a subscriber connects, then delivers
%% buffered + live notifications. Only one subscriber allowed.

start_single_stream() ->
    spawn_link(fun() -> single_stream_loop(waiting, []) end).

single_stream_loop(waiting, Pending) ->
    receive
        {stream_subscribe, Ref, Pid, AckRef} ->
            %% Flush pending notifications
            lists:foreach(
                fun(N) -> Pid ! {factor_child, Ref, N} end,
                lists:reverse(Pending)
            ),
            Pid ! {stream_subscribed, AckRef},
            single_stream_loop({subscribed, Ref, Pid}, []);
        {stream_notify, Notification} ->
            single_stream_loop(waiting, [Notification | Pending]);
        {stream_notify_terminal, Notification} ->
            single_stream_loop(waiting, [Notification | Pending])
    end;
single_stream_loop({subscribed, Ref, Pid}, _Pending) ->
    receive
        {stream_unsubscribe, Ref} ->
            single_stream_loop(waiting, []);
        {stream_notify, Notification} ->
            Pid ! {factor_child, Ref, Notification},
            single_stream_loop({subscribed, Ref, Pid}, []);
        {stream_notify_terminal, Notification} ->
            Pid ! {factor_child, Ref, Notification},
            exit(normal)
    end.

%% --- Synchronous subscribe ---
%%
%% Sends subscribe request and waits for acknowledgement.
%% This prevents races between subscribe and first notify.

subscribe(StreamPid, Ref) ->
    AckRef = make_ref(),
    StreamPid ! {stream_subscribe, Ref, self(), AckRef},
    receive
        {stream_subscribed, AckRef} -> ok
    after 5000 ->
        error(stream_subscribe_timeout)
    end.
