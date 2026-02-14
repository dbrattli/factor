-module(test_runner).
-export([run/0]).

%% Simple test runner that discovers and runs all *_test/0 functions
%% from compiled test modules.

-define(TEST_MODULES, [
    create_test,
    transform_test,
    filter_test,
    combine_test,
    timeshift_test,
    subject_test,
    error_test,
    builder_test,
    new_operators_test,
    merge_inner_test,
    amb_forkjoin_test,
    share_test,
    group_by_test
]).

run() ->
    io:format("~n=== Factor Test Suite ===~n~n"),
    {TotalPass, TotalFail, TotalSkip} = lists:foldl(
        fun(Mod, {AccPass, AccFail, AccSkip}) ->
            {P, F, S} = run_module(Mod),
            {AccPass + P, AccFail + F, AccSkip + S}
        end,
        {0, 0, 0},
        ?TEST_MODULES
    ),
    Total = TotalPass + TotalFail + TotalSkip,
    io:format("~n=== Results ===~n"),
    io:format("Total: ~p | Passed: ~p | Failed: ~p | Skipped: ~p~n",
              [Total, TotalPass, TotalFail, TotalSkip]),
    case TotalFail of
        0 -> io:format("~nAll tests passed!~n"), ok;
        _ -> io:format("~nSome tests FAILED!~n"), halt(1)
    end.

run_module(Mod) ->
    io:format("--- ~s ---~n", [Mod]),
    Exports = Mod:module_info(exports),
    TestFuns = [F || {F, 0} <- Exports, is_test_fun(F)],
    lists:foldl(
        fun(Fun, {Pass, Fail, Skip}) ->
            case run_test(Mod, Fun) of
                pass -> {Pass + 1, Fail, Skip};
                fail -> {Pass, Fail + 1, Skip};
                skip -> {Pass, Fail, Skip + 1}
            end
        end,
        {0, 0, 0},
        lists:sort(TestFuns)
    ).

is_test_fun(Name) ->
    Str = atom_to_list(Name),
    lists:suffix("_test", Str).

run_test(Mod, Fun) ->
    try
        Mod:Fun(),
        io:format("  \e[32m✓\e[0m ~s~n", [Fun]),
        pass
    catch
        error:{badmatch, _} = Err ->
            io:format("  \e[31m✗\e[0m ~s~n    ~p~n", [Fun, Err]),
            fail;
        Class:Reason:Stack ->
            io:format("  \e[31m✗\e[0m ~s~n    ~p:~p~n    ~p~n", [Fun, Class, Reason, Stack]),
            fail
    end.
