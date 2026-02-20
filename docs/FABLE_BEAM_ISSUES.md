# Fable.Beam Issues Found While Testing Factor

## Issue 1: `let rec` inside closures generates invalid Erlang (Critical)

### Description

When using `let rec` inside a closure (e.g., inside a `Subscribe` function), Fable.Beam generates a module-level function call instead of referencing the local variable.

### F# Code

```fsharp
let concat (sources: Observable<'a> list) : Observable<'a> =
    { Subscribe =
        fun observer ->
            let rec subscribeTo (remaining: Observable<'a> list) =
                match remaining with
                | [] -> observer.Notify(OnCompleted)
                | current :: rest ->
                    current.Subscribe({ Notify = fun n ->
                        match n with
                        | OnCompleted -> subscribeTo rest  // recursive call
                        | _ -> observer.Notify(n) })
                    |> ignore
            subscribeTo sources
            { Dispose = fun () -> () } }
```

### Generated Erlang (Broken)

```erlang
concat(Sources) ->
    #{subscribe => fun(Observer) ->
        SubscribeTo = fun(Remaining) ->
            ...
            (subscribe_to())(erlang:tl(Remaining))  %% ERROR: subscribe_to/0 undefined
            ...
        end,
        SubscribeTo(Sources),
        ...
    end}.
```

The recursive call generates `(subscribe_to())(...)` — a module-level function call — instead of `SubscribeTo(...)` (the local variable).

### Expected Erlang

```erlang
%% Option A: Use process dictionary for self-reference
erlang:put(subscribe_to, fun(Remaining) -> ... (erlang:get(subscribe_to))(Rest) ... end)

%% Option B: Use named fun (Erlang named funs support recursion)
SubscribeTo = fun SubscribeTo(Remaining) -> ... SubscribeTo(Rest) ... end
```

### Workaround

Replace `let rec` with a mutable function reference:

```fsharp
let mutable subscribeTo: Observable<'a> list -> unit = fun _ -> ()
subscribeTo <- fun (remaining) ->
    match remaining with
    | [] -> observer.Notify(OnCompleted)
    | current :: rest ->
        current.Subscribe({ Notify = fun n ->
            match n with
            | OnCompleted -> subscribeTo rest
            | _ -> observer.Notify(n) })
        |> ignore
subscribeTo sources
```

### Files Affected

- `src/Combine.fs` — `concat` function
- `src/Error.fs` — `retry` function
- `src/Transform.fs` — `mergeInner` function
- `src/TimeShift.fs` — `interval`, `throttle` functions

---

## Issue 2: Process dictionary key collisions with mutable variables (Critical)

### Description

Fable.Beam implements F# `let mutable` variables using the Erlang process dictionary with fixed key names derived from the variable name. When multiple instances of the same function (or different functions with same-named variables) run in the same process, their mutable state collides.

### F# Code

```fsharp
let mergeInner (source: Observable<Observable<'a>>) : Observable<'a> =
    { Subscribe = fun observer ->
        let mutable innerCount = 0       // → erlang:put(inner_count, 0)
        let mutable disposed = false     // → erlang:put(disposed, false)
        let mutable outerStopped = false // → erlang:put(outer_stopped, false)
        ... }
```

### Problem

When you nest operators like `flatMap(G, flatMap(F, M))`, two instances of `mergeInner` run in the same process. Both write to `erlang:put(inner_count, ...)`, `erlang:put(disposed, ...)`, etc. — overwriting each other's state.

### Generated Erlang

```erlang
merge_inner(MaxConcurrency, Source) ->
    #{subscribe => fun(Observer) ->
        erlang:put(inner_count, 0),     %% Same key for ALL instances!
        erlang:put(outer_stopped, false),
        erlang:put(disposed, false),
        ...
    end}.
```

### Impact

This causes:
- **Nested flatMap**: State corruption, infinite loops, or `system_limit` errors
- **groupBy + flatMap**: Group values delivered to wrong groups
- **Any nested stateful operators**: Unpredictable behavior

### Expected Behavior

Each closure instantiation should have its own isolated mutable state. Options:
1. **Unique keys**: Generate keys with a counter suffix or use `erlang:make_ref()` as part of the key
2. **Closure-local refs**: Use `erlang:make_ref()` at closure creation time to create unique process dictionary keys
3. **Map-based state**: Store all mutable state in a map keyed by a unique ref, rather than individual process dictionary entries

### Example Fix

```erlang
merge_inner(MaxConcurrency, Source) ->
    #{subscribe => fun(Observer) ->
        StateRef = erlang:make_ref(),
        erlang:put({inner_count, StateRef}, 0),
        erlang:put({outer_stopped, StateRef}, false),
        erlang:put({disposed, StateRef}, false),
        ...
    end}.
```

### Test Cases That Fail Due to This

- `flat_map_monad_law_associativity_test` — nested flatMap causes `system_limit`
- `group_by_basic_test` — groupBy + flatMap state collision
- `group_by_with_strings_test` — same issue
- `skip_then_take_test` — skip + take state collision
- `switch_map_async_cancels_previous_test` — switchMap state collision

---

## Summary

| Issue | Severity | Impact |
|-------|----------|--------|
| `let rec` in closures | Critical | Compilation error |
| Process dictionary key collisions | Critical | Silent state corruption in nested operators |
