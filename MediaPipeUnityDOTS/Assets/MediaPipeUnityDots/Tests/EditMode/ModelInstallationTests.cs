using System;
using System.IO;
using MediaPipeUnityDots.EditorTool;
using NUnit.Framework;

namespace MediaPipeUnityDots.Tests.EditMode
{
    public sealed class ModelInstallationTests
    {
        [Test]
        public void CorruptReplacementPreservesInstalledModelAndRemovesPartialFile()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var installed = Path.Combine(directory, "model.task");
                var partial = Path.Combine(directory, "model.download");
                File.WriteAllText(installed, "installed-model");
                File.WriteAllText(partial, "truncated-download");
                var expected = ModelCatalog.ComputeSha256(installed);

                Assert.Throws<InvalidDataException>(() => ModelCatalog.CommitVerifiedFile(partial, installed, expected));
                Assert.AreEqual("installed-model", File.ReadAllText(installed));
                Assert.IsFalse(File.Exists(partial));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void VerifiedDownloadReplacesOldFileWithoutLeavingPartialModel()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var installed = Path.Combine(directory, "model.task");
                var partial = Path.Combine(directory, "model.download");
                File.WriteAllText(installed, "old-version");
                File.WriteAllText(partial, "verified-version");
                var expected = ModelCatalog.ComputeSha256(partial);

                ModelCatalog.CommitVerifiedFile(partial, installed, expected);
                Assert.AreEqual("verified-version", File.ReadAllText(installed));
                Assert.IsFalse(File.Exists(partial));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
