# Touching a line inside an untested `catch` costs diff coverage before it pays anything

_A trap this repo has paid for at least once._

The nine error-reporting call sites had no tests at all, so #404's one-line rewrite of each dropped diff coverage to **55%** without changing any behaviour. Budget for covering an error path you intend to retype — and note the technique that made it cheap: a test-only `CssBox` subclass whose `PerformLayoutImp` throws reaches every engine's handler, and the same subclassing trick states a driver-level condition (a box that hands back the same break record every pass) that no known markup produces.
