using System;
using System.IO;

namespace Egis.Revit.SystemFilter.WorkItemRunner.Services;

public static class InputChecks
{
    public static void EnsureValid(RunnerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ModelPath))
            throw new Exception("Input model is missing.");
        if (!File.Exists(options.ModelPath))
            throw new Exception("Input model does not exist.");
    }
}
