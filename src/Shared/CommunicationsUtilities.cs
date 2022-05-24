﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

using Microsoft.Build.Shared;
using System.Reflection;

#if !FEATURE_APM
using System.Threading.Tasks;
#endif

namespace Microsoft.Build.Internal
{
    /// <summary>
    /// Enumeration of all possible (currently supported) options for handshakes.
    /// </summary>
    [Flags]
    internal enum HandshakeOptions
    {
        None = 0,

        /// <summary>
        /// Process is a TaskHost
        /// </summary>
        TaskHost = 1,

        /// <summary>
        /// Using the 2.0 CLR
        /// </summary>
        CLR2 = 2,

        /// <summary>
        /// 64-bit Intel process
        /// </summary>
        X64 = 4,

        /// <summary>
        /// Node reuse enabled
        /// </summary>
        NodeReuse = 8,

        /// <summary>
        /// Building with BelowNormal priority
        /// </summary>
        LowPriority = 16,

        /// <summary>
        /// Building with administrator privileges
        /// </summary>
        Administrator = 32,

        /// <summary>
        /// RAR service
        /// </summary>
        RarService = 64
    }

    internal readonly struct Handshake
    {
        readonly int options;
        readonly int salt;
        readonly int fileVersionMajor;
        readonly int fileVersionMinor;
        readonly int fileVersionBuild;
        readonly int fileVersionPrivate;
        readonly int sessionId;

        internal Handshake(HandshakeOptions nodeType)
        {
            // We currently use 6 bits of this 32-bit integer. Very old builds will instantly reject any handshake that does not start with F5 or 06; slightly old builds always lead with 00.
            // This indicates in the first byte that we are a modern build.
            options = (int)nodeType | (((int)CommunicationsUtilities.handshakeVersion) << 24);
            string handshakeSalt = Environment.GetEnvironmentVariable("MSBUILDNODEHANDSHAKESALT");
            CommunicationsUtilities.Trace("Handshake salt is {0}", handshakeSalt);
            string toolsDirectory = (nodeType & HandshakeOptions.X64) == HandshakeOptions.X64 ? BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64 : BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32;
            CommunicationsUtilities.Trace("Tools directory is {0}", toolsDirectory);
            salt = CommunicationsUtilities.GetHashCode(handshakeSalt + toolsDirectory);
            Version fileVersion = new Version(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion);
            fileVersionMajor = fileVersion.Major;
            fileVersionMinor = fileVersion.Minor;
            fileVersionBuild = fileVersion.Build;
            fileVersionPrivate = fileVersion.Revision;
            sessionId = (Environment.GetEnvironmentVariable("MSBUILDCLEARXMLCACHEONBUILDMANAGER") == "1")
                ? Process.GetCurrentProcess().SessionId
                : 0;
        }

        // This is used as a key, so it does not need to be human readable.
        public override string ToString()
        {
            return $"{options} {salt} {fileVersionMajor} {fileVersionMinor} {fileVersionBuild} {fileVersionPrivate} {sessionId}";
        }

        internal int[] RetrieveHandshakeComponents()
        {
            return new int[]
            {
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(options),
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(salt),
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(fileVersionMajor),
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(fileVersionMinor),
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(fileVersionBuild),
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(fileVersionPrivate),
                CommunicationsUtilities.AvoidEndOfHandshakeSignal(sessionId)
            };
        }
    }

    /// <summary>
    /// This class contains utility methods for the MSBuild engine.
    /// </summary>
    static internal class CommunicationsUtilities
    {
        /// <summary>
        /// Indicates to the NodeEndpoint that all the various parts of the Handshake have been sent.
        /// </summary>
        private const int EndOfHandshakeSignal = -0x2a2a2a2a;

        /// <summary>
        /// The version of the handshake. This should be updated each time the handshake structure is altered.
        /// </summary>
        internal const byte handshakeVersion = 0x01;

        /// <summary>
        /// The timeout to connect to a node.
        /// </summary>
        private const int DefaultNodeConnectionTimeout = 900 * 1000; // 15 minutes; enough time that a dev will typically do another build in this time

        /// <summary>
        /// Whether to trace communications
        /// </summary>
        private static bool s_trace = String.Equals(Environment.GetEnvironmentVariable("MSBUILDDEBUGCOMM"), "1", StringComparison.Ordinal);

        /// <summary>
        /// Place to dump trace
        /// </summary>
        private static string s_debugDumpPath;

        /// <summary>
        /// exclusive lock to folder write access
        /// </summary>
        private static Object s_traceLock = new();

        /// <summary>
        /// Ticks at last time logged
        /// </summary>
        private static long s_lastLoggedTicks = DateTime.UtcNow.Ticks;

        /// <summary>
        /// Delegate to debug the communication utilities.
        /// </summary>
        internal delegate void LogDebugCommunications(string format, params object[] stuff);

        /// <summary>
        /// Gets or sets the node connection timeout.
        /// </summary>
        static internal int NodeConnectionTimeout
        {
            get { return GetIntegerVariableOrDefault("MSBUILDNODECONNECTIONTIMEOUT", DefaultNodeConnectionTimeout); }
        }

        /// <summary>
        /// Get environment block
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static unsafe extern char* GetEnvironmentStrings();

        /// <summary>
        /// Free environment block
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static unsafe extern bool FreeEnvironmentStrings(char* pStrings);

        /// <summary>
        /// Copied from the BCL implementation to eliminate some expensive security asserts.
        /// Returns key value pairs of environment variables in a new dictionary
        /// with a case-insensitive key comparer.
        /// </summary>
        internal static Dictionary<string, string> GetEnvironmentVariables()
        {
            Dictionary<string, string> table = new Dictionary<string, string>(200, StringComparer.OrdinalIgnoreCase); // Razzle has 150 environment variables

            if (NativeMethodsShared.IsWindows)
            {
                unsafe
                {
                    char* pEnvironmentBlock = null;

                    try
                    {
                        pEnvironmentBlock = GetEnvironmentStrings();
                        if (pEnvironmentBlock == null)
                        {
                            throw new OutOfMemoryException();
                        }

                        // Search for terminating \0\0 (two unicode \0's).
                        char* pEnvironmentBlockEnd = pEnvironmentBlock;
                        while (!(*pEnvironmentBlockEnd == '\0' && *(pEnvironmentBlockEnd + 1) == '\0'))
                        {
                            pEnvironmentBlockEnd++;
                        }
                        long stringBlockLength = pEnvironmentBlockEnd - pEnvironmentBlock;

                        // Copy strings out, parsing into pairs and inserting into the table.
                        // The first few environment variable entries start with an '='!
                        // The current working directory of every drive (except for those drives
                        // you haven't cd'ed into in your DOS window) are stored in the 
                        // environment block (as =C:=pwd) and the program's exit code is 
                        // as well (=ExitCode=00000000)  Skip all that start with =.
                        // Read docs about Environment Blocks on MSDN's CreateProcess page.

                        // Format for GetEnvironmentStrings is:
                        // (=HiddenVar=value\0 | Variable=value\0)* \0
                        // See the description of Environment Blocks in MSDN's
                        // CreateProcess page (null-terminated array of null-terminated strings).
                        // Note the =HiddenVar's aren't always at the beginning.
                        for (int i = 0; i < stringBlockLength; i++)
                        {
                            int startKey = i;

                            // Skip to key
                            // On some old OS, the environment block can be corrupted. 
                            // Some lines will not have '=', so we need to check for '\0'. 
                            while (*(pEnvironmentBlock + i) != '=' && *(pEnvironmentBlock + i) != '\0')
                            {
                                i++;
                            }

                            if (*(pEnvironmentBlock + i) == '\0')
                            {
                                continue;
                            }

                            // Skip over environment variables starting with '='
                            if (i - startKey == 0)
                            {
                                while (*(pEnvironmentBlock + i) != 0)
                                {
                                    i++;
                                }

                                continue;
                            }

                            string key = new string(pEnvironmentBlock, startKey, i - startKey);
                            i++;

                            // skip over '='
                            int startValue = i;

                            while (*(pEnvironmentBlock + i) != 0)
                            {
                                // Read to end of this entry
                                i++;
                            }

                            string value = new string(pEnvironmentBlock, startValue, i - startValue);

                            // skip over 0 handled by for loop's i++
                            table[key] = value;
                        }
                    }
                    finally
                    {
                        if (pEnvironmentBlock != null)
                        {
                            FreeEnvironmentStrings(pEnvironmentBlock);
                        }
                    }
                }
            }
            else
            {
                var vars = Environment.GetEnvironmentVariables();
                foreach (var key in vars.Keys)
                {
                    table[(string) key] = (string) vars[key];
                }
            }

            return table;
        }

        /// <summary>
        /// Updates the environment to match the provided dictionary.
        /// </summary>
        internal static void SetEnvironment(IDictionary<string, string> newEnvironment)
        {
            if (newEnvironment != null)
            {
                // First, empty out any new variables
                foreach (KeyValuePair<string, string> entry in CommunicationsUtilities.GetEnvironmentVariables())
                {
                    if (!newEnvironment.ContainsKey(entry.Key))
                    {
                        Environment.SetEnvironmentVariable(entry.Key, null);
                    }
                }

                // Then, make sure the old ones have their old values. 
                foreach (KeyValuePair<string, string> entry in newEnvironment)
                {
                    Environment.SetEnvironmentVariable(entry.Key, entry.Value);
                }
            }
        }

#nullable enable
        /// <summary>
        /// Indicate to the client that all elements of the Handshake have been sent.
        /// </summary>
        internal static void WriteEndOfHandshakeSignal(this Stream stream)
        {
            stream.WriteIntForHandshake(EndOfHandshakeSignal);
        }

        /// <summary>
        /// Extension method to write a series of bytes to a stream
        /// </summary>
        internal static void WriteIntForHandshake(this Stream stream, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            // We want to read the long and send it from left to right (this means big endian)
            // if we are little endian we need to reverse the array to keep the left to right reading
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            ErrorUtilities.VerifyThrow(bytes.Length == 4, "Int should be 4 bytes");

            stream.Write(bytes, 0, bytes.Length);
        }

        // TODO: spannification - if possible
        internal static void ReadEndOfHandshakeSignal(this MemoryStream stream, bool isProvider)
        {
            // Accept only the first byte of the EndOfHandshakeSignal
            int valueRead = stream.ReadIntForHandshake();

            if (valueRead != EndOfHandshakeSignal)
            {
                if (isProvider)
                {
                    CommunicationsUtilities.Trace("Handshake failed on part {0}. Probably the client is a different MSBuild build.", valueRead);
                }
                else
                {
                    CommunicationsUtilities.Trace("Expected end of handshake signal but received {0}. Probably the host is a different MSBuild build.", valueRead);
                }
                throw new InvalidOperationException("Not matching 'End of handshake'.");
            }
        }

        internal static int ReadHandshakeWithTimeout(this Stream stream, byte[] buffer, int offset, int count, int timeout)
        {
            // Enforce a minimum timeout because the Windows code can pass
            // a timeout of 0 for the connection, but that doesn't work for
            // the actual timeout here.
            timeout = Math.Max(timeout, 50);

#if !CLR2COMPATIBILITY
            // A legacy MSBuild.exe won't try to connect to MSBuild running
            // in a dotnet host process, so we can read the bytes simply.
            var readTask = stream.ReadAsync(buffer, offset, count);

            // Manual timeout here because the timeout passed to Connect() just before
            // calling this method does not apply on UNIX domain socket-based
            // implementations of PipeStream.
            // https://github.com/dotnet/corefx/issues/28791
            if (!readTask.Wait(timeout))
            {
                throw new IOException(string.Format(CultureInfo.InvariantCulture, "Did not receive {0} handshake bytes in {1}ms", count, timeout));
            }

            var readBytes = readTask.GetAwaiter().GetResult();

            if (readBytes != count)
            {
                throw new IOException(string.Format(CultureInfo.InvariantCulture, "Receive only {0} from required {1} handshake bytes", readBytes, count));
            }

            return readBytes;
#else
            return stream.Read(buffer, offset, count);
#endif
        }

        /// <summary>
        /// Extension method to read a series of bytes from a stream.
        /// If specified, leading byte matches one in the supplied array if any, returns rejection byte and throws IOException.
        /// </summary>
        internal static int ReadIntForHandshake(this MemoryStream stream)
        {
            byte[] bytes = new byte[4];

            stream.Read(bytes, 0, bytes.Length);

            try
            {
                // We want to read the long and send it from left to right (this means big endian)
                // If we are little endian the stream has already been reversed by the sender, we need to reverse it again to get the original number
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                return BitConverter.ToInt32(bytes, 0 /* start index */);
            }
            catch (ArgumentException ex)
            {
                throw new IOException(String.Format(CultureInfo.InvariantCulture, "Failed to convert the handshake to big-endian. {0}", ex.Message));
            }
        }
#nullable disable

#if !FEATURE_APM
        internal static async Task<int> ReadAsync(Stream stream, byte[] buffer, int bytesToRead)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < bytesToRead)
            {
                int bytesRead = await stream.ReadAsync(buffer, totalBytesRead, bytesToRead - totalBytesRead);
                if (bytesRead == 0)
                {
                    return totalBytesRead;
                }
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }
#endif

        /// <summary>
        /// Given the appropriate information, return the equivalent HandshakeOptions.
        /// </summary>
        internal static HandshakeOptions GetHandshakeOptions(bool taskHost, bool is64Bit = false, bool nodeReuse = false, bool lowPriority = false, IDictionary<string, string> taskHostParameters = null)
        {
            HandshakeOptions context = taskHost ? HandshakeOptions.TaskHost : HandshakeOptions.None;

            int clrVersion = 0;

            // We don't know about the TaskHost. Figure it out.
            if (taskHost)
            {
                // Take the current TaskHost context
                if (taskHostParameters == null)
                {
                    clrVersion = typeof(bool).GetTypeInfo().Assembly.GetName().Version.Major;
                    is64Bit = XMakeAttributes.GetCurrentMSBuildArchitecture().Equals(XMakeAttributes.MSBuildArchitectureValues.x64);
                }
                else
                {
                    ErrorUtilities.VerifyThrow(taskHostParameters.TryGetValue(XMakeAttributes.runtime, out string runtimeVersion), "Should always have an explicit runtime when we call this method.");
                    ErrorUtilities.VerifyThrow(taskHostParameters.TryGetValue(XMakeAttributes.architecture, out string architecture), "Should always have an explicit architecture when we call this method.");

                    clrVersion = runtimeVersion.Equals(XMakeAttributes.MSBuildRuntimeValues.clr4, StringComparison.OrdinalIgnoreCase) ? 4 : 2;
                    is64Bit = architecture.Equals(XMakeAttributes.MSBuildArchitectureValues.x64);
                }
            }

            if (is64Bit)
            {
                context |= HandshakeOptions.X64;
            }
            if (clrVersion == 2)
            {
                context |= HandshakeOptions.CLR2;
            }
            if (nodeReuse)
            {
                context |= HandshakeOptions.NodeReuse;
            }
            if (lowPriority)
            {
                context |= HandshakeOptions.LowPriority;
            }
#if FEATURE_SECURITY_PRINCIPAL_WINDOWS
            // If we are running in elevated privs, we will only accept a handshake from an elevated process as well.
            // Both the client and the host will calculate this separately, and the idea is that if they come out the same
            // then we can be sufficiently confident that the other side has the same elevation level as us.  This is complementary
            // to the username check which is also done on connection.
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                context |= HandshakeOptions.Administrator;
            }
#endif
            return context;
        }

        /// <summary>
        /// Gets the value of an integer environment variable, or returns the default if none is set or it cannot be converted.
        /// </summary>
        internal static int GetIntegerVariableOrDefault(string environmentVariable, int defaultValue)
        {
            string environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
            if (String.IsNullOrEmpty(environmentValue))
            {
                return defaultValue;
            }

            int localDefaultValue;
            if (Int32.TryParse(environmentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out localDefaultValue))
            {
                defaultValue = localDefaultValue;
            }

            return defaultValue;
        }

        /// <summary>
        /// Writes trace information to a log file
        /// </summary>
        internal static void Trace(string format, params object[] args)
        {
            Trace(/* nodeId */ -1, format, args);
        }

        /// <summary>
        /// Writes trace information to a log file
        /// </summary>
        internal static void Trace(int nodeId, string format, params object[] args)
        {
            if (s_trace)
            {
                if (s_debugDumpPath == null)
                {
                    s_debugDumpPath = Environment.GetEnvironmentVariable("MSBUILDDEBUGPATH");

                    if (String.IsNullOrEmpty(s_debugDumpPath))
                    {
                        s_debugDumpPath = Path.GetTempPath();
                    }
                    else
                    {
                        Directory.CreateDirectory(s_debugDumpPath);
                    }
                }

                try
                {
                    string fileName = @"MSBuild_CommTrace_PID_{0}";
                    if (nodeId != -1)
                    {
                        fileName += "_node_" + nodeId;
                    }

                    fileName += ".txt";

                    lock (s_traceLock)
                    {
                        using (StreamWriter file =
                            FileUtilities.OpenWrite(String.Format(CultureInfo.CurrentCulture, Path.Combine(s_debugDumpPath, fileName), Process.GetCurrentProcess().Id, nodeId),
                                append: true))
                        {
                            string message = String.Format(CultureInfo.CurrentCulture, format, args);
                            long now = DateTime.UtcNow.Ticks;
                            float millisecondsSinceLastLog = (float) (now - s_lastLoggedTicks) / 10000L;
                            s_lastLoggedTicks = now;
                            file.WriteLine("{0} (TID {1}) {2,15} +{3,10}ms: {4}", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId, now, millisecondsSinceLastLog,
                                message);
                        }
                    }
                }
                catch (IOException)
                {
                    // Ignore
                }
                catch (Exception)
                {
                    // Tracing shall never throw in production
                    Debug.Assert(false, "Exception during CommunicationsUtilities.Trace");
                }
            }
        }

        /// <summary>
        /// Gets a hash code for this string.  If strings A and B are such that A.Equals(B), then
        /// they will return the same hash code.
        /// This is as implemented in CLR String.GetHashCode() [ndp\clr\src\BCL\system\String.cs]
        /// but stripped out architecture specific defines
        /// that causes the hashcode to be different and this causes problem in cross-architecture handshaking
        /// </summary>
        internal static int GetHashCode(string fileVersion)
        {
            unsafe
            {
                fixed (char* src = fileVersion)
                {
                    int hash1 = (5381 << 16) + 5381;
                    int hash2 = hash1;

                    int* pint = (int*)src;
                    int len = fileVersion.Length;
                    while (len > 0)
                    {
                        hash1 = ((hash1 << 5) + hash1 + (hash1 >> 27)) ^ pint[0];
                        if (len <= 2)
                        {
                            break;
                        }

                        hash2 = ((hash2 << 5) + hash2 + (hash2 >> 27)) ^ pint[1];
                        pint += 2;
                        len -= 4;
                    }

                    return hash1 + (hash2 * 1566083941);
                }
            }
        }

        internal static int AvoidEndOfHandshakeSignal(int x)
        {
            return x == EndOfHandshakeSignal ? ~x : x;
        }

        internal static bool WasServerMutexOpen(string mutexName)
        {
            try
            {
                if (PlatformInformation.IsRunningOnMono)
                {
                    IServerMutex? mutex = null;
                    bool createdNew = false;
                    try
                    {
                        mutex = new ServerFileMutexPair(mutexName, false, out createdNew);
                        return !createdNew;
                    }
                    finally
                    {
                        mutex?.Dispose();
                    }
                }
                else
                {
                    return ServerNamedMutex.WasOpen(mutexName);
                }
            }
            catch
            {
                // In the case an exception occurred trying to open the Mutex then 
                // the assumption is that it's not open. 
                return false;
            }
        }

        internal static IServerMutex OpenOrCreateMutex(string name, out bool createdNew)
        {
            if (PlatformInformation.IsRunningOnMono)
            {
                return new ServerFileMutexPair(name, initiallyOwned: true, out createdNew);
            }
            else
            {
                return new ServerNamedMutex(name, out createdNew);
            }
        }

        internal interface IServerMutex : IDisposable
        {
            bool TryLock(int timeoutMs);
            bool IsDisposed { get; }
        }

        /// <summary>
        /// An interprocess mutex abstraction based on OS advisory locking (FileStream.Lock/Unlock).
        /// If multiple processes running as the same user create FileMutex instances with the same name,
        ///  those instances will all point to the same file somewhere in a selected temporary directory.
        /// The TryLock method can be used to attempt to acquire the mutex, with Unlock or Dispose used to release.
        /// Unlike Win32 named mutexes, there is no mechanism for detecting an abandoned mutex. The file
        ///  will simply revert to being unlocked but remain where it is.
        /// </summary>
        internal sealed class FileMutex : IDisposable
        {
            public readonly FileStream Stream;
            public readonly string FilePath;

            public bool IsLocked { get; private set; }

            internal static string GetMutexDirectory()
            {
                var tempPath = GetTempPath(null);
                var result = Path.Combine(tempPath!, ".msbuild");
                Directory.CreateDirectory(result);
                return result;
            }

            public FileMutex(string name)
            {
                FilePath = Path.Combine(GetMutexDirectory(), name);
                Stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }

            public bool TryLock(int timeoutMs)
            {
                if (IsLocked)
                    throw new InvalidOperationException("Lock already held");

                var sw = Stopwatch.StartNew();
                do
                {
                    try
                    {
                        Stream.Lock(0, 0);
                        IsLocked = true;
                        return true;
                    }
                    catch (IOException)
                    {
                        // Lock currently held by someone else.
                        // We want to sleep for a short period of time to ensure that other processes
                        //  have an opportunity to finish their work and relinquish the lock.
                        // Spinning here (via Yield) would work but risks creating a priority
                        //  inversion if the lock is held by a lower-priority process.
                        Thread.Sleep(1);
                    }
                    catch (Exception)
                    {
                        // Something else went wrong.
                        return false;
                    }
                } while (sw.ElapsedMilliseconds < timeoutMs);

                return false;
            }

            public void Unlock()
            {
                if (!IsLocked)
                    return;
                Stream.Unlock(0, 0);
                IsLocked = false;
            }

            public void Dispose()
            {
                var wasLocked = IsLocked;
                if (wasLocked)
                    Unlock();
                Stream.Dispose();
                // We do not delete the lock file here because there is no reliable way to perform a
                //  'delete if no one has the file open' operation atomically on *nix. This is a leak.
            }
        }

        internal sealed class ServerNamedMutex : IServerMutex
        {
            public readonly Mutex ServerMutex;

            public bool IsDisposed { get; private set; }
            public bool IsLocked { get; private set; }

            public ServerNamedMutex(string mutexName, out bool createdNew)
            {
                ServerMutex = new Mutex(
                    initiallyOwned: true,
                    name: mutexName,
                    createdNew: out createdNew
                );
                if (createdNew)
                    IsLocked = true;
            }

            public static bool WasOpen(string mutexName)
            {
                try
                {
                    // we can't use TryOpenExisting as it is not supported in net3.5
                    using var m = Mutex.OpenExisting(mutexName);
                    return true;
                }
                catch
                {
                    // In the case an exception occurred trying to open the Mutex then 
                    // the assumption is that it's not open.
                    return false;
                }
            }

            public bool TryLock(int timeoutMs)
            {
                if (IsDisposed)
                    throw new ObjectDisposedException("Mutex");
                if (IsLocked)
                    throw new InvalidOperationException("Lock already held");
                return IsLocked = ServerMutex.WaitOne(timeoutMs);
            }

            public void Dispose()
            {
                if (IsDisposed)
                    return;
                IsDisposed = true;

                try
                {
                    if (IsLocked)
                        ServerMutex.ReleaseMutex();
                }
                finally
                {
                    (ServerMutex as IDisposable).Dispose();
                    IsLocked = false;
                }
            }
        }

        /// <summary>
        /// Approximates a named mutex with 'locked', 'unlocked' and 'abandoned' states.
        /// There is no reliable way to detect whether a mutex has been abandoned on some target platforms,
        ///  so we use the AliveMutex to manually track whether the creator of a mutex is still running,
        ///  while the HeldMutex represents the actual lock state of the mutex.
        /// </summary>
        internal sealed class ServerFileMutexPair : IServerMutex
        {
            public readonly FileMutex AliveMutex;
            public readonly FileMutex HeldMutex;

            public bool IsDisposed { get; private set; }

            public ServerFileMutexPair(string mutexName, bool initiallyOwned, out bool createdNew)
            {
                AliveMutex = new FileMutex(mutexName + "-alive");
                HeldMutex = new FileMutex(mutexName + "-held");
                createdNew = AliveMutex.TryLock(0);
                if (initiallyOwned && createdNew)
                {
                    if (!TryLock(0))
                        throw new Exception("Failed to lock mutex after creating it");
                }
            }

            public bool TryLock(int timeoutMs)
            {
                if (IsDisposed)
                    throw new ObjectDisposedException("Mutex");
                return HeldMutex.TryLock(timeoutMs);
            }

            public void Dispose()
            {
                if (IsDisposed)
                    return;
                IsDisposed = true;

                try
                {
                    HeldMutex.Unlock();
                    AliveMutex.Unlock();
                }
                finally
                {
                    AliveMutex.Dispose();
                    HeldMutex.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets the value of the temporary path for the current environment assuming the working directory
        /// is <paramref name="workingDir"/>.  This function must emulate <see cref="Path.GetTempPath"/> as 
        /// closely as possible.
        /// </summary>
        public static string GetTempPath(string workingDir)
        {
            if (PlatformInformation.IsUnix)
            {
                // Unix temp path is fine: it does not use the working directory
                // (it uses ${TMPDIR} if set, otherwise, it returns /tmp)
                return Path.GetTempPath();
            }

            var tmp = Environment.GetEnvironmentVariable("TMP");
            if (Path.IsPathRooted(tmp))
            {
                return tmp;
            }

            var temp = Environment.GetEnvironmentVariable("TEMP");
            if (Path.IsPathRooted(temp))
            {
                return temp;
            }

            if (!string.IsNullOrEmpty(workingDir))
            {
                if (!string.IsNullOrEmpty(tmp))
                {
                    return Path.Combine(workingDir, tmp);
                }

                if (!string.IsNullOrEmpty(temp))
                {
                    return Path.Combine(workingDir, temp);
                }
            }

            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (Path.IsPathRooted(userProfile))
            {
                return userProfile;
            }

            return Environment.GetEnvironmentVariable("SYSTEMROOT");
        }

        /// <summary>
        /// This class provides simple properties for determining whether the current platform is Windows or Unix-based.
        /// We intentionally do not use System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(...) because
        /// it incorrectly reports 'true' for 'Windows' in desktop builds running on Unix-based platforms via Mono.
        /// </summary>
        internal static class PlatformInformation
        {
            public static bool IsWindows => Path.DirectorySeparatorChar == '\\';
            public static bool IsUnix => Path.DirectorySeparatorChar == '/';
            public static bool IsRunningOnMono
            {
                get
                {
                    try
                    {
                        return !(Type.GetType("Mono.Runtime") is null);
                    }
                    catch
                    {
                        // Arbitrarily assume we're not running on Mono.
                        return false;
                    }
                }
            }
        }
    }
}
