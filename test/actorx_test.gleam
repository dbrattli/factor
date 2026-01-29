//// Main entry point for ActorX tests
////
//// Tests are organized into separate modules:
//// - test/create_test.gleam - Creation operators (single, empty, from_list, etc.)
//// - test/transform_test.gleam - Transform operators (map, flat_map, concat_map)
//// - test/filter_test.gleam - Filter operators (filter, take, skip, etc.)
//// - test/builder_test.gleam - Builder module for `use` syntax
//// - test/test_utils.gleam - Shared test utilities

import gleeunit

pub fn main() -> Nil {
  gleeunit.main()
}
