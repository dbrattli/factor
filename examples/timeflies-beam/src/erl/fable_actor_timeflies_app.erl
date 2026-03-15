-module(fable_actor_timeflies_app).
-export([start/0]).

%% Starts the Cowboy HTTP server with routes for the timeflies demo.
%%
%% Routes:
%%   GET /    → serves HTML page (fable_actor_timeflies_http)
%%   GET /ws  → WebSocket upgrade (fable_actor_timeflies_ws)

start() ->
    application:ensure_all_started(cowboy),

    Dispatch = cowboy_router:compile([
        {'_', [
            {"/", fable_actor_timeflies_http, []},
            {"/ws", fable_actor_timeflies_ws, []}
        ]}
    ]),

    {ok, _} = cowboy:start_clear(
        timeflies_listener,
        [{port, 3000}],
        #{env => #{dispatch => Dispatch}}
    ),

    io:format("Timeflies demo running at http://localhost:3000~n"),
    ok.
