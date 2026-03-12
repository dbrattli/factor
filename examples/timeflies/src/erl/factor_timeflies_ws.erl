-module(factor_timeflies_ws).
-export([init/2, websocket_init/1, websocket_handle/2, websocket_info/2, terminate/3]).

%% Cowboy WebSocket handler for the timeflies demo.
%%
%% On init, calls the F# setupPipeline which returns an Actor (distributor).
%% Mouse events are sent to the distributor, which fans out to per-letter
%% child actors with delay timers. Letter actors send {send, Json} back
%% to the websocket process.

init(Req, State) ->
    {cowboy_websocket, Req, State}.

websocket_init(_State) ->
    Self = self(),
    SendFn = fun(Json) -> Self ! {send, Json} end,

    %% Returns Actor<MousePos> = #{pid => Pid}
    Distributor = timeflies:setup_pipeline(SendFn),
    {ok, #{distributor => Distributor}}.

websocket_handle({text, Json}, State) ->
    case jsx:decode(Json, [return_maps]) of
        #{<<"x">> := X, <<"y">> := Y} when is_integer(X), is_integer(Y) ->
            #{pid := Pid} = maps:get(distributor, State),
            Pid ! {factor_msg, #{x => X, y => Y}},
            {ok, State};
        _ ->
            {ok, State}
    end;
websocket_handle(_Frame, State) ->
    {ok, State}.

%% Forward letter position JSON to browser
websocket_info({send, Text}, State) ->
    {[{text, Text}], State};

websocket_info(_Info, State) ->
    {ok, State}.

terminate(_Reason, _Req, #{distributor := #{pid := Pid}}) ->
    exit(Pid, kill),
    ok;
terminate(_Reason, _Req, _State) ->
    ok.
