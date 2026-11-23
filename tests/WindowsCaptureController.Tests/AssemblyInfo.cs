// The logger is static (one log directory per process): LogTests redirect it to a temp folder while
// other tests (controller calls) write log lines. Running test classes in parallel made the rotation test
// flaky (1 failure observed in phase 5). Tests are fast (< 1 s total): run them sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
