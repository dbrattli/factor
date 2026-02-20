# Factor development tasks

build_path := "build"
src_path := "src"

fable_beam := "dotnet run --project ../fable/main/src/Fable.Cli --"
fable_lib_beam := "fable_modules/fable-library-beam"

# List available recipes
default:
    @just --list

# Clean build artifacts
clean:
    rm -rf {{build_path}}

# Build F# to Erlang via Fable.Beam
build: clean
    mkdir -p {{build_path}}
    {{fable_beam}} {{src_path}} --exclude Fable.Core --lang beam --outDir {{build_path}}

# Build F# project only (type check)
check:
    dotnet build src/

# Format source files
format:
    dotnet fantomas src -r

# Setup tooling
setup:
    dotnet tool restore

# Type-check test project
check-test:
    dotnet build test/

# Build test F# to Erlang via Fable.Beam
build-test: build
    {{fable_beam}} test --exclude Fable.Core --lang beam --outDir {{build_path}} --noCache

# Run tests on BEAM
test: build build-test
    cp test_runner.erl {{build_path}}/
    cp src/erl/*.erl {{build_path}}/
    cd {{build_path}} && erlc -o {{fable_lib_beam}} {{fable_lib_beam}}/*.erl && erlc *.erl && erl -pa {{fable_lib_beam}} -noshell -eval "test_runner:run()" -s init stop

# Build and check
all: check build

# Check all (src + test)
check-all: check check-test

# --- Timeflies example ---

timeflies_path := "examples/timeflies"
timeflies_build := timeflies_path / "build"
timeflies_src := timeflies_path / "src"
# Path to rebar3 deps relative to inside timeflies_build (i.e. from examples/timeflies/build/)
timeflies_deps := "../_build/default/lib"

# Build timeflies example: F# → Erlang, fetch deps, compile
build-timeflies: build
    mkdir -p {{timeflies_build}}
    {{fable_beam}} {{timeflies_src}} --exclude Fable.Core --lang beam --outDir {{timeflies_build}}
    cd {{timeflies_path}} && rebar3 compile
    cp {{build_path}}/*.erl {{timeflies_build}}/ 2>/dev/null || true
    cp src/erl/*.erl {{timeflies_build}}/
    cp {{timeflies_src}}/erl/*.erl {{timeflies_build}}/
    cd {{timeflies_build}} && erlc -o {{fable_lib_beam}} {{fable_lib_beam}}/*.erl
    cd {{timeflies_build}} && erlc -pa {{fable_lib_beam}} \
        -pa {{timeflies_deps}}/cowboy/ebin \
        -pa {{timeflies_deps}}/cowlib/ebin \
        -pa {{timeflies_deps}}/ranch/ebin \
        -pa {{timeflies_deps}}/jsx/ebin \
        *.erl

# Run timeflies demo server on http://localhost:3000
run-timeflies: build-timeflies
    cd {{timeflies_build}} && erl \
        -pa {{fable_lib_beam}} \
        -pa {{timeflies_deps}}/cowboy/ebin \
        -pa {{timeflies_deps}}/cowlib/ebin \
        -pa {{timeflies_deps}}/ranch/ebin \
        -pa {{timeflies_deps}}/jsx/ebin \
        -noshell \
        -eval "factor_timeflies_app:start()" \
        -eval "receive stop -> ok end"
