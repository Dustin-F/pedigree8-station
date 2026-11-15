using Xunit;

// Tests replace the shared DbTime clock, so test classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
