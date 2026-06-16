// Enable test parallelization for faster test execution.
// Parallelize at the class level: one worker per processor, classes run in parallel,
// but methods within a single class run sequentially since many tests share class-level
// fixtures and Redis containers.
[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.ClassLevel )]
