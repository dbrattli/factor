-module(interop_demo).
-export([run/0]).

%% Entry point — run from the shell with:
%%   erl -pa fable_modules/fable-library-beam -noshell -eval "interop_demo:run()" -s init stop

run() ->
    io:format("~n=== F# <-> Erlang Actor Interop ===~n~n"),

    %% --- Start Joe (native Erlang process) ---
    %% Joe is a simple receive loop that forwards replies to the demo process.
    Self = self(),
    JoePid = spawn(fun() -> joe_loop(Self) end),

    %% --- Start Dag (F# actor) ---
    %% interop_example:start_dag/1 returns #{pid => RawPid} (F# record compiles to Erlang map)
    DagWrapped = interop_example:start_dag(JoePid),
    DagPid = maps:get(pid, DagWrapped),

    %% === Demo 1: Fire-and-forget (Erlang → F#) ===
    %%
    %% Actor.send wraps messages as {fable_actor_msg, Msg}, so Erlang must do the same.
    %% F# DU: HelloFrom of string → Erlang tuple: {hello_from, <<"Joe">>}
    io:format("  [Joe/Erlang] Saying hello to Dag...~n"),
    fable_actor_core:send_msg(DagPid, {hello_from, <<"Joe">>}),

    %% Wait for Dag's raw reply (no envelope — just a binary)
    receive
        {joe_said, Reply} ->
            io:format("  [Joe/Erlang] Got reply: ~s~n~n", [Reply])
    after 1000 ->
        io:format("  Timeout waiting for reply!~n~n")
    end,

    %% === Demo 2: Call pattern (Erlang → F#) ===
    %%
    %% F# ReplyChannel<string> compiles to: #{reply => fun(V) -> ... end}
    %% Erlang can construct this map manually to call F# actors.
    io:format("  [Joe/Erlang] Asking Dag his full name (call pattern)...~n"),
    Ref = make_ref(),
    Me = self(),  %% Capture caller pid BEFORE the lambda (self() in lambda runs in callee's process!)
    Rc = #{reply => fun(V) -> Me ! {fable_actor_reply, Ref, V} end},
    fable_actor_core:send_msg(DagPid, {ask_name, Rc}),

    receive
        {fable_actor_reply, Ref, Name} ->
            io:format("  [Joe/Erlang] Dag says his name is: ~s~n", [Name])
    after 1000 ->
        io:format("  Timeout waiting for name!~n")
    end,

    io:format("~nDone!~n").

%% Joe's receive loop — forwards any message back to the demo process.
joe_loop(Parent) ->
    receive
        Msg -> Parent ! {joe_said, Msg}
    end.
