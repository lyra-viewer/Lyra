using System.Runtime.CompilerServices;
using Lyra.Common;

namespace Lyra.Core.Tests;

internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Apply() => NumberCulture.Apply();
}