using Xunit;

// xUnit runs distinct test classes (implicit collections) in parallel by default. Every class in this project
// either runs subprocess-based peak-RSS measurements or does its own heavy 10,000-file packaging, and running
// any of that concurrently with an RSS-measured scenario would skew the measurement with unrelated CPU/disk/
// memory pressure. This assembly's tests are already slow by design, so trading away intra-assembly
// parallelism for measurement accuracy is the right default here.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
