//// Tests for builder module - Gleam's `use` keyword support

import actorx
import actorx/builder.{bind, combine, filter_with, for_each, map_over, return}
import gleeunit/should
import test_utils.{
  get_bool_ref, get_list_ref, make_bool_ref, make_list_ref, test_observer,
}

// ============================================================================
// bind tests
// ============================================================================

pub fn bind_simple_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // use x <- bind(source) desugars to bind(source, fn(x) { ... })
  let observable = {
    use x <- bind(actorx.single(42))
    return(x * 2)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([84])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn bind_chained_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // Multiple binds - like F#'s let! x = ... let! y = ...
  let observable = {
    use x <- bind(actorx.single(10))
    use y <- bind(actorx.single(20))
    return(x + y)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([30])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn bind_three_values_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use x <- bind(actorx.single(1))
    use y <- bind(actorx.single(2))
    use z <- bind(actorx.single(3))
    return(x + y + z)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([6])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn bind_flatmap_behavior_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // bind with from_list creates flatMap behavior
  // For each x in [1,2,3], emit x+10
  let observable = {
    use x <- bind(actorx.from_list([1, 2, 3]))
    return(x + 10)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([11, 12, 13])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn bind_nested_flatmap_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // Cartesian product: [1,2] x [10,20] = [11,21,12,22]
  let observable = {
    use x <- bind(actorx.from_list([1, 2]))
    use y <- bind(actorx.from_list([10, 20]))
    return(x + y)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([11, 21, 12, 22])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn bind_empty_source_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use x <- bind(actorx.empty())
    return(x * 10)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn bind_with_empty_inner_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use _ <- bind(actorx.from_list([1, 2, 3]))
    builder.empty()
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// return/pure tests
// ============================================================================

pub fn return_single_value_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = return(42)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([42])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn pure_alias_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = builder.pure(99)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([99])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// map_over tests
// ============================================================================

pub fn map_over_transforms_values_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // map_over is like map but with use syntax
  let observable = {
    use x <- map_over(actorx.from_list([1, 2, 3]))
    x * 100
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([100, 200, 300])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn map_over_single_value_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use x <- map_over(actorx.single(5))
    x * x
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([25])
}

pub fn map_over_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use x <- map_over(actorx.empty())
    x * 10
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// filter_with tests
// ============================================================================

pub fn filter_with_keeps_matching_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // filter_with is like filter but with use syntax
  let observable = {
    use x <- filter_with(actorx.from_list([1, 2, 3, 4, 5]))
    x > 2
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([3, 4, 5])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn filter_with_all_pass_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use _x <- filter_with(actorx.from_list([1, 2, 3]))
    True
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn filter_with_none_pass_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use _x <- filter_with(actorx.from_list([1, 2, 3]))
    False
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn filter_with_even_numbers_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use x <- filter_with(actorx.from_list([1, 2, 3, 4, 5, 6]))
    x % 2 == 0
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([2, 4, 6])
}

// ============================================================================
// for_each tests
// ============================================================================

pub fn for_each_iterates_list_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // for_each is like F#'s for in computation expressions
  let observable = for_each([1, 2, 3], fn(x) { actorx.single(x * 10) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([10, 20, 30])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn for_each_empty_list_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = for_each([], fn(x) { actorx.single(x * 10) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn for_each_single_item_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = for_each([42], fn(x) { actorx.single(x * 2) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([84])
}

pub fn for_each_multiple_emissions_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // Each item in list produces multiple emissions
  let observable = for_each([1, 2], fn(x) { actorx.from_list([x, x * 10]) })

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 10, 2, 20])
}

// ============================================================================
// combine tests
// ============================================================================

pub fn combine_concatenates_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = combine(actorx.from_list([1, 2]), actorx.from_list([3, 4]))

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3, 4])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn combine_first_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = combine(actorx.empty(), actorx.from_list([1, 2, 3]))

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn combine_second_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = combine(actorx.from_list([1, 2, 3]), actorx.empty())

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}

pub fn combine_both_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable: actorx.Observable(Int) =
    combine(actorx.empty(), actorx.empty())

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// empty/zero tests
// ============================================================================

pub fn builder_empty_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = builder.empty()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn builder_zero_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = builder.zero()

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([])
  get_bool_ref(completed) |> should.equal(True)
}

// ============================================================================
// Complex composition tests
// ============================================================================

pub fn complex_composition_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // Complex composition combining bind, map, and filter
  let observable = {
    use x <- bind(actorx.from_list([1, 2, 3, 4, 5]))
    use y <- bind(actorx.single(10))
    case x % 2 == 0 {
      True -> return(x * y)
      False -> builder.empty()
    }
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  // Only even numbers (2, 4) multiplied by 10
  get_list_ref(results) |> should.equal([20, 40])
  get_bool_ref(completed) |> should.equal(True)
}

pub fn nested_for_each_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  // Nested iteration - cartesian product
  let observable = {
    use x <- bind(for_each([1, 2], fn(x) { actorx.single(x) }))
    use y <- bind(for_each([10, 20], fn(y) { actorx.single(y) }))
    return(x + y)
  }

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([11, 21, 12, 22])
}

pub fn map_over_then_filter_with_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let observable = {
    use x <- map_over(actorx.from_list([1, 2, 3, 4, 5]))
    x * 10
  }
  let observable2 = {
    use x <- filter_with(observable)
    x > 20
  }

  let _ =
    actorx.subscribe(observable2, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([30, 40, 50])
}

pub fn yield_from_identity_test() {
  let results = make_list_ref()
  let completed = make_bool_ref(False)
  let errors = make_list_ref()

  let source = actorx.from_list([1, 2, 3])
  let observable = builder.yield_from(source)

  let _ =
    actorx.subscribe(observable, test_observer(results, completed, errors))

  get_list_ref(results) |> should.equal([1, 2, 3])
}
