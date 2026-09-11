using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SW2URDF.URDF;

namespace SW2URDF.URDFExport
{
    // Opt-in only: additional COM getters can themselves expose native API defects.
    // Never infer a sign conversion or replace the source tensor from these observations.
    internal sealed class InertiaDiagnostics : IDisposable
    {
        [ThreadStatic] private static InertiaDiagnostics current;
        private readonly InertiaDiagnostics previous;
        private readonly Action<string> sink;
        private readonly string id = Guid.NewGuid().ToString("N");
        internal static bool Enabled { get { return current != null; } }

        internal static InertiaDiagnostics Begin(string link, Action<string> sink = null)
        {
            if (sink == null && Environment.GetEnvironmentVariable("SW2URDF_INERTIA_DIAGNOSTICS") != "1")
                return null;
            return new InertiaDiagnostics(link, sink ?? (text =>
                log4net.LogManager.GetLogger(typeof(InertiaDiagnostics)).Info(text)));
        }

        private InertiaDiagnostics(string link, Action<string> sink)
        {
            previous = current;
            this.sink = sink;
            current = this;
            var assembly = typeof(InertiaDiagnostics).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Write("begin link=" + link + " version=" + (version == null ? assembly.GetName().Version.ToString() : version.InformationalVersion) +
                " parent=" + (previous == null ? "none" : previous.id));
        }

        internal static void Write(string message)
        {
            if (current == null) return;
            // A failed diagnostic sink must never interrupt host restoration or export.
            try { current.sink("inertia-diagnostic id=" + current.id + " " + message); }
            catch (Exception) { }
        }

        internal static T Call<T>(string stage, Func<T> read)
        {
            Write("begin " + stage);
            try
            {
                T result = read();
                Write("end " + stage);
                return result;
            }
            catch (Exception error)
            {
                Write("failed " + stage + " " + error.GetType().Name + ": " + error.Message);
                throw;
            }
        }

        internal static void Observe(string name, Func<object> read)
        {
            if (!Enabled) return;
            try { Write(name + "=" + Format(Call(name, read))); }
            catch (Exception error) { Write(name + " unavailable: " + error.Message); }
        }

        internal static string Format(object value)
        {
            if (value == null) return "<null>";
            var array = value as double[];
            return array == null ? Convert.ToString(value, CultureInfo.InvariantCulture) :
                "[" + string.Join(",", array.Select(x => x.ToString("G17", CultureInfo.InvariantCulture))) + "]";
        }

        internal static void Snapshot(string stage, MassPropertySnapshot value)
        {
            if (!Enabled) return;
            Write(stage + " mass=" + Format(value.Mass) + " com=" + Format(value.CenterOfMass) +
                " tensor=" + Format(value.Moment) + (stage == "C.final" ? "" : " overrides=" + value.HasMassOverride + "," +
                value.HasCenterOfMassOverride + "," + value.HasInertiaOverride));
            Observe(stage + ".trace", () => value.Moment[0] + value.Moment[4] + value.Moment[8]);
            Observe(stage + ".eigenvalues", () => "[" + string.Join(",", MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfRowMajor(3, 3, value.Moment)
                .Evd().EigenValues.OrderBy(x => x.Real).Select(x => Format(x.Real) + "+(" + Format(x.Imaginary) + ")i")) + "]");
        }

        internal static void Final(Link link)
        {
            if (!Enabled) return;
            var state = link.InertialEditing;
            Write("C editing=" + (state == null ? "none" :
                "mass:" + state.MassEdited + ",tensor:" + state.TensorEdited + ",legacy:" +
                state.LegacyValuesPreserved + ",calibrationDisabled:" + state.CalibrationDisabled +
                ",explicitSource:" + state.CalibrateExplicitSource));
            Snapshot("C.final", new MassPropertySnapshot(link.Inertial.Mass.Value,
                link.Inertial.Origin.GetXYZ(), link.Inertial.Inertia.GetMoment()));
        }

        internal static string InvalidSourceLabel(Link link)
        {
            var state = link.InertialEditing;
            return state != null && (state.TensorEdited || state.LegacyValuesPreserved) ?
                "Edited or preserved inertia" : state != null && state.MassEdited ?
                "Inertia after mass editing" : "Computed inertia";
        }

        public void Dispose()
        {
            Write("end scope");
            current = previous;
        }
    }
}
