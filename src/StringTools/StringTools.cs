// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Data;
using System.Linq;
using System.Text;

namespace StringTools
{
    /// <summary>
    /// A callback type to register with the library. The library will invoke this callback in registration order until
    /// true is returned. If no callback returns true, the library will fall back to its default interning logic.
    /// </summary>
    /// <param name="internableString">The string to be interned.</param>
    /// <param name="result">The interned string. Set to null and return true if the string should not be interned.</param>
    /// <returns>True if the <paramref name="internableString" /> was handled by the callback.</returns>
    public delegate bool TryInternStringDelegate(ref InternableString internableString, out string result);

    public static class StringTools
    {
        private static volatile TryInternStringDelegate[] s_internStringCallbacks = new TryInternStringDelegate[0];

        private static object s_locker = new object();

        public static void EnableDiagnostics()
        {
            OpportunisticIntern.Instance.EnableStatisticsGathering();
        }

        public static string CreateDiagnosticReport()
        {
            StringBuilder callbackReport = new StringBuilder();
            callbackReport.AppendFormat("{0} with {1} string interning callbacks registered", nameof(StringTools), s_internStringCallbacks.Length);
            callbackReport.AppendLine();

            return callbackReport.ToString() + OpportunisticIntern.Instance.FormatStatistics();
        }

        public static void RegisterStringInterningCallback(TryInternStringDelegate callback)
        {
            lock (s_locker)
            {
                if (!s_internStringCallbacks.Any(existingCallback => existingCallback == callback))
                {
                    TryInternStringDelegate[] newInternStringCallback = new TryInternStringDelegate[s_internStringCallbacks.Length + 1];
                    s_internStringCallbacks.CopyTo(newInternStringCallback, 0);
                    newInternStringCallback[s_internStringCallbacks.Length] = callback;

                    s_internStringCallbacks = newInternStringCallback;
                }
            }
        }

        public static void UnregisterStringInterningCallback(TryInternStringDelegate callback)
        {
            lock (s_locker)
            {
                s_internStringCallbacks = s_internStringCallbacks.Where(existingCallback => existingCallback != callback).ToArray();
            }
        }

        internal static bool CallStringInterningCallbacks(ref InternableString candidate, out string interned)
        {
            TryInternStringDelegate[] callbacks = s_internStringCallbacks;
            foreach (TryInternStringDelegate callback in callbacks)
            {
                if (callback(ref candidate, out interned))
                {
                    return true;
                }
            }
            interned = null;
            return false;
        }

        internal static void ResetForTests()
        {
            s_internStringCallbacks = new TryInternStringDelegate[0];
        }
    }
}
