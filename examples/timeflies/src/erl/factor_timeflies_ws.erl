-module(factor_timeflies_ws).
-export([init/2, websocket_init/1, websocket_handle/2, websocket_info/2, terminate/3]).

%% Cowboy WebSocket handler for the timeflies demo.
%%
%% On init, calls the F# setupPipeline function which returns
%% {MouseObserver, Disposable}. Mouse events from the browser
%% are forwarded to the observer, and timer/send messages are
%% handled inline.

init(Req, State) ->
    {cowboy_websocket, Req, State}.

websocket_init(_State) ->
    %% SendFn sends a message to self() which websocket_info will forward as a WS frame
    Self = self(),
    SendFn = fun(Json) -> Self ! {send, Json} end,

    %% Call the compiled F# setup_pipeline function
    {MouseObserver, Disposable} = timeflies:setup_pipeline(SendFn),

    {ok, #{mouse_observer => MouseObserver, disposable => Disposable}}.

websocket_handle({text, Json}, State) ->
    case jsx:decode(Json, [return_maps]) of
        #{<<"x">> := X, <<"y">> := Y} when is_integer(X), is_integer(Y) ->
            MouseObserver = maps:get(mouse_observer, State),
            %% MousePos record compiles to #{x => X, y => Y}
            MousePos = #{x => X, y => Y},
            %% Notify with OnNext: Fable.Beam encodes OnNext as {on_next, Value}
            Notify = maps:get(notify, MouseObserver),
            Notify({on_next, MousePos}),
            {ok, State};
        _ ->
            {ok, State}
    end;
websocket_handle(_Frame, State) ->
    {ok, State}.

%% Handle timer callbacks from factor_timer:schedule
websocket_info({factor_timer, _Ref, Callback}, State) ->
    Callback(ok),
    {ok, State};

%% Handle child notifications from stream actors
websocket_info({factor_child, Ref, Notification}, State) ->
    case get(factor_children) of
        undefined -> ok;
        Map ->
            case maps:find(Ref, Map) of
                {ok, Handler} -> Handler(Notification);
                error -> ok
            end
    end,
    {ok, State};

%% Handle send messages from the Rx pipeline
websocket_info({send, Text}, State) ->
    {[{text, Text}], State};

websocket_info(_Info, State) ->
    {ok, State}.

terminate(_Reason, _Req, #{disposable := Disposable}) ->
    (maps:get(dispose, Disposable))(ok),
    ok;
terminate(_Reason, _Req, _State) ->
    ok.
