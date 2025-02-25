using ClosedXML.Examples;
using ClosedXML.Excel;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Path = System.IO.Path;

namespace ClosedXML.Tests
{
    internal static class TestHelper
    {
        public static string CurrencySymbol
        {
            get { return Thread.CurrentThread.CurrentCulture.NumberFormat.CurrencySymbol; }
        }

        //Note: Run example tests parameters
        public static string TestsOutputDirectory
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Generated");
            }
        }

        public const string ActualTestResultPostFix = "";
        public static readonly string ExampleTestsOutputDirectory = Path.Combine(TestsOutputDirectory, "Examples");

        private const bool CompareWithResources = true;

        private static readonly ResourceFileExtractor _extractor = new ResourceFileExtractor(".Resource.");

        public static void SaveWorkbook(XLWorkbook workbook, params string[] fileNameParts)
        {
            workbook.SaveAs(Path.Combine(new string[] { TestsOutputDirectory }.Concat(fileNameParts).ToArray()), true);
        }

        // Because different fonts are installed on Unix,
        // the columns widths after AdjustToContents() will
        // cause the tests to fail.
        // Therefore we ignore the width attribute when running on Unix
        public static bool StripColumnWidths { get { return IsRunningOnUnix; } }

        public static bool IsRunningOnUnix
        {
            get
            {
                int p = (int)Environment.OSVersion.Platform;
                return ((p == 4) || (p == 6) || (p == 128));
            }
        }

        public static void RunTestExample<T>(string filePartName, bool evaluateFormulae = false)
                where T : IXLExample, new()
        {
            // Make sure tests run on a deterministic culture
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            var example = new T();
            string[] pathParts = filePartName.Split(new char[] { '\\' });
            string filePath1 = Path.Combine(new List<string>() { ExampleTestsOutputDirectory }.Concat(pathParts).ToArray());

            var extension = Path.GetExtension(filePath1);
            var directory = Path.GetDirectoryName(filePath1);

            var fileName = Path.GetFileNameWithoutExtension(filePath1);
            fileName += ActualTestResultPostFix;
            fileName = Path.ChangeExtension(fileName, extension);

            filePath1 = Path.Combine(directory, "z" + fileName);
            var filePath2 = Path.Combine(directory, fileName);

            //Run test
            example.Create(filePath1);
            using (var wb = new XLWorkbook(filePath1))
                wb.SaveAs(filePath2, validate: true, evaluateFormulae);

            // Also load from template and save it again - but not necessary to test against reference file
            // We're just testing that it can save.
            using (var ms = new MemoryStream())
            using (var wb = XLWorkbook.OpenFromTemplate(filePath1))
                wb.SaveAs(ms, validate: true, evaluateFormulae);

            if (CompareWithResources)
            {
                string resourcePath = "Examples." + filePartName.Replace('\\', '.').TrimStart('.');
                using (var streamExpected = _extractor.ReadFileFromResourceToStream(resourcePath))
                using (var streamActual = File.OpenRead(filePath2))
                {
                    var success = ExcelDocsComparer.Compare(streamActual, streamExpected, out string message);
                    var formattedMessage =
                        String.Format(
                            "Actual file '{0}' is different than the expected file '{1}'. The difference is: '{2}'",
                            filePath2, resourcePath, message);

                    Assert.IsTrue(success, formattedMessage);
                }
            }
        }

        /// <summary>
        /// Create a workbook and compare it with a saved resource.
        /// </summary>
        /// <param name="workbookGenerator">A function that gets an empty workbook and fills it with data.</param>
        /// <param name="referenceResource">Reference workbook saved in resources</param>
        /// <param name="evaluateFormulae">Should formulas of created workbook be evaluated and values saved?</param>
        /// <param name="validate">Should the created workbook be validated during by OpenXmlSdk validator?</param>
        public static void CreateAndCompare(Action<IXLWorkbook> workbookGenerator, string referenceResource, bool evaluateFormulae = false, bool validate = true)
        {
            CreateAndCompare(() =>
            {
                var wb = new XLWorkbook();
                workbookGenerator(wb);
                return wb;
            }, referenceResource, evaluateFormulae, validate);
        }

        public static void CreateAndCompare(Func<IXLWorkbook> workbookGenerator, string referenceResource, bool evaluateFormulae = false, bool validate = true)
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            string[] pathParts = referenceResource.Split(new char[] { '\\' });
            string filePath1 = Path.Combine(new List<string>() { TestsOutputDirectory }.Concat(pathParts).ToArray());

            var extension = Path.GetExtension(filePath1);
            var directory = Path.GetDirectoryName(filePath1);

            var fileName = Path.GetFileNameWithoutExtension(filePath1);
            fileName += ActualTestResultPostFix;
            fileName = Path.ChangeExtension(fileName, extension);

            var filePath2 = Path.Combine(directory, fileName);

            using (var wb = workbookGenerator.Invoke())
                wb.SaveAs(filePath2, validate, evaluateFormulae);

            if (CompareWithResources)
            {
                string resourcePath = referenceResource.Replace('\\', '.').TrimStart('.');
                using (var streamExpected = _extractor.ReadFileFromResourceToStream(resourcePath))
                using (var streamActual = File.OpenRead(filePath2))
                {
                    var success = ExcelDocsComparer.Compare(streamActual, streamExpected, out string message);
                    var formattedMessage =
                        String.Format(
                            "Actual file '{0}' is different than the expected file '{1}'. The difference is: '{2}'",
                            filePath2, resourcePath, message);

                    Assert.IsTrue(success, formattedMessage);
                }
            }
        }

        /// <summary>
        /// Load a file from the <paramref name="loadResourcePath"/>, save it through ClosedXML without modifications
        /// and compare the saved file against the <paramref name="expectedOutputResourcePath"/>.
        /// </summary>
        /// <remarks>Useful for checking whether we can load data from Excel and save it while keeping various feature in the OpenXML intact.</remarks>
        public static void LoadSaveAndCompare(string loadResourcePath, string expectedOutputResourcePath, bool evaluateFormulae = false, bool validate = true)
        {
            using var stream = GetStreamFromResource(GetResourcePath(loadResourcePath));
            using var ms = new MemoryStream();
            CreateAndCompare(() =>
            {
                var wb = new XLWorkbook(stream);
                wb.SaveAs(ms, validate);
                return wb;
            }, expectedOutputResourcePath, evaluateFormulae, validate);
        }

        /// <summary>
        /// A testing method to load a workbook from resource and assert the state of the loaded workbook.
        /// </summary>
        public static void LoadAndAssert(Action<XLWorkbook> assertWorkbook, string loadResourcePath)
        {
            using var stream = GetStreamFromResource(GetResourcePath(loadResourcePath));
            using var wb = new XLWorkbook(stream);

            assertWorkbook(wb);
        }

        public static string GetResourcePath(string filePartName)
        {
            return filePartName.Replace('\\', '.').TrimStart('.');
        }

        public static Stream GetStreamFromResource(string resourcePath)
        {
            return _extractor.ReadFileFromResourceToStream(resourcePath);
        }

        public static void LoadFile(string filePartName)
        {
            IXLWorkbook wb;
            using (var stream = GetStreamFromResource(GetResourcePath(filePartName)))
            {
                Assert.DoesNotThrow(() => wb = new XLWorkbook(stream), "Unable to load resource {0}", filePartName);
            }
        }

        public static IEnumerable<String> ListResourceFiles(Func<String, Boolean> predicate = null)
        {
            return _extractor.GetFileNames(predicate);
        }
    }
}
