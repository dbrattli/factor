# ActorX development tasks

# List available recipes
default:
    @just --list

# Build the project
build:
    gleam build

# Run all tests
test:
    gleam test

# Check code formatting
check:
    gleam format --check src test

# Format source and test files
format:
    gleam format src test

# Download dependencies
deps:
    gleam deps download

# Build and test
all: build test

# Clean build artifacts
clean:
    rm -rf build

# Run a specific test file (e.g., just test-file create)
test-file name:
    gleam test -- --module {{name}}_test

# Publish to Hex (requires authentication)
publish:
    gleam publish

# Generate documentation
docs:
    gleam docs build

# Open documentation in browser
docs-open:
    gleam docs build --open

# Run the Timeflies example
run-timeflies:
    cd examples/timeflies && gleam run
