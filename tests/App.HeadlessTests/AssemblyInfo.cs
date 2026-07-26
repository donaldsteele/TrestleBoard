using Xunit;

// Avalonia initializes ONCE per process and every test class shares that single
// HeadlessUnitTestSession (see HeadlessSession). Two classes dispatching into it at the same time
// race the dispatcher's ResetForUnitTests teardown, which then executes a queued layout pass
// against a torn-down font manager ("fonts:SystemFonts was not present"). Serialising the
// assembly is the fix; these tests are fast and there are few of them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
