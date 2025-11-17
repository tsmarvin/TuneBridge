// Enable test parallelization for faster test execution
// Parallelize at the class level - tests within a class run sequentially, but different classes run in parallel
[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.ClassLevel )]
