using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ValheimCliBridge
{
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), new[] { typeof(string) })]
    public static class GameConsole
    {
        [ThreadStatic] private static Terminal context;
        [ThreadStatic] private static List<string> output;
        [ThreadStatic] private static bool truncated;

        internal static Response Execute(Request request, Response response)
        {
            context = Console.instance;
            output = new List<string>();
            truncated = false;
            try
            {
                context.TryRunCommand(request.command, silentFail: false, skipAllowedCheck: false);
                response.state = "submitted";
                response.consoleOutput = output.ToArray();
                response.consoleOutputTruncated = truncated;
                return response;
            }
            finally { context = null; output = null; }
        }

        public static void Postfix(Terminal __instance, string text)
        {
            if (output == null || __instance != context || text == null) return;
            if (output.Count == 32) { truncated = true; return; }
            if (text.Length > 256) { text = text.Substring(0, char.IsHighSurrogate(text[255]) ? 255 : 256); truncated = true; }
            output.Add(text);
        }
    }
}
