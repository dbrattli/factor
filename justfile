# Factor development tasks

build_path := "build"
src_path := "src"

fable_beam := "dotnet run --project ../fable/fable-beam/src/Fable.Cli --"
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
