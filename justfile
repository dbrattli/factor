# Factor development tasks

# Development mode: use local Fable repo instead of dotnet tool
# Usage: just dev=true test
dev := "false"
fable_repo := justfile_directory() / "../fable/beam-improvements-16"
fable := if dev == "true" { "dotnet run --project " + fable_repo / "src/Fable.Cli" + " --" } else { "dotnet fable" }

# List available recipes
default:
    @just --list

# Clean build artifacts
clean:
    rm -rf apps _build

# Build F# to Erlang via Fable.Beam, then compile with rebar3
build: clean
    {{fable}} src/Factor.Reactive --exclude Fable.Core --lang beam --outDir apps/factor --noCache
    mv apps/factor/src/factor_reactive.app.src apps/factor/src/factor.app.src
    sed -i '' 's/factor_reactive/factor/' apps/factor/src/factor.app.src
    cp src/Factor.Beam/erl/*.erl apps/factor/src/
    rebar3 compile

# Build F# projects only (type check)
check:
    dotnet build src/Factor.Actor
    dotnet build src/Factor.Beam
    dotnet build src/Factor.Reactive

# Format source files
format:
    dotnet fantomas src -r

# Setup tooling
restore:
    dotnet tool restore

# Type-check test project
check-test:
    dotnet build test/

# Build test F# to Erlang via Fable.Beam
build-test: build
    {{fable}} test --exclude Fable.Core --lang beam --outDir apps/factor --noCache
    rm -f apps/factor/src/test.app.src
    cp test/test_runner.erl apps/factor/src/
    rebar3 compile

# Run tests on BEAM
test: build-test
    erl -pa _build/default/lib/*/ebin -noshell -eval "test_runner:run()" -s init stop

# Build and check
all: check build

# Check all (src + test)
check-all: check check-test

# --- Interop example ---

interop_path := "examples/interop"
interop_src := interop_path / "src"

# Build interop example: F# → Erlang, compile with rebar3
build-interop: build
    {{fable}} {{interop_src}} --exclude Fable.Core --lang beam --outDir apps/factor --noCache
    rm -f apps/factor/src/interop_example.app.src
    cp {{interop_path}}/erl/*.erl apps/factor/src/
    rebar3 compile

# Run interop demo: Joe (Erlang) talks to Dag (F#)
run-interop: build-interop
    erl -pa _build/default/lib/*/ebin -noshell -eval "interop_demo:run()" -s init stop

# --- Timeflies example ---

timeflies_path := "examples/timeflies"
timeflies_src := timeflies_path / "src"
timeflies_app := timeflies_path / "apps/timeflies"

# Build timeflies example: F# → Erlang, compile with rebar3
build-timeflies: build
    {{fable}} {{timeflies_src}} --exclude Fable.Core --lang beam --outDir {{timeflies_app}} --noCache
    cp apps/factor/src/*.erl {{timeflies_app}}/src/
    cp {{timeflies_src}}/erl/*.erl {{timeflies_app}}/src/
    cd {{timeflies_path}} && rebar3 compile

# Run timeflies demo server on http://localhost:3000
run-timeflies: build-timeflies
    cd {{timeflies_path}} && erl \
        -pa _build/default/lib/*/ebin \
        -noshell \
        -eval "factor_timeflies_app:start()" \
        -eval "receive stop -> ok end"
