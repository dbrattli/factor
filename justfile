# Fable.Actor development tasks

# Development mode: use local Fable repo (required for BEAM target)
# The release Fable doesn't support [<ImportAll>] for BEAM yet
dev := "true"
fable_repo := justfile_directory() / "../fable/beam-improvements-17"
fable := if dev == "true" { "dotnet run --project " + fable_repo / "src/Fable.Cli" + " --" } else { "dotnet fable" }

# List available recipes
default:
    @just --list

# Clean build artifacts
clean:
    rm -rf apps _build

# Build F# to Erlang via Fable.Beam, then compile with rebar3
build: clean
    {{fable}} src/Fable.Actor --exclude Fable.Core --lang beam --outDir apps/factor --noCache
    mv apps/factor/src/fable_actor.app.src apps/factor/src/factor.app.src
    sed -i '' 's/fable_actor/factor/' apps/factor/src/factor.app.src
    cp src/Fable.Actor/erl/*.erl apps/factor/src/
    rebar3 compile

# Build F# projects only (type check)
check:
    dotnet build src/Fable.Actor

# --- Fable Python ---

fable_python := "dotnet run --project " + justfile_directory() / "../fable/main/src/Fable.Cli" + " --"

# Format source files
format:
    dotnet fantomas src -r

# Setup tooling
restore:
    dotnet tool restore

# Run .NET tests
test:
    dotnet run --project test/Test.fsproj

# Build and check
all: check build

# --- Timeflies example ---

timeflies_path := "examples/timeflies"
timeflies_src := timeflies_path / "src"
timeflies_app := timeflies_path / "apps/timeflies"

# Build timeflies example: F# → Erlang, compile with rebar3
build-timeflies: build
    {{fable}} {{timeflies_src}} --exclude Fable.Core --lang beam --outDir {{timeflies_app}} --noCache
    cp apps/factor/src/*.erl {{timeflies_app}}/src/
    cp {{timeflies_src}}/erl/factor_timeflies_ws.erl {{timeflies_app}}/src/
    cd {{timeflies_path}} && rebar3 compile

# Run timeflies demo server on http://localhost:3000
run-timeflies: build-timeflies
    cd {{timeflies_path}} && erl \
        -pa _build/default/lib/*/ebin \
        -noshell \
        -eval "factor_timeflies_app:start()" \
        -eval "receive stop -> ok end"

# --- Timeflies Python example ---

timeflies_py_path := "examples/timeflies-python"
timeflies_py_src := timeflies_py_path / "src"
timeflies_py_out := timeflies_py_path / "output"

# Build timeflies-python: F# → Python via Fable
build-timeflies-python:
    rm -rf {{timeflies_py_out}}
    {{fable_python}} {{timeflies_py_src}} --lang python --outDir {{timeflies_py_out}} --exclude Fable.Core --noCache
    touch {{timeflies_py_out}}/src/__init__.py
    touch {{timeflies_py_out}}/src/Fable_Actor/__init__.py
    cp {{timeflies_py_path}}/py/factor_platform.py {{timeflies_py_out}}/factor_platform.py

# Run timeflies-python demo
run-timeflies-python: build-timeflies-python
    cd {{timeflies_py_out}} && uv run --project ../pyproject.toml python program.py
