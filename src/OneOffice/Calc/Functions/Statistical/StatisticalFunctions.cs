namespace RecursiveAI.OneOffice.Calc;

/// <summary>Registers the statistical function family: counting and averages, distributions, regression and the significance tests.</summary>
internal static class StatisticalFunctions
{
    public static void Register(FunctionRegistry r)
    {
        DescriptiveFunctions.Register(r);
        DistributionFunctions.Register(r);
        RegressionFunctions.Register(r);
    }
}
