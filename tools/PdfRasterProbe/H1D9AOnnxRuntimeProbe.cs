using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;

namespace PdfRasterProbe
{
    internal static class H1D9AOnnxRuntimeProbe
    {
        private const int Repetitions = 1000;
        private static readonly float[] Input = { 1, 2, 3, 4, 5, 6 };
        private static readonly float[] Expected = { 1, 4, 9, 16, 25, 36 };
        private static readonly long[] Shape = { 3, 2 };

        internal static int Run(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Uso: PdfRasterProbe --h1d9a-onnx-runtime");
                return 2;
            }

            try
            {
                return Execute();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("H1D9A | Gate=FAIL");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int Execute()
        {
            var assembly = typeof(InferenceSession).Assembly;
            Console.WriteLine("ENTORNO | Windows=" + RuntimeInformation.OSDescription +
                              " | EnvironmentOSVersion=" + Environment.OSVersion +
                              " | CLR=" + Environment.Version +
                              " | Is64BitProcess=" + Environment.Is64BitProcess +
                              " | Arquitectura=" + RuntimeInformation.ProcessArchitecture +
                              " | ORT=" + assembly.GetName().Version +
                              " | Assembly=" + assembly.Location);
            if (!Environment.Is64BitProcess)
                throw new BadImageFormatException("H1D9A requiere un proceso x64 real.");

            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "H1D9A", "mul_1.onnx");
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("No se encontró el asset ONNX local en el output.", modelPath);

            var model = new FileInfo(modelPath);
            var sha256 = ComputeSha256(modelPath);
            Console.WriteLine("MODELO | Ruta=" + model.FullName + " | Bytes=" + model.Length + " | SHA256=" + sha256 +
                              " | Procedencia=microsoft/onnxruntime onnxruntime/test/testdata/mul_1.onnx");

            var sessionWatch = Stopwatch.StartNew();
            using (var options = new SessionOptions())
            using (var session = new InferenceSession(modelPath, options))
            {
                sessionWatch.Stop();
                ReportNativeRuntime();
                ValidateMetadata(session);

                var privateBefore = PrivateMemory();
                ValidateInference(session);
                var privateAfterWarmup = PrivateMemory();
                var managedBeforeBatch = GC.GetTotalMemory(false);
                var timings = new List<double>(Repetitions);
                var batchWatch = Stopwatch.StartNew();
                for (var i = 0; i < Repetitions; i++)
                {
                    var inferenceWatch = Stopwatch.StartNew();
                    ValidateInference(session);
                    inferenceWatch.Stop();
                    timings.Add(inferenceWatch.Elapsed.TotalMilliseconds);
                }
                batchWatch.Stop();
                var privateAfterBatch = PrivateMemory();
                var managedAfterBatch = GC.GetTotalMemory(false);
                timings.Sort();

                Console.WriteLine("INFERENCIA | Inicial=OK | Correctas=" + Repetitions + " | Total=" + Repetitions +
                                  " | Esperado=1,4,9,16,25,36");
                Console.WriteLine("METRICAS | SessionCreationMs=" + Format(sessionWatch.Elapsed.TotalMilliseconds) +
                                  " | BatchTotalMs=" + Format(batchWatch.Elapsed.TotalMilliseconds) +
                                  " | PromedioMs=" + Format(timings.Average()) +
                                  " | P50Ms=" + Format(Percentile(timings, 0.50)) +
                                  " | P95Ms=" + Format(Percentile(timings, 0.95)) +
                                  " | MaxMs=" + Format(timings[timings.Count - 1]));
                Console.WriteLine("MEMORIA | PrivateAntesWarmup=" + privateBefore +
                                  " | PrivateDespuesWarmup=" + privateAfterWarmup +
                                  " | PrivateDespuesLote=" + privateAfterBatch +
                                  " | GCManagedAntesLote=" + managedBeforeBatch +
                                  " | GCManagedDespuesLote=" + managedAfterBatch);
                Console.WriteLine("AISLAMIENTO | GPU=false | Cloud=false | RedRuntime=false | SQL=false | Gmail=false");
                Console.WriteLine("H1D9A | Gate=PASS");
                return 0;
            }
        }

        private static void ValidateMetadata(InferenceSession session)
        {
            ValidateNode(session.InputMetadata, "X", "input");
            ValidateNode(session.OutputMetadata, "Y", "output");
            if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1)
                throw new InvalidDataException("La cantidad de inputs/outputs del modelo no coincide con 1/1.");
            Console.WriteLine("METADATA | Input=X [3,2] float | Output=Y [3,2] float | Gate=PASS");
        }

        private static void ValidateNode(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, string kind)
        {
            NodeMetadata node;
            if (!metadata.TryGetValue(name, out node))
                throw new InvalidDataException("No existe el " + kind + " esperado '" + name + "'.");
            if (node.ElementType != typeof(float) || node.Dimensions.Length != 2 || node.Dimensions[0] != 3 || node.Dimensions[1] != 2)
                throw new InvalidDataException("Metadata inesperada para " + kind + " '" + name + "'.");
        }

        private static void ValidateInference(InferenceSession session)
        {
            using (var input = OrtValue.CreateTensorValueFromMemory(Input, Shape))
            {
                var inputNames = new[] { "X" };
                var inputs = new[] { input };
                var outputNames = new[] { "Y" };
                using (var runOptions = new RunOptions())
                using (var outputs = session.Run(runOptions, inputNames, inputs, outputNames))
                {
                    var actual = outputs[0].GetTensorDataAsSpan<float>();
                    if (actual.Length != Expected.Length)
                        throw new InvalidDataException("Cantidad de elementos de salida inesperada: " + actual.Length + ".");
                    for (var i = 0; i < Expected.Length; i++)
                    {
                        if (Math.Abs(actual[i] - Expected[i]) > 0.000001f)
                            throw new InvalidDataException("Salida incorrecta en índice " + i + ": " + actual[i].ToString("R", CultureInfo.InvariantCulture) + " != " + Expected[i].ToString("R", CultureInfo.InvariantCulture) + ".");
                    }
                }
            }
        }

        private static void ReportNativeRuntime()
        {
            ProcessModule native = null;
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (string.Equals(module.ModuleName, "onnxruntime.dll", StringComparison.OrdinalIgnoreCase))
                {
                    native = module;
                    break;
                }
            }
            if (native == null)
                throw new DllNotFoundException("La sesión fue creada, pero no se localizó onnxruntime.dll entre los módulos del proceso.");
            var version = FileVersionInfo.GetVersionInfo(native.FileName).FileVersion;
            Console.WriteLine("RUNTIME_NATIVO | Ruta=" + native.FileName + " | VersionArchivo=" + version + " | ProcesoX64=" + Environment.Is64BitProcess);
        }

        private static long PrivateMemory()
        {
            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                return process.PrivateMemorySize64;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            var rank = (sorted.Count - 1) * percentile;
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * (rank - lower));
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
