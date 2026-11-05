using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class EngineeringFunctions
{
    private enum UnitCategory : byte
    {
        Mass,
        Distance,
        Time,
        Pressure,
        Force,
        Energy,
        Power,
        Magnetism,
        Temperature,
        Volume,
        Area,
        Information,
        Speed,
    }

    // Factor is the size of the unit in the category's base unit (g, m, s, Pa, N, J, W, T, K, m³, m², bit, m/s); Power is the exponent a
    // metric prefix is raised to (km² is (1000 m)²).
    private readonly record struct UnitInfo(UnitCategory Category, double Factor, bool Prefixable = false, int Power = 1, bool Binary = false);

    private static readonly Dictionary<string, UnitInfo> Units = BuildUnits();

    private static readonly (string Name, double Factor)[] Prefixes =
    [
        ("Yi", 1208925819614629174706176.0), ("Zi", 1180591620717411303424.0), ("Ei", 1152921504606846976.0), ("Pi", 1125899906842624.0),
        ("Ti", 1099511627776.0), ("Gi", 1073741824.0), ("Mi", 1048576.0), ("ki", 1024.0), ("da", 10),
        ("Y", 1e24), ("Z", 1e21), ("E", 1e18), ("P", 1e15), ("T", 1e12), ("G", 1e9), ("M", 1e6), ("k", 1e3), ("h", 1e2), ("e", 10),
        ("d", 1e-1), ("c", 1e-2), ("m", 1e-3), ("u", 1e-6), ("n", 1e-9), ("p", 1e-12), ("f", 1e-15), ("a", 1e-18), ("z", 1e-21), ("y", 1e-24),
    ];

    private static Dictionary<string, UnitInfo> BuildUnits()
    {
        var u = new Dictionary<string, UnitInfo>(StringComparer.Ordinal);
        void Add(UnitCategory c, double f, bool prefix, int power, params string[] names)
        {
            foreach (string n in names)
            {
                u[n] = new UnitInfo(c, f, prefix, power);
            }
        }

        const double ly = 9.4607304725808e15;
        const double pc = 3.0856775813057e16;
        const double inch = 0.0254;
        double point = inch / 72, pica = inch / 6;

        // Weight and mass (base: gram)
        Add(UnitCategory.Mass, 1, true, 1, "g");
        Add(UnitCategory.Mass, 14593.8424189287, false, 1, "sg");
        Add(UnitCategory.Mass, 453.59237, false, 1, "lbm");
        Add(UnitCategory.Mass, 1.660538782e-24, true, 1, "u");
        Add(UnitCategory.Mass, 28.349523125, false, 1, "ozm");
        Add(UnitCategory.Mass, 0.06479891, false, 1, "grain");
        Add(UnitCategory.Mass, 45359.237, false, 1, "cwt", "shweight");
        Add(UnitCategory.Mass, 50802.34544, false, 1, "uk_cwt", "lcwt", "hweight");
        Add(UnitCategory.Mass, 6350.29318, false, 1, "stone");
        Add(UnitCategory.Mass, 907184.74, false, 1, "ton");
        Add(UnitCategory.Mass, 1016046.9088, false, 1, "uk_ton", "LTON", "brton");

        // Distance (base: metre)
        Add(UnitCategory.Distance, 1, true, 1, "m");
        Add(UnitCategory.Distance, 1609.344, false, 1, "mi");
        Add(UnitCategory.Distance, 1852, false, 1, "Nmi");
        Add(UnitCategory.Distance, inch, false, 1, "in");
        Add(UnitCategory.Distance, 0.3048, false, 1, "ft");
        Add(UnitCategory.Distance, 0.9144, false, 1, "yd");
        Add(UnitCategory.Distance, 1e-10, true, 1, "ang");
        Add(UnitCategory.Distance, 1.143, false, 1, "ell");
        Add(UnitCategory.Distance, ly, false, 1, "ly");
        Add(UnitCategory.Distance, pc, true, 1, "parsec", "pc");
        Add(UnitCategory.Distance, point, false, 1, "Pica", "Picapt");
        Add(UnitCategory.Distance, pica, false, 1, "pica");
        Add(UnitCategory.Distance, 1609.3472186944373, false, 1, "survey_mi");

        // Time (base: second)
        Add(UnitCategory.Time, 31557600, false, 1, "yr");
        Add(UnitCategory.Time, 86400, false, 1, "day", "d");
        Add(UnitCategory.Time, 3600, false, 1, "hr");
        Add(UnitCategory.Time, 60, false, 1, "mn", "min");
        Add(UnitCategory.Time, 1, true, 1, "sec", "s");

        // Pressure (base: pascal)
        Add(UnitCategory.Pressure, 1, true, 1, "Pa", "p");
        Add(UnitCategory.Pressure, 101325, true, 1, "atm", "at");
        Add(UnitCategory.Pressure, 133.322368421053, true, 1, "mmHg");
        Add(UnitCategory.Pressure, 6894.757293168361, false, 1, "psi");
        Add(UnitCategory.Pressure, 133.322368421053, false, 1, "Torr");

        // Force (base: newton)
        Add(UnitCategory.Force, 1, true, 1, "N");
        Add(UnitCategory.Force, 1e-5, true, 1, "dyn", "dy");
        Add(UnitCategory.Force, 4.4482216152605, false, 1, "lbf");
        Add(UnitCategory.Force, 0.00980665, true, 1, "pond");

        // Energy (base: joule)
        Add(UnitCategory.Energy, 1, true, 1, "J");
        Add(UnitCategory.Energy, 1e-7, true, 1, "e");
        Add(UnitCategory.Energy, 4.184, true, 1, "c");
        Add(UnitCategory.Energy, 4.1868, true, 1, "cal");
        Add(UnitCategory.Energy, 1.602176487e-19, true, 1, "eV", "ev");
        Add(UnitCategory.Energy, 2684519.537696172792, false, 1, "HPh", "hh");
        Add(UnitCategory.Energy, 3600, true, 1, "Wh", "wh");
        Add(UnitCategory.Energy, 1.3558179483314004, false, 1, "flb");
        Add(UnitCategory.Energy, 1055.05585262, false, 1, "BTU", "btu");

        // Power (base: watt)
        Add(UnitCategory.Power, 745.69987158227022, false, 1, "HP", "h");
        Add(UnitCategory.Power, 735.49875, false, 1, "PS");
        Add(UnitCategory.Power, 1, true, 1, "W", "w");

        // Magnetism (base: tesla)
        Add(UnitCategory.Magnetism, 1, true, 1, "T");
        Add(UnitCategory.Magnetism, 1e-4, true, 1, "ga");

        // Temperature: the factor is unused; the conversion goes through kelvins.
        foreach (string n in new[] { "C", "cel", "F", "fah", "K", "kel", "Rank", "Reau" })
        {
            u[n] = new UnitInfo(UnitCategory.Temperature, 1);
        }

        // Volume (base: cubic metre)
        Add(UnitCategory.Volume, 4.92892159375e-6, false, 1, "tsp");
        Add(UnitCategory.Volume, 5e-6, false, 1, "tspm");
        Add(UnitCategory.Volume, 1.478676478125e-5, false, 1, "tbs");
        Add(UnitCategory.Volume, 2.95735295625e-5, false, 1, "oz");
        Add(UnitCategory.Volume, 2.365882365e-4, false, 1, "cup");
        Add(UnitCategory.Volume, 4.73176473e-4, false, 1, "pt", "us_pt");
        Add(UnitCategory.Volume, 5.6826125e-4, false, 1, "uk_pt");
        Add(UnitCategory.Volume, 9.46352946e-4, false, 1, "qt");
        Add(UnitCategory.Volume, 1.1365225e-3, false, 1, "uk_qt");
        Add(UnitCategory.Volume, 3.785411784e-3, false, 1, "gal");
        Add(UnitCategory.Volume, 4.54609e-3, false, 1, "uk_gal");
        Add(UnitCategory.Volume, 1e-3, true, 1, "l", "L", "lt");
        Add(UnitCategory.Volume, 1e-30, true, 3, "ang3", "ang^3");
        Add(UnitCategory.Volume, 0.158987294928, false, 1, "barrel");
        Add(UnitCategory.Volume, 0.03523907016688, false, 1, "bushel");
        Add(UnitCategory.Volume, 2.8316846592, false, 1, "regton", "GRT");
        Add(UnitCategory.Volume, 0.000425, false, 1, "Schooner");
        Add(UnitCategory.Volume, 0.0005, false, 1, "Humpen");
        Add(UnitCategory.Volume, 0.028316846592, false, 1, "ft3");
        Add(UnitCategory.Volume, 1.6387064e-5, false, 1, "in3");
        Add(UnitCategory.Volume, ly * ly * ly, false, 1, "ly3");
        Add(UnitCategory.Volume, 1, true, 3, "m3");
        Add(UnitCategory.Volume, 1609.344 * 1609.344 * 1609.344, false, 1, "mi3");
        Add(UnitCategory.Volume, 0.9144 * 0.9144 * 0.9144, false, 1, "yd3");
        Add(UnitCategory.Volume, 1852.0 * 1852 * 1852, false, 1, "Nmi3");
        Add(UnitCategory.Volume, point * point * point, false, 1, "Pica3", "Picapt3");
        Add(UnitCategory.Volume, 1.13267386368, false, 1, "MTON");

        // Area (base: square metre)
        Add(UnitCategory.Area, 4046.8564224, false, 1, "uk_acre");
        Add(UnitCategory.Area, 4046.87261, false, 1, "us_acre");
        Add(UnitCategory.Area, 1e-20, true, 2, "ang2", "ang^2");
        Add(UnitCategory.Area, 100, true, 1, "ar");
        Add(UnitCategory.Area, 0.09290304, false, 1, "ft2");
        Add(UnitCategory.Area, 10000, false, 1, "ha");
        Add(UnitCategory.Area, 0.00064516, false, 1, "in2");
        Add(UnitCategory.Area, ly * ly, false, 1, "ly2");
        Add(UnitCategory.Area, 1, true, 2, "m2");
        Add(UnitCategory.Area, 2500, false, 1, "Morgen");
        Add(UnitCategory.Area, 1609.344 * 1609.344, false, 1, "mi2");
        Add(UnitCategory.Area, 1852.0 * 1852, false, 1, "Nmi2");
        Add(UnitCategory.Area, point * point, false, 1, "Pica2", "Picapt2");
        Add(UnitCategory.Area, 0.9144 * 0.9144, false, 1, "yd2");

        // Information (base: bit)
        u["bit"] = new UnitInfo(UnitCategory.Information, 1, true, 1, true);
        u["byte"] = new UnitInfo(UnitCategory.Information, 8, true, 1, true);

        // Speed (base: metre per second)
        Add(UnitCategory.Speed, 0.514773333333333, false, 1, "admkn");
        Add(UnitCategory.Speed, 0.514444444444444, false, 1, "kn");
        Add(UnitCategory.Speed, 1.0 / 3600, false, 1, "m/h", "m/hr");
        Add(UnitCategory.Speed, 1, false, 1, "m/s", "m/sec");
        Add(UnitCategory.Speed, 0.44704, false, 1, "mph");
        return u;
    }

    private static void RegisterConvert(FunctionRegistry r) =>
        r.Add(FunctionDefinition.Create("CONVERT", 3, 3, [], ConvertFunction));

    // A unit name is looked up as written (names are case-sensitive); failing that, a metric prefix followed by a prefixable unit.
    private static bool TryUnit(string name, out UnitInfo unit, out double prefix)
    {
        prefix = 1;
        if (Units.TryGetValue(name, out unit))
        {
            return true;
        }

        foreach ((string p, double f) in Prefixes)
        {
            if (name.Length > p.Length && name.StartsWith(p, StringComparison.Ordinal) && Units.TryGetValue(name[p.Length..], out UnitInfo u)
                && u.Prefixable && (p.Length != 2 || p[1] != 'i' || u.Binary))
            {
                unit = u;
                prefix = u.Power == 1 ? f : Math.Pow(f, u.Power);
                return true;
            }
        }

        return false;
    }

    private static double ToKelvin(string unit, double v) => unit switch
    {
        "C" or "cel" => v + 273.15,
        "F" or "fah" => ((v - 32) * 5 / 9) + 273.15,
        "Rank" => v * 5 / 9,
        "Reau" => (v * 1.25) + 273.15,
        _ => v,
    };

    private static double FromKelvin(string unit, double k) => unit switch
    {
        "C" or "cel" => k - 273.15,
        "F" or "fah" => ((k - 273.15) * 9 / 5) + 32,
        "Rank" => k * 9 / 5,
        "Reau" => (k - 273.15) * 0.8,
        _ => k,
    };

    // Excel: a unit that is not known, or two units of different kinds, give #N/A.
    private static CalcValue ConvertFunction(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryNum(a[0], out double v, out CalcValue e1))
        {
            return e1;
        }

        if (a[1].Kind != ValueKind.Text || a[2].Kind != ValueKind.Text)
        {
            return Fx.Err(CalcError.NA);
        }

        string fromName = a[1].Text, toName = a[2].Text;
        if (!TryUnit(fromName, out UnitInfo from, out double fp) || !TryUnit(toName, out UnitInfo to, out double tp) || from.Category != to.Category)
        {
            return Fx.Err(CalcError.NA);
        }

        if (from.Category == UnitCategory.Temperature)
        {
            return Num(FromKelvin(toName, ToKelvin(fromName, v)));
        }

        if (fromName == toName)
        {
            return Num(v);
        }

        return Num(v * from.Factor * fp / (to.Factor * tp));
    }
}
