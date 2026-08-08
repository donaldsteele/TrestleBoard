using System.Runtime.CompilerServices;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M39. The one rule this suite cannot enforce with a type: never hand an <c>async</c> lambda to
/// Avalonia's <c>Session.Dispatch</c> directly.
///
/// <para>There is no <c>Dispatch(Func&lt;Task&gt;)</c> overload, so such a lambda binds to
/// <c>Dispatch&lt;T&gt;(Func&lt;T&gt;)</c> with <c>T = Task</c>. The call then returns
/// <c>Task&lt;Task&gt;</c> and the framework treats the body as finished at its first
/// <c>await</c> — so every assertion after that point runs unobserved, and any that throws is
/// swallowed. It compiles, it is one keyword away from correct, and the tests go green.</para>
///
/// <para>Eleven lambdas across six files were in that state when this was found, which is roughly
/// every asynchronous shell test the project had. Two of them were hiding real failures: a menu item
/// looked up by an <c>x:Name</c> no control has ever had, and a crash on removing the page the caret
/// was standing on. This test is a string search because the failure mode is a compiler-legal
/// overload choice — there is nothing else left to check it with.</para>
/// </summary>
public sealed class DispatchDisciplineTests
{
    [Fact]
    public void NoTestHandsAnAsyncLambdaStraightToDispatch()
    {
        string directory = Path.GetDirectoryName(ThisFile())!;
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            // This file names the pattern in order to ban it, and HeadlessSession.DispatchAsync is
            // the sanctioned wrapper — both talk about it, neither does it.
            string name = Path.GetFileName(file);
            if (name is "DispatchDisciplineTests.cs" or "HeadlessSession.cs")
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Session.Dispatch(async", StringComparison.Ordinal)
                    && !lines[i].Contains("HeadlessSession.DispatchAsync", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These call Session.Dispatch with an async lambda, so the body is dropped unawaited and "
            + "its assertions prove nothing. Use HeadlessSession.DispatchAsync instead: "
            + string.Join(", ", offenders));
    }

    /// <summary>Where this source file sits, so the scan does not depend on the working directory.</summary>
    private static string ThisFile([CallerFilePath] string path = "") => path;
}
